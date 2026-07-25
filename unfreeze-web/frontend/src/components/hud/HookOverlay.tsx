// Unfreeze — 兜底钩子浮层 + 倒计时动画
// 钩子激活时展示居中卡片：钩子类型徽章 + 大字钩子文案 + 倒计时
// 键盘交互：Space = wave（挥手清除），Enter = double_click（确认恢复）

import { useEffect, useRef, useState } from 'react';
import { useSessionStore } from '../../stores/sessionStore';
import type { LiveHook } from '../../lib/types';

const HOOK_COLORS: Record<string, string> = {
  开口: '#00d4ff',
  思路: '#a78bfa',
  衔接: '#00e676',
  节奏: '#ffb800',
};

export function HookOverlay() {
  const currentHook = useSessionStore((s) => s.currentHook);
  const dismissHook = useSessionStore((s) => s.dismissHook);
  const sendRingCommand = useSessionStore((s) => s.sendRingCommand);

  return (
    <HookCard
      hook={currentHook}
      onWave={() => {
        const ts = currentHook?.ts ?? 0;
        sendRingCommand('wave', ts);
        dismissHook();
      }}
      onConfirm={() => {
        const ts = currentHook?.ts ?? 0;
        sendRingCommand('double_click', ts);
        dismissHook();
      }}
    />
  );
}

function HookCard({
  hook,
  onWave,
  onConfirm,
}: {
  hook: LiveHook | null;
  onWave: () => void;
  onConfirm: () => void;
}) {
  const [countdown, setCountdown] = useState(0);
  const [visible, setVisible] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const fadeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // 倒计时逻辑
  useEffect(() => {
    if (hook && hook.active) {
      setCountdown(hook.countdown);
      setVisible(true);

      // 清除已有定时器
      if (intervalRef.current) clearInterval(intervalRef.current);
      if (fadeTimerRef.current) clearTimeout(fadeTimerRef.current);

      intervalRef.current = setInterval(() => {
        setCountdown((prev) => {
          if (prev <= 1) {
            if (intervalRef.current) clearInterval(intervalRef.current);
            // 倒计时归零，淡出
            setVisible(false);
            return 0;
          }
          return prev - 1;
        });
      }, 1000);
    } else {
      setVisible(false);
    }

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
      if (fadeTimerRef.current) clearTimeout(fadeTimerRef.current);
    };
  }, [hook]);

  // 键盘快捷键
  useEffect(() => {
    if (!hook) return;
    const handler = (e: KeyboardEvent) => {
      if (e.code === 'Space') {
        e.preventDefault();
        onWave();
      } else if (e.code === 'Enter') {
        e.preventDefault();
        onConfirm();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [hook, onWave, onConfirm]);

  if (!hook) return null;

  const color = HOOK_COLORS[hook.hookType] ?? '#00d4ff';

  return (
    <div
      style={{
        ...styles.overlay,
        opacity: visible ? 1 : 0,
        pointerEvents: visible ? 'auto' : 'none',
      }}
    >
      <div
        style={{
          ...styles.card,
          borderColor: `${color}50`,
          boxShadow: `0 0 40px ${color}30, inset 0 0 20px ${color}10`,
        }}
      >
        <div style={{ ...styles.badge, background: `${color}20`, color, borderColor: `${color}50` }}>
          {hook.hookType}
        </div>
        <div style={{ ...styles.hookText, color, textShadow: `0 0 20px ${color}60` }}>
          {hook.hookText}
        </div>
        <div style={styles.countdownWrap}>
          <span style={{ ...styles.countdownNum, color: countdown <= 1 ? '#ff3b6b' : color }}>
            {countdown}
          </span>
          <span style={styles.countdownUnit}>s</span>
        </div>
        <div style={styles.hints}>
          <kbd style={styles.kbd}>Space</kbd>
          <span style={styles.hintText}>挥手清除</span>
          <span style={styles.hintDivider}>·</span>
          <kbd style={styles.kbd}>Enter</kbd>
          <span style={styles.hintText}>确认恢复</span>
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  overlay: {
    position: 'absolute',
    inset: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 50,
    transition: 'opacity 0.3s ease',
    pointerEvents: 'none',
  },
  card: {
    background: 'rgba(15, 15, 28, 0.92)',
    backdropFilter: 'blur(20px)',
    border: '1px solid',
    borderRadius: 20,
    padding: '32px 48px',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 16,
    minWidth: 280,
  },
  badge: {
    fontSize: 12,
    fontWeight: 600,
    padding: '4px 14px',
    borderRadius: 20,
    border: '1px solid',
    letterSpacing: 2,
    fontFamily: 'monospace',
  },
  hookText: {
    fontSize: 42,
    fontWeight: 800,
    letterSpacing: 4,
    fontFamily: 'sans-serif',
  },
  countdownWrap: {
    display: 'flex',
    alignItems: 'baseline',
    gap: 2,
  },
  countdownNum: {
    fontSize: 32,
    fontWeight: 700,
    fontFamily: 'monospace',
    transition: 'color 0.3s',
  },
  countdownUnit: {
    fontSize: 14,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
  hints: {
    display: 'flex',
    alignItems: 'center',
    gap: 6,
    fontSize: 11,
    color: '#5a6a80',
  },
  kbd: {
    background: 'rgba(255,255,255,0.08)',
    border: '1px solid rgba(255,255,255,0.15)',
    borderRadius: 4,
    padding: '2px 6px',
    fontSize: 10,
    fontFamily: 'monospace',
    color: '#8b9bb4',
  },
  hintText: {
    color: '#5a6a80',
  },
  hintDivider: {
    color: '#3a4a60',
    margin: '0 2px',
  },
};
