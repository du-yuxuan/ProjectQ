// Q v17 — 钱包服务（WalletStrategy 登录）
// PICO 空间显示 WalletConnect 二维码 → 用户用手机钱包扫码 → 授权连接
// 钱包地址绑定到当前 PICO 会话，后续铸证直接铸造到该地址
//
// 支持钱包：Keplr / MetaMask / WalletConnect / Leap / Ledger
// WalletStrategy：Injective 官方推荐的钱包抽象层，统一接口

import { config } from '../config.js';
import { prisma } from '../db/index.js';
import type { WalletStatusMessage } from '../types.js';

export class WalletService {
  /**
   * 生成 WalletConnect 连接 URI
   * PICO 空间用此 URI 生成二维码图片显示
   */
  generateConnectUri(): string {
    // WalletConnect v2 协议的 URI 格式
    // 实际生产环境用 @walletconnect/sign-client 生成
    const bridge = config.injective.walletConnectBridge;
    const sessionId = `wc_${Date.now()}_${Math.random().toString(36).slice(2)}`;
    const uri = `wc:${sessionId}@2?relay-protocol=irn&symKey=${this.generateSymKey()}&bridge=${encodeURIComponent(bridge)}`;
    return uri;
  }

  /**
   * 绑定钱包地址到用户
   * PICO 端扫码授权后，钱包地址回传 Q 系统
   */
  async connectWallet(
    userId: string,
    address: string,
    walletType: string,
  ): Promise<WalletStatusMessage> {
    try {
      // 存储钱包地址
      await prisma.walletAddress.upsert({
        where: { address },
        create: {
          userId,
          address,
          walletType,
        },
        update: {
          userId,
          walletType,
          connectedAt: new Date(),
        },
      });

      console.log(
        `[WalletService] 钱包已连接: ${address} (${walletType}) → 用户 ${userId}`,
      );

      return {
        type: 'wallet_status',
        connected: true,
        address,
        walletType,
      };
    } catch (err) {
      console.error('[WalletService] 钱包连接失败:', err);
      return {
        type: 'wallet_status',
        connected: false,
      };
    }
  }

  /**
   * 断开钱包连接
   */
  async disconnectWallet(address: string): Promise<WalletStatusMessage> {
    try {
      await prisma.walletAddress.delete({
        where: { address },
      }).catch(() => {});
      console.log(`[WalletService] 钱包已断开: ${address}`);
      return {
        type: 'wallet_status',
        connected: false,
      };
    } catch (err) {
      console.error('[WalletService] 断开失败:', err);
      return {
        type: 'wallet_status',
        connected: false,
      };
    }
  }

  /**
   * 查询用户已绑定的钱包地址
   */
  async getWalletAddress(userId: string): Promise<string | null> {
    const wallet = await prisma.walletAddress.findFirst({
      where: { userId },
      orderBy: { connectedAt: 'desc' },
    });
    return wallet?.address ?? null;
  }

  /**
   * 获取钱包连接状态消息（含 QR URI）
   */
  getStatusMessage(userId: string): Promise<WalletStatusMessage> {
    return this.getWalletAddress(userId).then((address) => ({
      type: 'wallet_status',
      connected: !!address,
      address: address ?? undefined,
      qrUri: address ? undefined : this.generateConnectUri(),
    }));
  }

  /** 生成对称密钥（WalletConnect 协议用） */
  private generateSymKey(): string {
    const chars = '0123456789abcdef';
    let key = '';
    for (let i = 0; i < 64; i++) {
      key += chars[Math.floor(Math.random() * chars.length)];
    }
    return key;
  }
}
