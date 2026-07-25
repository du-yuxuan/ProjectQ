// ============================================================
// CountdownTimer.cs
// Q (Cue) — EMA 自适应倒计时器
// ============================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Q.Pico
{
    public class CountdownTimer : MonoBehaviour
    {
        [Header("UI")]
        public TMP_Text countdownText;          // 可选 TMP
        public Component countdownTextAny;      // TMP 或 uGUI Text
        public string displayFormat = "倒计时 {0:0.0}s";
        public string idleText = "倒计时 —";

        [Header("EMA 参数")]
        [Range(0.01f, 0.5f)] public float emaAlpha = 0.15f;
        public float initialBaseline = 5.0f;
        public float minBaseline = 2.0f;
        public float maxBaseline = 15.0f;

        [Header("自适应参数")]
        public double slowPaceThreshold = 4.0;
        public double fastPaceThreshold = 7.0;
        public float slowMultiplier = 1.5f;
        public float fastMultiplier = 0.7f;

        public bool debugLog = false;

        private double emaBaseline;
        private double remainingTime;
        private double totalDuration;
        private bool isCountingDown;
        private float t1LocalTime;
        private float t2LocalTime;
        private bool hasRecordedT2;
        private double currentPaceScore = 5.0;
        private int sampleCount;
        private double lastInterval;

        public double EmaBaseline => emaBaseline;
        public int SampleCount => sampleCount;
        public double LastInterval => lastInterval;
        public double TotalDuration => totalDuration;

        void Start()
        {
            emaBaseline = initialBaseline;
            remainingTime = 0;
            RefreshDisplay();
        }

        void Update()
        {
            if (!isCountingDown) return;
            remainingTime -= Time.deltaTime;
            if (remainingTime <= 0)
            {
                remainingTime = 0;
                isCountingDown = false;
                if (!hasRecordedT2 && debugLog)
                    Debug.Log("[Countdown] 倒计时超时（用户未开口）");
            }
            RefreshDisplay();
        }

        public void StartCountdown(double ts, double duration)
        {
            t1LocalTime = Time.time;
            t2LocalTime = 0;
            hasRecordedT2 = false;
            totalDuration = duration;
            remainingTime = duration;
            isCountingDown = true;
            RefreshDisplay();
            if (debugLog) Debug.Log($"[Countdown] 启动倒计时: {duration:F1}s (T1={ts})");
        }

        public void StartCountdown(double ts, float duration) => StartCountdown(ts, (double)duration);

        public void RecordUserOpening()
        {
            if (!isCountingDown || hasRecordedT2) return;
            t2LocalTime = Time.time;
            hasRecordedT2 = true;
            lastInterval = t2LocalTime - t1LocalTime;
            UpdateEmaBaseline(lastInterval);
            remainingTime = 0;
            isCountingDown = false;
            RefreshDisplay();
            if (debugLog) Debug.Log($"[Countdown] T2 interval={lastInterval:F2}s EMA={emaBaseline:F2}s");
        }

        void UpdateEmaBaseline(double interval)
        {
            if (interval < 0) interval = 0;
            emaBaseline = sampleCount == 0
                ? interval
                : emaAlpha * interval + (1.0 - emaAlpha) * emaBaseline;
            emaBaseline = Math.Max(minBaseline, Math.Min(maxBaseline, emaBaseline));
            sampleCount++;
        }

        public void UpdatePaceScore(double paceScore) => currentPaceScore = paceScore;

        public double GetAdaptiveDuration()
        {
            double duration = emaBaseline;
            if (currentPaceScore < slowPaceThreshold) duration *= slowMultiplier;
            else if (currentPaceScore > fastPaceThreshold) duration *= fastMultiplier;
            return Math.Max(minBaseline, Math.Min(maxBaseline, duration));
        }

        public double GetRemainingTime() => Math.Max(0, remainingTime);
        public bool IsRunning() => isCountingDown;
        public bool HasUserOpened() => hasRecordedT2;

        public void BindText(Component label)
        {
            countdownTextAny = label;
            countdownText = label as TMP_Text;
            RefreshDisplay();
        }

        public void BindText(TMP_Text label) => BindText((Component)label);

        public void ClearDisplay()
        {
            isCountingDown = false;
            remainingTime = 0;
            RefreshDisplay();
        }

        void RefreshDisplay()
        {
            var target = (Component)countdownText ?? countdownTextAny;
            if (target == null) return;

            if (isCountingDown || remainingTime > 0.01)
            {
                QUIFactory.SetText(target, string.Format(displayFormat, remainingTime));
                QUIFactory.SetColor(target, remainingTime <= 1.5f
                    ? new Color(1f, 0.35f, 0.3f)
                    : new Color(1f, 0.85f, 0.5f));
            }
            else if (hasRecordedT2)
            {
                QUIFactory.SetText(target, "已开口 ✓");
                QUIFactory.SetColor(target, new Color(0.4f, 0.9f, 0.5f));
            }
            else
            {
                QUIFactory.SetText(target, idleText);
                QUIFactory.SetColor(target, new Color(1f, 0.85f, 0.5f));
            }
        }
    }
}
