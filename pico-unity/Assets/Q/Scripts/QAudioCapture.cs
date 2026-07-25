// ============================================================
// QAudioCapture.cs
// Q (Cue) — PICO 音频采集与上行模块
//
// 功能：
//   1. 会话开始时自动启动 Unity Microphone 录音
//   2. 每 100ms 读取新增 PCM 数据 → 16kHz 16bit mono 编码
//   3. 通过 QWebSocketClient.SendAudioFrame 发送到后端
//   4. 同步计算能量包络并通过 SendEnergy 上报
//   5. 会话结束时自动停止录音
//
// 后端期望：base64 编码 16kHz 16bit 单声道 PCM（type="audio"）
// ============================================================

using System;
using UnityEngine;

namespace Q.Pico
{
    public class QAudioCapture : MonoBehaviour
    {
        [Header("音频参数")]
        [Tooltip("目标采样率（Hz）。16kHz 兼顾语音识别精度与带宽。若设备不支持则自动降级。")]
        public int targetSampleRate = 16000;

        [Tooltip("每帧发送间隔（毫秒）。100ms ≈ 1600 采样 @16kHz，约 3.2KB base64。")]
        public int frameDurationMs = 100;

        [Tooltip("环形缓冲区秒数。1s 足够容纳最长读取窗口。")]
        public int micBufferSeconds = 1;

        [Header("能量")]
        [Tooltip("能量计算窗宽（采样点数）。160 点 @16kHz ≈ 10ms。")]
        public int energyWindow = 160;

        [Header("调试")]
        public bool debugLog = true;

        // ============================================================
        // 状态
        // ============================================================

        QWebSocketClient wsClient;
        AudioClip micClip;
        bool isCapturing;
        int lastReadPos;          // 上一帧已经读取到的采样位置
        int clipFreq;             // 实际 AudioClip 采样率
        int samplesPerFrame;      // 每帧应读取的采样点数（目标）
        float elapsedSinceSend;
        float sendInterval;
        int audioSeq;             // 发送序号

        // ============================================================
        // Unity 生命周期
        // ============================================================

        void Awake()
        {
            sendInterval = frameDurationMs / 1000f;
            samplesPerFrame = Mathf.CeilToInt(targetSampleRate * sendInterval);
        }

        void Start()
        {
            wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient == null)
            {
                Debug.LogWarning("[AudioCapture] QWebSocketClient 未找到，音频采集待命");
                return;
            }

            wsClient.OnSessionStart.AddListener(OnSessionStart);
            wsClient.OnSessionEndAck.AddListener(OnSessionEnd);
        }

        void Update()
        {
            if (!isCapturing) return;
            if (micClip == null) return;

            elapsedSinceSend += Time.unscaledDeltaTime;
            if (elapsedSinceSend < sendInterval) return;
            elapsedSinceSend = 0f;

            ProcessMicBuffer();
        }

        void OnDestroy()
        {
            StopRecording();
            if (wsClient != null)
            {
                wsClient.OnSessionStart.RemoveListener(OnSessionStart);
                wsClient.OnSessionEndAck.RemoveListener(OnSessionEnd);
            }
        }

        // ============================================================
        // 会话生命周期
        // ============================================================

        void OnSessionStart(SessionStartMessage msg)
        {
            StartRecording();
        }

        void OnSessionEnd(SessionEndAckMessage msg)
        {
            StopRecording();
        }

        // ============================================================
        // 录音控制
        // ============================================================

        void StartRecording()
        {
            if (isCapturing) return;

            string device = null; // 默认麦克风
            try
            {
                // 检查是否有可用麦克风
                if (Microphone.devices == null || Microphone.devices.Length == 0)
                {
                    Debug.LogError("[AudioCapture] 未检测到麦克风设备");
                    return;
                }
                device = Microphone.devices[0];

                // 查询设备支持的最小/最大采样率
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);

                clipFreq = targetSampleRate;
                if (targetSampleRate < minFreq || targetSampleRate > maxFreq)
                {
                    // 降级到设备支持的最接近采样率
                    int fallback = Mathf.Clamp(targetSampleRate, minFreq, maxFreq);
                    Debug.LogWarning($"[AudioCapture] {targetSampleRate}Hz 不支持，降级使用 {fallback}Hz (设备支持 {minFreq}-{maxFreq}Hz)");
                    clipFreq = fallback;
                }

                // 循环录音：loop=true, 1s 环形缓冲
                micClip = Microphone.Start(device, loop: true, lengthSec: micBufferSeconds, frequency: clipFreq);
                if (micClip == null)
                {
                    Debug.LogError("[AudioCapture] Microphone.Start 返回 null，录音启动失败");
                    return;
                }

                // 等待至少一帧让 mic 开始采集
                lastReadPos = 0;
                elapsedSinceSend = 0f;
                audioSeq = 0;
                isCapturing = true;

                if (debugLog)
                    Debug.Log($"[AudioCapture] 录音已启动: device={device}, freq={clipFreq}Hz, channels={micClip.channels}, buffer={micBufferSeconds}s, frame={frameDurationMs}ms");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioCapture] 录音启动异常: {e.Message}\n{e.StackTrace}");
                StopRecording();
            }
        }

        void StopRecording()
        {
            isCapturing = false;

            if (micClip != null)
            {
                try { Microphone.End(null); } catch { }
                Destroy(micClip);
                micClip = null;
            }

            lastReadPos = 0;
            elapsedSinceSend = 0f;

            if (debugLog)
                Debug.Log("[AudioCapture] 录音已停止");
        }

        // ============================================================
        // 音频读取与发送
        // ============================================================

        void ProcessMicBuffer()
        {
            if (micClip == null || wsClient == null) return;
            if (!wsClient.IsConnected) return;

            try
            {
                int micPos = Microphone.GetPosition(null);
                if (micPos < 0) return;

                int clipSamples = micClip.samples;

                // 计算从上一次读取后新增的采样点数
                int newSamples;
                if (micPos >= lastReadPos)
                {
                    newSamples = micPos - lastReadPos;
                }
                else
                {
                    // 环形缓冲区回绕
                    newSamples = (clipSamples - lastReadPos) + micPos;
                }

                if (newSamples <= 0) return;

                // 限制每次最多读取 0.5s 的数据，防止一次性堆积太多
                int maxFrameSamples = clipFreq / 2;
                if (newSamples > maxFrameSamples)
                {
                    if (debugLog)
                        Debug.LogWarning($"[AudioCapture] 缓冲区堆积 {newSamples} 采样（>{maxFrameSamples}），丢弃旧数据");
                    lastReadPos = Mathf.Max(0, micPos - maxFrameSamples);
                    newSamples = maxFrameSamples;
                }

                // 读取样本数据（环形缓冲区：可能需要跨尾→头分两次读）
                float[] samples = new float[newSamples];
                if (lastReadPos + newSamples <= clipSamples)
                {
                    // 连续段，一次读完
                    micClip.GetData(samples, lastReadPos);
                }
                else
                {
                    // 跨边界：先读尾部，再读头部
                    int tailLen = clipSamples - lastReadPos;
                    int headLen = newSamples - tailLen;
                    float[] tailBuf = new float[tailLen];
                    float[] headBuf = new float[headLen];
                    micClip.GetData(tailBuf, lastReadPos);
                    micClip.GetData(headBuf, 0);
                    Array.Copy(tailBuf, 0, samples, 0, tailLen);
                    Array.Copy(headBuf, 0, samples, tailLen, headLen);
                }

                // 更新读取位置
                lastReadPos = (lastReadPos + newSamples) % clipSamples;

                // 重采样到目标采样率（若 clipFreq != targetSampleRate）
                float[] resampled;
                if (clipFreq == targetSampleRate)
                {
                    resampled = samples;
                }
                else
                {
                    resampled = ResampleLinear(samples, clipFreq, targetSampleRate);
                }

                // 编码为 16-bit PCM
                byte[] pcm = FloatToPCM16(resampled);

                // 发送音频帧
                wsClient.SendAudioFrame(pcm);
                if (debugLog && audioSeq % 10 == 0)
                    Debug.Log($"[AudioCapture] 音频帧 #{audioSeq}: {resampled.Length} 采样, {pcm.Length} bytes PCM (base64)");

                audioSeq++;

                // 计算并发送能量
                double energy = ComputeRMS(resampled);
                double ts = Time.realtimeSinceStartupAsDouble;
                wsClient.SendEnergy(ts, energy, energy > 0.01f);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioCapture] 处理 mic 数据异常: {e.Message}");
            }
        }

        // ============================================================
        // 信号处理
        // ============================================================

        /// <summary>
        /// float[-1,1] → 16-bit PCM little-endian byte[]。
        /// </summary>
        static byte[] FloatToPCM16(float[] samples)
        {
            if (samples == null || samples.Length == 0) return Array.Empty<byte>();
            byte[] pcm = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                // clamp 并 scale 到 Int16 范围
                float v = Mathf.Clamp(samples[i], -1f, 1f);
                short s = (short)(v * short.MaxValue);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return pcm;
        }

        /// <summary>
        /// 简单线性重采样：srcFreq → dstFreq。
        /// </summary>
        static float[] ResampleLinear(float[] src, int srcFreq, int dstFreq)
        {
            if (src == null || src.Length == 0) return Array.Empty<float>();
            if (srcFreq == dstFreq) return src;

            double ratio = (double)dstFreq / srcFreq;
            int dstLen = Math.Max(1, (int)(src.Length * ratio));
            float[] dst = new float[dstLen];

            for (int i = 0; i < dstLen; i++)
            {
                double srcIdx = i / ratio;
                int idx0 = (int)srcIdx;
                int idx1 = Math.Min(idx0 + 1, src.Length - 1);
                float frac = (float)(srcIdx - idx0);
                dst[i] = src[idx0] * (1f - frac) + src[idx1] * frac;
            }

            return dst;
        }

        /// <summary>
        /// 计算 RMS 能量（0-1 归一化）。
        /// </summary>
        static double ComputeRMS(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0.0;
            double sumSq = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                sumSq += (double)samples[i] * samples[i];
            }
            return Math.Sqrt(sumSq / samples.Length);
        }
    }
}