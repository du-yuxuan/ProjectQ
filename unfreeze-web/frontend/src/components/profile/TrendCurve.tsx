// Unfreeze — 进步曲线（纯 SVG 折线图）
// 显示流畅度 / 逻辑 / 语速随时间变化

import { useMemo } from 'react';
import type { TrendPoint } from '../../lib/types';

interface TrendCurveProps {
  data: TrendPoint[];
  width?: number;
  height?: number;
}

const SERIES = [
  { key: 'fluencyAvg' as const, label: '流畅度', color: '#00d4ff' },
  { key: 'logicAvg' as const, label: '逻辑', color: '#00e676' },
  { key: 'paceAvg' as const, label: '语速', color: '#a78bfa' },
];

export function TrendCurve({ data, width = 480, height = 200 }: TrendCurveProps) {
  const padding = { top: 20, right: 20, bottom: 36, left: 36 };
  const plotW = width - padding.left - padding.right;
  const plotH = height - padding.top - padding.bottom;

  const { paths, xTicks } = useMemo(() => {
    if (data.length === 0) {
      return { paths: SERIES.map((s) => ({ ...s, d: '', dots: [] as { x: number; y: number }[] })), xTicks: [] as { x: number; label: string }[] };
    }
    const n = data.length;
    const xStep = n > 1 ? plotW / (n - 1) : 0;

    const paths = SERIES.map((s) => {
      const coords = data.map((d, i) => ({
        x: padding.left + i * xStep,
        y: padding.top + plotH - (d[s.key] / 10) * plotH,
      }));
      const d = coords.map((c, i) => `${i === 0 ? 'M' : 'L'}${c.x},${c.y}`).join(' ');
      return { ...s, d, dots: coords };
    });

    const xTicks = data.map((d, i) => ({
      x: padding.left + i * xStep,
      label: d.date.slice(5), // MM-DD
    }));

    return { paths, xTicks };
  }, [data, plotW, plotH, padding.left, padding.top]);

  // Y 轴刻度
  const yTicks = [0, 2, 4, 6, 8, 10];

  return (
    <div style={styles.wrap}>
      <div style={styles.title}>进步曲线</div>
      <svg width={width} height={height}>
        {/* Y 轴网格线 */}
        {yTicks.map((v) => {
          const y = padding.top + plotH - (v / 10) * plotH;
          return (
            <g key={v}>
              <line
                x1={padding.left}
                y1={y}
                x2={padding.left + plotW}
                y2={y}
                stroke="rgba(255,255,255,0.05)"
                strokeWidth={1}
              />
              <text
                x={padding.left - 8}
                y={y}
                textAnchor="end"
                dominantBaseline="middle"
                style={{ fill: '#5a6a80', fontSize: 9, fontFamily: 'monospace' }}
              >
                {v}
              </text>
            </g>
          );
        })}

        {/* X 轴刻度 */}
        {xTicks.map((t, i) => (
          <text
            key={i}
            x={t.x}
            y={padding.top + plotH + 16}
            textAnchor="middle"
            style={{ fill: '#5a6a80', fontSize: 9, fontFamily: 'monospace' }}
          >
            {t.label}
          </text>
        ))}

        {/* 折线 */}
        {paths.map((p) => (
          <g key={p.key}>
            <path
              d={p.d}
              fill="none"
              stroke={p.color}
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              style={{ filter: `drop-shadow(0 0 4px ${p.color}80)` }}
            />
            {p.dots.map((c, i) => (
              <circle key={i} cx={c.x} cy={c.y} r={3} fill={p.color} />
            ))}
          </g>
        ))}

        {/* 图例 */}
        {SERIES.map((s, i) => (
          <g key={s.key}>
            <line
              x1={padding.left + i * 100}
              y1={6}
              x2={padding.left + i * 100 + 16}
              y2={6}
              stroke={s.color}
              strokeWidth={2}
            />
            <text
              x={padding.left + i * 100 + 20}
              y={6}
              dominantBaseline="middle"
              style={{ fill: '#8b9bb4', fontSize: 10, fontFamily: 'monospace' }}
            >
              {s.label}
            </text>
          </g>
        ))}

        {data.length === 0 && (
          <text
            x={width / 2}
            y={height / 2}
            textAnchor="middle"
            style={{ fill: '#3a4a60', fontSize: 13 }}
          >
            暂无趋势数据
          </text>
        )}
      </svg>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 4,
  },
  title: {
    fontSize: 13,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
};
