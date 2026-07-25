// Q v17 — 钱包路由

import { Router } from 'express';
import { WalletService } from '../services/wallet.js';

export const walletRouter = Router();
const walletService = new WalletService();

// 获取钱包连接状态 + QR URI
walletRouter.get('/status/:userId', async (req, res) => {
  try {
    const status = await walletService.getStatusMessage(req.params.userId);
    res.json(status);
  } catch (err) {
    res.status(500).json({ error: '获取钱包状态失败' });
  }
});

// 获取用户钱包地址
walletRouter.get('/address/:userId', async (req, res) => {
  try {
    const address = await walletService.getWalletAddress(req.params.userId);
    res.json({ address });
  } catch (err) {
    res.status(500).json({ error: '获取钱包地址失败' });
  }
});
