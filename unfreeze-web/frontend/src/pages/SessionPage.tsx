// Unfreeze — 核心演讲会话页（AR 眼镜 HUD 模拟）
// 顶: ScoreMeter · 中: HookOverlay + Transcript · 右: RingPanel · 底: Timeline + 音频

import { useEffect, useState } from 'react';
import { useSessionStore } from '../stores/sessionStore';
import { getCurrentUser } from '../lib/auth';
import { AudioCapture } from '../components/common/AudioCapture';
import { ScoreMeter } from '../components/hud/ScoreMeter';
import { HookOverlay } from '../components/hud/HookOverlay';
import { Transcript } from '../components/hud/Transcript';
import { Timeline } from '../components/hud/Timeline';
import { RingPanel } from '../components/ring/RingPanel';

const STATUS_COLORS: Record<string, string> = {
  disconnected: '#5a6a80',
  connecting: '#ffb800',
  connected: '#00e676',
  error: '#ff3b6b',
};

const STATUS_LABELS: Record<string, string> = {
  disconnected: '未连接',
  connecting: '连接中…',
  connected: '已连接',
  error: '连接异常',
};

export function SessionPage() {
  const connectionStatus = useSessionStore((s) => s.connectionStatus);
  const isSessionActive = useSessionStore((s) => s.isSessionActive);
  const sessionId = useSessionStore((s) => s.sessionId);
  const currentScore = useSessionStore((s) => s.currentScore);
  const currentHook = useSessionStore((s) => s.currentHook);
  const paceUpdate = useSessionStore((s) => s.paceUpdate);
  const segmentEnd = useSessionStore((s) => s.segmentEnd);
  const error = useSessionStore((s) => s.error);
  const connectWebSocket = useSessionStore((s) => s.connectWebSocket);
  const startSession = useSessionStore((s) => s.startSession);
  const endSession = useSessionStore((s) => s.endSession);

  const [audioActive, setAudioActive] = useState(false);
  const [sessionElapsed, setSessionElapsed] = useState(0);

  // 挂载时连接 WS
  useEffect(() => {
    connectWebSocket();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // 仅在会话真正启动（session_started）后开启麦克风/STT
  // 避免在后端尚未就绪时就开始采集导致转写丢失
  useEffect(() => {
    if (isSessionActive) {
      setAudioActive(true);
    } else {
      setAudioActive(false);
    }
  }, [isSessionActive]);

  // 会话计时
  useEffect(() => {
    if (!isSessionActive) {
      setSessionElapsed(0);
      return;
    }
    const start = performance.now();
    let raf = 0;
    const tick = () => {
      setSessionElapsed((performance.now() - start) / 1000);
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [isSessionActive]);

  const handleStart = () => {
    const { userId, userName } = getCurrentUser();
    startSession(userId, userName);
    // 不在这里 setAudioActive(true)：等 isSessionActive 变为 true
  };

  const handleEnd = () => {
    endSession();
    // audioActive 由 isSessionActive effect 自动关闭
  };

  const fmtTime = (s: number) => {
    const m = Math.floor(s / 60);
    const sec = Math.floor(s % 60);
    return `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
  };

  return (
    <div style={styles.page}>
      {/* 顶部状态栏 */}
      <div style={styles.topBar}>
        <div style={styles.topLeft}>
          <span style={styles.hudLabel}>SESSION</span>
          <span
            style={{
              ...styles.status,
              color: STATUS_COLORS[connectionStatus],
              borderColor: `${STATUS_COLORS[connectionStatus]}50`,
              background: `${STATUS_COLORS[connectionStatus]}10`,
            }}
          >
            <span style={{ ...styles.statusDot, background: STATUS_COLORS[connectionStatus] }} />
            {STATUS_LABELS[connectionStatus]}
          </span>
          {sessionId && <span style={styles.sessionId}>{sessionId.slice(0, 8)}</span>}
        </div>
        <div style={styles.topCenter}>
          <span style={styles.timer}>{fmtTime(sessionElapsed)}</span>
        </div>
        <div style={styles.topRight}>
          {isSessionActive ? (
            <button style={styles.endBtn} onClick={handleEnd}>
              ⏹ 结束演讲
            </button>
          ) : (
            <button
              style={styles.startBtn}
              onClick={handleStart}
              disabled={connectionStatus !== 'connected'}
            >
              ▶ 开始演讲
            </button>
          )}
        </div>
      </div>

      {/* 错误提示 */}
      {error && (
        <div style={styles.errorBar}>
          ⚠ {error}
          <button style={styles.errRetry} onClick={() => connectWebSocket()}>
            重连
          </button>
        </div>
      )}

      {/* 主体网格 */}
      <div style={styles.body}>
        {/* 左：评分 + 转写 */}
        <div style={styles.leftCol}>
          <div style={styles.scorePanel}>
            <ScoreMeter score={currentScore} />
            {(currentScore?.fluency ?? 0) > 0 && (
              <div style={styles.scoreMeta}>
                <span style={styles.metaItem}>填充 {currentScore ? '' : ''}</span>
              </div>
            )}
          </div>

          {/* 节奏面板 */}
          <div style={styles.pacePanel}>
            <div style={styles.paceItem}>
              <span style={styles.paceLabel}>语速评分</span>
              <span style={styles.paceValue}>{paceUpdate?.paceScore.toFixed(1) ?? '—'}</span>
            </div>
            <div style={styles.paceDivider} />
            <div style={styles.paceItem}>
              <span style={styles.paceLabel}>字/秒</span>
              <span style={styles.paceValue}>{paceUpdate?.charsPerSec.toFixed(1) ?? '—'}</span>
            </div>
            <div style={styles.paceDivider} />
            <div style={styles.paceItem}>
              <span style={styles.paceLabel}>停顿率</span>
              <span style={styles.paceValue}>
                {paceUpdate ? `${(paceUpdate.pauseRate * 100).toFixed(0)}%` : '—'}
              </span>
            </div>
          </div>

          {/* 段结束摘要 */}
          {segmentEnd && (
            <div style={styles.segmentPanel}>
              <div style={styles.segmentTitle}>段结束汇总</div>
              <div style={styles.segmentStats}>
                <span>流畅 {segmentEnd.avgFluency.toFixed(1)}</span>
                <span>逻辑 {segmentEnd.avgLogic.toFixed(1)}</span>
                <span>时长 {segmentEnd.duration}s</span>
                <span>填充 {segmentEnd.fillers}</span>
                <span>停顿 {segmentEnd.pauses}</span>
              </div>
            </div>
          )}

          <Transcript height={180} />
        </div>

        {/* 右：HookOverlay 区域 + 指环 */}
        <div style={styles.rightCol}>
          <div style={styles.hookArea}>
            <HookOverlay />
            {!currentScore && !currentHook && (
              <div style={styles.placeholder}>
                <div style={styles.placeholderIcon}>🎙</div>
                <div style={styles.placeholderText}>
                  {isSessionActive ? '等待评分信号…' : '点击「开始演讲」启动 HUD'}
                </div>
              </div>
            )}
          </div>

          <RingPanel />
        </div>
      </div>

      {/* 底部：时间轴 + 音频 */}
      <div style={styles.bottom}>
        <Timeline />
        <div style={styles.audioBar}>
          <AudioCapture active={audioActive} compact />
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  page: {
    height: '100%',
    display: 'flex',
    flexDirection: 'column',
    background: 'radial-gradient(ellipse at top, #0d0d1a 0%, #0a0a0f 60%)',
    overflow: 'hidden',
  },
  topBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '10px 20px',
    borderBottom: '1px solid rgba(0,212,255,0.08)',
    flexShrink: 0,
  },
  topLeft: {
    display: 'flex',
    alignItems: 'center',
    gap: 12,
  },
  hudLabel: {
    fontSize: 10,
    color: '#3a4a60',
    fontFamily: 'monospace',
    letterSpacing: 2,
  },
  status: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 6,
    fontSize: 11,
    fontFamily: 'monospace',
    padding: '3px 10px',
    borderRadius: 12,
    border: '1px solid',
  },
  statusDot: {
    width: 6,
    height: 6,
    borderRadius: '50%',
  },
  sessionId: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
  topCenter: {
    display: 'flex',
    alignItems: 'center',
  },
  timer: {
    fontSize: 22,
    fontWeight: 700,
    color: '#00d4ff',
    fontFamily: 'monospace',
    letterSpacing: 2,
    textShadow: '0 0 12px rgba(0,212,255,0.4)',
  },
  topRight: {},
  startBtn: {
    padding: '8px 24px',
    fontSize: 14,
    fontWeight: 600,
    color: '#0a0a0f',
    background: 'linear-gradient(135deg, #00e676, #00c853)',
    border: 'none',
    borderRadius: 8,
    cursor: 'pointer',
    transition: 'opacity 0.2s',
    boxShadow: '0 0 16px rgba(0,230,118,0.3)',
  },
  endBtn: {
    padding: '8px 24px',
    fontSize: 14,
    fontWeight: 600,
    color: '#ff3b6b',
    background: 'rgba(255,59,107,0.12)',
    border: '1px solid rgba(255,59,107,0.4)',
    borderRadius: 8,
    cursor: 'pointer',
  },
  errorBar: {
    display: 'flex',
    alignItems: 'center',
    gap: 12,
    padding: '8px 20px',
    background: 'rgba(255,59,107,0.1)',
    color: '#ff6b6b',
    fontSize: 12,
    borderBottom: '1px solid rgba(255,59,107,0.2)',
  },
  errRetry: {
    marginLeft: 'auto',
    padding: '4px 12px',
    fontSize: 11,
    color: '#ff3b6b',
    background: 'rgba(255,59,107,0.15)',
    border: '1px solid rgba(255,59,107,0.3)',
    borderRadius: 6,
    cursor: 'pointer',
  },
  body: {
    flex: 1,
    display: 'flex',
    gap: 16,
    padding: 16,
    overflow: 'hidden',
    minHeight: 0,
  },
  leftCol: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: 12,
    minWidth: 0,
  },
  scorePanel: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 8,
    padding: '16px 0',
    background: 'rgba(20,20,35,0.4)',
    borderRadius: 12,
    border: '1px solid rgba(0,212,255,0.08)',
  },
  scoreMeta: {
    display: 'flex',
    gap: 12,
  },
  metaItem: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
  pacePanel: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-around',
    padding: '10px 16px',
    background: 'rgba(20,20,35,0.4)',
    borderRadius: 12,
    border: '1px solid rgba(0,212,255,0.08)',
  },
  paceItem: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 2,
  },
  paceLabel: {
    fontSize: 10,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
  paceValue: {
    fontSize: 18,
    fontWeight: 700,
    color: '#00d4ff',
    fontFamily: 'monospace',
  },
  paceDivider: {
    width: 1,
    height: 28,
    background: 'rgba(255,255,255,0.06)',
  },
  segmentPanel: {
    padding: '10px 14px',
    background: 'rgba(167,139,250,0.08)',
    borderRadius: 12,
    border: '1px solid rgba(167,139,250,0.2)',
  },
  segmentTitle: {
    fontSize: 11,
    color: '#a78bfa',
    fontFamily: 'monospace',
    marginBottom: 4,
  },
  segmentStats: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: 12,
    fontSize: 12,
    color: '#c0ccdd',
    fontFamily: 'monospace',
  },
  rightCol: {
    width: 280,
    display: 'flex',
    flexDirection: 'column',
    gap: 12,
    flexShrink: 0,
  },
  hookArea: {
    position: 'relative',
    flex: 1,
    minHeight: 180,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    background: 'rgba(20,20,35,0.4)',
    borderRadius: 12,
    border: '1px solid rgba(0,212,255,0.08)',
    overflow: 'hidden',
  },
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 8,
    color: '#3a4a60',
  },
  placeholderIcon: {
    fontSize: 36,
    opacity: 0.4,
  },
  placeholderText: {
    fontSize: 12,
    textAlign: 'center',
  },
  bottom: {
    display: 'flex',
    flexDirection: 'column',
    gap: 10,
    padding: '0 16px 16px',
    flexShrink: 0,
  },
  audioBar: {
    background: 'rgba(20,20,35,0.5)',
    border: '1px solid rgba(0,212,255,0.1)',
    borderRadius: 12,
    padding: '10px 14px',
    backdropFilter: 'blur(8px)',
  },
};
