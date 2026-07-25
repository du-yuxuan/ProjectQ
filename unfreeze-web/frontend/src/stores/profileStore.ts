// Unfreeze — 画像/复盘数据状态（Zustand）
// 管理 REST API 拉取的画像、会话详情、凭证等数据

import { create } from 'zustand';
import {
  getCredentials,
  getProfile,
  getSession,
  getSessionList,
  getTrend,
  mintCredential,
} from '../lib/api';
import type {
  CredentialResponse,
  ProfileResponse,
  SessionDetailResponse,
  SessionListItem,
  TrendPoint,
} from '../lib/types';

interface ProfileState {
  currentProfile: ProfileResponse | null;
  currentSession: SessionDetailResponse | null;
  sessionList: SessionListItem[];
  credentials: CredentialResponse[];
  trend: TrendPoint[];
  loading: boolean;
  error: string | null;

  loadProfile: (userId: string) => Promise<void>;
  loadSession: (id: string) => Promise<void>;
  loadSessionList: (userId: string) => Promise<void>;
  loadTrend: (userId: string) => Promise<void>;
  loadCredentials: (userId: string) => Promise<void>;
  mintCredential: (userId: string, milestone: string) => Promise<void>;
  clearError: () => void;
}

export const useProfileStore = create<ProfileState>((set, get) => ({
  currentProfile: null,
  currentSession: null,
  sessionList: [],
  credentials: [],
  trend: [],
  loading: false,
  error: null,

  loadProfile: async (userId) => {
    set({ loading: true, error: null });
    try {
      const profile = await getProfile(userId);
      const trend = await getTrend(userId).catch(() => [] as TrendPoint[]);
      set({ currentProfile: profile, trend, loading: false });
    } catch (e) {
      set({ loading: false, error: (e as Error).message });
    }
  },

  loadSession: async (id) => {
    set({ loading: true, error: null });
    try {
      const session = await getSession(id);
      set({ currentSession: session, loading: false });
    } catch (e) {
      set({ loading: false, error: (e as Error).message });
    }
  },

  loadSessionList: async (userId) => {
    set({ loading: true, error: null });
    try {
      const list = await getSessionList(userId);
      set({ sessionList: list, loading: false });
    } catch (e) {
      set({ loading: false, error: (e as Error).message });
    }
  },

  loadTrend: async (userId) => {
    try {
      const trend = await getTrend(userId);
      set({ trend });
    } catch (e) {
      set({ error: (e as Error).message });
    }
  },

  loadCredentials: async (userId) => {
    set({ loading: true, error: null });
    try {
      const creds = await getCredentials(userId);
      set({ credentials: creds, loading: false });
    } catch (e) {
      set({ loading: false, error: (e as Error).message });
    }
  },

  mintCredential: async (userId, milestone) => {
    set({ loading: true, error: null });
    try {
      const cred = await mintCredential(userId, milestone);
      set({ credentials: [...get().credentials, cred], loading: false });
    } catch (e) {
      set({ loading: false, error: (e as Error).message });
    }
  },

  clearError: () => set({ error: null }),
}));
