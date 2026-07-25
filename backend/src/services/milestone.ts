// Q v17 — 里程碑引擎
// 定义铸证触发条件，检测里程碑达成
// 流畅度突破 / 卡壳率下降 / 口头禅减少 / 综合里程碑

import { prisma } from '../db/index.js';

export interface MilestoneDefinition {
  type: string;
  name: string;
  level: string;
  check: (stats: SessionStats) => boolean;
}

export interface SessionStats {
  fluencyAvg: number;
  logicAvg: number;
  receptionAvg: number;
  paceAvg: number;
  overallScore: number | null;
  duration: number;
  fillerCount: number;
  pauseCount: number;
  hookCount: number;
  recoveredCount: number;
}

export interface MilestoneResult {
  milestone: string;
  level: string;
  credentialType: string;
  triggered: boolean;
  metadata: {
    credentialType: string;
    level: string;
    metrics: { fluency: number; logic: number; reception: number; stallRate: number };
    improvement: string;
  };
}

/** 里程碑定义表 */
const MILESTONES: MilestoneDefinition[] = [
  {
    type: 'first_speech',
    name: '首次演讲',
    level: '入门表达者',
    check: (_stats) => false, // 由 checkMilestones 中的会话数判断
  },
  {
    type: 'fluency_breakthrough',
    name: '流畅度突破',
    level: '流畅表达者',
    check: (stats) => stats.fluencyAvg > 80, // 注：评分 0-10 制，>8 视为 >80%
  },
  {
    type: 'stall_rate_down',
    name: '卡壳率下降',
    level: '进步显著',
    check: () => false, // 需要跨会话对比，由 checkMilestones 处理
  },
  {
    type: 'filler_reduction',
    name: '口头禅减少',
    level: '表达精炼',
    check: (stats) => {
      // 口头禅率 < 5次/分钟（假设会话 60 秒，fillerCount < 5）
      return stats.duration > 0 && stats.fillerCount / (stats.duration / 60) < 5;
    },
  },
  {
    type: 'comprehensive',
    name: '综合里程碑',
    level: '认证表达者',
    check: (stats) =>
      stats.fluencyAvg > 7.5 &&
      stats.logicAvg > 7.5 &&
      stats.receptionAvg > 7.5 &&
      stats.hookCount === 0, // 无卡壳
  },
  {
    type: 'high_score',
    name: '高分演讲',
    level: '优秀表达者',
    check: (stats) =>
      stats.overallScore !== null && stats.overallScore >= 8,
  },
];

export class MilestoneEngine {
  /**
   * 检查所有里程碑
   * @param sessionId 当前会话 ID
   * @param userId 用户 ID
   * @param stats 当前会话统计
   * @returns 触发的里程碑列表
   */
  async checkMilestones(
    sessionId: string,
    userId: string,
    stats: SessionStats,
  ): Promise<MilestoneResult[]> {
    const results: MilestoneResult[] = [];

    // 获取用户历史会话
    const sessions = await prisma.session.findMany({
      where: { userId, endTime: { not: null } },
      orderBy: { startTime: 'asc' },
      include: {
        segments: { select: { fluencyScore: true, logicScore: true, paceScore: true, fillerCount: true, receptionScore: true } },
        hookEvents: { select: { recovered: true, responseTimeMs: true } },
      },
    });

    const currentSession = sessions.find((s) => s.id === sessionId);

    // === 首次演讲 ===
    if (sessions.length === 1) {
      results.push(this.buildResult('first_speech', '首次演讲', '入门表达者', stats));
    }

    // === 高分演讲 ===
    if (
      currentSession?.overallScore !== null &&
      currentSession?.overallScore !== undefined &&
      currentSession.overallScore >= 8
    ) {
      results.push(this.buildResult('high_score', '高分演讲', '优秀表达者', stats));
    }

    // === 流畅度突破（连续 3 次会话流畅度 > 8）===
    if (sessions.length >= 3) {
      const last3 = sessions.slice(-3);
      const allFluent = last3.every((s) => {
        const avgFluency = s.segments.length > 0
          ? s.segments.reduce((a, seg) => a + (seg.fluencyScore ?? 0), 0) / s.segments.length
          : 0;
        return avgFluency > 8;
      });
      if (allFluent) {
        results.push(this.buildResult('fluency_breakthrough', '流畅度突破', '流畅表达者', stats));
      }
    }

    // === 卡壳率下降（较初始下降 > 50%）===
    if (sessions.length >= 2) {
      const firstSession = sessions[0];
      const lastSession = sessions[sessions.length - 1];
      const initialHookRate = firstSession.hookEvents.length > 0
        ? firstSession.hookEvents.length / Math.max(1, (firstSession.duration ?? 60) / 3600)
        : 0;
      const currentHookRate = lastSession.hookEvents.length > 0
        ? lastSession.hookEvents.length / Math.max(1, (lastSession.duration ?? 60) / 3600)
        : 0;
      if (initialHookRate > 0 && currentHookRate < initialHookRate * 0.5) {
        results.push(this.buildResult('stall_rate_down', '卡壳率下降', '进步显著', stats));
      }
    }

    // === 口头禅减少 ===
    const fillerRate = stats.duration > 0 ? stats.fillerCount / (stats.duration / 60) : 0;
    if (fillerRate < 5 && stats.duration > 30) {
      results.push(this.buildResult('filler_reduction', '口头禅减少', '表达精炼', stats));
    }

    // === 综合里程碑 ===
    if (
      stats.fluencyAvg > 7.5 &&
      stats.logicAvg > 7.5 &&
      stats.receptionAvg > 7.5 &&
      stats.hookCount === 0
    ) {
      results.push(this.buildResult('comprehensive', '综合里程碑', '认证表达者', stats));
    }

    // 过滤已铸造的
    const existing = await prisma.credential.findMany({
      where: { userId },
      select: { milestone: true },
    });
    const existingMilestones = new Set(existing.map((c) => c.milestone));

    return results.filter((r) => !existingMilestones.has(r.milestone));
  }

  /** 构建里程碑结果 */
  private buildResult(
    type: string,
    name: string,
    level: string,
    stats: SessionStats,
  ): MilestoneResult {
    const stallRate = stats.duration > 0
      ? (stats.hookCount / stats.duration) * 3600 // 卡壳次数/小时
      : 0;

    return {
      milestone: name,
      level,
      credentialType: '表达能力认证',
      triggered: true,
      metadata: {
        credentialType: '表达能力认证',
        level,
        metrics: {
          fluency: Math.round(stats.fluencyAvg * 10),
          logic: Math.round(stats.logicAvg * 10),
          reception: Math.round(stats.receptionAvg * 10),
          stallRate: Math.round(stallRate * 10) / 10,
        },
        improvement: `卡壳率 ${stats.hookCount}次/${Math.round(stats.duration / 60)}分钟`,
      },
    };
  }
}
