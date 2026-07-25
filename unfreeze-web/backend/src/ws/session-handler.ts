// Unfreeze — /ws/session WebSocket 连接处理
// 等待 session_control start → 创建 Orchestrator → 路由后续消息

import WebSocket from 'ws';
import type { IncomingMessage } from 'http';
import { v4 as uuidv4 } from 'uuid';
import { Orchestrator } from '../services/orchestrator.js';
import { prisma } from '../db/index.js';
import type {
  ClientMessage,
  SessionControlMessage,
  SessionStartMessage,
} from '../types.js';

/**
 * 处理 /ws/session WebSocket 连接
 */
export async function handleSessionConnection(
  ws: WebSocket,
  _req: IncomingMessage,
): Promise<void> {
  const sessionId = uuidv4();
  let orchestrator: Orchestrator | null = null;
  let sessionStarted = false;

  console.log(`[SessionHandler] 新连接，分配 sessionId: ${sessionId}`);

  ws.on('message', (data) => {
    const messageText = data.toString();
    let msg: ClientMessage;

    try {
      msg = JSON.parse(messageText) as ClientMessage;
    } catch {
      ws.send(JSON.stringify({ type: 'error', message: '消息格式错误' }));
      return;
    }

    // 尚未启动会话，等待 session_control start
    if (!sessionStarted) {
      if (msg.type === 'session_control' && msg.action === 'start') {
        handleSessionStart(ws, msg, sessionId)
          .then((orch) => {
            orchestrator = orch;
            sessionStarted = true;
          })
          .catch((err) => {
            console.error('[SessionHandler] 启动会话失败:', err);
            ws.send(
              JSON.stringify({ type: 'error', message: '会话启动失败' }),
            );
          });
      } else {
        ws.send(
          JSON.stringify({
            type: 'error',
            message: '请先发送 session_control start 消息',
          }),
        );
      }
      return;
    }

    // 路由后续消息到 Orchestrator
    if (orchestrator) {
      orchestrator.handleClientMessage(msg);
    }
  });

  ws.on('close', () => {
    console.log(`[SessionHandler] 连接关闭: ${sessionId}`);
    if (orchestrator && sessionStarted) {
      orchestrator
        .endSession()
        .catch((err) =>
          console.error('[SessionHandler] 结束会话失败:', err),
        );
    }
  });

  ws.on('error', (err: Error) => {
    console.error(`[SessionHandler] WS 错误 (${sessionId}):`, err.message);
  });
}

/**
 * 处理会话启动：创建用户、会话记录，初始化 Orchestrator
 */
async function handleSessionStart(
  ws: WebSocket,
  msg: SessionControlMessage,
  sessionId: string,
): Promise<Orchestrator> {
  const userId = msg.userId || uuidv4();
  const userName = msg.userName;

  try {
    // 创建或更新用户
    await prisma.user.upsert({
      where: { id: userId },
      update: { name: userName },
      create: { id: userId, name: userName },
    });

    // 创建会话记录
    await prisma.session.create({
      data: { id: sessionId, userId },
    });
  } catch (err) {
    console.error('[SessionHandler] 创建用户/会话记录失败:', err);
    // 不中断流程，继续启动会话
  }

  // 创建并启动 Orchestrator
  const orchestrator = new Orchestrator(ws, sessionId, userId);
  await orchestrator.start();

  // 发送会话开始确认
  const startMsg: SessionStartMessage = {
    type: 'session_started',
    sessionId,
    userId,
    startTime: new Date().toISOString(),
  };
  ws.send(JSON.stringify(startMsg));

  console.log(
    `[SessionHandler] 会话 ${sessionId} 已启动 (用户: ${userId})`,
  );

  return orchestrator;
}
