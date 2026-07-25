package com.unfreeze.ringdebug.ble

import android.annotation.SuppressLint
import android.bluetooth.*
import android.bluetooth.le.*
import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import com.unfreeze.ringdebug.proto.RingProtocol
import java.util.UUID

/**
 * 指环 BLE 管理器 (v4 - 修复 requestMtu 和顺序流程)
 *
 * 关键修复:
 * 1. requestMtu 只调用一次 (onDescriptorWrite 后), 不在 send() 里调
 * 2. 断开后彻底清理
 * 3. 日志全部同步 UI
 */
class RingBleManager(private val ctx: Context) {
    private val bm = ctx.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager
    private val adapter get() = bm.adapter
    private var gatt: BluetoothGatt? = null
    private var txChar: BluetoothGattCharacteristic? = null
    private var rxChar: BluetoothGattCharacteristic? = null
    private val stream = RingProtocol.PacketStream()
    private var notifReady = false
    private val writeQueue = ArrayDeque<ByteArray>()
    private var writing = false
    private var scanning = false
    private var connecting = false
    private val ringMap = mutableMapOf<String, Int>()
    private var emptyScanCount = 0
    private val cancelCb = Runnable { checkConnect() }
    private var connectRetries = 0

    var connectedMac: String = ""
        private set
    var lastRawHex: String = ""
        private set

    var onLog: ((String) -> Unit)? = null
    var onPacket: ((RingProtocol.Packet) -> Unit)? = null
    var onState: ((State) -> Unit)? = null
    var onScan: ((String, String) -> Unit)? = null
    var onRawData: ((String) -> Unit)? = null

    enum class State { DISCONNECTED, SCANNING, CONNECTING, CONNECTED, NUS_READY }

    private fun log(s: String) { Log.i("RingBLE", s); onLog?.invoke(s) }

    @SuppressLint("MissingPermission")
    fun startScan() {
        if (scanning) { log("📡 扫描已在运行"); return }
        if (adapter == null || !adapter.isEnabled) { log("❌ 蓝牙未开启"); return }
        val sc = adapter.bluetoothLeScanner ?: run { log("❌ 无 BLE scanner"); return }
        ringMap.clear()
        connecting = false
        scanning = true
        onState?.invoke(State.SCANNING)
        log("📡 连续扫描中...")
        val settings = ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_BALANCED).build()
        try { sc.startScan(null, settings, scanCb) }
        catch (e: Exception) { log("❌ 扫描失败: ${e.message}"); scanning = false }
        h.removeCallbacks(cancelCb)
        h.removeCallbacks(scanRestartCb)
        h.postDelayed(cancelCb, 10000)
        h.postDelayed(scanRestartCb, 30000)
    }

    @SuppressLint("MissingPermission")
    fun restartScan() {
        log("🔄 重启扫描")
        disconnect()
        stopScanInner()
        scanning = false; connecting = false
        ringMap.clear(); emptyScanCount = 0
        startScan()
    }

    @SuppressLint("MissingPermission")
    fun stopScan() { stopScanInner(); scanning = false; h.removeCallbacks(cancelCb) }

    @SuppressLint("MissingPermission")
    private fun stopScanInner() {
        try { adapter?.bluetoothLeScanner?.stopScan(scanCb) } catch (_: Exception) {}
    }

    private val h = Handler(Looper.getMainLooper())

    private val scanRestartCb = Runnable {
        if (scanning && !connecting && !isConnected()) {
            Log.i("RingBLE", "⏰ 30s到期, 重启扫描(防止系统暂停)")
            stopScanInner()
            scanning = false
            ringMap.clear()
            startScan()
        }
    }

    private fun checkConnect() {
        if (connecting || isConnected()) { emptyScanCount = 0; h.postDelayed(cancelCb, 10000); return }
        if (ringMap.isEmpty()) {
            emptyScanCount++
            if (emptyScanCount >= 3) {
                log("⚠ 空信号3次, 强制重启扫描...")
                emptyScanCount = 0; stopScanInner(); startScan()
            } else {
                log("⏳ 暂无指环 (${emptyScanCount}/3), 10s后重试...")
                h.postDelayed(cancelCb, 10000)
            }
            return
        }
        emptyScanCount = 0
        val best = ringMap.maxByOrNull { it.value } ?: return
        connecting = true
        log("🎯 最强信号 RSSI=${best.value}dBm, 连接...")
        stopScanInner()
        connect(best.key)
    }

    private val scanCb = object : ScanCallback() {
        @SuppressLint("MissingPermission")
        override fun onScanResult(callbackType: Int, result: ScanResult) {
            val dev = result.device ?: return
            val name = result.scanRecord?.deviceName ?: dev.name ?: ""
            val mac = dev.address?.uppercase() ?: ""
            if (mac.isEmpty()) return
            // 日志: 所有 BLE 设备 (不丢弃任何设备)
            val isRing = name.contains("ring", ignoreCase = true) || mac.contains("ZILO", ignoreCase = true)
            val hasNus = result.scanRecord?.serviceUuids?.any {
                it.uuid.toString().uppercase() == RingProtocol.NUS_SERVICE.uppercase()
            } ?: false
            // 记录所有设备到日志 (调试用)
            if (name.isNotEmpty() || isRing || hasNus) {
                Log.i("RingBLE", "BLE: [$mac] name=" + name + " nus=" + hasNus + " rssi=" + result.rssi)
            }
            // 只把匹配指环的设备加入地图
            if (!isRing && !hasNus) return
            val oldRssi = ringMap[mac]
            if (oldRssi == null) {
                ringMap[mac] = result.rssi
                log("📶 发现指环 [${mac.takeLast(5)}] RSSI=${result.rssi}dBm")
                onScan?.invoke(name, mac)
            } else if (result.rssi > oldRssi) {
                ringMap[mac] = result.rssi
            }
        }
        override fun onScanFailed(code: Int) {
            log("❌ 扫描失败 code=$code, 5s后重启")
            scanning = false; h.postDelayed({ startScan() }, 5000)
        }
    }

    private fun connect(mac: String) {
        closeGatt()
        val dev = adapter?.getRemoteDevice(mac) ?: run {
            log("❌ 无此设备 $mac"); connecting = false; startScan(); return
        }
        connectedMac = mac
        nusFound = false; notifReady = false
        onState?.invoke(State.CONNECTING)
        log("🔗 连接 $mac ...")
        gatt = dev.connectGatt(ctx, false, gattCb, BluetoothDevice.TRANSPORT_LE)
        h.removeCallbacks(connectTimeoutCb)
        h.postDelayed(connectTimeoutCb, 10000)
        connectRetries = 1
    }

    private val connectTimeoutCb = Runnable {
        if (!isConnected() && connecting) {
            if (connectRetries >= 3) {
                log("❌ 连接超时 (${connectRetries}次), 恢复扫描")
                closeGatt(); connecting = false; connectRetries = 0; startScan()
            } else {
                connectRetries++
                log("⏳ 连接超时, 重试 (${connectRetries}/3)...")
                closeGatt()
                h.postDelayed({
                    if (connecting) {
                        val best = ringMap.maxByOrNull { it.value }
                        if (best != null) connect(best.key) else { connecting = false; startScan() }
                    }
                }, 2000)
            }
        }
    }

    private fun closeGatt() {
        try { gatt?.disconnect(); gatt?.close() } catch(_: Exception) {}
        gatt = null; txChar = null; rxChar = null
        notifReady = false; writing = false; writeQueue.clear()
    }

    fun isConnected(): Boolean = gatt != null && notifReady
    fun ringCount(): Int = ringMap.size
    fun bestRssi(): Int = ringMap.maxByOrNull { it.value }?.value ?: -128

    @SuppressLint("MissingPermission")
    fun disconnect() {
        log("🔌 断开连接")
        h.removeCallbacks(cancelCb)
        h.removeCallbacks(connectTimeoutCb)
        h.removeCallbacks(scanRestartCb)
        stopScanInner()
        closeGatt()
        scanning = false; connecting = false; connectRetries = 0
        connectedMac = ""; nusFound = false
        onState?.invoke(State.DISCONNECTED)
        log("✓ 已断开")
    }

    // 内部分配的新 nusFound 变量
    private var nusFound = false

    private val gattCb = object : BluetoothGattCallback() {
        override fun onConnectionStateChange(g: BluetoothGatt, status: Int, newState: Int) {
            h.removeCallbacks(connectTimeoutCb)
            when (newState) {
                BluetoothProfile.STATE_CONNECTED -> {
                    connectRetries = 0
                    onState?.invoke(State.CONNECTED)
                    log("✅ 蓝牙已连接 (status=$status), 发现服务...")
                    g.discoverServices()
                }
                BluetoothProfile.STATE_DISCONNECTED -> {
                    onState?.invoke(State.DISCONNECTED)
                    notifReady = false; connecting = false
                    log("⚠ 断开 (status=$status)")
                    if (status != 0) {
                        log("🔄 异常断开, 3s后重新扫描")
                        closeGatt()
                        h.postDelayed({ if (!scanning && !connecting) startScan() }, 3000)
                    }
                }
            }
        }

        override fun onServicesDiscovered(g: BluetoothGatt, status: Int) {
            if (status != BluetoothGatt.GATT_SUCCESS) {
                log("❌ 发现服务失败 status=$status"); return
            }
            val svc = g.getService(UUID.fromString(RingProtocol.NUS_SERVICE))
            if (svc == null) {
                log("❌ 未找到 NUS 服务, 断开重试...")
                g.disconnect(); g.close(); gatt = null; connecting = false
                h.postDelayed({ startScan() }, 500); return
            }
            txChar = svc.getCharacteristic(UUID.fromString(RingProtocol.NUS_TX))
            rxChar = svc.getCharacteristic(UUID.fromString(RingProtocol.NUS_RX))
            if (txChar == null || rxChar == null) {
                log("❌ 未找到 NUS 特性!"); return
            }
            log("📋 NUS 服务已找到, 启用通知...")
            val notifOk = g.setCharacteristicNotification(txChar, true)
            val cccd = txChar!!.getDescriptor(UUID.fromString("00002902-0000-1000-8000-00805f9b34fb"))
            if (cccd == null) {
                log("❌ 无 CCCD 描述符!"); return
            }
            cccd.value = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
            val writeOk = g.writeDescriptor(cccd)
            nusFound = true
            log("  setNotif=$notifOk 写CCCD: $writeOk")
        }

        override fun onDescriptorWrite(g: BluetoothGatt, d: BluetoothGattDescriptor, status: Int) {
            if (status == BluetoothGatt.GATT_SUCCESS) {
                // 请求 MTU (只请求一次)
                g.requestMtu(512)
                log("📏 请求 MTU 512...")
            } else {
                log("❌ CCCD 写入失败 status=$status")
            }
        }

        override fun onMtuChanged(g: BluetoothGatt, mtu: Int, status: Int) {
            if (status == BluetoothGatt.GATT_SUCCESS) {
                notifReady = true
                connecting = false
                onState?.invoke(State.NUS_READY)
                log("✅ NUS 就绪! MTU=$mtu 可发指令")
                flushQueue()
            } else {
                // MTU 协商失败也当作就绪
                notifReady = true; connecting = false
                onState?.invoke(State.NUS_READY)
                log("✅ NUS 就绪! (MTU协商失败, 用默认值)")
                flushQueue()
            }
        }

        override fun onCharacteristicChanged(g: BluetoothGatt, c: BluetoothGattCharacteristic) {
            val data = c.value ?: run { log("⚠ 收到 null 数据"); return }
            val hex = data.joinToString(" ") { String.format("%02X", it) }
            val hexPreview = if (data.size > 24) hex.take(72) + "..." else hex
            lastRawHex = hexPreview
            onRawData?.invoke(hexPreview)
            log("📩 RX ${data.size}B CRC=0x${String.format("%04X", RingProtocol.crc16(data))}")
            val pkts = stream.feed(data)
            if (pkts.isEmpty()) {
                log("  ⚠ 数据残留 (等待后续分片)")
            }
            for (p in pkts) {
                log("  → 0x${String.format("%04X", p.command)} (${p.body.size}B)")
                onPacket?.invoke(p)
            }
        }

        override fun onCharacteristicWrite(g: BluetoothGatt, c: BluetoothGattCharacteristic, status: Int) {
            writing = false
            if (status != BluetoothGatt.GATT_SUCCESS) log("❌ BLE 写入失败 status=$status")
            flushQueue()
        }
    }

    @SuppressLint("MissingPermission")
    fun send(command: Int, body: ByteArray = ByteArray(0)) {
        if (!isConnected()) {
            log("⚠ 未连接, 无法发送")
            return
        }
        val bytes = RingProtocol.encode(command, body)
        log("📤 0x${String.format("%04X", command)} ${bytes.size}B")
        val mtu = 20
        var off = 0
        while (off < bytes.size) {
            val len = minOf(mtu, bytes.size - off)
            writeQueue.add(bytes.copyOfRange(off, off + len))
            off += len
        }
        flushQueue()
    }

    @SuppressLint("MissingPermission")
    private fun flushQueue() {
        if (writing || writeQueue.isEmpty() || rxChar == null || gatt == null) return
        val chunk = writeQueue.removeFirst()
        try {
            rxChar!!.value = chunk
            rxChar!!.writeType = BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE
            val ok = gatt!!.writeCharacteristic(rxChar!!)
            log("  ✏️ ${chunk.size}B ok=$ok")
            writing = true
        } catch (e: Exception) {
            log("  ❌ ${e.message}")
            writing = false
        }
    }
}
