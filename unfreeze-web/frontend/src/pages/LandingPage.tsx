// Unfreeze — 落地页
// 项目标题 + 标语 + 开始按钮 + 四模块简介

import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCurrentUser, setUserName, resetUser } from '../lib/auth';

const MODULES = [
  {
    icon: '🎙',
    title: '实时 HUD',
    desc: 'AR 眼镜视角的演讲仪表盘，三维评分、转写流、时间轴全程可视。',
    color: '#00d4ff',
  },
  {
    icon: '⚡',
    title: '兜底钩子',
    desc: '卡顿时 5 字内推送提示（开口 / 思路 / 衔接 / 节奏），倒计时逼你开口。',
    color: '#a78bfa',
  },
  {
    icon: '💍',
    title: '指环模拟',
    desc: '键盘模拟指环手势，挥手清除、双击确认、旋转切换钩子。',
    color: '#00e676',
  },
  {
    icon: '⛓',
    title: '链上凭证',
    desc: '演讲里程碑铸证 Injective，能力成长可验证、不可篡改。',
    color: '#ffb800',
  },
];

export function LandingPage() {
  const navigate = useNavigate();
  const initial = getCurrentUser();
  const [name, setName] = useState(initial.userName);
  const [userId, setUserId] = useState(initial.userId);

  const handleNameChange = (value: string) => {
    setName(value);
    setUserName(value); // 即时持久化，避免未失焦跳转导致丢失
  };

  const handleReset = () => {
    const next = resetUser();
    setUserId(next.userId);
    setName(next.userName);
  };

  const handleStart = () => {
    setUserName(name);
    navigate('/session');
  };

  return (
    <div style={styles.page}>
      {/* 背景光效 */}
      <div style={styles.glowBg} />

      <div style={styles.hero}>
        <div style={styles.badge}>
          <span style={styles.badgeDot} />
          ADVENTURE X 2025
        </div>

        <h1 style={styles.title}>
          <span style={styles.titleMain}>Unfreeze</span>
          <span style={styles.titleSub}>表达指引眼镜</span>
        </h1>

        <p style={styles.tagline}>
          实时语音教练 · 卡顿兜底 · 指环手势 · 链上铸证
          <br />
          <span style={styles.taglineSub}>让每一次开口，都不再冻住</span>
        </p>

        <div style={styles.accountCard}>
          <label style={styles.accountLabel}>测试账号昵称</label>
          <input
            style={styles.accountInput}
            value={name}
            onChange={(e) => handleNameChange(e.target.value)}
            placeholder="给自己起个名字"
            maxLength={20}
          />
          <div style={styles.accountMeta}>
            <span style={styles.accountId}>ID：{userId}</span>
            <button style={styles.accountReset} onClick={handleReset}>
              换个账号
            </button>
          </div>
        </div>

        <button style={styles.cta} onClick={handleStart}>
          <span>开始演讲</span>
          <span style={styles.ctaArrow}>→</span>
        </button>
      </div>

      <div style={styles.modules}>
        {MODULES.map((m) => (
          <div
            key={m.title}
            style={{
              ...styles.module,
              borderColor: `${m.color}30`,
              boxShadow: `inset 0 0 0 1px ${m.color}15`,
            }}
          >
            <div style={{ ...styles.moduleIcon, color: m.color, textShadow: `0 0 12px ${m.color}60` }}>
              {m.icon}
            </div>
            <div style={styles.moduleTitle}>{m.title}</div>
            <div style={styles.moduleDesc}>{m.desc}</div>
          </div>
        ))}
      </div>

      <div style={styles.footer}>
        <span style={styles.footerTech}>Web Audio API · WebSocket · Zustand · Injective</span>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  page: {
    position: 'relative',
    minHeight: '100%',
    padding: '48px 24px',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    overflow: 'hidden',
  },
  glowBg: {
    position: 'absolute',
    top: '-20%',
    left: '50%',
    transform: 'translateX(-50%)',
    width: 600,
    height: 600,
    background: 'radial-gradient(circle, rgba(0,212,255,0.15) 0%, transparent 60%)',
    filter: 'blur(40px)',
    pointerEvents: 'none',
  },
  hero: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 16,
    textAlign: 'center',
    position: 'relative',
    zIndex: 1,
    maxWidth: 600,
  },
  badge: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 8,
    fontSize: 11,
    color: '#00d4ff',
    fontFamily: 'monospace',
    letterSpacing: 3,
    padding: '6px 14px',
    border: '1px solid rgba(0,212,255,0.2)',
    borderRadius: 20,
    background: 'rgba(0,212,255,0.05)',
  },
  badgeDot: {
    width: 6,
    height: 6,
    borderRadius: '50%',
    background: '#00d4ff',
    boxShadow: '0 0 8px #00d4ff',
    animation: 'pulse 2s infinite',
  },
  title: {
    margin: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: 4,
  },
  titleMain: {
    fontSize: 64,
    fontWeight: 800,
    background: 'linear-gradient(135deg, #00d4ff 0%, #a78bfa 100%)',
    WebkitBackgroundClip: 'text',
    WebkitTextFillColor: 'transparent',
    letterSpacing: 4,
    lineHeight: 1,
  },
  titleSub: {
    fontSize: 20,
    color: '#8b9bb4',
    fontWeight: 400,
    letterSpacing: 8,
  },
  tagline: {
    margin: 0,
    fontSize: 15,
    color: '#c0ccdd',
    lineHeight: 1.8,
  },
  taglineSub: {
    fontSize: 13,
    color: '#5a6a80',
  },
  accountCard: {
    marginTop: 20,
    width: '100%',
    maxWidth: 360,
    display: 'flex',
    flexDirection: 'column',
    gap: 10,
    padding: 16,
    background: 'rgba(20,20,35,0.6)',
    border: '1px solid rgba(0,212,255,0.15)',
    borderRadius: 14,
    textAlign: 'left',
  },
  accountLabel: {
    fontSize: 11,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
  accountInput: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '10px 12px',
    fontSize: 15,
    color: '#e0e0e0',
    background: 'rgba(10,10,20,0.6)',
    border: '1px solid rgba(0,212,255,0.2)',
    borderRadius: 8,
    outline: 'none',
  },
  accountMeta: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 8,
  },
  accountId: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  accountReset: {
    flexShrink: 0,
    padding: '4px 12px',
    fontSize: 11,
    color: '#a78bfa',
    background: 'rgba(167,139,250,0.1)',
    border: '1px solid rgba(167,139,250,0.3)',
    borderRadius: 6,
    cursor: 'pointer',
  },
  cta: {
    marginTop: 12,
    display: 'inline-flex',
    alignItems: 'center',
    gap: 10,
    padding: '14px 40px',
    fontSize: 16,
    fontWeight: 600,
    color: '#0a0a0f',
    background: 'linear-gradient(135deg, #00d4ff, #00a8d4)',
    border: 'none',
    borderRadius: 12,
    cursor: 'pointer',
    transition: 'all 0.2s',
    boxShadow: '0 0 24px rgba(0,212,255,0.4)',
  },
  ctaArrow: {
    transition: 'transform 0.2s',
  },
  modules: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    gap: 16,
    marginTop: 64,
    maxWidth: 880,
    width: '100%',
    position: 'relative',
    zIndex: 1,
  },
  module: {
    background: 'rgba(20, 20, 35, 0.6)',
    backdropFilter: 'blur(12px)',
    border: '1px solid',
    borderRadius: 16,
    padding: 24,
    transition: 'transform 0.2s',
  },
  moduleIcon: {
    fontSize: 32,
    marginBottom: 12,
  },
  moduleTitle: {
    fontSize: 16,
    fontWeight: 700,
    color: '#e0e0e0',
    marginBottom: 8,
  },
  moduleDesc: {
    fontSize: 13,
    color: '#8b9bb4',
    lineHeight: 1.6,
  },
  footer: {
    marginTop: 64,
    paddingBottom: 24,
    zIndex: 1,
  },
  footerTech: {
    fontSize: 11,
    color: '#3a4a60',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
};
