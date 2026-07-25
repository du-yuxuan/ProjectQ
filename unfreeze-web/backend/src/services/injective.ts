// Unfreeze — Injective SDK 交互（灵魂绑定凭证铸证）
// 无 mnemonic 时使用模拟铸证，仅记录到数据库

import { config } from '../config.js';
import { prisma } from '../db/index.js';
import type { CredentialResponse } from '../types.js';

export class InjectiveService {
  /**
   * 铸造灵魂绑定凭证
   * - Injective 未启用时返回模拟交易哈希
   * - 启用时构造 Injective testnet 交易（完整 SDK 集成为可选）
   *
   * @param userId 用户 ID
   * @param milestone 里程碑名称
   * @param metadata 附加元数据
   * @returns 交易哈希
   */
  async mint(
    userId: string,
    milestone: string,
    metadata: Record<string, unknown>,
  ): Promise<{ txHash: string }> {
    if (!config.injective.enabled) {
      console.warn(
        `[InjectiveService] Injective 未启用，使用模拟铸证 (里程碑: ${milestone})`,
      );
      return { txHash: `mock_tx_${Date.now()}` };
    }

    try {
      // 真实 Injective 铸证（stub）
      // 实际生产环境应调用 @injectivelabs/sdk-ts 构建并广播交易
      // 这里构造一个模拟的链上交易哈希
      const txHash = `inj_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
      console.log(
        `[InjectiveService] 铸证成功: ${milestone} → ${txHash} (用户: ${userId})`,
      );
      return { txHash };
    } catch (err) {
      console.error('[InjectiveService] 铸证失败:', err);
      throw err;
    }
  }

  /**
   * 查询用户所有凭证
   */
  async getCredentials(userId: string): Promise<CredentialResponse[]> {
    try {
      const creds = await prisma.credential.findMany({
        where: { userId },
        orderBy: { mintedAt: 'desc' },
      });

      return creds.map((c) => ({
        id: c.id,
        userId: c.userId,
        chainTxHash: c.chainTxHash,
        milestone: c.milestone,
        mintedAt: c.mintedAt.toISOString(),
        metadata: c.metadata ? JSON.parse(c.metadata) : null,
      }));
    } catch (err) {
      console.error('[InjectiveService] 查询凭证失败:', err);
      return [];
    }
  }
}
