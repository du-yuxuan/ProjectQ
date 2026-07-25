// Unfreeze — 全局布局
// 深色主题 + 顶部导航 + 主内容区，响应式适配手机模拟场景

import { type ReactNode } from 'react';
import { NavLink } from 'react-router-dom';

const NAV_ITEMS = [
  { to: '/', label: '首页', icon: '◉' },
  { to: '/session', label: '演讲', icon: '🎙' },
  { to: '/report', label: '复盘', icon: '📋' },
  { to: '/profile', label: '画像', icon: '📊' },
  { to: '/credentials', label: '凭证', icon: '⛓' },
];

export function Layout({ children }: { children: ReactNode }) {
  return (
    <div style={styles.shell}>
      <nav style={styles.nav}>
        <div style={styles.brand}>
          <span style={styles.brandDot} />
          <span style={styles.brandText}>UNFREEZE</span>
        </div>
        <div style={styles.navLinks}>
          {NAV_ITEMS.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              style={({ isActive }) => ({
                ...styles.navLink,
                ...(isActive ? styles.navLinkActive : {}),
              })}
            >
              <span style={styles.navIcon}>{item.icon}</span>
              <span style={styles.navLabel}>{item.label}</span>
            </NavLink>
          ))}
        </div>
      </nav>
      <main style={styles.main}>{children}</main>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  shell: {
    minHeight: '100vh',
    display: 'flex',
    flexDirection: 'column',
    background: '#0a0a0f',
    color: '#e0e0e0',
  },
  nav: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '0 24px',
    height: 56,
    background: 'rgba(15, 15, 25, 0.85)',
    backdropFilter: 'blur(20px)',
    borderBottom: '1px solid rgba(0, 212, 255, 0.1)',
    position: 'sticky',
    top: 0,
    zIndex: 100,
    flexShrink: 0,
  },
  brand: {
    display: 'flex',
    alignItems: 'center',
    gap: 8,
  },
  brandDot: {
    width: 8,
    height: 8,
    borderRadius: '50%',
    background: '#00d4ff',
    boxShadow: '0 0 10px #00d4ff',
  },
  brandText: {
    fontSize: 15,
    fontWeight: 700,
    letterSpacing: 3,
    fontFamily: 'monospace',
    color: '#00d4ff',
  },
  navLinks: {
    display: 'flex',
    gap: 4,
  },
  navLink: {
    display: 'flex',
    alignItems: 'center',
    gap: 6,
    padding: '8px 14px',
    borderRadius: 8,
    fontSize: 13,
    color: '#8b9bb4',
    textDecoration: 'none',
    transition: 'all 0.2s',
    whiteSpace: 'nowrap',
  },
  navLinkActive: {
    color: '#00d4ff',
    background: 'rgba(0, 212, 255, 0.1)',
    boxShadow: 'inset 0 0 0 1px rgba(0, 212, 255, 0.25)',
  },
  navIcon: {
    fontSize: 13,
  },
  navLabel: {
    fontSize: 13,
  },
  main: {
    flex: 1,
    overflow: 'auto',
    position: 'relative',
  },
};
