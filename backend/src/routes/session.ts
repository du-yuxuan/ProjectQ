// Unfreeze — 会话 REST API 路由
// GET  /api/session/list    — 会话列表（支持 ?userId= 筛选）
// GET  /api/session/:id     — 会话详情（含段和钩子事件）
// POST /api/session/end     — stub（实际通过 WS 处理）

import { Router } from 'express';
import { prisma } from '../db/index.js';
import type {
  HookEventData,
  SegmentData,
  SessionDetailResponse,
  SessionListItem,
} from '../types.js';

export const sessionRouter = Router();

// POST /api/session/end — stub（会话结束通过 WS session_control end 处理）
sessionRouter.post('/end', (_req, res) => {
  res.json({
    message: '会话结束请通过 WebSocket 发送 session_control end 消息',
  });
});

// GET /api/session/list — 会话列表
sessionRouter.get('/list', async (req, res) => {
  try {
    const userId =
      typeof req.query.userId === 'string' ? req.query.userId : undefined;

    const sessions = await prisma.session.findMany({
      where: userId ? { userId } : {},
      orderBy: { startTime: 'desc' },
      include: {
        _count: { select: { hookEvents: true } },
      },
    });

    const items: SessionListItem[] = sessions.map((s) => ({
      id: s.id,
      startTime: s.startTime.toISOString(),
      duration: s.duration,
      overallScore: s.overallScore,
      hookCount: s._count.hookEvents,
    }));

    res.json(items);
  } catch (err) {
    console.error('[SessionRoute] 查询会话列表失败:', err);
    res.status(500).json({ error: '服务器错误' });
  }
});

// GET /api/session/:id — 会话详情
sessionRouter.get('/:id', async (req, res) => {
  try {
    const session = await prisma.session.findUnique({
      where: { id: req.params.id },
      include: {
        segments: { orderBy: { ts: 'asc' } },
        hookEvents: { orderBy: { ts: 'asc' } },
      },
    });

    if (!session) {
      res.status(404).json({ error: '会话不存在' });
      return;
    }

    const response: SessionDetailResponse = {
      id: session.id,
      userId: session.userId,
      startTime: session.startTime.toISOString(),
      endTime: session.endTime?.toISOString() ?? null,
      duration: session.duration,
      overallScore: session.overallScore,
      transcript: session.transcript,
      walletAddress: session.walletAddress ?? null,
      segments: session.segments.map(
        (s): SegmentData => ({
          id: s.id,
          ts: s.ts,
          duration: s.duration,
          fluencyScore: s.fluencyScore,
          logicScore: s.logicScore,
          paceScore: s.paceScore,
          receptionScore: s.receptionScore,
          fillerCount: s.fillerCount,
          pauseCount: s.pauseCount,
          text: s.text,
        }),
      ),
      hookEvents: session.hookEvents.map(
        (h): HookEventData => ({
          id: h.id,
          ts: h.ts,
          hookType: h.hookType,
          hookText: h.hookText,
          countdown: h.countdown,
          responseTimeMs: h.responseTimeMs,
          recovered: h.recovered,
          feedback: h.feedback,
        }),
      ),
    };

    res.json(response);
  } catch (err) {
    console.error('[SessionRoute] 查询会话详情失败:', err);
    res.status(500).json({ error: '服务器错误' });
  }
});
