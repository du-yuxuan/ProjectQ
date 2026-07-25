// Q v17 — 心率路由

import { Router } from 'express';
import { prisma } from '../db/index.js';

export const heartRateRouter = Router();

// 获取用户心率历史
heartRateRouter.get('/history/:userId', async (req, res) => {
  try {
    const records = await prisma.heartRateRecord.findMany({
      where: { userId: req.params.userId },
      orderBy: { ts: 'asc' },
      take: 500,
    });
    res.json(records);
  } catch (err) {
    res.status(500).json({ error: '获取心率历史失败' });
  }
});

// 获取会话内心率记录
heartRateRouter.get('/session/:sessionId', async (req, res) => {
  try {
    const records = await prisma.heartRateRecord.findMany({
      where: { sessionId: req.params.sessionId },
      orderBy: { ts: 'asc' },
    });
    res.json(records);
  } catch (err) {
    res.status(500).json({ error: '获取会话心率记录失败' });
  }
});
