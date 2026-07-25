// Unfreeze — 指环手势提示浮层
// 按键时短暂显示对应手势名称

import { useEffect, useState } from 'react';
import type { RingCommand } from '../../lib/types';

const CMD_LABELS: Record<RingCommand, { label: string; icon: string }> = {
  rotate_back: { label: '上一钩子', icon: '◀' },
  rotate_front: { label: '下一钩子', icon: '▶' },
  wave: { label: '挥手清除', icon: '👋' },
  single_click: { label: '模式切换', icon: '●' },
  double_click: { label: '确认恢复', icon: '✓' },
};

export function GestureHint({ cmd }: { cmd: RingCommand | null }) {
  const [show, setShow] = useState(false);
  const [current, setCurrent] = useState<RingCommand | null>(null);

  useEffect(() => {
    if (cmd) {
      setCurrent(cmd);
      setShow(true);
      const t = setTimeout(() => setShow(false), 1200);
      return () => clearTimeout(t);
    }
  }, [cmd]);

  if (!current) return null;
  const info = CMD_LABELS[current];

  return (
    <div
      style={{
        ...styles.hint,
        opacity: show ? 1 : 0,
        transform: show ? 'translateY(0)' : 'translateY(-8px)',
      }}
    >
      <span style={styles.icon}>{info.icon}</span>
      <span style={styles.label}>{info.label}</span>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  hint: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 8,
    padding: '6px 14px',
    background: 'rgba(0, 212, 255, 0.12)',
    border: '1px solid rgba(0, 212, 255, 0.3)',
    borderRadius: 20,
    transition: 'opacity 0.25s, transform 0.25s',
    pointerEvents: 'none',
  },
  icon: {
    fontSize: 16,
  },
  label: {
    fontSize: 12,
    color: '#00d4ff',
    fontFamily: 'monospace',
    fontWeight: 600,
  },
};
