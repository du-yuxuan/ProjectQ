// Unfreeze — 三维评分仪表（流畅度 / 逻辑 / 语速）
// 纯 SVG 圆环仪表，颜色随分数变化（红<5 黄5-7 绿>7）

import type { LiveScore } from '../../lib/types';

interface ScoreMeterProps {
  score: LiveScore | null;
}

interface GaugeProps {
  label: string;
  value: number;
  color: string;
}

const SIZE = 100;
const STROKE = 8;
const RADIUS = (SIZE - STROKE) / 2;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

function scoreColor(score: number): string {
  if (score < 5) return '#ff3b6b';
  if (score <= 7) return '#ffb800';
  return '#00e676';
}

function Gauge({ label, value, color }: GaugeProps) {
  // 直接显示值，不用动画（高频更新时动画会卡在中间值）
  const displayValue = value;

  const pct = displayValue / 10;
  const offset = CIRCUMFERENCE * (1 - pct);
  const rotate = -90; // 从顶部开始

  return (
    <div style={styles.gaugeWrap}>
      <svg width={SIZE} height={SIZE} style={styles.gaugeSvg}>
        <circle
          cx={SIZE / 2}
          cy={SIZE / 2}
          r={RADIUS}
          fill="none"
          stroke="rgba(255,255,255,0.06)"
          strokeWidth={STROKE}
        />
        <circle
          cx={SIZE / 2}
          cy={SIZE / 2}
          r={RADIUS}
          fill="none"
          stroke={color}
          strokeWidth={STROKE}
          strokeLinecap="round"
          strokeDasharray={CIRCUMFERENCE}
          strokeDashoffset={offset}
          style={{
            transition: 'stroke 0.3s',
            filter: `drop-shadow(0 0 6px ${color}80)`,
            transform: `rotate(${rotate}deg)`,
            transformOrigin: 'center',
          }}
        />
        <text
          x="50%"
          y="46%"
          textAnchor="middle"
          dominantBaseline="middle"
          style={{
            fill: color,
            fontSize: 24,
            fontWeight: 700,
            fontFamily: 'monospace',
          }}
        >
          {displayValue.toFixed(1)}
        </text>
        <text
          x="50%"
          y="64%"
          textAnchor="middle"
          dominantBaseline="middle"
          style={{ fill: '#5a6a80', fontSize: 9, fontFamily: 'monospace' }}
        >
          / 10
        </text>
      </svg>
      <span style={styles.gaugeLabel}>{label}</span>
    </div>
  );
}

export function ScoreMeter({ score }: ScoreMeterProps) {
  const fluency = score?.fluency ?? 0;
  const logic = score?.logic ?? 0;
  const pace = score?.pace ?? 0;

  return (
    <div style={styles.wrap}>
      <Gauge label="流畅度" value={fluency} color={scoreColor(fluency)} />
      <Gauge label="逻辑性" value={logic} color={scoreColor(logic)} />
      <Gauge label="语速" value={pace} color={scoreColor(pace)} />
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: {
    display: 'flex',
    gap: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  gaugeWrap: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 6,
  },
  gaugeSvg: {
    display: 'block',
  },
  gaugeLabel: {
    fontSize: 12,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
};
