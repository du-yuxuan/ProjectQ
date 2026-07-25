// Q v17 — 心率服务（手动输入面板）
// 操作员通过 PICO 空间数字键盘手动输入心率值（如 70=平静/120=紧张/160=恐慌）
// 模拟不同场景下的心率变化，叠加到效果分和紧张度估算

import { config } from '../config.js';
import { prisma } from '../db/index.js';
import type { HeartRateUpdateMessage } from '../types.js';

export class HeartRateService {
  private userId: string;
  private sessionId: string | null = null;

  /** 心率历史（用于趋势分析） */
  private history: Array<{ ts: number; bpm: number }> = [];

  constructor(userId: string) {
    this.userId = userId;
  }

  setSessionId(sessionId: string): void {
    this.sessionId = sessionId;
  }

  /**
   * 处理心率输入
   * @param ts 会话内秒数
   * @param bpm 心率值
   * @returns 心率更新消息（推送给 PICO 空间显示）
   */
  async handleHeartRate(ts: number, bpm: number): Promise<HeartRateUpdateMessage> {
    // 记录历史
    this.history.push({ ts, bpm });
    // 保留最近 60 秒
    this.history = this.history.filter((h) => h.ts >= ts - 60);

    const tension = this.classifyTension(bpm);
    const trend = this.analyzeTrend();

    // 持久化到数据库
    try {
      await prisma.heartRateRecord.create({
        data: {
          userId: this.userId,
          sessionId: this.sessionId,
          bpm,
          ts: Math.round(ts),
          tension,
        },
      });
    } catch (err) {
      console.error('[HeartRateService] 记录失败:', err);
    }

    const msg: HeartRateUpdateMessage = {
      type: 'heart_rate_update',
      ts,
      bpm,
      tension,
    };

    console.log(
      `[HeartRateService] bpm=${bpm} tension=${tension}${trend !== 'stable' ? ` trend=${trend}` : ''}`,
    );

    return msg;
  }

  /**
   * 分类紧张度
   * calm: < 心率基线+20
   * normal: 基线+20 ~ 紧张阈值
   * tense: 紧张阈值 ~ 恐慌阈值
   * panic: > 恐慌阈值
   */
  private classifyTension(bpm: number): 'calm' | 'normal' | 'tense' | 'panic' {
    const { calmBaseline, tensionThreshold, panicThreshold } = config.heartRate;
    if (bpm < calmBaseline + 20) return 'calm';
    if (bpm < tensionThreshold) return 'normal';
    if (bpm < panicThreshold) return 'tense';
    return 'panic';
  }

  /**
   * 分析心率趋势（上升/下降/稳定）
   * 基于最近 30 秒的心率变化
   */
  private analyzeTrend(): 'rising' | 'falling' | 'stable' {
    if (this.history.length < 2) return 'stable';
    const recent = this.history.slice(-5);
    const first = recent[0].bpm;
    const last = recent[recent.length - 1].bpm;
    const delta = last - first;
    if (delta > 10) return 'rising';
    if (delta < -10) return 'falling';
    return 'stable';
  }

  /**
   * 获取紧张时刻列表（复盘报告用）
   * 心率超过紧张阈值的时间点
   */
  getTensionMoments(): Array<{ ts: number; bpm: number; tension: string }> {
    return this.history
      .filter((h) => h.bpm >= config.heartRate.tensionThreshold)
      .map((h) => ({
        ts: h.ts,
        bpm: h.bpm,
        tension: this.classifyTension(h.bpm),
      }));
  }

  /** 获取平均心率 */
  getAverageBpm(): number {
    if (this.history.length === 0) return config.heartRate.calmBaseline;
    const sum = this.history.reduce((a, h) => a + h.bpm, 0);
    return Math.round(sum / this.history.length);
  }

  /** 获取峰值心率 */
  getPeakBpm(): number {
    if (this.history.length === 0) return 0;
    return Math.max(...this.history.map((h) => h.bpm));
  }

  /** 重置 */
  reset(): void {
    this.history = [];
  }
}
