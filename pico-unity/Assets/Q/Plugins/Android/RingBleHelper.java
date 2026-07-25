package com.q.cue.ble;

import android.annotation.SuppressLint;
import android.bluetooth.*;
import android.bluetooth.le.*;
import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

// ============================================================
// RingBleHelper — Zilo 指环 BLE 管理器（Unity 原生插件）
//
// 功能：BLE 扫描 → GATT 连接 → NUS 服务发现 → TX 通知启用
//       → v4 协议包重组 → 回调 C#
//
// NUS 标准 UART Service:
//   Service: 6e400001-b5a3-f393-e0a9-e50e24dcca9e
//   TX (notify, 指环→主机): 6e400003-...
//   RX (write, 主机→指环): 6e400002-...
//
// v4 协议: magic(0x3F) | version(u16=4) | command(u16) | body_len(u32) | body_crc(u16) | body
// ============================================================

public class RingBleHelper {
    private static final String TAG = "RingBLE";
    private static final String NUS_SERVICE = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
    private static final String NUS_TX      = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";
    private static final String NUS_RX      = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
    private static final String CCCD_UUID   = "00002902-0000-1000-8000-00805f9b34fb";
    private static final byte HEADER_MAGIC = 0x3F;
    private static final int PROTOCOL_VERSION = 4;
    private static final int HEADER_SIZE = 11;
    private static final int MAX_BODY = 4096;

    public interface Callback {
        void onStateChanged(int state);       // 0=DISCONNECTED 1=SCANNING 2=CONNECTING 3=CONNECTED 4=NUS_READY
        void onScanResult(String name, String mac, int rssi);
        void onPacket(int command, byte[] body);
        void onLog(String message);
    }

    private final Context ctx;
    private final Callback cb;
    private final Handler h = new Handler(Looper.getMainLooper());
    private final BluetoothManager bm;
    private final PacketStream stream = new PacketStream();

    private BluetoothGatt gatt;
    private BluetoothGattCharacteristic txChar, rxChar;
    private boolean notifReady = false;
    private boolean scanning = false;
    private boolean connecting = false;
    private final ArrayDeque<byte[]> writeQueue = new ArrayDeque<>();
    private boolean writing = false;

    public RingBleHelper(Context context, Callback callback) {
        this.ctx = context;
        this.cb = callback;
        this.bm = (BluetoothManager) ctx.getSystemService(Context.BLUETOOTH_SERVICE);
    }

    // ===== 公共 API =====

    @SuppressLint("MissingPermission")
    public void startScan() {
        if (scanning) { log("已在扫描"); return; }
        BluetoothAdapter adapter = bm.getAdapter();
        if (adapter == null || !adapter.isEnabled()) { log("蓝牙未开启"); return; }
        BluetoothLeScanner sc = adapter.getBluetoothLeScanner();
        if (sc == null) { log("无 BLE scanner"); return; }
        scanning = true;
        cb.onStateChanged(1);
        log("开始扫描...");
        try {
            sc.startScan(null, new ScanSettings.Builder()
                .setScanMode(ScanSettings.SCAN_MODE_BALANCED).build(), scanCb);
        } catch (Exception e) { log("扫描失败: " + e.getMessage()); scanning = false; }
        h.postDelayed(this::checkScanResults, 10000);
    }

    @SuppressLint("MissingPermission")
    public void connectByMac(String mac) {
        log("连接 " + mac);
        closeGatt();
        BluetoothAdapter adapter = bm.getAdapter();
        if (adapter == null) return;
        BluetoothDevice dev = adapter.getRemoteDevice(mac);
        if (dev == null) { log("无此设备"); return; }
        connecting = true;
        cb.onStateChanged(2);
        gatt = dev.connectGatt(ctx, false, gattCb, BluetoothDevice.TRANSPORT_LE);
    }

    @SuppressLint("MissingPermission")
    public void disconnect() {
        log("断开");
        h.removeCallbacksAndMessages(null);
        stopScanInner();
        closeGatt();
        scanning = false; connecting = false; notifReady = false;
        cb.onStateChanged(0);
    }

    public boolean isConnected() { return gatt != null && notifReady; }

    @SuppressLint("MissingPermission")
    public void send(int command, byte[] body) {
        if (!isConnected()) { log("未连接"); return; }
        byte[] bytes = encode(command, body != null ? body : new byte[0]);
        int mtu = 20;
        for (int off = 0; off < bytes.length; off += mtu) {
            int len = Math.min(mtu, bytes.length - off);
            byte[] chunk = new byte[len];
            System.arraycopy(bytes, off, chunk, 0, len);
            writeQueue.add(chunk);
        }
        flushQueue();
    }

    // ===== 扫描 =====

    private void checkScanResults() {
        if (connecting || isConnected()) return;
        log("未找到指环，继续扫描...");
        startScan();
    }

    private final ScanCallback scanCb = new ScanCallback() {
        @SuppressLint("MissingPermission")
        @Override
        public void onScanResult(int callbackType, ScanResult result) {
            BluetoothDevice dev = result.getDevice();
            if (dev == null) return;
            String name = result.getScanRecord() != null ? result.getScanRecord().getDeviceName() : "";
            String mac = dev.getAddress();
            if (mac == null) return;
            boolean isRing = (name != null && name.toLowerCase().contains("ring"))
                          || (name != null && name.toLowerCase().contains("zilo"));
            boolean hasNus = result.getScanRecord() != null && result.getScanRecord().getServiceUuids() != null
                          && result.getScanRecord().getServiceUuids().stream()
                             .anyMatch(p -> p.getUuid().toString().equalsIgnoreCase(NUS_SERVICE));
            if (!isRing && !hasNus) return;
            log("发现指环: " + (name != null ? name : "?") + " [" + mac.substring(mac.length()-5) + "] RSSI=" + result.getRssi());
            cb.onScanResult(name != null ? name : "", mac, result.getRssi());
            // 自动连接最强信号
            if (!connecting && !isConnected()) {
                connecting = true;
                stopScanInner();
                connectByMac(mac);
            }
        }
        @Override
        public void onScanFailed(int errorCode) { log("扫描失败 code=" + errorCode); scanning = false; }
    };

    // ===== GATT =====

    private final BluetoothGattCallback gattCb = new BluetoothGattCallback() {
        @SuppressLint("MissingPermission")
        @Override
        public void onConnectionStateChange(BluetoothGatt g, int status, int newState) {
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                cb.onStateChanged(3);
                log("蓝牙已连接，发现服务...");
                g.discoverServices();
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                notifReady = false; connecting = false;
                cb.onStateChanged(0);
                log("断开 (status=" + status + ")");
                if (status != 0) {
                    closeGatt();
                    h.postDelayed(() -> { if (!scanning) startScan(); }, 3000);
                }
            }
        }

        @SuppressLint("MissingPermission")
        @Override
        public void onServicesDiscovered(BluetoothGatt g, int status) {
            if (status != BluetoothGatt.GATT_SUCCESS) { log("发现服务失败 status=" + status); return; }
            BluetoothGattService svc = g.getService(UUID.fromString(NUS_SERVICE));
            if (svc == null) { log("未找到 NUS 服务"); g.disconnect(); return; }
            txChar = svc.getCharacteristic(UUID.fromString(NUS_TX));
            rxChar = svc.getCharacteristic(UUID.fromString(NUS_RX));
            if (txChar == null || rxChar == null) { log("未找到 NUS 特性"); return; }
            log("NUS 服务已找到，启用通知...");
            g.setCharacteristicNotification(txChar, true);
            BluetoothGattDescriptor cccd = txChar.getDescriptor(UUID.fromString(CCCD_UUID));
            if (cccd == null) { log("无 CCCD 描述符"); return; }
            cccd.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
            g.writeDescriptor(cccd);
        }

        @SuppressLint("MissingPermission")
        @Override
        public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor d, int status) {
            if (status == BluetoothGatt.GATT_SUCCESS) {
                g.requestMtu(512);
                log("请求 MTU 512...");
            } else { log("CCCD 写入失败 status=" + status); }
        }

        @Override
        public void onMtuChanged(BluetoothGatt g, int mtu, int status) {
            notifReady = true; connecting = false;
            cb.onStateChanged(4);
            log("NUS 就绪! MTU=" + mtu);
            flushQueue();
        }

        @SuppressLint("MissingPermission")
        @Override
        public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
            byte[] data = c.getValue();
            if (data == null) return;
            log("RX " + data.length + "B");
            List<int[]> packets = stream.feed(data); // returns list of [command] → 但我们需要 body
            // PacketStream.feed 返回 Packet 对象列表
            for (Packet pkt : stream.feedPackets(data)) {
                log("  → 0x" + String.format("%04X", pkt.command) + " (" + pkt.body.length + "B)");
                cb.onPacket(pkt.command, pkt.body);
            }
        }

        @SuppressLint("MissingPermission")
        @Override
        public void onCharacteristicWrite(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
            writing = false;
            if (status != BluetoothGatt.GATT_SUCCESS) log("写入失败 status=" + status);
            flushQueue();
        }
    };

    @SuppressLint("MissingPermission")
    private void flushQueue() {
        if (writing || writeQueue.isEmpty() || rxChar == null || gatt == null) return;
        byte[] chunk = writeQueue.poll();
        try {
            rxChar.setValue(chunk);
            rxChar.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE);
            gatt.writeCharacteristic(rxChar);
            writing = true;
        } catch (Exception e) { log("写入异常: " + e.getMessage()); writing = false; }
    }

    @SuppressLint("MissingPermission")
    private void stopScanInner() {
        try { bm.getAdapter().getBluetoothLeScanner().stopScan(scanCb); } catch (Exception e) {}
    }

    @SuppressLint("MissingPermission")
    private void closeGatt() {
        try { if (gatt != null) { gatt.disconnect(); gatt.close(); } } catch (Exception e) {}
        gatt = null; txChar = null; rxChar = null;
        notifReady = false; writing = false; writeQueue.clear();
    }

    private void log(String msg) { Log.i(TAG, msg); cb.onLog(msg); }

    // ===== v4 协议 =====

    private static class Packet { int command; byte[] body; int version; int crc; }

    private static class PacketStream {
        private final byte[] buf = new byte[8192];
        private int size = 0;

        List<Packet> feedPackets(byte[] chunk) {
            List<Packet> out = new ArrayList<>();
            for (byte b : chunk) { if (size < buf.length) buf[size++] = b; else size = 0; }
            while (true) {
                int magicIdx = -1;
                for (int i = 0; i < size; i++) { if (buf[i] == HEADER_MAGIC) { magicIdx = i; break; } }
                if (magicIdx < 0) { size = 0; break; }
                if (magicIdx > 0) { shiftLeft(magicIdx); }
                if (size < HEADER_SIZE) break;
                int bodyLen = peekInt(5);
                if (bodyLen > MAX_BODY) { size = 0; break; }
                int pktLen = HEADER_SIZE + bodyLen;
                if (size < pktLen) break;
                try {
                    Packet pkt = decode(buf, pktLen);
                    if (pkt != null) out.add(pkt);
                } catch (Exception e) { Log.w(TAG, "decode: " + e.getMessage()); }
                shiftLeft(pktLen);
            }
            return out;
        }

        // 兼容旧接口（返回空 list）
        List<int[]> feed(byte[] chunk) { return new ArrayList<>(); }

        private void shiftLeft(int n) { for (int i = n; i < size; i++) buf[i-n] = buf[i]; size -= n; }
        private int peekInt(int off) {
            return ((buf[off] & 0xFF) << 24) | ((buf[off+1] & 0xFF) << 16) | ((buf[off+2] & 0xFF) << 8) | (buf[off+3] & 0xFF);
        }
    }

    private static Packet decode(byte[] data, int len) {
        if (len < HEADER_SIZE) return null;
        ByteBuffer bb = ByteBuffer.wrap(data, 0, HEADER_SIZE).order(ByteOrder.BIG_ENDIAN);
        byte magic = bb.get();
        if (magic != HEADER_MAGIC) return null;
        int version = bb.getShort() & 0xFFFF;
        int command = bb.getShort() & 0xFFFF;
        int bodyLen = bb.getInt();
        if (bodyLen > MAX_BODY || len < HEADER_SIZE + bodyLen) return null;
        int bodyCrc = bb.getShort() & 0xFFFF;
        byte[] body = new byte[bodyLen];
        System.arraycopy(data, HEADER_SIZE, body, 0, bodyLen);
        if (bodyLen > 0) {
            int actual = crc16(body);
            if (actual != bodyCrc) { Log.w(TAG, "CRC mismatch"); return null; }
        }
        Packet pkt = new Packet();
        pkt.command = command; pkt.body = body; pkt.version = version; pkt.crc = bodyCrc;
        return pkt;
    }

    public static byte[] encode(int command, byte[] body) {
        int bodyCrc = body.length > 0 ? crc16(body) : 0;
        ByteBuffer buf = ByteBuffer.allocate(HEADER_SIZE + body.length).order(ByteOrder.BIG_ENDIAN);
        buf.put(HEADER_MAGIC);
        buf.putShort((short) PROTOCOL_VERSION);
        buf.putShort((short) (command & 0xFFFF));
        buf.putInt(body.length);
        buf.putShort((short) bodyCrc);
        buf.put(body);
        return buf.array();
    }

    public static int crc16(byte[] data) {
        int crc = 0xFFFF;
        for (byte b : data) {
            int byt = b & 0xFF;
            crc = ((crc >>> 8) | ((crc << 8) & 0xFFFF)) & 0xFFFF;
            crc ^= byt; crc &= 0xFFFF;
            crc ^= (crc & 0xFF) >>> 4; crc &= 0xFFFF;
            crc ^= (crc << 12) & 0xFFFF;
            crc ^= ((crc & 0xFF) << 5) & 0xFFFF;
        }
        return crc & 0xFFFF;
    }
}
