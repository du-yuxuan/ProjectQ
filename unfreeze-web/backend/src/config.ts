// Unfreeze 后端配置

import dotenv from 'dotenv';
dotenv.config();

export const config = {
  port: parseInt(process.env.PORT || '3001', 10),

  // 讯飞实时语音转写（ASR）
  iflytek: {
    appId: process.env.IFLYTEK_APPID || '',
    apiKey: process.env.IFLYTEK_API_KEY || '',
    apiSecret: process.env.IFLYTEK_API_SECRET || '',
    wsUrl: process.env.IFLYTEK_WS_URL || 'wss://office-api-ast-dx.iflyaisol.com',
    // 讯飞要求 16kHz 16bit PCM
    sampleRate: 16000,
    // 说话人分离：0 关闭（默认），2 开启实时角色分离（盲分模式）
    roleType: parseInt(process.env.IFLYTEK_ROLE_TYPE || '2', 10),
    // 说话人平滑参数
    maxSpeakers: parseInt(process.env.IFLYTEK_MAX_SPEAKERS || '4', 10),
    debounceWindowS: parseFloat(process.env.IFLYTEK_DEBOUNCE_WINDOW_S || '0.8'),
    minSwitchIntervalS: parseFloat(process.env.IFLYTEK_MIN_SWITCH_INTERVAL_S || '1.5'),
  },

  // 阶跃星辰 Step-3.7-Flash（LLM 分析）
  stepLlm: {
    apiKey: process.env.STEPAUDIO_API_KEY || '',
    url: process.env.STEPAUDIO_LLM_URL || 'https://api.stepfun.com/v1/chat/completions',
    model: process.env.STEPAUDIO_LLM_MODEL || 'step-3.7-flash',
    // 逻辑性评判间隔（秒）：距上次评判不足此值则跳过
    logicEvalIntervalS: parseFloat(process.env.STEPAUDIO_LOGIC_INTERVAL_S || '5'),
  },

  databaseUrl: process.env.DATABASE_URL || 'file:./dev.db',

  injective: {
    rpc: process.env.INJECTIVE_RPC || 'https://testnet-rpc.injective.dev',
    mnemonic: process.env.INJECTIVE_MNEMONIC || '',
    chainId: process.env.INJECTIVE_CHAIN_ID || 'injective-888',
    enabled: !!process.env.INJECTIVE_MNEMONIC,
  },
} as const;
