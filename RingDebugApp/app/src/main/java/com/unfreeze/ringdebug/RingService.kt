package com.unfreeze.ringdebug

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.Binder
import android.util.Log
import android.os.Build
import androidx.core.app.NotificationCompat
import com.unfreeze.ringdebug.ble.RingBleManager
import com.unfreeze.ringdebug.cmd.RingCommander

/**
 * 前台服务: 持有指环 BLE 连接, 切后台/灭屏不断连。
 * 连接成功 -> startForeground(通知保活); 断开 -> 取消前台。
 */
class RingService : Service() {
    lateinit var ble: RingBleManager
        private set
    lateinit var cmd: RingCommander
        private set
    var stateListener: ((RingBleManager.State) -> Unit)? = null
    private var isFg = false

    inner class LocalBinder : Binder() { val service: RingService get() = this@RingService }
    private val binder = LocalBinder()

    override fun onCreate() {
        super.onCreate()
        Log.i("RingSvc", "onCreate")
        ble = RingBleManager(this)
        cmd = RingCommander(ble)
        // 连接状态: 连上则前台保活, 断开则取消; 同时回调给 Activity 更新 UI
        ble.onState = { st ->
            when (st) {
                RingBleManager.State.CONNECTED -> startForegroundSvc()
                RingBleManager.State.DISCONNECTED -> stopForegroundSvc()
                else -> {}
            }
            stateListener?.invoke(st)
        }
        startForegroundSvc()  // 服务创建即前台(经 startForegroundService 创建时合法)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        Log.i("RingSvc", "onStartCommand")
        startForegroundSvc()
        return START_STICKY
    }

    override fun onBind(intent: Intent?) = binder

    private fun startForegroundSvc() {
        Log.i("RingSvc", "startForegroundSvc isFg=$isFg")
        if (isFg) return
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= 26) {
            nm.createNotificationChannel(NotificationChannel(CH, "指环连接", NotificationManager.IMPORTANCE_LOW))
        }
        val notif: Notification = NotificationCompat.Builder(this, CH)
            .setContentTitle("指环调试")
            .setContentText("运行中 · 后台保持连接")
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setOngoing(true)
            .build()
        try {
            startForeground(1, notif)
            isFg = true
            Log.i("RingSvc", "startForeground OK, isFg=true")
        } catch (e: Exception) {
            Log.e("RingSvc", "startForeground FAILED: ${e.javaClass.simpleName}: ${e.message}")
        }
    }

    private fun stopForegroundSvc() {
        if (!isFg) return
        stopForeground(STOP_FOREGROUND_REMOVE)
        isFg = false
    }

    override fun onDestroy() {
        super.onDestroy()
        stopForegroundSvc()
        ble.disconnect()
    }

    companion object { private const val CH = "ring_conn" }
}
