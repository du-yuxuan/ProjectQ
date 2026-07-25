package com.unfreeze.ringdebug.proto

import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Zilo Whisper 指环 v4 通信协议 (NUS BLE)。
 * 依据: 参考资料/ring_sound_SDK/{protocol.md, ring_sound.py, ring_sound_use.md}
 *
 * 协议头固定 11 字节，大端序：
 *   magic u8(0x3F) | version u16(4) | command u16 | body_length u32 | body_crc u16 | body...
 * CRC16 覆盖 body，初始 0xFFFF，算法与 ring_sound.py crc16_compute() 一致。
 */
object RingProtocol {
    const val HEADER_MAGIC: Byte = 0x3F
    const val PROTOCOL_VERSION = 4
    const val HEADER_SIZE = 11
    const val MAX_BODY_LENGTH = 4096

    // NUS 标准 UART Service (与 ring_sound.py 完全一致)
    const val NUS_SERVICE = "6e400001-b5a3-f393-e0a9-e50e24dcca9e"
    const val NUS_TX = "6e400003-b5a3-f393-e0a9-e50e24dcca9e"  // 戒指 -> 主机 (notify)
    const val NUS_RX = "6e400002-b5a3-f393-e0a9-e50e24dcca9e"  // 主机 -> 戒指 (write)

    // ===== 命令号 =====
    object Cmd {
        // 系统
        const val SYS_INFO_REQ = 0x0101; const val SYS_INFO_RSP = 0x0102
        // 日志
        const val LOG_STORAGE_REQ = 0x0301; const val LOG_STORAGE_RSP = 0x0302
        const val LOG_DATA_REQ = 0x0303;   const val LOG_DATA_RSP = 0x0304
        // 校时
        const val TIME_REQ = 0x0401; const val TIME_RSP = 0x0402
        // 录音
        const val AUDIO_COUNT_REQ = 0x0501; const val AUDIO_COUNT_RSP = 0x0502
        const val AUDIO_EXTRACT_REQ = 0x0503; const val AUDIO_INFO_RSP = 0x0504
        const val AUDIO_FRAME = 0x0505
        const val AUDIO_READ_OFFSET_REQ = 0x0506
        const val AUDIO_END_REQ = 0x0507; const val AUDIO_END_RSP = 0x0508
        const val AUDIO_QUICK_REQ = 0x0509
        const val AUDIO_CLEAR_REQ = 0x050B; const val AUDIO_CLEAR_RSP = 0x050C
        // IMU
        const val SENSOR_START_REQ = 0x0601; const val SENSOR_START_RSP = 0x0602
        const val SENSOR_STOP_REQ = 0x0603;  const val SENSOR_STOP_RSP = 0x0604
        const val SENSOR_DATA = 0x0605
        // 事件
        const val EVT_DOUBLE_TAP = 0x0701
        const val EVT_GESTURE = 0x0702
        const val EVT_KEY_DOUBLE = 0x0703
        const val EVT_KEY_SINGLE = 0x0704
    }

    /** CRC16 (与 ring_sound.py crc16_compute 一致，初始 0xFFFF) */
    fun crc16(data: ByteArray, initial: Int = 0xFFFF): Int {
        var crc = initial and 0xFFFF
        for (b in data) {
            val byte = b.toInt() and 0xFF
            crc = ((crc ushr 8) or ((crc shl 8) and 0xFFFF)) and 0xFFFF
            crc = crc xor byte
            crc = crc and 0xFFFF
            crc = crc xor ((crc and 0xFF) ushr 4)
            crc = crc and 0xFFFF
            crc = (crc xor (crc shl 12)) and 0xFFFF
            crc = (crc xor ((crc and 0xFF) shl 5)) and 0xFFFF
        }
        return crc and 0xFFFF
    }

    /** 编码一个 v4 协议包 (header + body) */
    fun encode(command: Int, body: ByteArray = ByteArray(0)): ByteArray {
        val bodyCrc = if (body.isNotEmpty()) crc16(body) else 0
        val buf = ByteBuffer.allocate(HEADER_SIZE + body.size).order(ByteOrder.BIG_ENDIAN)
        buf.put(HEADER_MAGIC)
        buf.putShort(PROTOCOL_VERSION.toShort())
        buf.putShort((command and 0xFFFF).toShort())
        buf.putInt(body.size)
        buf.putShort(bodyCrc.toShort())
        buf.put(body)
        return buf.array()
    }

    /** 解析后的包 */
    data class Packet(val command: Int, val body: ByteArray, val version: Int, val bodyCrc: Int)

    /** 解析一个完整包 (不含流重组) */
    fun decode(data: ByteArray): Packet {
        require(data.size >= HEADER_SIZE) { "Packet too short: ${data.size}" }
        val bb = ByteBuffer.wrap(data, 0, HEADER_SIZE).order(ByteOrder.BIG_ENDIAN)
        val magic = bb.get()
        require(magic == HEADER_MAGIC) { "Invalid magic: 0x${String.format("%02X", magic)}" }
        val version = bb.short.toInt() and 0xFFFF
        val command = bb.short.toInt() and 0xFFFF
        val bodyLen = bb.int
        require(bodyLen <= MAX_BODY_LENGTH) { "Body too large: $bodyLen" }
        require(data.size >= HEADER_SIZE + bodyLen) { "Incomplete: need ${HEADER_SIZE + bodyLen}, got ${data.size}" }
        val bodyCrc = bb.short.toInt() and 0xFFFF
        val body = data.copyOfRange(HEADER_SIZE, HEADER_SIZE + bodyLen)
        if (bodyLen > 0) {
            val actual = crc16(body)
            require(actual == bodyCrc) { "CRC mismatch: exp 0x${String.format("%04X", bodyCrc)}, got 0x${String.format("%04X", actual)}" }
        }
        return Packet(command, body, version, bodyCrc)
    }

    /** 从 BLE 通知片段重组完整包 (与 ring_sound.py PacketStream 等价) */
    class PacketStream {
        private val buffer = ByteArray(8192)
        private var size = 0

        fun feed(chunk: ByteArray): List<Packet> {
            val out = ArrayList<Packet>()
            chunk.forEach { b ->
                if (size < buffer.size) { buffer[size++] = b } else { android.util.Log.w("PacketStream", "buffer overflow, resetting"); size = 0 }
            }
            while (true) {
                var magicIdx = -1
                for (i in 0 until size) { if (buffer[i] == HEADER_MAGIC) { magicIdx = i; break } }
                if (magicIdx < 0) { size = 0; break }
                if (magicIdx > 0) {
                    android.util.Log.w("PacketStream", "skip ${magicIdx} non-magic bytes")
                    shiftLeft(magicIdx)
                }
                if (size < HEADER_SIZE) break
                val bodyLen = peekInt(5)
                if (bodyLen > MAX_BODY_LENGTH) {
                    android.util.Log.w("PacketStream", "bad bodyLen=$bodyLen, resetting")
                    size = 0; break
                }
                val packetLen = HEADER_SIZE + bodyLen
                if (size < packetLen) break
                val pktBytes = buffer.copyOfRange(0, packetLen)
                try {
                    val pkt = decode(pktBytes)
                    out.add(pkt)
                } catch (e: Exception) {
                    android.util.Log.w("PacketStream", "decode failed: ${e.message}")
                }
                shiftLeft(packetLen)
            }
            return out
        }
        private fun shiftLeft(n: Int) { for (i in n until size) { buffer[i - n] = buffer[i] }; size -= n }
        private fun peekInt(off: Int): Int {
            return ((buffer[off].toInt() and 0xFF) shl 24) or ((buffer[off+1].toInt() and 0xFF) shl 16) or
                   ((buffer[off+2].toInt() and 0xFF) shl 8) or (buffer[off+3].toInt() and 0xFF)
        }
    }

    // ===== 大端序读取工具 =====
    // 错误码 (来自 ring-sound-web constants)
    val ERROR_MESSAGES = mapOf(
        0 to "成功", 1 to "未知错误", 2 to "设备忙碌(录音模式,需切手势模式)",
        3 to "文件不存在", 4 to "命令组不存在", 5 to "命令不存在",
        6 to "操作超时", 7 to "参数无效", 8 to "通信错误"
    )
    fun errMsg(code: Int) = ERROR_MESSAGES[code] ?: "未知错误($code)"

    class Reader(val data: ByteArray) {
        var pos = 0
        val remaining get() = data.size - pos
        fun u16() = ((data[pos++].toInt() and 0xFF) shl 8 or (data[pos++].toInt() and 0xFF))
        fun i16(): Int { val v = u16(); return if (v >= 0x8000) v - 0x10000 else v }
        fun u32(): Long = ((data[pos++].toInt() and 0xFF).toLong() shl 24) or ((data[pos++].toInt() and 0xFF).toLong() shl 16) or ((data[pos++].toInt() and 0xFF).toLong() shl 8) or (data[pos++].toInt() and 0xFF).toLong()
        fun u8() = data[pos++].toInt() and 0xFF
        fun ascii(len: Int): String { val s = String(data, pos, len); pos += len; return s.trimEnd('\u0000') }
    }

    // ===== 解析结果数据类 =====
    data class SysInfo(val firmware: String, val systemTime: Long, val total: Long, val avail: Long)
    data class AudioCount(val count: Long)
    data class AudioInfo(val index: Long, val recordTime: Long, val dataSize: Long)
    data class AudioFrame(val index: Long, val offset: Long, val size: Int, val isEnd: Boolean, val data: ByteArray)
    data class SensorStart(val sampleRate: Int, val accelG: Int, val gyroDps: Int)
    data class SensorSample(val tsMs: Long, val ax: Int, val ay: Int, val az: Int, val gx: Int, val gy: Int, val gz: Int)
    data class SensorBatch(val seq: Long, val count: Int, val samples: List<SensorSample>)
    data class GestureEvent(val tsMs: Long, val gestureId: Int)
    data class TimeEvent(val tsMs: Long)

    // ===== 解析函数 =====
    fun parseSysInfo(body: ByteArray): SysInfo {
        val r = Reader(body); val err = r.u16()
        require(err == 0) { "err=$err" }
        val fwLen = r.u16(); val fw = r.ascii(fwLen)
        val t = r.u32(); val total = r.u32(); val avail = r.u32()
        return SysInfo(fw, t, total, avail)
    }
    fun parseAudioCount(body: ByteArray): AudioCount { val r = Reader(body); require(r.u16()==0){"err"}; return AudioCount(r.u32()) }
    fun parseAudioInfo(body: ByteArray): AudioInfo { val r = Reader(body); require(r.u16()==0){"err"}; val i=r.u32(); val rt=r.u32(); val ds=r.u32(); return AudioInfo(i, rt, ds) }
    fun parseAudioFrame(body: ByteArray): AudioFrame { val r = Reader(body); val err=r.u16(); val i=r.u32(); val off=r.u32(); val sz=r.u32().toInt(); val end=r.u8(); val d=ByteArray(sz); System.arraycopy(body, r.pos, d, 0, sz); return AudioFrame(i, off, sz, end==1, d) }
    fun parseSensorStart(body: ByteArray): SensorStart { val r = Reader(body); require(r.u16()==0){"err"}; return SensorStart(r.u16(), r.u16(), r.u16()) }
    fun parseSensorBatch(body: ByteArray): SensorBatch { val r = Reader(body); require(r.u16()==0){"err"}; val seq=r.u32(); val cnt=r.u16(); val ss=r.u16(); val samples=ArrayList<SensorSample>(); repeat(cnt){ val ts=r.u32(); val ax=r.i16(); val ay=r.i16(); val az=r.i16(); val gx=r.i16(); val gy=r.i16(); val gz=r.i16(); samples.add(SensorSample(ts,ax,ay,az,gx,gy,gz)) }; return SensorBatch(seq, cnt, samples) }
    fun parseGesture(body: ByteArray): GestureEvent { val r = Reader(body); return GestureEvent(r.u32(), r.u8()) }
    fun parseTime(body: ByteArray): TimeEvent { val r = Reader(body); return TimeEvent(r.u32()) }
}
