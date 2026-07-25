// Q v17 后端配置 — PICO 中心化架构
// 移除 Dify（阶跃星辰直接调用）、移除 Rokid（PICO 承担全部感知+显示）
// 新增：钱包登录、心率手动输入、物种映射、观众反馈

import dotenv from 'dotenv';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
dotenv.config({ path: path.resolve(__dirname, '../.env') });

export const config = {
  port: parseInt(process.env.PORT || '3001', 10),

  // 讯飞实时语音转写（ASR）+ 韵律
  iflytek: {
    appId: process.env.IFLYTEK_APPID || '',
    apiKey: process.env.IFLYTEK_API_KEY || '',
    apiSecret: process.env.IFLYTEK_API_SECRET || '',
    wsUrl: process.env.IFLYTEK_WS_URL || 'wss://office-api-ast-dx.iflyaisol.com',
    sampleRate: 16000,
    roleType: parseInt(process.env.IFLYTEK_ROLE_TYPE || '2', 10),
    maxSpeakers: parseInt(process.env.IFLYTEK_MAX_SPEAKERS || '4', 10),
    debounceWindowS: parseFloat(process.env.IFLYTEK_DEBOUNCE_WINDOW_S || '0.8'),
    minSwitchIntervalS: parseFloat(process.env.IFLYTEK_MIN_SWITCH_INTERVAL_S || '1.5'),
  },

  // 阶跃星辰（直接调用，无 Dify 中间层）
  stepLlm: {
    apiKey: process.env.STEPAUDIO_API_KEY || '',
    url: process.env.STEPAUDIO_LLM_URL || 'https://api.stepfun.com/v1/chat/completions',
    model: process.env.STEPAUDIO_LLM_MODEL || 'step-3.7-flash',
    logicEvalIntervalS: parseFloat(process.env.STEPAUDIO_LOGIC_INTERVAL_S || '5'),
  },

  databaseUrl: process.env.DATABASE_URL || 'file:./dev.db',

  // Injective 链上凭证（v17 真实 SDK 集成）
  injective: {
    rpc: process.env.INJECTIVE_RPC || 'https://testnet.sentry.tm.injective.network:443',
    rest: process.env.INJECTIVE_REST || 'https://testnet.sentry.tm.injective.network:443',
    mnemonic: process.env.INJECTIVE_MNEMONIC || '',
    chainId: process.env.INJECTIVE_CHAIN_ID || 'injective-888',
    enabled: !!process.env.INJECTIVE_MNEMONIC,
    // 预部署的 CosmWasm 合约地址（测试网）
    contractAddress: process.env.INJECTIVE_CONTRACT_ADDRESS || '',
    // 钱包连接 WebSocket Bridge URL (WalletConnect)
    walletConnectBridge: process.env.WALLETCONNECT_BRIDGE || 'https://bridge.walletconnect.org',
  },

  // v17 新增：心率手动输入面板
  heartRate: {
    // 紧张度阈值（bpm）：>120 标记紧张时刻，>160 标记恐慌
    tensionThreshold: parseInt(process.env.HR_TENSION_THRESHOLD || '120', 10),
    panicThreshold: parseInt(process.env.HR_PANIC_THRESHOLD || '160', 10),
    calmBaseline: parseInt(process.env.HR_CALM_BASELINE || '70', 10),
  },

  // v17 新增：PICO 空间渲染推送
  spatial: {
    // 是否启用物种化身自动映射
    speciesMapping: process.env.SPECIES_MAPPING !== 'false',
    // 是否启用观众反馈转写（来自 PICO 摄像头 SpatialML）
    audienceFeedback: process.env.AUDIENCE_FEEDBACK !== 'false',
  },
} as const;
