// Unfreeze — 会话复盘页
// 展示会话 ID、时长、总分、评分细分、带注释转写、钩子事件列表

import { useEffect } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useProfileStore } from '../stores/profileStore';
import { getUserId } from '../lib/auth';

const HOOK_COLORS: Record<string, string> = {
  开口: '#00d4ff',
  思路: '#a78bfa',
  衔接: '#00e676',
  节奏: '#ffb800',
};

function fmtDuration(sec: number | null): string {
  if (sec == null) return '—';
  const m = Math.floor(sec / 60);
  const s = Math.floor(sec % 60);
  return `${m}m ${s}s`;
}

function fmtDate(iso: string | null): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString('zh-CN', { hour12: false });
  } catch {
    return iso;
  }
}

export function ReportPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const sessionId = params.get('sessionId') ?? params.get('id');

  const { currentSession, loading, error, loadSession } = useProfileStore();

  useEffect(() => {
    if (sessionId) loadSession(sessionId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId]);

  if (!sessionId) {
    return (
      <div style={styles.center}>
        <div style={styles.emptyIcon}>📋</div>
        <div style={styles.emptyText}>未指定会话 ID</div>
        <div style={styles.emptyHint}>请从「演讲」页结束后跳转，或在 URL 加 ?sessionId=xxx</div>
      </div>
    );
  }

  if (loading && !currentSession) {
    return <div style={styles.center}><div style={styles.loading}>加载复盘中…</div></div>;
  }

  if (error && !currentSession) {
    return (
      <div style={styles.center}>
        <div style={styles.errorText}>⚠ {error}</div>
        <button style={styles.retryBtn} onClick={() => sessionId && loadSession(sessionId)}>
          重试
        </button>
      </div>
    );
  }

  if (!currentSession) {
    return <div style={styles.center}><div style={styles.loading}>无数据</div></div>;
  }

  const s = currentSession;
  const avgFluency = s.segments.length
    ? s.segments.reduce((a, b) => a + (b.fluencyScore ?? 0), 0) / s.segments.length
    : 0;
  const avgLogic = s.segments.length
    ? s.segments.reduce((a, b) => a + (b.logicScore ?? 0), 0) / s.segments.length
    : 0;
  const avgPace = s.segments.length
    ? s.segments.reduce((a, b) => a + (b.paceScore ?? 0), 0) / s.segments.length
    : 0;

  return (
    <div style={styles.page}>
      <div style={styles.header}>
        <div>
          <div style={styles.headerTitle}>会话复盘</div>
          <div style={styles.headerSub}>
            {s.id} · {fmtDate(s.startTime)} → {fmtDate(s.endTime)}
          </div>
        </div>
        <div style={styles.headerActions}>
          <button style={styles.btnGhost} onClick={() => navigate(`/profile?userId=${s.userId || getUserId()}`)}>
            查看画像
          </button>
          <button
            style={styles.btnPrimary}
            onClick={() => navigate(`/credentials?userId=${s.userId || getUserId()}`)}
          >
            铸造凭证
          </button>
        </div>
      </div>

      {/* 总览 */}
      <div style={styles.overviewGrid}>
        <div style={styles.overviewCard}>
          <div style={styles.overviewLabel}>总分</div>
          <div style={styles.overviewValue}>{s.overallScore?.toFixed(1) ?? '—'}</div>
        </div>
        <div style={styles.overviewCard}>
          <div style={styles.overviewLabel}>时长</div>
          <div style={styles.overviewValue}>{fmtDuration(s.duration)}</div>
        </div>
        <div style={styles.overviewCard}>
          <div style={styles.overviewLabel}>流畅均分</div>
          <div style={styles.overviewValue}>{avgFluency.toFixed(1)}</div>
        </div>
        <div style={styles.overviewCard}>
          <div style={styles.overviewLabel}>逻辑均分</div>
          <div style={styles.overviewValue}>{avgLogic.toFixed(1)}</div>
        </div>
        <div style={styles.overviewCard}>
          <div style={styles.overviewLabel}>语速均分</div>
          <div style={styles.overviewValue}>{avgPace.toFixed(1)}</div>
        </div>
        <div style={styles.overviewCard}>
          <div style={styles.overviewLabel}>钩子次数</div>
          <div style={styles.overviewValue}>{s.hookEvents.length}</div>
        </div>
      </div>

      {/* 钩子事件 */}
      <div style={styles.section}>
        <div style={styles.sectionTitle}>钩子事件 & 恢复</div>
        {s.hookEvents.length === 0 ? (
          <div style={styles.empty}>本次会话无钩子触发</div>
        ) : (
          <div style={styles.hookList}>
            {s.hookEvents.map((h) => {
              const color = HOOK_COLORS[h.hookType] ?? '#8b9bb4';
              return (
                <div key={h.id} style={{ ...styles.hookItem, borderColor: `${color}30` }}>
                  <span style={{ ...styles.hookBadge, background: `${color}20`, color, borderColor: `${color}40` }}>
                    {h.hookType}
                  </span>
                  <span style={styles.hookText}>"{h.hookText}"</span>
                  <span style={styles.hookMeta}>
                    @ {h.ts.toFixed(1)}s · 倒计时 {h.countdown}s
                  </span>
                  <span style={styles.hookMeta}>
                    响应 {h.responseTimeMs != null ? `${(h.responseTimeMs / 1000).toFixed(1)}s` : '—'}
                  </span>
                  <span
                    style={{
                      ...styles.recoveryTag,
                      color: h.recovered ? '#00e676' : '#ff3b6b',
                      background: h.recovered ? 'rgba(0,230,118,0.1)' : 'rgba(255,59,107,0.1)',
                    }}
                  >
                    {h.recovered ? '已恢复' : '未恢复'}
                  </span>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* 段落转写 */}
      <div style={styles.section}>
        <div style={styles.sectionTitle}>分段转写</div>
        {s.segments.length === 0 ? (
          <div style={styles.empty}>无分段数据</div>
        ) : (
          <div style={styles.segmentList}>
            {s.segments.map((seg) => (
              <div key={seg.id} style={styles.segmentItem}>
                <div style={styles.segmentHeader}>
                  <span style={styles.segmentTs}>{seg.ts.toFixed(0)}s</span>
                  <span style={styles.segmentScores}>
                    {seg.fluencyScore != null && <span style={styles.segScore}>流畅 {seg.fluencyScore.toFixed(1)}</span>}
                    {seg.logicScore != null && <span style={styles.segScore}>逻辑 {seg.logicScore.toFixed(1)}</span>}
                    {seg.paceScore != null && <span style={styles.segScore}>语速 {seg.paceScore.toFixed(1)}</span>}
                    {seg.fillerCount != null && <span style={styles.segScore}>填充 {seg.fillerCount}</span>}
                    {seg.pauseCount != null && <span style={styles.segScore}>停顿 {seg.pauseCount}</span>}
                  </span>
                </div>
                <div style={styles.segmentText}>{seg.text || '（无文本）'}</div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  page: {
    padding: '24px',
    maxWidth: 900,
    margin: '0 auto',
  },
  center: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
    padding: 80,
  },
  loading: {
    color: '#5a6a80',
    fontSize: 14,
  },
  errorText: {
    color: '#ff6b6b',
    fontSize: 14,
  },
  retryBtn: {
    padding: '6px 18px',
    color: '#00d4ff',
    background: 'rgba(0,212,255,0.1)',
    border: '1px solid rgba(0,212,255,0.3)',
    borderRadius: 8,
    cursor: 'pointer',
    fontSize: 13,
  },
  emptyIcon: {
    fontSize: 48,
    opacity: 0.3,
  },
  emptyText: {
    fontSize: 16,
    color: '#8b9bb4',
  },
  emptyHint: {
    fontSize: 12,
    color: '#5a6a80',
  },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: 24,
    flexWrap: 'wrap',
    gap: 12,
  },
  headerTitle: {
    fontSize: 22,
    fontWeight: 700,
    color: '#e0e0e0',
  },
  headerSub: {
    fontSize: 12,
    color: '#5a6a80',
    fontFamily: 'monospace',
    marginTop: 4,
  },
  headerActions: {
    display: 'flex',
    gap: 10,
  },
  btnPrimary: {
    padding: '8px 20px',
    fontSize: 13,
    fontWeight: 600,
    color: '#0a0a0f',
    background: 'linear-gradient(135deg, #00d4ff, #00a8d4)',
    border: 'none',
    borderRadius: 8,
    cursor: 'pointer',
  },
  btnGhost: {
    padding: '8px 20px',
    fontSize: 13,
    color: '#00d4ff',
    background: 'rgba(0,212,255,0.08)',
    border: '1px solid rgba(0,212,255,0.25)',
    borderRadius: 8,
    cursor: 'pointer',
  },
  overviewGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))',
    gap: 12,
    marginBottom: 32,
  },
  overviewCard: {
    background: 'rgba(20,20,35,0.5)',
    border: '1px solid rgba(0,212,255,0.1)',
    borderRadius: 12,
    padding: '16px',
    textAlign: 'center',
  },
  overviewLabel: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
    marginBottom: 6,
  },
  overviewValue: {
    fontSize: 24,
    fontWeight: 700,
    color: '#00d4ff',
    fontFamily: 'monospace',
  },
  section: {
    marginBottom: 32,
  },
  sectionTitle: {
    fontSize: 15,
    fontWeight: 600,
    color: '#e0e0e0',
    marginBottom: 12,
    paddingBottom: 8,
    borderBottom: '1px solid rgba(255,255,255,0.06)',
  },
  empty: {
    color: '#5a6a80',
    fontSize: 13,
    padding: '16px 0',
  },
  hookList: {
    display: 'flex',
    flexDirection: 'column',
    gap: 8,
  },
  hookItem: {
    display: 'flex',
    alignItems: 'center',
    gap: 12,
    padding: '10px 14px',
    background: 'rgba(20,20,35,0.4)',
    border: '1px solid',
    borderRadius: 10,
    flexWrap: 'wrap',
  },
  hookBadge: {
    fontSize: 11,
    fontWeight: 600,
    padding: '3px 10px',
    borderRadius: 12,
    border: '1px solid',
    fontFamily: 'monospace',
  },
  hookText: {
    fontSize: 14,
    color: '#e0e0e0',
    fontWeight: 600,
  },
  hookMeta: {
    fontSize: 12,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
  recoveryTag: {
    fontSize: 11,
    padding: '2px 10px',
    borderRadius: 10,
    marginLeft: 'auto',
    fontWeight: 600,
  },
  segmentList: {
    display: 'flex',
    flexDirection: 'column',
    gap: 10,
  },
  segmentItem: {
    background: 'rgba(20,20,35,0.4)',
    border: '1px solid rgba(255,255,255,0.05)',
    borderRadius: 10,
    padding: '12px 14px',
  },
  segmentHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: 12,
    marginBottom: 6,
    flexWrap: 'wrap',
  },
  segmentTs: {
    fontSize: 12,
    color: '#00d4ff',
    fontFamily: 'monospace',
    fontWeight: 600,
  },
  segmentScores: {
    display: 'flex',
    gap: 10,
    flexWrap: 'wrap',
  },
  segScore: {
    fontSize: 11,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    background: 'rgba(255,255,255,0.04)',
    padding: '1px 6px',
    borderRadius: 4,
  },
  segmentText: {
    fontSize: 13,
    color: '#c0ccdd',
    lineHeight: 1.6,
  },
};
