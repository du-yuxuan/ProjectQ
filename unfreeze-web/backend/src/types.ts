// Unfreeze — 全部 TypeScript 类型定义
// 前后端共享的 WS 消息协议 & REST 响应类型

// ============================================================
// WebSocket 上行消息（浏览器 → 后端）
// ============================================================

export type ClientMessage =
  | AudioFrameMessage
  | EnergyReportMessage
  | RingCommandMessage
  | SessionControlMessage
  | TranscriptMessage;

/** 音频帧：base64 编码的 16kHz 16bit PCM */
export interface AudioFrameMessage {
  type: 'audio';
  data: string;
  seq: number;
}

/** 客户端 AudioContext 计算的能量包络 */
export interface EnergyReportMessage {
  type: 'energy';
  ts: number; // 会话内秒数
  energy: number; // 0-1 归一化
  isActive: boolean; // 是否有声
}

/** 指环模拟命令 */
export interface RingCommandMessage {
  type: 'ring';
  cmd: RingCommand;
  ts: number;
}

export type RingCommand =
  | 'rotate_back'
  | 'rotate_front'
  | 'wave'
  | 'single_click'
  | 'double_click';

/** 会话控制（开始/结束） */
export interface SessionControlMessage {
  type: 'session_control';
  action: 'start' | 'end';
  userId?: string;
  userName?: string;
}

/** 浏览器 Web Speech API 实时转写结果 */
export interface TranscriptMessage {
  type: 'transcript';
  ts: number; // 会话内秒数
  text: string; // 转写的文本
  isFinal: boolean; // 是否为最终结果（vs 中间结果）
}

// ============================================================
// WebSocket 下行消息（后端 → 浏览器）
// ============================================================

export type ServerMessage =
  | ScoreMessage
  | ScoreUpdateMessage
  | HookMessage
  | RecoveryMessage
  | SegmentEndMessage
  | PaceUpdateMessage
  | SessionStartMessage
  | SessionEndAckMessage
  | RingFeedbackMessage
  | AsrTranscriptMessage
  | RelayStatusMessage
  | ErrorMessage;

/** StepAudio ASR 用户转写（实时转写流） */
export interface AsrTranscriptMessage {
  type: 'asr_transcript';
  ts: number;
  text: string;
  isFinal: boolean;
  /** 说话人 ID（1, 2, 3...），未开启分离时为 0 */
  speaker?: number;
}

/** StepAudio 连接状态（前端可显示） */
export interface RelayStatusMessage {
  type: 'relay_status';
  status: 'connecting' | 'connected' | 'error' | 'closed';
  message?: string;
}

/** 评分消息（本地评分引擎或 LLM 生成） */
export interface ScoreMessage {
  type: 'score';
  ts: number;
  fluency: number; // 0-10
  logic: number; // 0-10（本地启发式，LLM 结果到达后由 score_update 覆盖）
  pace: number; // 0-10
  fillers: number; // 填充词次数
  pauses: number; // 停顿次数
  text: string; // 转写片段
}

/** LLM 逻辑性评分补丁（异步覆盖本地 logic 分数） */
export interface ScoreUpdateMessage {
  type: 'score_update';
  ts: number; // 对应原始 score 的 ts
  logic: number; // LLM 评估的逻辑性 0-10
  reason?: string; // 简短理由（可选）
}

/** 兜底钩子 */
export interface HookMessage {
  type: 'hook';
  ts: number;
  hookType: HookType;
  hookText: string; // ≤10字
  countdown: number; // 倒计时秒数
}

export type HookType = '开口' | '思路' | '衔接' | '节奏';

/** 恢复确认 */
export interface RecoveryMessage {
  type: 'recovery';
  ts: number;
  responseTimeMs: number;
  recovered: boolean;
}

/** 段结束汇总 */
export interface SegmentEndMessage {
  type: 'segment_end';
  ts: number;
  summary: {
    avgFluency: number;
    avgLogic: number;
    duration: number;
    fillers: number;
    pauses: number;
  };
}

/** 本地计算的语速更新 */
export interface PaceUpdateMessage {
  type: 'pace_update';
  ts: number;
  paceScore: number; // 0-10
  charsPerSec: number;
  pauseRate: number;
}

/** 会话开始确认 */
export interface SessionStartMessage {
  type: 'session_started';
  sessionId: string;
  userId: string;
  startTime: string;
}

/** 会话结束确认 */
export interface SessionEndAckMessage {
  type: 'session_ended';
  sessionId: string;
  reportUrl: string;
}

/** 指环状态反馈 */
export interface RingFeedbackMessage {
  type: 'ring_feedback';
  cmd: RingCommand;
  ts: number;
  acknowledged: boolean;
}

/** 错误消息 */
export interface ErrorMessage {
  type: 'error';
  message: string;
}

// ============================================================
// REST API 类型
// ============================================================

/** 会话详情响应 */
export interface SessionDetailResponse {
  id: string;
  userId: string;
  startTime: string;
  endTime: string | null;
  duration: number | null;
  overallScore: number | null;
  transcript: string | null;
  segments: SegmentData[];
  hookEvents: HookEventData[];
}

export interface SegmentData {
  id: string;
  ts: number;
  duration: number;
  fluencyScore: number | null;
  logicScore: number | null;
  paceScore: number | null;
  fillerCount: number | null;
  pauseCount: number | null;
  text: string | null;
}

export interface HookEventData {
  id: string;
  ts: number;
  hookType: string;
  hookText: string;
  countdown: number;
  responseTimeMs: number | null;
  recovered: boolean | null;
  feedback: number | null;
}

/** 会话列表项 */
export interface SessionListItem {
  id: string;
  startTime: string;
  duration: number | null;
  overallScore: number | null;
  hookCount: number;
}

/** 用户能力画像 */
export interface ProfileResponse {
  userId: string;
  metrics: {
    fluencyAvg: number;
    logicAvg: number;
    paceAvg: number;
    sessionsCount: number;
    totalDuration: number;
  };
  weaknesses: string[];
  strengths: string[];
  trendData: TrendPoint[];
  snapshotDate: string;
}

export interface TrendPoint {
  date: string;
  fluencyAvg: number;
  logicAvg: number;
  paceAvg: number;
}

/** 凭证 */
export interface CredentialResponse {
  id: string;
  userId: string;
  chainTxHash: string;
  milestone: string;
  mintedAt: string;
  metadata: Record<string, unknown> | null;
}

// ============================================================
// 前端 Store 类型
// ============================================================

export interface LiveScore {
  fluency: number;
  logic: number;
  pace: number;
  ts: number;
}

export interface LiveHook {
  hookType: HookType;
  hookText: string;
  countdown: number;
  ts: number;
  active: boolean;
}

export interface TranscriptEntry {
  text: string;
  ts: number;
  /** 说话人 ID（1, 2, 3...），未开启分离时为 0 */
  speaker?: number;
}
