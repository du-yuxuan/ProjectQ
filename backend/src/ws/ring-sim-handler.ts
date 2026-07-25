// Unfreeze — /ws/ring-sim WebSocket 连接处理（调试通道）
// 简单回环指环命令，用于独立调试指环模拟

import WebSocket from 'ws';
import type { IncomingMessage } from 'http';
import type { RingCommandMessage, RingFeedbackMessage } from '../types.js';

/**
 * 处理 /ws/ring-sim WebSocket 连接
 * 将收到的 ring 命令回环为 ring_feedback 确认消息
 */
export async function handleRingSimConnection(
  ws: WebSocket,
  _req: IncomingMessage,
): Promise<void> {
  console.log('[RingSimHandler] 指环模拟连接已建立');

  ws.on('message', (data) => {
    try {
      const msg = JSON.parse(data.toString()) as RingCommandMessage;

      if (msg.type !== 'ring') return;

      // 回环确认
      const feedback: RingFeedbackMessage = {
        type: 'ring_feedback',
        cmd: msg.cmd,
        ts: msg.ts,
        acknowledged: true,
      };

      ws.send(JSON.stringify(feedback));
    } catch (err) {
      console.error('[RingSimHandler] 处理消息失败:', err);
    }
  });

  ws.on('close', () => {
    console.log('[RingSimHandler] 指环模拟连接已关闭');
  });

  ws.on('error', (err: Error) => {
    console.error('[RingSimHandler] WS 错误:', err.message);
  });
}
