// Unfreeze — 指环模拟面板
// 键盘模拟指环手势：← 旋转后退 · → 旋转前进 · ↑/Space 挥手 · Enter 双击 · Shift 单击
// 按键时动画对应圆环动作，并通过 sessionStore.sendRingCommand 上行

import { useCallback, useEffect, useState } from 'react';
import { useSessionStore } from '../../stores/sessionStore';
import { GestureHint } from './GestureHint';
import type { RingCommand } from '../../lib/types';

interface KeyBinding {
  keys: string[];
  cmd: RingCommand;
  label: string;
  angle: number; // 圆环上对应角度（度）
}

const BINDINGS: KeyBinding[] = [
  { keys: ['ArrowLeft'], cmd: 'rotate_back', label: '上一钩子', angle: 180 },
  { keys: ['ArrowRight'], cmd: 'rotate_front', label: '下一钩子', angle: 0 },
  { keys: ['ArrowUp', 'Space'], cmd: 'wave', label: '挥手清除', angle: 270 },
  { keys: ['Enter'], cmd: 'double_click', label: '确认恢复', angle: 90 },
  { keys: ['Shift'], cmd: 'single_click', label: '模式切换', angle: 135 },
];

const CMD_COLORS: Record<RingCommand, string> = {
  rotate_back: '#a78bfa',
  rotate_front: '#a78bfa',
  wave: '#00e676',
  single_click: '#ffb800',
  double_click: '#00d4ff',
};

export function RingPanel() {
  const sendRingCommand = useSessionStore((s) => s.sendRingCommand);
  const isSessionActive = useSessionStore((s) => s.isSessionActive);
  const [activeCmd, setActiveCmd] = useState<RingCommand | null>(null);
  const [rotation, setRotation] = useState(0);
  const [lastCmd, setLastCmd] = useState<RingCommand | null>(null);

  const trigger = useCallback(
    (cmd: RingCommand) => {
      const ts = Date.now() / 1000;
      sendRingCommand(cmd, ts);
      setActiveCmd(cmd);
      setLastCmd(cmd);
      // 旋转动画
      if (cmd === 'rotate_back') setRotation((r) => r - 60);
      if (cmd === 'rotate_front') setRotation((r) => r + 60);
      setTimeout(() => setActiveCmd(null), 400);
    },
    [sendRingCommand],
  );

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      // 忽略输入框中的按键
      const target = e.target as HTMLElement;
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') return;

      for (const b of BINDINGS) {
        if (b.keys.some((k) => e.key === k || e.code === k)) {
          e.preventDefault();
          trigger(b.cmd);
          return;
        }
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [trigger]);

  const RING_SIZE = 120;
  const CENTER = RING_SIZE / 2;
  const RING_R = 44;

  return (
    <div style={styles.wrap}>
      <div style={styles.header}>
        <span style={styles.title}>指环模拟</span>
        <GestureHint cmd={lastCmd} />
      </div>

      <div style={styles.ringWrap}>
        <svg width={RING_SIZE} height={RING_SIZE}>
          {/* 外环 */}
          <circle
            cx={CENTER}
            cy={CENTER}
            r={RING_R}
            fill="none"
            stroke="rgba(255,255,255,0.08)"
            strokeWidth={3}
          />
          {/* 旋转指示器 */}
          <g style={{ transition: 'transform 0.3s ease', transform: `rotate(${rotation}deg)`, transformOrigin: 'center' }}>
            <circle cx={CENTER} cy={CENTER - RING_R} r={5} fill="#00d4ff" style={{ filter: 'drop-shadow(0 0 4px #00d4ff)' }} />
          </g>
          {/* 方向标记 */}
          {BINDINGS.map((b) => {
            const rad = ((b.angle - 90) * Math.PI) / 180;
            const x = CENTER + RING_R * Math.cos(rad);
            const y = CENTER + RING_R * Math.sin(rad);
            const isActive = activeCmd === b.cmd;
            const color = CMD_COLORS[b.cmd];
            return (
              <circle
                key={b.cmd}
                cx={x}
                cy={y}
                r={isActive ? 7 : 4}
                fill={color}
                opacity={isActive ? 1 : 0.5}
                style={{
                  transition: 'r 0.15s, opacity 0.15s',
                  filter: isActive ? `drop-shadow(0 0 8px ${color})` : 'none',
                }}
              />
            );
          })}
          {/* 中心点 */}
          <circle cx={CENTER} cy={CENTER} r={3} fill="#5a6a80" />
        </svg>
      </div>

      <div style={styles.bindings}>
        {BINDINGS.map((b) => (
          <div
            key={b.cmd}
            style={{
              ...styles.binding,
              ...(activeCmd === b.cmd ? styles.bindingActive : {}),
            }}
          >
            <span style={styles.keyChips}>
              {b.keys.map((k, i) => (
                <span key={k} style={styles.kbd}>
                  {k === 'ArrowLeft' ? '←' : k === 'ArrowRight' ? '→' : k === 'ArrowUp' ? '↑' : k === 'Space' ? 'Space' : k}
                  {i < b.keys.length - 1 ? <span style={styles.kbdOr}> / </span> : null}
                </span>
              ))}
            </span>
            <span style={styles.bindingLabel}>{b.label}</span>
          </div>
        ))}
      </div>

      {!isSessionActive && (
        <div style={styles.disabledHint}>会话未开始</div>
      )}
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
    display: 'flex',
    flexDirection: 'column',
    gap: 12,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  title: {
    fontSize: 13,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
  ringWrap: {
    display: 'flex',
    justifyContent: 'center',
  },
  bindings: {
    display: 'flex',
    flexDirection: 'column',
    gap: 6,
  },
  binding: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '4px 8px',
    borderRadius: 6,
    transition: 'background 0.15s',
  },
  bindingActive: {
    background: 'rgba(0, 212, 255, 0.12)',
  },
  keyChips: {
    display: 'flex',
    alignItems: 'center',
    gap: 2,
  },
  kbd: {
    display: 'inline-flex',
    alignItems: 'center',
    background: 'rgba(255,255,255,0.08)',
    border: '1px solid rgba(255,255,255,0.15)',
    borderRadius: 4,
    padding: '2px 6px',
    fontSize: 11,
    fontFamily: 'monospace',
    color: '#c0ccdd',
    minWidth: 22,
    justifyContent: 'center',
  },
  kbdOr: {
    color: '#3a4a60',
    fontSize: 10,
  },
  bindingLabel: {
    fontSize: 12,
    color: '#8b9bb4',
  },
  disabledHint: {
    fontSize: 11,
    color: '#5a6a80',
    textAlign: 'center',
    fontStyle: 'italic',
  },
};
