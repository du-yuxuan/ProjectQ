// Q v17 — Express + WebSocket 入口
// PICO 中心化架构：HTTP REST API + /ws/session + /ws/ring-sim
// v17 新增：钱包路由、心率路由

import express from 'express';
import cors from 'cors';
import http from 'http';
import { WebSocketServer } from 'ws';
import { config } from './config.js';
import { prisma } from './db/index.js';
import { handleSessionConnection } from './ws/session-handler.js';
import { handleRingSimConnection } from './ws/ring-sim-handler.js';
import { sessionRouter } from './routes/session.js';
import { profileRouter } from './routes/profile.js';
import { credentialRouter } from './routes/credential.js';
import { walletRouter } from './routes/wallet.js';
import { heartRateRouter } from './routes/heart-rate.js';

const app = express();

// ============================================================
// 中间件
// ============================================================
app.use(cors());
app.use(express.json({ limit: '10mb' }));

// ============================================================
// REST 路由
// ============================================================
app.use('/api/session', sessionRouter);
app.use('/api/profile', profileRouter);
app.use('/api/credential', credentialRouter);
app.use('/api/wallet', walletRouter);
app.use('/api/heart-rate', heartRateRouter);

// 健康检查
app.get('/api/health', (_req, res) => {
  res.json({
    status: 'ok',
    version: 'v17',
    timestamp: new Date().toISOString(),
    asrMode: !!config.iflytek.appId,
    llmMode: !!config.stepLlm.apiKey,
    injectiveEnabled: config.injective.enabled,
    speciesMapping: config.spatial.speciesMapping,
    audienceFeedback: config.spatial.audienceFeedback,
  });
});

// ============================================================
// HTTP 服务器
// ============================================================
const server = http.createServer(app);

// ============================================================
// WebSocket: /ws/session + /ws/ring-sim
// ============================================================
const sessionWss = new WebSocketServer({ noServer: true });
const ringSimWss = new WebSocketServer({ noServer: true });

server.on('upgrade', (request, socket, head) => {
  const { pathname } = new URL(request.url || '', 'http://localhost');
  if (pathname === '/ws/session') {
    sessionWss.handleUpgrade(request, socket, head, (ws) => {
      sessionWss.emit('connection', ws, request);
    });
  } else if (pathname === '/ws/ring-sim') {
    ringSimWss.handleUpgrade(request, socket, head, (ws) => {
      ringSimWss.emit('connection', ws, request);
    });
  } else {
    socket.destroy();
  }
});

sessionWss.on('connection', (ws, req) => {
  handleSessionConnection(ws, req).catch((err) => {
    console.error('[App] /ws/session 处理失败:', err);
    ws.close();
  });
});

ringSimWss.on('connection', (ws, req) => {
  handleRingSimConnection(ws, req).catch((err) => {
    console.error('[App] /ws/ring-sim 处理失败:', err);
    ws.close();
  });
});

// ============================================================
// 启动
// ============================================================
async function main(): Promise<void> {
  try {
    await prisma.$connect();
    console.log('[App] 数据库连接成功');
  } catch (err) {
    console.error('[App] 数据库连接失败:', err);
    process.exit(1);
  }

  server.listen(config.port, () => {
    console.log('════════════════════════════════════════');
    console.log('  Q v17 后端服务已启动（PICO 中心化）');
    console.log('════════════════════════════════════════');
    console.log(`  HTTP:       http://localhost:${config.port}`);
    console.log(`  WS Session: ws://localhost:${config.port}/ws/session`);
    console.log(`  WS RingSim: ws://localhost:${config.port}/ws/ring-sim`);
    console.log(`  讯飞ASR:    ${config.iflytek.appId ? '✅ 已配置' : '⚡ 未配置（Mock）'}`);
    console.log(`  Step LLM:  ${config.stepLlm.apiKey ? '✅ 已配置' : '⚡ 未配置'}`);
    console.log(`  Injective:  ${config.injective.enabled ? '✅ 已启用' : '⚡ 未启用（模拟铸证）'}`);
    console.log(`  物种映射:   ${config.spatial.speciesMapping ? '✅ 已启用' : '⚡ 未启用'}`);
    console.log(`  观众反馈:   ${config.spatial.audienceFeedback ? '✅ 已启用' : '⚡ 未启用'}`);
    console.log('════════════════════════════════════════');
  });
}

process.on('SIGINT', async () => {
  console.log('\n[App] 正在关闭服务...');
  await prisma.$disconnect();
  server.close();
  process.exit(0);
});

main();
