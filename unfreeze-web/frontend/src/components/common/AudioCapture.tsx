// Unfreeze — 麦克风采集
// 输出 24kHz PCM16 mono base64 → sessionStore.sendAudio → 后端 StepAudio ASR+LLM
// 不再使用浏览器 Web Speech API

import { useCallback, useEffect, useRef, useState } from 'react';
import { useSessionStore } from '../../stores/sessionStore';

/** 讯飞 ASR 要求 16kHz PCM16 mono */
const TARGET_SAMPLE_RATE = 16000;
const PCM_BUFFER_SIZE = 4096;
const ENERGY_INTERVAL_MS = 100;

interface AudioCaptureProps {
  active?: boolean;
  compact?: boolean;
}

function floatTo16BitPCM(input: Float32Array): Int16Array {
  const out = new Int16Array(input.length);
  for (let i = 0; i < input.length; i++) {
    const s = Math.max(-1, Math.min(1, input[i]));
    out[i] = s < 0 ? s * 0x8000 : s * 0x7fff;
  }
  return out;
}

function int16ToBase64(buf: Int16Array): string {
  const bytes = new Uint8Array(buf.buffer, buf.byteOffset, buf.byteLength);
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...Array.from(bytes.subarray(i, i + chunk)));
  }
  return btoa(binary);
}

/** 线性重采样到目标采样率 */
function resampleLinear(input: Float32Array, fromRate: number, toRate: number): Float32Array {
  if (Math.abs(fromRate - toRate) < 1) return input;
  const ratio = toRate / fromRate;
  const outLen = Math.round(input.length * ratio);
  const out = new Float32Array(outLen);
  for (let i = 0; i < outLen; i++) {
    const srcIdx = i / ratio;
    const i0 = Math.floor(srcIdx);
    const i1 = Math.min(i0 + 1, input.length - 1);
    const frac = srcIdx - i0;
    out[i] = input[i0] * (1 - frac) + input[i1] * frac;
  }
  return out;
}

export function AudioCapture({ active = false, compact = false }: AudioCaptureProps) {
  const sendAudio = useSessionStore((s) => s.sendAudio);
  const sendEnergy = useSessionStore((s) => s.sendEnergy);
  const isSessionActive = useSessionStore((s) => s.isSessionActive);
  const connectionStatus = useSessionStore((s) => s.connectionStatus);

  const [isCapturing, setIsCapturing] = useState(false);
  const [level, setLevel] = useState(0);
  const [permError, setPermError] = useState<string | null>(null);
  const [statusLabel, setStatusLabel] = useState('未启动');

  const audioCtxRef = useRef<AudioContext | null>(null);
  const mediaStreamRef = useRef<MediaStream | null>(null);
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const silentGainRef = useRef<GainNode | null>(null);
  const seqRef = useRef(0);
  const lastEnergyTsRef = useRef(0);
  const sessionStartRef = useRef(0);

  const stop = useCallback(() => {
    if (processorRef.current) {
      processorRef.current.onaudioprocess = null;
      try {
        processorRef.current.disconnect();
      } catch {
        /* ignore */
      }
      processorRef.current = null;
    }
    if (silentGainRef.current) {
      try {
        silentGainRef.current.disconnect();
      } catch {
        /* ignore */
      }
      silentGainRef.current = null;
    }
    if (analyserRef.current) {
      try {
        analyserRef.current.disconnect();
      } catch {
        /* ignore */
      }
      analyserRef.current = null;
    }
    if (sourceRef.current) {
      try {
        sourceRef.current.disconnect();
      } catch {
        /* ignore */
      }
      sourceRef.current = null;
    }
    if (mediaStreamRef.current) {
      mediaStreamRef.current.getTracks().forEach((t) => t.stop());
      mediaStreamRef.current = null;
    }
    if (audioCtxRef.current && audioCtxRef.current.state !== 'closed') {
      audioCtxRef.current.close().catch(() => {});
    }
    audioCtxRef.current = null;
    setIsCapturing(false);
    setLevel(0);
    setStatusLabel('未启动');
  }, []);

  const start = useCallback(async () => {
    setPermError(null);
    setStatusLabel('请求麦克风…');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      });
      mediaStreamRef.current = stream;

      // 尽量直接以 24kHz 打开；不支持则默认率 + 重采样
      let ctx: AudioContext;
      try {
        ctx = new AudioContext({ sampleRate: TARGET_SAMPLE_RATE });
      } catch {
        ctx = new AudioContext();
      }
      await ctx.resume();
      audioCtxRef.current = ctx;
      sessionStartRef.current = performance.now();
      seqRef.current = 0;
      lastEnergyTsRef.current = 0;

      const actualRate = ctx.sampleRate;
      console.log(
        `[AudioCapture] AudioContext sampleRate=${actualRate} (target=${TARGET_SAMPLE_RATE})`,
      );

      const source = ctx.createMediaStreamSource(stream);
      sourceRef.current = source;

      const analyser = ctx.createAnalyser();
      analyser.fftSize = 512;
      analyser.smoothingTimeConstant = 0.6;
      analyserRef.current = analyser;

      // ScriptProcessor 需接入活跃图；gain=0 避免回授
      const processor = ctx.createScriptProcessor(PCM_BUFFER_SIZE, 1, 1);
      processorRef.current = processor;
      const silentGain = ctx.createGain();
      silentGain.gain.value = 0;
      silentGainRef.current = silentGain;

      source.connect(analyser);
      analyser.connect(processor);
      processor.connect(silentGain);
      silentGain.connect(ctx.destination);

      processor.onaudioprocess = (e: AudioProcessingEvent) => {
        const input = e.inputBuffer.getChannelData(0);
        const resampled = resampleLinear(input, actualRate, TARGET_SAMPLE_RATE);
        const pcm = floatTo16BitPCM(resampled);
        sendAudio(int16ToBase64(pcm), seqRef.current++);

        let sumSq = 0;
        for (let i = 0; i < input.length; i++) sumSq += input[i] * input[i];
        const rms = Math.sqrt(sumSq / input.length);
        const energy = Math.min(1, rms * 4);
        setLevel(energy);

        const now = performance.now();
        if (now - lastEnergyTsRef.current >= ENERGY_INTERVAL_MS) {
          lastEnergyTsRef.current = now;
          const ts = (now - sessionStartRef.current) / 1000;
          sendEnergy(ts, energy, energy > 0.015);
        }
      };

      setIsCapturing(true);
      setStatusLabel(`采集中 · ${TARGET_SAMPLE_RATE / 1000}kHz → 讯飞ASR`);
    } catch (e) {
      const err = e as Error;
      if (err.name === 'NotAllowedError' || err.name === 'SecurityError') {
        setPermError('麦克风权限被拒绝。请在浏览器地址栏允许麦克风后重试。');
      } else if (err.name === 'NotFoundError') {
        setPermError('未找到麦克风设备。');
      } else {
        setPermError(`音频采集失败: ${err.message}`);
      }
      setStatusLabel('采集失败');
      stop();
    }
  }, [sendAudio, sendEnergy, stop]);

  // 仅在 active=true（session_started 后）开始采集
  useEffect(() => {
    if (active && !isCapturing) {
      void start();
    } else if (!active && isCapturing) {
      stop();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active]);

  useEffect(() => () => stop(), [stop]);

  const barColor = level > 0.6 ? '#ff3b6b' : level > 0.25 ? '#ffb800' : '#00d4ff';
  const statusColor = isCapturing
    ? '#00e676'
    : permError
      ? '#ff3b6b'
      : connectionStatus === 'connected'
        ? '#8b9bb4'
        : '#5a6a80';

  if (compact) {
    return (
      <div style={styles.compactWrap}>
        <div style={styles.compactRow}>
          <div style={styles.levelTrack}>
            <div
              style={{
                ...styles.levelFill,
                width: `${Math.min(100, level * 100)}%`,
                background: barColor,
              }}
            />
          </div>
          <span style={{ ...styles.badge, color: statusColor, borderColor: `${statusColor}55` }}>
            {isCapturing ? '讯飞ASR' : !isSessionActive ? '等待会话' : statusLabel}
          </span>
        </div>
        {permError && <div style={styles.errMini}>{permError}</div>}
      </div>
    );
  }

  return (
    <div style={styles.wrap}>
      <div style={styles.header}>
        <span style={styles.title}>🎙️ 麦克风 → 讯飞ASR</span>
        <button
          style={{
            ...styles.btn,
            ...(isCapturing ? styles.btnActive : styles.btnIdle),
          }}
          onClick={() => (isCapturing ? stop() : void start())}
          disabled={!isSessionActive && !isCapturing}
        >
          {isCapturing ? '⏹ 停止' : '🎤 开始采集'}
        </button>
      </div>
      <div style={styles.levelTrack}>
        <div
          style={{
            ...styles.levelFill,
            width: `${Math.min(100, level * 100)}%`,
            background: barColor,
            boxShadow: `0 0 12px ${barColor}`,
          }}
        />
      </div>
      <div style={styles.meta}>
        <span style={{ color: statusColor }}>{statusLabel}</span>
        <span>{(level * 100).toFixed(0)}%</span>
      </div>
      {permError && <div style={styles.err}>{permError}</div>}
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: {
    background: 'rgba(20, 20, 35, 0.6)',
    border: '1px solid rgba(0, 212, 255, 0.15)',
    borderRadius: 12,
    padding: 16,
    backdropFilter: 'blur(12px)',
  },
  compactWrap: { width: '100%' },
  compactRow: { display: 'flex', alignItems: 'center', gap: 10 },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 12,
  },
  title: {
    fontSize: 13,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
  btn: {
    border: 'none',
    borderRadius: 8,
    padding: '8px 16px',
    fontSize: 13,
    cursor: 'pointer',
    fontWeight: 600,
  },
  btnIdle: {
    background: 'rgba(0, 212, 255, 0.15)',
    color: '#00d4ff',
    border: '1px solid rgba(0, 212, 255, 0.4)',
  },
  btnActive: {
    background: 'rgba(255, 59, 107, 0.2)',
    color: '#ff3b6b',
    border: '1px solid rgba(255, 59, 107, 0.5)',
  },
  levelTrack: {
    flex: 1,
    height: 8,
    background: 'rgba(255, 255, 255, 0.06)',
    borderRadius: 4,
    overflow: 'hidden',
  },
  levelFill: {
    height: '100%',
    borderRadius: 4,
    transition: 'width 0.08s linear, background 0.2s',
  },
  meta: {
    display: 'flex',
    justifyContent: 'space-between',
    marginTop: 8,
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
  badge: {
    flexShrink: 0,
    fontSize: 11,
    fontFamily: 'monospace',
    padding: '2px 8px',
    borderRadius: 10,
    border: '1px solid',
    whiteSpace: 'nowrap',
  },
  err: {
    marginTop: 8,
    fontSize: 12,
    color: '#ff6b6b',
    background: 'rgba(255, 59, 107, 0.1)',
    padding: '6px 10px',
    borderRadius: 6,
  },
  errMini: { marginTop: 6, fontSize: 11, color: '#ff6b6b' },
};
