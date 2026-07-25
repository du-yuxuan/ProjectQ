// Q v17 — 凭证 REST API 路由
// GET  /api/credential/list/:userId — 查询用户所有凭证
// POST /api/credential/mint         — 手动铸证（需钱包地址）

import { Router } from 'express';
import { InjectiveService } from '../services/injective.js';
import { prisma } from '../db/index.js';
import type { CredentialResponse } from '../types.js';

export const credentialRouter = Router();

// GET /api/credential/list/:userId — 查询用户凭证
credentialRouter.get('/list/:userId', async (req, res) => {
  try {
    const service = new InjectiveService();
    const creds = await service.getCredentials(req.params.userId);
    res.json(creds);
  } catch (err) {
    console.error('[CredentialRoute] 查询凭证失败:', err);
    res.status(500).json({ error: '服务器错误' });
  }
});

// POST /api/credential/mint — 手动铸证
credentialRouter.post('/mint', async (req, res) => {
  try {
    const body = req.body as { userId?: string; milestone?: string };

    if (!body.userId || !body.milestone) {
      res.status(400).json({ error: '缺少 userId 或 milestone' });
      return;
    }

    const { userId, milestone } = body;

    // 检查是否已有该里程碑凭证
    const existing = await prisma.credential.findFirst({
      where: { userId, milestone },
    });
    if (existing) {
      res.status(409).json({ error: '该里程碑凭证已存在', credential: existing });
      return;
    }

    // 查询用户钱包地址
    const wallet = await prisma.walletAddress.findFirst({
      where: { userId },
      orderBy: { connectedAt: 'desc' },
    });
    const walletAddress = wallet?.address ?? '';
    if (!walletAddress) {
      res.status(400).json({ error: '用户未连接钱包，无法铸证' });
      return;
    }

    // 铸证
    const service = new InjectiveService();
    const metadata = {
      credentialType: '表达能力认证',
      level: '手动铸证',
      metrics: { fluency: 0, logic: 0, reception: 0, stallRate: 0 },
      improvement: '手动触发铸证',
    };
    const result = await service.mint(userId, walletAddress, milestone, metadata);

    // 保存到数据库
    const cred = await prisma.credential.create({
      data: {
        userId,
        chainTxHash: result.txHash,
        milestone,
        metadata: JSON.stringify(metadata),
      },
    });

    const response: CredentialResponse = {
      id: cred.id,
      userId: cred.userId,
      chainTxHash: cred.chainTxHash,
      milestone: cred.milestone,
      mintedAt: cred.mintedAt.toISOString(),
      metadata,
    };

    res.status(201).json(response);
  } catch (err) {
    console.error('[CredentialRoute] 铸证失败:', err);
    res.status(500).json({ error: '服务器错误' });
  }
});
