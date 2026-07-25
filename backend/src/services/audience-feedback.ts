// Q v17 — 观众反馈服务（PICO 摄像头 SpatialML）
// 接收 PICO 摄像头的人脸检测结果，分析专注/走神比例 → 接收度分
// 叠加到模块1 三维评分（流畅度/逻辑度/接收度）

import { prisma } from '../db/index.js';

export interface AudienceFeedbackResult {
  receptionScore: number; // 0-10
  attentive: number;
  distracted: number;
  total: number;
}

export class AudienceFeedbackService {
  private sessionId: string | null = null;

  /** 观众反馈历史（用于趋势分析） */
  private history: Array<{
    ts: number;
    attentive: number;
    distracted: number;
  }> = [];

  setSessionId(sessionId: string): void {
    this.sessionId = sessionId;
  }

  /**
   * 处理 PICO 摄像头观众反馈
   * @param ts 会话内秒数
   * @param faceCount 检测到的总人脸数
   * @param attentive 专注人数
   * @param distracted 走神人数
   * @returns 接收度评分结果
   */
  async handleFeedback(
    ts: number,
    faceCount: number,
    attentive: number,
    distracted: number,
  ): Promise<AudienceFeedbackResult> {
    // 记录历史
    this.history.push({ ts, attentive, distracted });
    // 保留最近 60 秒
    this.history = this.history.filter((h) => h.ts >= ts - 60);

    // 计算接收度评分
    const receptionScore = this.calculateReception(attentive, distracted);

    // 持久化到数据库
    try {
      if (this.sessionId) {
        await prisma.audienceFeedback.create({
          data: {
            sessionId: this.sessionId,
            ts: Math.round(ts),
            attentive,
            distracted,
            receptionScore,
          },
        });
      }
    } catch (err) {
      console.error('[AudienceFeedbackService] 记录失败:', err);
    }

    return {
      receptionScore,
      attentive,
      distracted,
      total: faceCount,
    };
  }

  /**
   * 计算接收度评分（0-10）
   * 专注率 = 专注人数 / 总人数
   * 专注率 100% → 10分，专注率 50% → 5分
   */
  private calculateReception(attentive: number, distracted: number): number {
    const total = attentive + distracted;
    if (total === 0) return 5; // 无人时给中性分
    const rate = attentive / total;
    return Math.round(rate * 10);
  }

  /**
   * 获取会话平均接收度
   */
  getAverageReception(): number {
    if (this.history.length === 0) return 5;
    const scores = this.history.map((h) => {
      const total = h.attentive + h.distracted;
      return total === 0 ? 5 : (h.attentive / total) * 10;
    });
    return Math.round(scores.reduce((a, b) => a + b, 0) / scores.length);
  }

  /**
   * 获取观众走神时刻列表（复盘报告用）
   */
  getDistractionMoments(): Array<{ ts: number; distracted: number; attentive: number }> {
    return this.history
      .filter((h) => {
        const total = h.attentive + h.distracted;
        return total > 0 && h.distracted / total > 0.4; // 走神率 > 40%
      })
      .map((h) => ({ ts: h.ts, distracted: h.distracted, attentive: h.attentive }));
  }

  /** 重置 */
  reset(): void {
    this.history = [];
  }
}
