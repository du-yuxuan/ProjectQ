package com.unfreeze.ringdebug

import android.Manifest
import android.annotation.SuppressLint
import android.content.ComponentName
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import com.unfreeze.ringdebug.ble.RingBleManager
import com.unfreeze.ringdebug.cmd.RingCommander
import com.unfreeze.ringdebug.databinding.ActivityMainBinding
import com.unfreeze.ringdebug.proto.RingProtocol
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream

/**
 * 指环调试 — 顺序流程版
 *
 * 核心改动: 连接后按顺序逐步推进, 每一步等待响应后再继续:
 *   NUS_READY → getSystemInfo → wait SYS_INFO_RSP
 *            → getAudioCount → wait AUDIO_COUNT_RSP
 *            → startSensor  → wait SENSOR_START_RSP
 *            → 实时数据流
 *
 * 移除自动 IMU 轮询 (导致超时堆积), 改为手动启动 + 显式状态机
 */
class MainActivity : AppCompatActivity() {
    private lateinit var b: ActivityMainBinding
    private lateinit var ble: RingBleManager
    private lateinit var cmd: RingCommander
    private val h = Handler(Looper.getMainLooper())
    private val fmt = SimpleDateFormat("HH:mm:ss", Locale.getDefault())
    private val sb = StringBuilder()
    private var bound = false
    private var svcStarted = false
    private var audioIndex = 0L
    private val audioBuffers = mutableMapOf<Long, ByteArrayOutputStream>()
    private var savedFiles = mutableListOf<String>()

    // ===== IMU 状态 =====
    private var imuMode = "unknown"
    private var imuRunning = false
    private var imuStarting = false
    private var lastImuSampleAt = 0L

    // ===== 顺序推进状态 (连接后的逐步流程) =====
    private var step = 0  // 0=未开始, 1=等待系统信息, 2=等待录音数, 3=等待IMU, 4=已就绪

    // 事件流
    private val evSb = StringBuilder()
    private var eventSeq = 0
    private val MAX_EVENTS_LINES = 16

    // ===== Service 连接 =====
    private val conn = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, s: IBinder?) {
            val svc = (s as RingService.LocalBinder).service
            ble = svc.ble
            cmd = svc.cmd
            bound = true

            ble.onLog = { msg -> h.post { appendLog(msg) } }
            ble.onPacket = { p -> h.post { handlePacket(p) } }
            ble.onRawData = { hex -> h.post { b.tvRawData.text = "📦 $hex" } }
            ble.onScan = { _, mac -> h.post {
                b.tvScanInfo.text = "📡 ${ble.ringCount()}个 | 最强${ble.bestRssi()}dBm | ${mac.takeLast(5)}"
            } }

            svc.stateListener = { st -> h.post {
                when (st) {
                    RingBleManager.State.DISCONNECTED -> {
                        setStatus("● 未连接", 0xFFFF4444.toInt())
                        imuRunning = false; imuMode = "unknown"; imuStarting = false
                        lastImuSampleAt = 0L; step = 0
                        b.tvDeviceInfo.text = "未连接"; b.tvImuStatus.text = "IMU: 未开启"
                        b.tvImuStatus.setTextColor(0xFFFFA500.toInt())
                        b.btnScan.text = "连接"
                        b.tvMode.text = "等待连接"; b.tvMode.setTextColor(0xFF888888.toInt())
                        b.tvRawData.text = "等待数据..."
                    }
                    RingBleManager.State.CONNECTING -> {
                        setStatus("● 连接中...", 0xFFFFA500.toInt()); b.btnScan.text = "连接中..."
                    }
                    RingBleManager.State.SCANNING -> {
                        setStatus("● 扫描中...", 0xFFFFA500.toInt()); b.btnScan.text = "扫描中..."
                    }
                    RingBleManager.State.CONNECTED -> {
                        setStatus("● 蓝牙已连", 0xFFFFFF00.toInt()); b.btnScan.text = "断开"
                    }
                    RingBleManager.State.NUS_READY -> {
                        setStatus("● NUS就绪", 0xFF00FF00.toInt()); b.btnScan.text = "断开"
                        val mac = ble.connectedMac
                        b.tvDeviceInfo.text = "MAC: ${mac}\n状态: 已连接"
                        b.tvMode.text = "⏳ 正在获取信息..."; b.tvMode.setTextColor(0xFFFFA500.toInt())
                        appendLog("✅ NUS 就绪! 开始顺序流程")
                        // 【顺序流程第一步】获取系统信息
                        step = 1
                        cmd.getSystemInfo()
                        h.postDelayed(stepTimeout, 5000)
                    }
                }
            } }
            appendLog("指环调试 v4")
            appendLog("操作: 长按至🔴=IMU, 单击切换模式")
            b.btnScan.postDelayed({
                appendLog("自动扫描...")
                ensurePerms { ble.startScan() }
            }, 1500)
        }
        override fun onServiceDisconnected(name: ComponentName?) { bound = false }
    }

    // ===== 顺序流程超时 =====
    private val stepTimeout = Runnable {
        when (step) {
            1 -> appendLog("⏰ 系统信息超时 (指环无响应)")
            2 -> appendLog("⏰ 录音数超时")
            3 -> appendLog("⏰ IMU 超时, 可能处在录音模式")
            else -> {}
        }
        step = 4
        b.tvMode.text = "模式: 未知"; b.tvMode.setTextColor(0xFF888888.toInt())
    }

    // ===== 手动启动 IMU (替换自动轮询) =====
    private fun requestImu() {
        if (!ble.isConnected()) { appendLog("⛔ 未连接, 无法启动 IMU"); return }
        imuStarting = true
        step = 3
        b.tvImuStatus.text = "IMU: 请求中..."; b.tvImuStatus.setTextColor(0xFFFFA500.toInt())
        b.tvMode.text = "⏳ 请求 IMU..."; b.tvMode.setTextColor(0xFFFFA500.toInt())
        h.removeCallbacks(stepTimeout)
        h.postDelayed(stepTimeout, 5000)
        cmd.startSensor()
        appendLog("📤 请求 IMU 开启...")
    }

    private fun stopImu() {
        imuRunning = false; imuMode = "unknown"; imuStarting = false
        cmd.stopSensor()
        b.tvImuStatus.text = "IMU: 已停止"; b.tvImuStatus.setTextColor(0xFF888888.toInt())
        appendLog("IMU 已停止")
    }

    private fun updateModeDisplay() {
        when (imuMode) {
            "gesture" -> { b.tvMode.text = "🖐️ 手势·IMU可用"; b.tvMode.setTextColor(0xFF00FF00.toInt()) }
            "recording" -> { b.tvMode.text = "🎤 录音模式 (单击切换)"; b.tvMode.setTextColor(0xFFFFA500.toInt()) }
            else -> { b.tvMode.text = "模式: $imuMode"; b.tvMode.setTextColor(0xFF888888.toInt()) }
        }
    }

    override fun onResume() {
        super.onResume()
        if (!svcStarted) {
            val it = Intent(this, RingService::class.java)
            startForegroundService(it)
            bindService(it, conn, BIND_AUTO_CREATE)
            svcStarted = true
        }
    }

    @SuppressLint("MissingPermission")
    override fun onCreate(s: Bundle?) {
        super.onCreate(s)
        b = ActivityMainBinding.inflate(layoutInflater)
        setContentView(b.root)
        window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        b.btnScan.post { b.btnScan.requestFocus() }

        b.btnScan.setOnClickListener {
            if (ble.isConnected()) { ble.disconnect(); appendLog("手动断开") }
            else { ensurePerms { ble.restartScan(); appendLog("开始扫描...") } }
        }
        b.btnRefreshInfo.setOnClickListener { cmd.getSystemInfo(); appendLog("刷新信息") }
        b.btnSensorStart.setOnClickListener { requestImu() }
        b.btnSensorStop.setOnClickListener { stopImu() }
        b.btnAudioCount.setOnClickListener { cmd.getAudioCount() }
        b.btnIndexMinus.setOnClickListener { if (audioIndex > 0) audioIndex--; b.tvIndex.text = audioIndex.toString() }
        b.btnIndexPlus.setOnClickListener { audioIndex++; b.tvIndex.text = audioIndex.toString() }
        b.btnAudioExtract.setOnClickListener { cmd.startQuickExtract(audioIndex); appendLog("提取录音 #$audioIndex") }
        b.btnAudioClear.setOnClickListener { cmd.clearAudio(); appendLog("清空录音") }
        b.btnClear.setOnClickListener { sb.clear(); b.tvLog.text = ""; b.tvRawData.text = "等待数据..." }
    }

    // ===== 包处理 =====
    @SuppressLint("SetTextI18n")
    private fun handlePacket(p: RingProtocol.Packet) {
        val body = p.body
        try {
            when (p.command) {
                RingProtocol.Cmd.SYS_INFO_RSP -> {
                    h.removeCallbacks(stepTimeout)
                    val info = RingProtocol.parseSysInfo(body)
                    b.tvDeviceInfo.text = "MAC: ${ble.connectedMac}\n固件: ${info.firmware}\n存储: ${info.avail}/${info.total}B\n时间: ${info.systemTime}"
                    appendLog("✅ 系统信息已获取: ${info.firmware}")
                    // 【第二步】获取录音数
                    if (step == 1) {
                        step = 2
                        cmd.getAudioCount()
                        h.postDelayed(stepTimeout, 5000)
                    }
                }

                RingProtocol.Cmd.AUDIO_COUNT_RSP -> {
                    h.removeCallbacks(stepTimeout)
                    val c = RingProtocol.parseAudioCount(body)
                    b.tvDeviceInfo.append("\n录音数: ${c.count}")
                    appendLog("✅ 录音数: ${c.count}")
                    // 【第三步】自动请求 IMU
                    if (step == 2) {
                        appendLog("📋 数据流验证通过, 尝试开启 IMU...")
                        requestImu()
                    }
                }

                RingProtocol.Cmd.AUDIO_INFO_RSP -> {
                    val info = RingProtocol.parseAudioInfo(body)
                    appendLog("录音 #${info.index}: ${info.recordTime}s ${info.dataSize}B")
                }

                RingProtocol.Cmd.AUDIO_FRAME -> {
                    val frame = RingProtocol.parseAudioFrame(body)
                    var buf = audioBuffers[frame.index]
                    if (buf == null) { buf = ByteArrayOutputStream(); audioBuffers[frame.index] = buf }
                    buf.write(frame.data)
                    b.pbDownload.max = 100
                    b.pbDownload.progress = if (frame.isEnd) 100 else kotlin.math.min((buf.size() * 100 / (buf.size() + 2048)), 99)
                    b.pbDownload.visibility = android.view.View.VISIBLE
                    if (frame.isEnd) {
                        appendLog("📥 录音 #${frame.index} 完成: ${buf.size()}B")
                        saveAudioFile(frame.index, buf.toByteArray())
                        audioBuffers.remove(frame.index)
                        b.pbDownload.visibility = android.view.View.GONE
                        cmd.endExtract(frame.index)
                    }
                }

                // ===== IMU 开启响应 =====
                RingProtocol.Cmd.SENSOR_START_RSP -> {
                    imuStarting = false
                    h.removeCallbacks(stepTimeout)
                    val errCode = if (body.size >= 2) ((body[0].toInt() and 0xFF) shl 8) or (body[1].toInt() and 0xFF) else -1
                    when (errCode) {
                        0 -> {
                            imuRunning = true; imuMode = "gesture"
                            lastImuSampleAt = System.currentTimeMillis()
                            b.tvImuStatus.text = "IMU: ✅ 运行中"; b.tvImuStatus.setTextColor(0xFF00FF00.toInt())
                            updateModeDisplay()
                            step = 4
                            appendLog("✅ IMU 已开启! 指环在手势模式")
                        }
                        2 -> {
                            imuMode = "recording"
                            b.tvImuStatus.text = "IMU: 🎤 录音中 (单击切换)"; b.tvImuStatus.setTextColor(0xFFFFA500.toInt())
                            updateModeDisplay()
                            step = 4
                            appendLog("ℹ️ 指环在录音模式, 请单击按钮切换手势")
                            appendLog("   ➡ 切换到手势后再点「开启IMU」")
                        }
                        else -> {
                            appendLog("❌ IMU 开启失败: err=$errCode (${RingProtocol.errMsg(errCode)})")
                            b.tvImuStatus.text = "IMU: 失败($errCode)"; b.tvImuStatus.setTextColor(0xFFFF4444.toInt())
                            step = 4; imuMode = "error"
                            updateModeDisplay()
                        }
                    }
                }

                RingProtocol.Cmd.SENSOR_STOP_RSP -> {
                    imuRunning = false; imuMode = "unknown"
                    appendLog("IMU 已停止")
                }

                // ===== IMU 批量数据 =====
                RingProtocol.Cmd.SENSOR_DATA -> {
                    val batch = RingProtocol.parseSensorBatch(body)
                    lastImuSampleAt = System.currentTimeMillis()
                    if (batch.samples.isNotEmpty()) {
                        val s = batch.samples.last()
                        b.tvImuData.text = "a(${s.ax},${s.ay},${s.az}) g(${s.gx},${s.gy},${s.gz})"
                        b.orientView.update(s.ax, s.ay, s.az)
                    }
                    if (!imuRunning) {
                        imuRunning = true; imuMode = "gesture"
                        b.tvImuStatus.text = "IMU: ✅ 数据流中"; b.tvImuStatus.setTextColor(0xFF00FF00.toInt())
                        updateModeDisplay()
                    }
                }

                // ===== 事件 =====
                RingProtocol.Cmd.EVT_DOUBLE_TAP -> addEvent("双击", "IMU 双击")
                RingProtocol.Cmd.EVT_GESTURE -> {
                    val e = RingProtocol.parseGesture(body)
                    addEvent("手势", listOf(1 to "后旋", 2 to "前旋", 3 to "挥手").firstOrNull { it.first == e.gestureId }?.second ?: "未知(${e.gestureId})")
                }
                RingProtocol.Cmd.EVT_KEY_DOUBLE -> addEvent("按键双击", "双击按键")
                RingProtocol.Cmd.EVT_KEY_SINGLE -> {
                    addEvent("按键单击", "切换模式")
                    imuRunning = false; imuMode = "unknown"
                }

                // 校时
                RingProtocol.Cmd.TIME_REQ -> {
                    val t = RingProtocol.parseTime(body)
                    val now = System.currentTimeMillis() / 1000
                    cmd.answerTime(t.tsMs, now, now)
                }

                else -> appendLog("包 0x${String.format("%04X", p.command)} (${body.size}B)")
            }
        } catch (e: Exception) {
            appendLog("⚠ 解析 0x${String.format("%04X", p.command)}: ${e.message}")
        }
    }

    private fun addEvent(kind: String, detail: String) {
        eventSeq++
        val line = "${fmt.format(Date())} [${kind}] ${detail}"
        evSb.insert(0, line + "\n")
        val lines = evSb.toString().split('\n').filter { it.isNotBlank() }
        if (lines.size > MAX_EVENTS_LINES) {
            evSb.clear(); lines.take(MAX_EVENTS_LINES).forEach { evSb.append(it).append('\n') }
        }
        b.tvEvents.text = evSb.toString().trimEnd()
    }

    private fun setStatus(text: String, color: Int) { b.tvStatus.text = text; b.tvStatus.setTextColor(color) }

    private fun appendLog(msg: String) {
        val ts = fmt.format(Date())
        sb.append(ts).append(" ").append(msg).append('\n')
        val lines = sb.toString().split('\n').filter { it.isNotBlank() }
        b.tvLog.text = lines.takeLast(12).joinToString("\n")
    }

    private fun ensurePerms(block: () -> Unit) {
        val perms = if (Build.VERSION.SDK_INT >= 31)
            arrayOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT, Manifest.permission.ACCESS_FINE_LOCATION)
        else arrayOf(Manifest.permission.ACCESS_FINE_LOCATION)
        val need = perms.filter { ActivityCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }
        if (need.isEmpty()) block() else ActivityCompat.requestPermissions(this, need.toTypedArray(), 1)
    }
    override fun onRequestPermissionsResult(rc: Int, p: Array<String>, g: IntArray) {
        super.onRequestPermissionsResult(rc, p, g)
        if (g.isNotEmpty() && g.all { it == PackageManager.PERMISSION_GRANTED }) ble.startScan()
        else appendLog("⛔ 权限被拒绝")
    }

    private fun saveAudioFile(index: Long, data: ByteArray) {
        try {
            val dir = File(filesDir, "ring_audio")
            if (!dir.exists()) dir.mkdirs()
            val file = File(dir, "recording_${index}.bin")
            FileOutputStream(file).use { it.write(data) }
            val label = "录音 #$index (${data.size}B)"
            savedFiles.add(label)
            appendLog("✓ $label")
            b.tvDeviceInfo.append("\n${label}")
        } catch (e: Exception) { appendLog("保存失败: ${e.message}") }
    }

    override fun onDestroy() {
        super.onDestroy()
        if (bound) {
            try {
                ble.onLog = null; ble.onPacket = null; ble.onScan = null; ble.onRawData = null
            } catch (_: Exception) {}
            unbindService(conn); bound = false
        }
    }
}
