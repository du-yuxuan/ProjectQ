// Unfreeze — 能力画像页
// 雷达图 + 进步曲线 + 优势/劣势列表

import { useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useProfileStore } from '../stores/profileStore';
import { getUserId } from '../lib/auth';
import { RadarChart } from '../components/profile/RadarChart';
import { TrendCurve } from '../components/profile/TrendCurve';

export function ProfilePage() {
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? getUserId();

  const { currentProfile, trend, loading, error, loadProfile } = useProfileStore();

  useEffect(() => {
    loadProfile(userId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId]);

  const metrics = currentProfile?.metrics;
  const fluency = metrics?.fluencyAvg ?? 0;
  const logic = metrics?.logicAvg ?? 0;
  const pace = metrics?.paceAvg ?? 0;

  // 互动度：用会话数与总时长推算一个近似值
  const engagement = currentProfile
    ? Math.min(10, (metrics!.sessionsCount * 0.5 + metrics!.totalDuration / 600))
    : 0;

  return (
    <div style={styles.page}>
      <div style={styles.header}>
        <div>
          <div style={styles.title}>能力画像</div>
          <div style={styles.sub}>用户 {userId}</div>
        </div>
        {currentProfile && (
          <div style={styles.snapshotTag}>
            快照 {new Date(currentProfile.snapshotDate).toLocaleDateString('zh-CN')}
          </div>
        )}
      </div>

      {loading && !currentProfile && <div style={styles.loading}>加载画像中…</div>}
      {error && !currentProfile && <div style={styles.error}>⚠ {error}</div>}

      {currentProfile && (
        <>
          {/* 指标卡片 */}
          <div style={styles.metricsGrid}>
            <div style={styles.metricCard}>
              <div style={styles.metricLabel}>总场次</div>
              <div style={styles.metricValue}>{metrics!.sessionsCount}</div>
            </div>
            <div style={styles.metricCard}>
              <div style={styles.metricLabel}>累计时长</div>
              <div style={styles.metricValue}>{Math.round(metrics!.totalDuration / 60)}m</div>
            </div>
            <div style={styles.metricCard}>
              <div style={styles.metricLabel}>流畅均分</div>
              <div style={styles.metricValue}>{fluency.toFixed(1)}</div>
            </div>
            <div style={styles.metricCard}>
              <div style={styles.metricLabel}>逻辑均分</div>
              <div style={styles.metricValue}>{logic.toFixed(1)}</div>
            </div>
            <div style={styles.metricCard}>
              <div style={styles.metricLabel}>语速均分</div>
              <div style={styles.metricValue}>{pace.toFixed(1)}</div>
            </div>
          </div>

          {/* 雷达图 + 曲线 */}
          <div style={styles.chartsRow}>
            <div style={styles.chartCard}>
              <RadarChart fluency={fluency} logic={logic} pace={pace} engagement={engagement} />
            </div>
            <div style={styles.chartCard}>
              <TrendCurve data={trend.length ? trend : currentProfile.trendData} />
            </div>
          </div>

          {/* 优势 / 劣势 */}
          <div style={styles.swRow}>
            <div style={styles.swCard}>
              <div style={{ ...styles.swTitle, color: '#00e676' }}>✓ 优势</div>
              {currentProfile.strengths.length === 0 ? (
                <div style={styles.swEmpty}>暂未发现明显优势</div>
              ) : (
                currentProfile.strengths.map((s, i) => (
                  <div key={i} style={styles.swItem}>{s}</div>
                ))
              )}
            </div>
            <div style={styles.swCard}>
              <div style={{ ...styles.swTitle, color: '#ff3b6b' }}>⚠ 待提升</div>
              {currentProfile.weaknesses.length === 0 ? (
                <div style={styles.swEmpty}>暂无明显短板</div>
              ) : (
                currentProfile.weaknesses.map((s, i) => (
                  <div key={i} style={styles.swItem}>{s}</div>
                ))
              )}
            </div>
          </div>
        </>
      )}

      {!currentProfile && !loading && !error && (
        <div style={styles.loading}>无画像数据</div>
      )}
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  page: {
    padding: 24,
    maxWidth: 900,
    margin: '0 auto',
  },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: 24,
  },
  title: {
    fontSize: 22,
    fontWeight: 700,
    color: '#e0e0e0',
  },
  sub: {
    fontSize: 12,
    color: '#5a6a80',
    fontFamily: 'monospace',
    marginTop: 4,
  },
  snapshotTag: {
    fontSize: 11,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    padding: '4px 12px',
    background: 'rgba(255,255,255,0.04)',
    borderRadius: 12,
  },
  loading: {
    color: '#5a6a80',
    fontSize: 14,
    textAlign: 'center',
    padding: 40,
  },
  error: {
    color: '#ff6b6b',
    fontSize: 14,
    textAlign: 'center',
    padding: 40,
  },
  metricsGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))',
    gap: 12,
    marginBottom: 24,
  },
  metricCard: {
    background: 'rgba(20,20,35,0.5)',
    border: '1px solid rgba(0,212,255,0.1)',
    borderRadius: 12,
    padding: 16,
    textAlign: 'center',
  },
  metricLabel: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
    marginBottom: 6,
  },
  metricValue: {
    fontSize: 22,
    fontWeight: 700,
    color: '#00d4ff',
    fontFamily: 'monospace',
  },
  chartsRow: {
    display: 'flex',
    gap: 16,
    marginBottom: 24,
    flexWrap: 'wrap',
  },
  chartCard: {
    flex: 1,
    minWidth: 240,
    background: 'rgba(20,20,35,0.5)',
    border: '1px solid rgba(0,212,255,0.1)',
    borderRadius: 12,
    padding: 16,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  swRow: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: 16,
  },
  swCard: {
    background: 'rgba(20,20,35,0.5)',
    border: '1px solid rgba(255,255,255,0.06)',
    borderRadius: 12,
    padding: 16,
  },
  swTitle: {
    fontSize: 14,
    fontWeight: 700,
    marginBottom: 12,
  },
  swItem: {
    fontSize: 13,
    color: '#c0ccdd',
    padding: '6px 0',
    borderBottom: '1px solid rgba(255,255,255,0.04)',
  },
  swEmpty: {
    fontSize: 13,
    color: '#5a6a80',
  },
};
