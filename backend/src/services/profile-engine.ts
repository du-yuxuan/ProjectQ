// Unfreeze — 用户能力画像引擎
// 从历史会话数据计算多维指标、弱项/优势诊断、趋势数据

import { prisma } from '../db/index.js';
import type { ProfileResponse, TrendPoint } from '../types.js';

export class ProfileEngine {
  /**
   * 计算用户能力画像
   * 聚合所有历史会话的段级评分，计算均值、弱项、优势、趋势
   */
  async computeProfile(userId: string): Promise<ProfileResponse> {
    const sessions = await prisma.session.findMany({
      where: { userId },
      orderBy: { startTime: 'asc' },
      include: { segments: true },
    });

    // 聚合段级评分
    let totalFluency = 0;
    let totalLogic = 0;
    let totalPace = 0;
    let totalReception = 0; // v17: 观众接收度
    let segmentCount = 0;
    let totalFillers = 0;
    let totalPauses = 0;
    let totalDuration = 0;

    const trendData: TrendPoint[] = [];

    for (const session of sessions) {
      let sFluency = 0;
      let sLogic = 0;
      let sPace = 0;
      let sReception = 0;
      let sCount = 0;

      for (const seg of session.segments) {
        if (seg.fluencyScore !== null) {
          totalFluency += seg.fluencyScore;
          sFluency += seg.fluencyScore;
          sCount++;
          segmentCount++;
        }
        if (seg.logicScore !== null) {
          totalLogic += seg.logicScore;
          sLogic += seg.logicScore;
        }
        if (seg.paceScore !== null) {
          totalPace += seg.paceScore;
          sPace += seg.paceScore;
        }
        // v17: 接收度分
        if (seg.receptionScore !== null) {
          totalReception += seg.receptionScore;
          sReception += seg.receptionScore;
        }
        if (seg.fillerCount !== null) totalFillers += seg.fillerCount;
        if (seg.pauseCount !== null) totalPauses += seg.pauseCount;
      }

      if (session.duration) totalDuration += session.duration;

      // 每个会话生成一个趋势点（v17: 含接收度）
      if (sCount > 0) {
        trendData.push({
          date: session.startTime.toISOString(),
          fluencyAvg: Math.round((sFluency / sCount) * 10) / 10,
          logicAvg: Math.round((sLogic / sCount) * 10) / 10,
          paceAvg: Math.round((sPace / sCount) * 10) / 10,
          receptionAvg: sCount > 0 ? Math.round((sReception / sCount) * 10) / 10 : undefined,
        });
      }
    }

    const fluencyAvg = segmentCount > 0 ? totalFluency / segmentCount : 0;
    const logicAvg = segmentCount > 0 ? totalLogic / segmentCount : 0;
    const paceAvg = segmentCount > 0 ? totalPace / segmentCount : 0;
    const receptionAvg = segmentCount > 0 ? totalReception / segmentCount : 0;
    const sessionsCount = sessions.length;
    const avgFillers = sessionsCount > 0 ? totalFillers / sessionsCount : 0;
    const avgPauses = sessionsCount > 0 ? totalPauses / sessionsCount : 0;

    // 诊断弱项
    const weaknesses: string[] = [];
    if (fluencyAvg < 7) weaknesses.push('流畅度不足');
    if (paceAvg < 7) weaknesses.push('语速控制待提升');
    if (receptionAvg < 6) weaknesses.push('观众接收度偏低'); // v17
    if (avgFillers > 10) weaknesses.push('语气词过多');
    if (avgPauses > 8) weaknesses.push('停顿频繁');

    // 诊断优势
    const strengths: string[] = [];
    if (fluencyAvg >= 8) strengths.push('表达流畅');
    if (logicAvg >= 8) strengths.push('逻辑清晰');
    if (paceAvg >= 8) strengths.push('语速适中');
    if (receptionAvg >= 8) strengths.push('观众参与度高'); // v17

    return {
      userId,
      metrics: {
        fluencyAvg: Math.round(fluencyAvg * 10) / 10,
        logicAvg: Math.round(logicAvg * 10) / 10,
        paceAvg: Math.round(paceAvg * 10) / 10,
        receptionAvg: Math.round(receptionAvg * 10) / 10, // v17
        sessionsCount,
        totalDuration,
      },
      weaknesses,
      strengths,
      trendData,
      snapshotDate: new Date().toISOString(),
    };
  }

  /**
   * 保存画像快照到数据库
   */
  async saveSnapshot(userId: string, profile: ProfileResponse): Promise<void> {
    try {
      await prisma.profileSnapshot.create({
        data: {
          userId,
          metrics: JSON.stringify(profile.metrics),
          weaknesses: JSON.stringify(profile.weaknesses),
          strengths: JSON.stringify(profile.strengths),
          trendData: JSON.stringify(profile.trendData),
        },
      });
      console.log(`[ProfileEngine] 画像快照已保存 (用户: ${userId})`);
    } catch (err) {
      console.error('[ProfileEngine] 保存画像快照失败:', err);
    }
  }

  /**
   * 检查用户已达成的里程碑（用于凭证铸证）
   * - 首次演讲：至少 1 个会话
   * - 高分演讲：任一会话 overallScore >= 8
   * - 持续进步：3+ 个会话
   */
  async checkMilestones(userId: string): Promise<string[]> {
    const milestones: string[] = [];

    try {
      const sessions = await prisma.session.findMany({
        where: { userId },
        orderBy: { startTime: 'asc' },
      });

      if (sessions.length >= 1) milestones.push('首次演讲');
      if (sessions.some((s) => s.overallScore !== null && s.overallScore >= 8)) {
        milestones.push('高分演讲');
      }
      if (sessions.length >= 3) milestones.push('持续进步');
    } catch (err) {
      console.error('[ProfileEngine] 检查里程碑失败:', err);
    }

    return milestones;
  }
}
