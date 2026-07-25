// Unfreeze — 链上凭证页
// 已铸凭证列表 + 手动铸造 + Injective 浏览器链接

import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useProfileStore } from '../stores/profileStore';
import { getUserId } from '../lib/auth';

const MILESTONES = [
  { value: 'first_session', label: '首次演讲' },
  { value: 'fluency_7', label: '流畅度达 7' },
  { value: 'logic_7', label: '逻辑性达 7' },
  { value: '10_sessions', label: '累计 10 场' },
  { value: 'no_hook', label: '零钩子完成' },
];

const EXPLORER_BASE = 'https://testnet.explorer.injective.network/transaction';

function isRealTx(hash: string): boolean {
  return /^[0-9A-Fa-f]{64}$/.test(hash);
}

function fmtDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString('zh-CN', { hour12: false });
  } catch {
    return iso;
  }
}

export function CredentialsPage() {
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? getUserId();

  const { credentials, loading, error, loadCredentials, mintCredential } = useProfileStore();
  const [selectedMilestone, setSelectedMilestone] = useState(MILESTONES[0].value);

  useEffect(() => {
    loadCredentials(userId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId]);

  const handleMint = () => {
    mintCredential(userId, selectedMilestone);
  };

  return (
    <div style={styles.page}>
      <div style={styles.header}>
        <div>
          <div style={styles.title}>链上凭证</div>
          <div style={styles.sub}>用户 {userId} · Injective testnet</div>
        </div>
      </div>

      {/* 铸造区 */}
      <div style={styles.mintCard}>
        <div style={styles.mintTitle}>手动铸造凭证</div>
        <div style={styles.mintRow}>
          <select
            style={styles.select}
            value={selectedMilestone}
            onChange={(e) => setSelectedMilestone(e.target.value)}
          >
            {MILESTONES.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label} ({m.value})
              </option>
            ))}
          </select>
          <button
            style={styles.mintBtn}
            onClick={handleMint}
            disabled={loading}
          >
            {loading ? '铸造中…' : '⛓ 铸造凭证'}
          </button>
        </div>
      </div>

      {error && <div style={styles.error}>⚠ {error}</div>}

      {/* 凭证列表 */}
      <div style={styles.section}>
        <div style={styles.sectionTitle}>
          已获凭证 <span style={styles.count}>{credentials.length}</span>
        </div>

        {loading && credentials.length === 0 && (
          <div style={styles.loading}>加载凭证中…</div>
        )}

        {!loading && credentials.length === 0 && (
          <div style={styles.empty}>
            <div style={styles.emptyIcon}>⛓</div>
            <div>暂无链上凭证</div>
            <div style={styles.emptyHint}>完成里程碑后可自动铸证，或使用上方手动铸造</div>
          </div>
        )}

        <div style={styles.credList}>
          {credentials.map((c) => {
            const real = isRealTx(c.chainTxHash);
            const milestoneLabel =
              MILESTONES.find((m) => m.value === c.milestone)?.label ?? c.milestone;
            return (
              <div key={c.id} style={styles.credCard}>
                <div style={styles.credIcon}>{real ? '⛓' : '🏷'}</div>
                <div style={styles.credBody}>
                  <div style={styles.credMilestone}>{milestoneLabel}</div>
                  <div style={styles.credMeta}>
                    <span style={styles.credDate}>{fmtDate(c.mintedAt)}</span>
                    <span
                      style={{
                        ...styles.credStatus,
                        color: real ? '#00e676' : '#ffb800',
                        background: real ? 'rgba(0,230,118,0.1)' : 'rgba(255,184,0,0.1)',
                      }}
                    >
                      {real ? '链上确认' : '待上链'}
                    </span>
                  </div>
                  <div style={styles.txHash}>
                    <span style={styles.txLabel}>Tx:</span>
                    <code style={styles.txCode}>
                      {c.chainTxHash.length > 20
                        ? `${c.chainTxHash.slice(0, 10)}…${c.chainTxHash.slice(-8)}`
                        : c.chainTxHash}
                    </code>
                  </div>
                </div>
                {real && (
                  <a
                    href={`${EXPLORER_BASE}/${c.chainTxHash}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    style={styles.explorerLink}
                  >
                    查看交易 ↗
                  </a>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  page: {
    padding: 24,
    maxWidth: 800,
    margin: '0 auto',
  },
  header: {
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
  mintCard: {
    background: 'rgba(20,20,35,0.5)',
    border: '1px solid rgba(0,212,255,0.15)',
    borderRadius: 12,
    padding: 16,
    marginBottom: 24,
  },
  mintTitle: {
    fontSize: 14,
    fontWeight: 600,
    color: '#e0e0e0',
    marginBottom: 12,
  },
  mintRow: {
    display: 'flex',
    gap: 10,
    flexWrap: 'wrap',
  },
  select: {
    flex: 1,
    minWidth: 200,
    padding: '8px 12px',
    fontSize: 13,
    color: '#e0e0e0',
    background: 'rgba(10,10,15,0.8)',
    border: '1px solid rgba(0,212,255,0.2)',
    borderRadius: 8,
    outline: 'none',
    cursor: 'pointer',
  },
  mintBtn: {
    padding: '8px 20px',
    fontSize: 13,
    fontWeight: 600,
    color: '#0a0a0f',
    background: 'linear-gradient(135deg, #ffb800, #ff9800)',
    border: 'none',
    borderRadius: 8,
    cursor: 'pointer',
    whiteSpace: 'nowrap',
  },
  error: {
    color: '#ff6b6b',
    fontSize: 13,
    padding: '10px 14px',
    background: 'rgba(255,59,107,0.08)',
    borderRadius: 8,
    marginBottom: 16,
  },
  section: {},
  sectionTitle: {
    fontSize: 15,
    fontWeight: 600,
    color: '#e0e0e0',
    marginBottom: 12,
  },
  count: {
    fontSize: 12,
    color: '#5a6a80',
    fontFamily: 'monospace',
    marginLeft: 4,
  },
  loading: {
    color: '#5a6a80',
    fontSize: 14,
    textAlign: 'center',
    padding: 40,
  },
  empty: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 6,
    color: '#5a6a80',
    fontSize: 14,
    padding: 48,
    textAlign: 'center',
  },
  emptyIcon: {
    fontSize: 40,
    opacity: 0.3,
  },
  emptyHint: {
    fontSize: 12,
    color: '#3a4a60',
  },
  credList: {
    display: 'flex',
    flexDirection: 'column',
    gap: 10,
  },
  credCard: {
    display: 'flex',
    alignItems: 'center',
    gap: 14,
    padding: '14px 16px',
    background: 'rgba(20,20,35,0.5)',
    border: '1px solid rgba(255,255,255,0.06)',
    borderRadius: 12,
  },
  credIcon: {
    fontSize: 24,
    flexShrink: 0,
  },
  credBody: {
    flex: 1,
    minWidth: 0,
  },
  credMilestone: {
    fontSize: 14,
    fontWeight: 600,
    color: '#e0e0e0',
    marginBottom: 4,
  },
  credMeta: {
    display: 'flex',
    alignItems: 'center',
    gap: 8,
    marginBottom: 4,
  },
  credDate: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
  },
  credStatus: {
    fontSize: 10,
    padding: '1px 8px',
    borderRadius: 8,
    fontWeight: 600,
  },
  txHash: {
    display: 'flex',
    alignItems: 'center',
    gap: 4,
  },
  txLabel: {
    fontSize: 10,
    color: '#3a4a60',
    fontFamily: 'monospace',
  },
  txCode: {
    fontSize: 11,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    background: 'rgba(0,212,255,0.06)',
    padding: '2px 6px',
    borderRadius: 4,
  },
  explorerLink: {
    fontSize: 12,
    color: '#00d4ff',
    textDecoration: 'none',
    whiteSpace: 'nowrap',
    flexShrink: 0,
  },
};
