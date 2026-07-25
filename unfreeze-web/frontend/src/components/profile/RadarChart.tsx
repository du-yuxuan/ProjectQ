// Unfreeze — 能力雷达图（纯 SVG）
// 维度：流畅度 / 逻辑性 / 语速 / 互动度

interface RadarChartProps {
  fluency: number; // 0-10
  logic: number;
  pace: number;
  engagement?: number;
}

const AXES = ['流畅度', '逻辑性', '语速', '互动度'];

export function RadarChart({ fluency, logic, pace, engagement = 6 }: RadarChartProps) {
  const size = 220;
  const center = size / 2;
  const maxR = 75;
  const values = [fluency, logic, pace, engagement];
  const levels = 5;

  // 计算多边形顶点
  const points = values.map((v, i) => {
    const angle = (i / AXES.length) * 2 * Math.PI - Math.PI / 2;
    const r = (v / 10) * maxR;
    return {
      x: center + r * Math.cos(angle),
      y: center + r * Math.sin(angle),
    };
  });

  const polygonStr = points.map((p) => `${p.x},${p.y}`).join(' ');

  // 网格圈顶点
  const gridPolygons = Array.from({ length: levels }, (_, lv) => {
    const r = ((lv + 1) / levels) * maxR;
    return AXES.map((_, i) => {
      const angle = (i / AXES.length) * 2 * Math.PI - Math.PI / 2;
      return `${center + r * Math.cos(angle)},${center + r * Math.sin(angle)}`;
    }).join(' ');
  });

  // 轴线
  const axisLines = AXES.map((_, i) => {
    const angle = (i / AXES.length) * 2 * Math.PI - Math.PI / 2;
    return {
      x2: center + maxR * Math.cos(angle),
      y2: center + maxR * Math.sin(angle),
    };
  });

  return (
    <div style={styles.wrap}>
      <div style={styles.title}>能力雷达</div>
      <svg width={size} height={size}>
        {/* 网格 */}
        {gridPolygons.map((pts, i) => (
          <polygon
            key={i}
            points={pts}
            fill="none"
            stroke="rgba(0, 212, 255, 0.08)"
            strokeWidth={1}
          />
        ))}
        {/* 轴线 */}
        {axisLines.map((l, i) => (
          <line
            key={i}
            x1={center}
            y1={center}
            x2={l.x2}
            y2={l.y2}
            stroke="rgba(255,255,255,0.06)"
            strokeWidth={1}
          />
        ))}
        {/* 数据多边形 */}
        <polygon
          points={polygonStr}
          fill="rgba(0, 212, 255, 0.15)"
          stroke="#00d4ff"
          strokeWidth={2}
          style={{ filter: 'drop-shadow(0 0 6px rgba(0,212,255,0.5))' }}
        />
        {/* 顶点 */}
        {points.map((p, i) => (
          <circle
            key={i}
            cx={p.x}
            cy={p.y}
            r={3}
            fill="#00d4ff"
            style={{ filter: 'drop-shadow(0 0 4px #00d4ff)' }}
          />
        ))}
        {/* 轴标签 */}
        {AXES.map((label, i) => {
          const angle = (i / AXES.length) * 2 * Math.PI - Math.PI / 2;
          const labelR = maxR + 18;
          const x = center + labelR * Math.cos(angle);
          const y = center + labelR * Math.sin(angle);
          return (
            <text
              key={label}
              x={x}
              y={y}
              textAnchor="middle"
              dominantBaseline="middle"
              style={{ fill: '#8b9bb4', fontSize: 11, fontFamily: 'monospace' }}
            >
              {label}
            </text>
          );
        })}
        {/* 数值 */}
        {values.map((v, i) => {
          const angle = (i / AXES.length) * 2 * Math.PI - Math.PI / 2;
          const r = (v / 10) * maxR;
          const x = center + r * Math.cos(angle);
          const y = center + r * Math.sin(angle);
          return (
            <text
              key={`v-${i}`}
              x={x}
              y={y - 8}
              textAnchor="middle"
              style={{ fill: '#00d4ff', fontSize: 10, fontWeight: 700, fontFamily: 'monospace' }}
            >
              {v.toFixed(1)}
            </text>
          );
        })}
      </svg>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 8,
  },
  title: {
    fontSize: 13,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
};
