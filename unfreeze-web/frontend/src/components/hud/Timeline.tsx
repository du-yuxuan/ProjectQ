// Unfreeze — 时间轴标注条
// 横向时间轴，标记评分点（彩色圆点）、钩子（图标）、停顿（间隙）
// 可点击查看详情，含当前位置指示

import { useEffect, useRef, useState } from 'react';
import { useSessionStore } from '../../stores/sessionStore';
import type { HookType } from '../../lib/types';

interface ScoreMark {
  ts: number;
  fluency: number;
  logic: number;
  pace: number;
}

interface HookMark {
  ts: number;
  hookType: HookType;
}

interface PauseMark {
  ts: number;
  count: number;
}

interface TimelineProps {
  duration?: number; // 预计时长（秒），用于缩放；不传则自适应
}

function scoreColor(score: number): string {
  if (score < 5) return '#ff3b6b';
  if (score <= 7) return '#ffb800';
  return '#00e676';
}

const HOOK_ICON: Record<string, string> = {
  开口: '◉',
  思路: '◈',
  衔接: '◆',
  节奏: '◐',
};

export function Timeline({ duration }: TimelineProps) {
  const currentScore = useSessionStore((s) => s.currentScore);
  const currentHook = useSessionStore((s) => s.currentHook);
  const paceUpdate = useSessionStore((s) => s.paceUpdate);
  const isSessionActive = useSessionStore((s) => s.isSessionActive);

  const [scores, setScores] = useState<ScoreMark[]>([]);
  const [hooks, setHooks] = useState<HookMark[]>([]);
  const [pauses, setPauses] = useState<PauseMark[]>([]);
  const [selected, setSelected] = useState<{ kind: 'score' | 'hook'; idx: number } | null>(null);
  const [elapsed, setElapsed] = useState(0);
  const rafRef = useRef<number>(0);

  // 累积评分标记
  const lastScoreTsRef = useRef(-1);
  useEffect(() => {
    if (currentScore && currentScore.ts !== lastScoreTsRef.current) {
      lastScoreTsRef.current = currentScore.ts;
      setScores((prev) => [...prev, { ...currentScore }].slice(-50));
    }
  }, [currentScore]);

  // 累积钩子标记
  const lastHookTsRef = useRef(-1);
  useEffect(() => {
    if (currentHook && currentHook.ts !== lastHookTsRef.current) {
      lastHookTsRef.current = currentHook.ts;
      setHooks((prev) => [...prev, { ts: currentHook.ts, hookType: currentHook.hookType }].slice(-30));
    }
  }, [currentHook]);

  // 从节奏更新近似停顿标记
  useEffect(() => {
    if (paceUpdate && paceUpdate.pauseRate > 0.2) {
      setPauses((prev) => [...prev, { ts: elapsed, count: 1 }].slice(-20));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [paceUpdate]);

  // 当前时间
  useEffect(() => {
    if (!isSessionActive) {
      setElapsed(0);
      return;
    }
    const start = performance.now();
    const tick = () => {
      setElapsed((performance.now() - start) / 1000);
      rafRef.current = requestAnimationFrame(tick);
    };
    rafRef.current = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(rafRef.current);
  }, [isSessionActive]);

  const maxTs = Math.max(duration ?? 0, elapsed, ...scores.map((s) => s.ts), ...hooks.map((h) => h.ts), 1);
  const posPct = Math.min(100, (elapsed / maxTs) * 100);

  const fmt = (ts: number) => {
    const m = Math.floor(ts / 60);
    const s = Math.floor(ts % 60);
    return `${m}:${String(s).padStart(2, '0')}`;
  };

  return (
    <div style={styles.wrap}>
      <div style={styles.header}>
        <span style={styles.title}>时间轴</span>
        <span style={styles.time}>{fmt(elapsed)}</span>
      </div>
      <div style={styles.trackWrap}>
        {/* 基线 */}
        <div style={styles.trackLine} />
        {/* 评分点 */}
        {scores.map((s, i) => (
          <div
            key={`s-${i}`}
            title={`流畅 ${s.fluency} · 逻辑 ${s.logic} · 语速 ${s.pace}`}
            onClick={() => setSelected({ kind: 'score', idx: i })}
            style={{
              ...styles.scoreDot,
              left: `${(s.ts / maxTs) * 100}%`,
              background: scoreColor((s.fluency + s.logic + s.pace) / 3),
            }}
          />
        ))}
        {/* 钩子点 */}
        {hooks.map((h, i) => (
          <div
            key={`h-${i}`}
            title={`${h.hookType} @ ${fmt(h.ts)}`}
            onClick={() => setSelected({ kind: 'hook', idx: i })}
            style={{
              ...styles.hookDot,
              left: `${(h.ts / maxTs) * 100}%`,
            }}
          >
            {HOOK_ICON[h.hookType] ?? '◈'}
          </div>
        ))}
        {/* 停顿间隙 */}
        {pauses.map((p, i) => (
          <div
            key={`p-${i}`}
            style={{
              ...styles.pauseMark,
              left: `${(p.ts / maxTs) * 100}%`,
            }}
          />
        ))}
        {/* 当前位置 */}
        <div style={{ ...styles.cursor, left: `${posPct}%` }} />
      </div>

      {selected && (
        <div style={styles.detail}>
          {selected.kind === 'score' && scores[selected.idx] && (
            <span>
              ⏱ {fmt(scores[selected.idx].ts)} · 流畅 {scores[selected.idx].fluency} · 逻辑{' '}
              {scores[selected.idx].logic} · 语速 {scores[selected.idx].pace}
            </span>
          )}
          {selected.kind === 'hook' && hooks[selected.idx] && (
            <span>
              ⏱ {fmt(hooks[selected.idx].ts)} · 钩子：{hooks[selected.idx].hookType}
            </span>
          )}
        </div>
      )}

      <div style={styles.legend}>
        <span style={styles.legendItem}>● 评分</span>
        <span style={styles.legendItem}>◈ 钩子</span>
        <span style={styles.legendItem}>│ 停顿</span>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: {
    background: 'rgba(20, 20, 35, 0.5)',
    border: '1px solid rgba(0, 212, 255, 0.1)',
    borderRadius: 12,
    padding: '10px 14px',
    backdropFilter: 'blur(8px)',
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
  },
  title: {
    fontSize: 12,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
  time: {
    fontSize: 13,
    color: '#00d4ff',
    fontFamily: 'monospace',
    fontWeight: 600,
  },
  trackWrap: {
    position: 'relative',
    height: 28,
    margin: '4px 0',
  },
  trackLine: {
    position: 'absolute',
    top: '50%',
    left: 0,
    right: 0,
    height: 2,
    background: 'rgba(255,255,255,0.1)',
    borderRadius: 1,
    transform: 'translateY(-50%)',
  },
  scoreDot: {
    position: 'absolute',
    top: '50%',
    width: 8,
    height: 8,
    borderRadius: '50%',
    transform: 'translate(-50%, -50%)',
    cursor: 'pointer',
    transition: 'transform 0.15s',
  },
  hookDot: {
    position: 'absolute',
    top: '50%',
    transform: 'translate(-50%, -50%)',
    fontSize: 13,
    color: '#a78bfa',
    cursor: 'pointer',
    textShadow: '0 0 6px rgba(167,139,250,0.5)',
  },
  pauseMark: {
    position: 'absolute',
    top: '50%',
    width: 2,
    height: 16,
    background: 'rgba(255, 184, 0, 0.4)',
    transform: 'translate(-50%, -50%)',
    borderRadius: 1,
  },
  cursor: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    width: 2,
    background: '#00d4ff',
    boxShadow: '0 0 8px #00d4ff',
    transition: 'left 0.1s linear',
  },
  detail: {
    fontSize: 12,
    color: '#c0ccdd',
    fontFamily: 'monospace',
    padding: '4px 0',
    minHeight: 20,
  },
  legend: {
    display: 'flex',
    gap: 16,
    marginTop: 4,
  },
  legendItem: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
};
