// Unfreeze — 用户画像 REST API 路由
// GET /api/profile/:userId        — 返回完整画像
// GET /api/profile/:userId/trend  — 返回趋势数据

import { Router } from 'express';
import { ProfileEngine } from '../services/profile-engine.js';

export const profileRouter = Router();

// GET /api/profile/:userId — 返回用户能力画像
profileRouter.get('/:userId', async (req, res) => {
  try {
    const engine = new ProfileEngine();
    const profile = await engine.computeProfile(req.params.userId);
    res.json(profile);
  } catch (err) {
    console.error('[ProfileRoute] 查询画像失败:', err);
    res.status(500).json({ error: '服务器错误' });
  }
});

// GET /api/profile/:userId/trend — 返回趋势数据
profileRouter.get('/:userId/trend', async (req, res) => {
  try {
    const engine = new ProfileEngine();
    const profile = await engine.computeProfile(req.params.userId);
    res.json(profile.trendData);
  } catch (err) {
    console.error('[ProfileRoute] 查询趋势失败:', err);
    res.status(500).json({ error: '服务器错误' });
  }
});
