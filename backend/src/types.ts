// Q v17 全部 TypeScript 类型定义
// PICO 中心化架构 — 前后端共享的 WS 消息协议 & REST 响应类型
// v17 变更：移除手机端消息、新增心率/钱包/物种/观众反馈消息

// ============================================================
// WebSocket 上行消息（PICO Unity → 后端）
// ============================================================

export type ClientMessage =
  | AudioFrameMessage
  | EnergyReportMessage
  | RingCommandMessage
  | SessionControlMessage
  | TranscriptMessage
  | HeartRateMessage        // v17 新增
  | WalletConnectMessage    // v17 新增
  | AudienceFeedbackMessage // v17 新增
  | CredentialMintMessage;  // v17 新增

/** 音频帧：base64 编码的 16kHz 16bit PCM */
export interface AudioFrameMessage {
  type: 'audio';
  data: string;
  seq: number;
}

/** PICO AudioContext 计算的能量包络 */
export interface EnergyReportMessage {
  type: 'energy';
  ts: number;
  energy: number;
  isActive: boolean;
}

/** 指环命令（BLE → PICO → 后端） */
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
  walletAddress?: string; // v17: 会话开始时可绑定钱包地址
}

/** 转写结果（本地 Web Speech API 或讯飞 ASR） */
export interface TranscriptMessage {
  type: 'transcript';
  ts: number;
  text: string;
  isFinal: boolean;
}

/** v17 新增：心率手动输入 */
export interface HeartRateMessage {
  type: 'heart_rate';
  ts: number;
  bpm: number;
  userId?: string; // v17: 可选，编排器已持有 userId
  source?: string;  // 来源标记（如 manual_panel）
}

/** v17 新增：钱包连接 */
export interface WalletConnectMessage {
  type: 'wallet_connect';
  action: 'connect' | 'disconnect' | 'status';
  address?: string;     // 钱包地址
  walletType?: string;   // keplr | metamask | walletconnect | leap | ledger
}

/** v17 新增：观众反馈（PICO 摄像头 SpatialML 检测结果） */
export interface AudienceFeedbackMessage {
  type: 'audience_feedback';
  ts: number;
  faceCount: number;   // 检测到的人脸数
  attentive: number;   // 专注人数
  distracted: number;   // 走神人数
}

/** v17 新增：手动触发铸证（演示用） */
export interface CredentialMintMessage {
  type: 'mint_credential';
  milestone: string;   // 里程碑类型
  metrics?: Record<string, number>;
}

// ============================================================
// WebSocket 下行消息（后端 → PICO Unity）
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
  | ErrorMessage
  | HeartRateUpdateMessage    // v17 新增
  | WalletStatusMessage       // v17 新增
  | WalletConnectUriMessage   // v17 新增
  | SpeciesUpdateMessage      // v17 新增
  | CredentialMintedMessage;  // v17 新增

/** ASR 用户转写（实时转写流） */
export interface AsrTranscriptMessage {
  type: 'asr_transcript';
  ts: number;
  text: string;
  isFinal: boolean;
  speaker?: number;
}

/** Relay 连接状态 */
export interface RelayStatusMessage {
  type: 'relay_status';
  status: 'connecting' | 'connected' | 'error' | 'closed';
  message?: string;
}

/** 评分消息 */
export interface ScoreMessage {
  type: 'score';
  ts: number;
  fluency: number;  // 0-10
  logic: number;    // 0-10
  pace: number;     // 0-10
  fillers: number;
  pauses: number;
  text: string;
  reception?: number; // v17: 观众接收度 0-10
}

/** LLM 逻辑性评分补丁 */
export interface ScoreUpdateMessage {
  type: 'score_update';
  ts: number;
  logic: number;
  reason?: string;
}

/** 兜底钩子 */
export interface HookMessage {
  type: 'hook';
  ts: number;
  hookType: HookType;
  hookText: string;
  countdown: number;
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
  paceScore: number;
  charsPerSec: number;
  pauseRate: number;
}

/** 会话开始确认 */
export interface SessionStartMessage {
  type: 'session_started';
  sessionId: string;
  userId: string;
  startTime: string;
  walletAddress?: string; // v17: 已连接的钱包地址
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
// v17 新增下行消息
// ============================================================

/** 心率更新推送（PICO 空间显示紧张度） */
export interface HeartRateUpdateMessage {
  type: 'heart_rate_update';
  ts: number;
  bpm: number;
  tension: 'calm' | 'normal' | 'tense' | 'panic';
}

/** 钱包状态更新（PICO 空间显示连接状态） */
export interface WalletStatusMessage {
  type: 'wallet_status';
  connected: boolean;
  address?: string;
  walletType?: string;
  qrUri?: string; // WalletConnect QR 内容
}

/** WalletConnect QR 推送（主动推送二维码内容给 PICO 显示） */
export interface WalletConnectUriMessage {
  type: 'wallet_connect_uri';
  uri: string; // WalletConnect 连接 URI
}

/** 物种映射更新（PICO 空间渲染物种化身） */
export interface SpeciesUpdateMessage {
  type: 'species_update';
  speaker: number;          // 说话人 ID
  species: SpeciesType;     // 物种类型
  emotion: string;          // 情绪标签
  confidence: number;       // 置信度 0-1
}

export type SpeciesType =
  | 'tiger'    // 老虎（咄咄逼人）
  | 'rabbit'   // 兔子（温和）
  | 'owl'      // 猫头鹰（缜密）
  | 'fox'      // 狐狸（活跃）
  | 'lion'     // 狮子（强势）
  | 'wolf'     // 狼（突然强硬）
  | 'deer'     // 鹿（中性/友好）
  | 'default'; // 默认

/** 凭证铸造成功通知（PICO 空间 3D 卡片展示）
 *  字段名与 C# CredentialMintedMessage 对齐：
 *  chainTxHash（非 txHash）、metadata 为嵌套对象 */
export interface CredentialMintedMessage {
  type: 'credential_minted';
  chainTxHash: string;
  milestone: string;
  metadata: {
    credential_type: string;
    level: string;
    fluency: number;
    logic: number;
    reception: number;
    stall_rate: number;
    improvement: string;
    soulbound: boolean;
  };
}

// ============================================================
// REST API 类型
// ============================================================

export interface SessionDetailResponse {
  id: string;
  userId: string;
  startTime: string;
  endTime: string | null;
  duration: number | null;
  overallScore: number | null;
  transcript: string | null;
  walletAddress: string | null;
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
  receptionScore: number | null;
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

export interface SessionListItem {
  id: string;
  startTime: string;
  duration: number | null;
  overallScore: number | null;
  hookCount: number;
}

export interface ProfileResponse {
  userId: string;
  metrics: {
    fluencyAvg: number;
    logicAvg: number;
    paceAvg: number;
    receptionAvg?: number;
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
  receptionAvg?: number;
}

export interface CredentialResponse {
  id: string;
  userId: string;
  chainTxHash: string;
  milestone: string;
  mintedAt: string;
  metadata: Record<string, unknown> | null;
}
