package com.unfreeze.ringdebug

import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.view.View

/** 3D 指向视图: 根据加速度计数据画一个 3D 箭头指向重力方向 */
class OrientationView @JvmOverloads constructor(ctx: Context, attrs: AttributeSet? = null) : View(ctx, attrs) {
    private var ax = 0f; private var ay = 0f; private var az = 0f
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val arrow = Paint(Paint.ANTI_ALIAS_FLAG)
    private val circleP = Paint(Paint.ANTI_ALIAS_FLAG)
    private val textP = Paint(Paint.ANTI_ALIAS_FLAG)

    init {
        paint.color = Color.parseColor("#222233"); paint.style = Paint.Style.FILL
        arrow.color = Color.parseColor("#00FF88"); arrow.style = Paint.Style.FILL; arrow.strokeWidth = 6f
        circleP.color = Color.parseColor("#444466"); circleP.style = Paint.Style.STROKE; circleP.strokeWidth = 3f
        textP.color = Color.parseColor("#00FF88"); textP.textSize = 28f; textP.typeface = Typeface.MONOSPACE
    }

    fun update(ax: Int, ay: Int, az: Int) {
        this.ax = ax.toFloat(); this.ay = ay.toFloat(); this.az = az.toFloat()
        postInvalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cx = width / 2f; val cy = height / 2f; val r = minOf(width, height) / 2f - 20f
        canvas.drawColor(Color.BLACK)
        // 外圆 (3D 球感)
        canvas.drawCircle(cx, cy, r, circleP)
        canvas.drawCircle(cx, cy, r * 0.7f, circleP)
        canvas.drawCircle(cx, cy, r * 0.4f, circleP)
        // 十字线
        paint.color = Color.parseColor("#333344"); paint.strokeWidth = 2f
        canvas.drawLine(cx - r, cy, cx + r, cy, paint)
        canvas.drawLine(cx, cy - r, cx, cy + r, paint)
        // 加速度向量 -> 2D 投影 (xy 平面) + z 影响箭头长度/颜色
        val mag = Math.sqrt((ax * ax + ay * ay + az * az).toDouble()).toFloat()
        val scale = if (mag > 0) r * 0.8f / mag else 0f
        val endX = cx + ax * scale
        val endY = cy + ay * scale
        // z 轴: 正(朝上)=绿色, 负(朝下)=红色
        arrow.color = if (az >= 0) Color.parseColor("#00FF88") else Color.parseColor("#FF6600")
        // 箭头杆
        arrow.style = Paint.Style.STROKE; arrow.strokeWidth = 8f
        canvas.drawLine(cx, cy, endX, endY, arrow)
        // 箭头头
        arrow.style = Paint.Style.FILL
        val angle = Math.atan2((endY - cy).toDouble(), (endX - cx).toDouble()).toFloat()
        val headLen = 25f
        canvas.drawPath(Path().apply {
            moveTo(endX, endY)
            lineTo(endX - headLen * Math.cos((angle - 0.4).toDouble()).toFloat(),
                   endY - headLen * Math.sin((angle - 0.4).toDouble()).toFloat())
            lineTo(endX - headLen * Math.cos((angle + 0.4).toDouble()).toFloat(),
                   endY - headLen * Math.sin((angle + 0.4).toDouble()).toFloat())
            close()
        }, arrow)
        // 数值
        textP.textSize = 22f
        canvas.drawText("a(${ax.toInt()},${ay.toInt()},${az.toInt()})", 10f, height - 10f, textP)
        textP.textSize = 16f; textP.color = Color.parseColor("#888888")
        canvas.drawText("|a|=${mag.toInt()}", 10f, height - 35f, textP)
        textP.color = Color.parseColor("#00FF88")
    }
}
