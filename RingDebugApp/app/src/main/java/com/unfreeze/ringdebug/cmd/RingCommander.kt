package com.unfreeze.ringdebug.cmd

import com.unfreeze.ringdebug.ble.RingBleManager
import com.unfreeze.ringdebug.proto.RingProtocol
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * 高层命令封装: 把 v4 协议命令包装成易用函数，处理请求/响应配对。
 * 实现指环全部功能: 系统信息/日志/校时/录音(计数/提取/下载/清空)/IMU(开/关/数据)/事件(双击/手势/按键)。
 */
class RingCommander(val ble: RingBleManager) {
    private val log get() = ble

    // ===== 系统信息 0x0101 =====
    fun getSystemInfo() { ble.send(RingProtocol.Cmd.SYS_INFO_REQ) }

    // ===== 日志 0x0301/0x0303 =====
    fun getLogStorage() { ble.send(RingProtocol.Cmd.LOG_STORAGE_REQ) }
    fun readLogChunk(offset: Long, length: Int) {
        val b = ByteBuffer.allocate(8).order(ByteOrder.BIG_ENDIAN)
        b.putInt(offset.toInt()); b.putInt(length)
        ble.send(RingProtocol.Cmd.LOG_DATA_REQ, b.array())
    }

    // ===== 校时 0x0401: 戒指请求，主机应答 0x0402 =====
    fun answerTime(t1: Long, t2: Long, t3: Long) {
        // 请求体: T1 u32(戒指发起) / T2 u32(主机收到) / T3 u32(主机发送)
        // 这里发的是应答体: T1 T2 T3
        val b = ByteBuffer.allocate(12).order(ByteOrder.BIG_ENDIAN)
        b.putInt(t1.toInt()); b.putInt(t2.toInt()); b.putInt(t3.toInt())
        ble.send(RingProtocol.Cmd.TIME_RSP, b.array())
    }

    // ===== 录音 =====
    fun getAudioCount() { ble.send(RingProtocol.Cmd.AUDIO_COUNT_REQ) }
    fun startQuickExtract(index: Long) {
        // 0x0503/0x0509 包体: error_code u16 + file_index u32
        val body = ByteBuffer.allocate(6).order(ByteOrder.BIG_ENDIAN).putShort(0).putInt(index.toInt()).array()
        ble.send(RingProtocol.Cmd.AUDIO_QUICK_REQ, body)
    }
    fun endExtract(index: Long) {
        val body = ByteBuffer.allocate(6).order(ByteOrder.BIG_ENDIAN).putShort(0).putInt(index.toInt()).array()
        ble.send(RingProtocol.Cmd.AUDIO_END_REQ, body)
    }
    fun clearAudio() { ble.send(RingProtocol.Cmd.AUDIO_CLEAR_REQ) }

    // ===== IMU =====
    fun startSensor() { ble.send(RingProtocol.Cmd.SENSOR_START_REQ) }
    fun stopSensor() { ble.send(RingProtocol.Cmd.SENSOR_STOP_REQ) }
}
