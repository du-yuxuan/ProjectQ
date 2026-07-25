// Unfreeze - 实时转写流
// 滚动列表，最新在底部，自动滚动；每条显示时间戳 + 说话人标签 + 文本
// 说话人分离：speaker>0 时显示彩色标签（说话人1/2/3...），speaker=0 时不显示

import { useEffect, useRef } from 'react';
import { useSessionStore } from '../../stores/sessionStore';
import type { TranscriptEntry } from '../../lib/types';

function formatTs(ts: number): string {
  const m = Math.floor(ts / 60);
  const s = Math.floor(ts % 60);
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

/** 说话人 ID -> 显示颜色（循环色板） */
const SPEAKER_COLORS: { bg: string; text: string; label: string }[] = [
  { bg: 'rgba(0, 212, 255, 0.15)',  text: '#00d4ff', label: '说话人 A' },
  { bg: 'rgba(255, 159, 67, 0.15)', text: '#ff9f43', label: '说话人 B' },
  { bg: 'rgba(0, 230, 118, 0.15)',  text: '#00e676', label: '说话人 C' },
  { bg: 'rgba(233, 30, 99, 0.15)',  text: '#e91e63', label: '说话人 D' },
  { bg: 'rgba(171, 71, 188, 0.15)', text: '#ab47bc', label: '说话人 E' },
  { bg: 'rgba(255, 235, 59, 0.15)', text: '#ffeb3b', label: '说话人 F' },
];

function getSpeakerStyle(speaker: number) {
  if (speaker <= 0) return null;
  return SPEAKER_COLORS[(speaker - 1) % SPEAKER_COLORS.length];
}

function SpeakerTag({ speaker }: { speaker: number }) {
  const style = getSpeakerStyle(speaker);
  if (!style) return null;
  return (
    <span style={{
      fontSize: 10,
      fontFamily: 'monospace',
      background: style.bg,
      color: style.text,
      padding: '1px 6px',
      borderRadius: 4,
      flexShrink: 0,
      letterSpacing: 0.5,
    }}>
      {style.label}
    </span>
  );
}

export function Transcript({ height = 240 }: { height?: number }) {
  const transcript = useSessionStore((s) => s.transcript);
  const interimText = useSessionStore((s) => s.interimText);
  const listRef = useRef<HTMLDivElement>(null);

  // 自动滚动到底部
  useEffect(() => {
    if (listRef.current) {
      listRef.current.scrollTop = listRef.current.scrollHeight;
    }
  }, [transcript, interimText]);

  return (
    <div style={styles.wrap}>
      <div style={styles.header}>
        <span style={styles.title}>转写流</span>
        <span style={styles.count}>{transcript.length}</span>
      </div>
      <div ref={listRef} style={{ ...styles.list, height }}>
        {transcript.length === 0 && !interimText ? (
          <div style={styles.empty}>等待语音转写…</div>
        ) : (
          <>
            {transcript.map((entry: TranscriptEntry, i) => {
              const spkStyle = getSpeakerStyle(entry.speaker ?? 0);
              return (
                <div
                  key={`${entry.ts}-${i}`}
                  style={{
                    ...styles.entry,
                    borderLeft: spkStyle ? `2px solid ${spkStyle.text}` : undefined,
                    paddingLeft: spkStyle ? 10 : undefined,
                  }}
                >
                  <span style={styles.ts}>{formatTs(entry.ts)}</span>
                  <SpeakerTag speaker={entry.speaker ?? 0} />
                  <span style={styles.text}>{entry.text}</span>
                </div>
              );
            })}
            {interimText && (
              <div style={{ ...styles.entry, opacity: 0.5 }}>
                <span style={styles.ts}>···</span>
                <span style={styles.text}>{interimText}</span>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: {
    background: 'rgba(20, 20, 35, 0.5)',
    border: '1px solid rgba(0, 212, 255, 0.1)',
    borderRadius: 12,
    overflow: 'hidden',
    backdropFilter: 'blur(8px)',
    display: 'flex',
    flexDirection: 'column',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '10px 14px',
    borderBottom: '1px solid rgba(255,255,255,0.05)',
  },
  title: {
    fontSize: 12,
    color: '#8b9bb4',
    fontFamily: 'monospace',
    letterSpacing: 1,
  },
  count: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
    background: 'rgba(255,255,255,0.05)',
    padding: '1px 8px',
    borderRadius: 10,
  },
  list: {
    overflowY: 'auto',
    padding: '8px 14px',
    scrollBehavior: 'smooth',
  },
  empty: {
    color: '#3a4a60',
    fontSize: 13,
    textAlign: 'center',
    padding: '40px 0',
  },
  entry: {
    display: 'flex',
    gap: 10,
    alignItems: 'flex-start',
    padding: '6px 0',
    borderBottom: '1px solid rgba(255,255,255,0.03)',
    animation: 'fadeIn 0.3s ease',
  },
  ts: {
    fontSize: 11,
    color: '#5a6a80',
    fontFamily: 'monospace',
    flexShrink: 0,
    minWidth: 40,
    paddingTop: 1,
  },
  text: {
    fontSize: 13,
    color: '#c0ccdd',
    lineHeight: 1.5,
    flex: 1,
  },
};
