// Unfreeze — 实时会话状态（Zustand）
// 管理 WebSocket 连接与所有下行消息派生的实时状态

import { create } from 'zustand';
import type {
  ClientMessage,
  ConnectionStatus,
  LiveHook,
  LiveScore,
  PaceUpdate,
  RingCommand,
  SegmentSummary,
  ServerMessage,
  TranscriptEntry,
} from '../lib/types';

// 自动重连相关常量
const RECONNECT_BASE_DELAY = 1000;
const RECONNECT_MAX_DELAY = 15000;
const SESSION_WS_PATH = '/ws/session';

interface SessionState {
  // 连接
  wsConnection: WebSocket | null;
  connectionStatus: ConnectionStatus;
  error: string | null;

  // 会话元信息
  sessionId: string | null;
  userId: string | null;
  isSessionActive: boolean;
  sessionStartTime: string | null;

  // 实时 HUD 状态
  currentScore: LiveScore | null;
  currentHook: LiveHook | null;
  transcript: TranscriptEntry[];
  interimText: string;
  paceUpdate: PaceUpdate | null;
  segmentEnd: SegmentSummary | null;

  // 内部：重连
  _reconnectAttempts: number;
  _reconnectTimer: ReturnType<typeof setTimeout> | null;
  _shouldReconnect: boolean;

  // Actions
  connectWebSocket: () => void;
  startSession: (userId: string, userName: string) => void;
  endSession: () => void;
  sendEnergy: (ts: number, energy: number, isActive: boolean) => void;
  sendAudio: (data: string, seq: number) => void;
  sendTranscript: (ts: number, text: string, isFinal: boolean) => void;
  sendRingCommand: (cmd: RingCommand, ts: number) => void;
  dismissHook: () => void;
  handleServerMessage: (msg: ServerMessage) => void;
  disconnect: () => void;
  reset: () => void;
  _sendMessage: (msg: ClientMessage) => void;
}

function scheduleReconnect(get: () => SessionState, set: (p: Partial<SessionState>) => void) {
  const { _shouldReconnect, _reconnectAttempts } = get();
  if (!_shouldReconnect) return;

  const delay = Math.min(
    RECONNECT_BASE_DELAY * Math.pow(2, _reconnectAttempts),
    RECONNECT_MAX_DELAY,
  );
  const timer = setTimeout(() => {
    set({ _reconnectTimer: null });
    get().connectWebSocket();
  }, delay);
  set({ _reconnectTimer: timer, _reconnectAttempts: _reconnectAttempts + 1 });
}

export const useSessionStore = create<SessionState>((set, get) => ({
  wsConnection: null,
  connectionStatus: 'disconnected',
  error: null,

  sessionId: null,
  userId: null,
  isSessionActive: false,
  sessionStartTime: null,

  currentScore: null,
  currentHook: null,
  transcript: [],
  interimText: '',
  paceUpdate: null,
  segmentEnd: null,

  _reconnectAttempts: 0,
  _reconnectTimer: null,
  _shouldReconnect: false,

  connectWebSocket: () => {
    const { wsConnection, _reconnectTimer } = get();

    // 清理已有连接
    if (wsConnection && (wsConnection.readyState === WebSocket.OPEN || wsConnection.readyState === WebSocket.CONNECTING)) {
      return;
    }
    if (_reconnectTimer) {
      clearTimeout(_reconnectTimer);
    }

    set({ connectionStatus: 'connecting', error: null, _shouldReconnect: true });

    let ws: WebSocket;
    try {
      // 构建相对 WS URL（适配 https → wss）
      const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const url = `${proto}//${window.location.host}${SESSION_WS_PATH}`;
      ws = new WebSocket(url);
    } catch (e) {
      set({ connectionStatus: 'error', error: `WebSocket 创建失败: ${(e as Error).message}` });
      scheduleReconnect(get, set);
      return;
    }

    ws.binaryType = 'arraybuffer';

    ws.onopen = () => {
      set({
        wsConnection: ws,
        connectionStatus: 'connected',
        error: null,
        _reconnectAttempts: 0,
      });
    };

    ws.onmessage = (event) => {
      try {
        const raw = typeof event.data === 'string' ? event.data : '';
        if (!raw) return;
        const msg = JSON.parse(raw) as ServerMessage;
        if (msg.type === 'score' || msg.type === 'score_update' || msg.type === 'asr_transcript' || msg.type === 'hook') {
          console.log('[SessionStore] ←', msg.type, msg.type === 'score' ? `f=${msg.fluency} l=${msg.logic} p=${msg.pace}` : msg.type === 'score_update' ? `logic=${msg.logic}` : msg.type === 'asr_transcript' ? `"${msg.text?.slice(0, 30)}"` : msg.hookType);
        }
        get().handleServerMessage(msg);
      } catch {
        // 忽略无法解析的消息
      }
    };

    ws.onerror = () => {
      set({ connectionStatus: 'error', error: 'WebSocket 连接异常' });
    };

    ws.onclose = () => {
      set({ wsConnection: null });
      const { _shouldReconnect } = get();
      if (_shouldReconnect) {
        set({ connectionStatus: 'connecting', error: null });
        scheduleReconnect(get, set);
      } else {
        set({ connectionStatus: 'disconnected' });
      }
    };

    set({ wsConnection: ws });
  },

  startSession: (userId, userName) => {
    const { wsConnection, connectionStatus } = get();
    if (!wsConnection || wsConnection.readyState !== WebSocket.OPEN) {
      set({
        error:
          connectionStatus === 'connecting'
            ? '正在连接服务器，请稍候再点「开始演讲」'
            : '未连接到服务器，请先等待右上角显示「已连接」',
      });
      // 尝试重连
      get().connectWebSocket();
      return;
    }
    set({ userId, error: null, transcript: [], interimText: '', currentScore: null, currentHook: null, paceUpdate: null, segmentEnd: null });
    get()._sendMessage({
      type: 'session_control',
      action: 'start',
      userId,
      userName,
    });
  },

  endSession: () => {
    get()._sendMessage({ type: 'session_control', action: 'end' });
    // 关闭后不再自动重连
    set({ _shouldReconnect: false, isSessionActive: false });
  },

  sendEnergy: (ts, energy, isActive) => {
    get()._sendMessage({ type: 'energy', ts, energy, isActive });
  },

  sendAudio: (data, seq) => {
    get()._sendMessage({ type: 'audio', data, seq });
  },

  sendTranscript: (ts, text, isFinal) => {
    // 本地即时回显：转写显示不依赖后端评分回传，保证实时性
    if (text && text.trim()) {
      if (isFinal) {
        set((state) => ({
          transcript: [...state.transcript, { text: text.trim(), ts, speaker: 0 }].slice(-200),
          interimText: '',
        }));
      } else {
        set({ interimText: text });
      }
    }
    get()._sendMessage({ type: 'transcript', ts, text, isFinal });
  },

  sendRingCommand: (cmd, ts) => {
    get()._sendMessage({ type: 'ring', cmd, ts });
  },

  dismissHook: () => {
    set({ currentHook: null });
  },

  handleServerMessage: (msg) => {
    switch (msg.type) {
      case 'session_started':
        set({
          sessionId: msg.sessionId,
          userId: msg.userId,
          sessionStartTime: msg.startTime,
          isSessionActive: true,
          error: null,
        });
        break;

      case 'asr_transcript':
        // StepAudio ASR 转写 → 转写流
        if (msg.isFinal && msg.text?.trim()) {
          set((state) => ({
            transcript: [...state.transcript, { text: msg.text.trim(), ts: msg.ts, speaker: msg.speaker ?? 0 }].slice(-200),
            interimText: '',
          }));
        } else if (msg.text?.trim()) {
          set({ interimText: msg.text });
        }
        break;

      case 'score':
        set({
          currentScore: {
            fluency: msg.fluency,
            logic: msg.logic,
            pace: msg.pace,
            ts: msg.ts,
          },
        });
        // 若 score 带文本且转写流还没有，补一条
        if (msg.text?.trim()) {
          set((state) => {
            const last = state.transcript[state.transcript.length - 1];
            if (last && last.text === msg.text.trim()) return state;
            return {
              transcript: [...state.transcript, { text: msg.text.trim(), ts: msg.ts, speaker: 0 }].slice(-200),
            };
          });
        }
        break;

      case 'score_update':
        // LLM 逻辑性补丁：更新当前评分的 logic
        set((state) => {
          if (!state.currentScore) return state;
          return {
            currentScore: {
              ...state.currentScore,
              logic: msg.logic,
              ts: msg.ts,
            },
          };
        });
        break;

      case 'hook':
        set({
          currentHook: {
            hookType: msg.hookType,
            hookText: msg.hookText,
            countdown: msg.countdown,
            ts: msg.ts,
            active: true,
          },
        });
        break;

      case 'relay_status':
        if (msg.status === 'error') {
          set({ error: msg.message ? `StepAudio: ${msg.message}` : 'StepAudio 连接异常' });
        } else if (msg.status === 'connected') {
          // 清除仅与 relay 相关的临时错误
          const { error } = get();
          if (error?.startsWith('StepAudio')) set({ error: null });
        }
        break;

      case 'recovery':
        // 恢复确认后清除钩子（若已恢复）
        if (msg.recovered) {
          set({ currentHook: null });
        }
        break;

      case 'segment_end':
        set({ segmentEnd: msg.summary });
        break;

      case 'pace_update':
        set({
          paceUpdate: {
            paceScore: msg.paceScore,
            charsPerSec: msg.charsPerSec,
            pauseRate: msg.pauseRate,
          },
        });
        break;

      case 'ring_feedback':
        // 可用于 UI 反馈；当前仅消费
        break;

      case 'session_ended':
        set({ isSessionActive: false });
        break;

      case 'error':
        set({ error: msg.message });
        break;

      default:
        // 类型穷尽保护
        break;
    }
  },

  disconnect: () => {
    const { wsConnection, _reconnectTimer } = get();
    set({ _shouldReconnect: false });
    if (_reconnectTimer) clearTimeout(_reconnectTimer);
    if (wsConnection) {
      wsConnection.onclose = null;
      wsConnection.onerror = null;
      wsConnection.onmessage = null;
      wsConnection.onopen = null;
      if (wsConnection.readyState === WebSocket.OPEN || wsConnection.readyState === WebSocket.CONNECTING) {
        wsConnection.close();
      }
    }
    set({
      wsConnection: null,
      connectionStatus: 'disconnected',
      _reconnectTimer: null,
      _reconnectAttempts: 0,
    });
  },

  reset: () => {
    get().disconnect();
    set({
      sessionId: null,
      userId: null,
      isSessionActive: false,
      sessionStartTime: null,
      currentScore: null,
      currentHook: null,
      transcript: [],
      interimText: '',
      paceUpdate: null,
      segmentEnd: null,
      error: null,
    });
  },

  _sendMessage: (msg) => {
    const { wsConnection } = get();
    if (wsConnection && wsConnection.readyState === WebSocket.OPEN) {
      wsConnection.send(JSON.stringify(msg));
    }
  },
}));
