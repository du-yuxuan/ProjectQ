// Unfreeze — REST API 客户端
// 所有请求走 Vite 代理的相对路径（/api → http://localhost:3001）

import type {
  CredentialResponse,
  ProfileResponse,
  SessionDetailResponse,
  SessionListItem,
  TrendPoint,
} from './types';

const API_BASE = '/api';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
    ...init,
  });

  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`API ${res.status} ${res.statusText}: ${body || path}`);
  }

  // 204 / 空响应
  if (res.status === 204) {
    return undefined as T;
  }
  const text = await res.text();
  if (!text) return undefined as T;
  return JSON.parse(text) as T;
}

/** 获取会话详情（含 Segments + HookEvents） */
export function getSession(id: string): Promise<SessionDetailResponse> {
  return request<SessionDetailResponse>(`/session/${encodeURIComponent(id)}`);
}

/** 用户会话列表 */
export function getSessionList(userId: string): Promise<SessionListItem[]> {
  const qs = new URLSearchParams({ userId });
  return request<SessionListItem[]>(`/session/list?${qs.toString()}`);
}

/** 用户能力画像 */
export function getProfile(userId: string): Promise<ProfileResponse> {
  return request<ProfileResponse>(`/profile/${encodeURIComponent(userId)}`);
}

/** 进步曲线数据 */
export function getTrend(userId: string): Promise<TrendPoint[]> {
  return request<TrendPoint[]>(`/profile/${encodeURIComponent(userId)}/trend`);
}

/** 用户凭证列表 */
export function getCredentials(userId: string): Promise<CredentialResponse[]> {
  return request<CredentialResponse[]>(`/credential/list/${encodeURIComponent(userId)}`);
}

/** 手动铸证（里程碑自动调用） */
export function mintCredential(
  userId: string,
  milestone: string,
): Promise<CredentialResponse> {
  return request<CredentialResponse>(`/credential/mint`, {
    method: 'POST',
    body: JSON.stringify({ userId, milestone }),
  });
}

/** 提交钩子评价 */
export function submitFeedback(
  hookEventId: string,
  feedback: number,
): Promise<void> {
  return request<void>(`/feedback`, {
    method: 'POST',
    body: JSON.stringify({ hookEventId, feedback }),
  });
}
