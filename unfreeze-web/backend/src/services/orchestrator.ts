// Unfreeze — 会话编排核心
// 串联语速计算、卡壳检测、StepAudio 代理、数据库持久化、里程碑铸证

import { v4 as uuidv4 } from 'uuid';
import WebSocket from 'ws';
import { config } from '../config.js';
import { prisma } from '../db/index.js';
import type {
  ClientMessage,
  EnergyReportMessage,
  PaceUpdateMessage,
  RingCommandMessage,
  ScoreMessage,
  ScoreUpdateMessage,
  ServerMessage,
  SessionEndAckMessage,
  TranscriptMessage,
} from '../types.js';
import { PaceCalculator } from './pace-calculator.js';
import { StutterDetector, type HookTrigger } from './stutter-detector.js';
import {
  AnalysisRelay,
  type RelayOutbound,
} from './analysis-relay.js';
import { StepLlmAnalyzer, type RescuePhrases } from './step-llm.js';
import { ProfileEngine } from './profile-engine.js';
import { InjectiveService } from './injective.js';
import { computeRealScore, TranscriptProcessor } from './score-engine.js';

/** 待持久化的段数据 */
interface PendingSegment {
  ts: number;
  duration: number;
  fluencyScore: number;
  logicScore: number;
  paceScore: number;
  fillerCount: number;
  pauseCount: number;
  text: string;
}

/** 待持久化的钩子事件 */
interface PendingHookEvent {
  ts: number;
  hookType: string;
  hookText: string;
  countdown: number;
  responseTimeMs: number | null;
  recovered: boolean | null;
  feedback: number | null;
}

/** 默认语速基线（舒适区中值） */
const DEFAULT_BASELINE_PACE = 3.5;

/** 语速更新间隔（秒） */
const PACE_UPDATE_INTERVAL_S = 1;

export class Orchestrator {
  private ws: WebSocket;
  private sessionId: string;
  private userId: string;
  private sessionStartTime: number;

  paceCalc: PaceCalculator;
  stutterDetector: StutterDetector;
  relay: AnalysisRelay;
  /** LLM 分析器（用于逻辑评分 + 救场话术生成） */
  private llm: StepLlmAnalyzer;

  private ended = false;
  private transcriptText = '';
  private pendingSegments: PendingSegment[] = [];
  private hookEvents: PendingHookEvent[] = [];
  private lastPaceUpdateTs = 0;
  private lastScoreTs = 0;
  private transcriptProcessor = new TranscriptProcessor();
  private realScoreTimer: ReturnType<typeof setInterval> | null = null;
  private pauseCounter = 0;
  /** 本地 VAD 兜底：跟踪上一帧是否有声，用于句末 commit */
  private lastEnergyActive = false;
  private lastCommitTs = 0;
  /** 最近转写文本缓冲（用于救场话术生成的上下文） */
  private recentTranscript = '';
  /** LLM 预生成的救场话术缓存（卡壳触发时使用） */
  private rescueCache: Partial<RescuePhrases> = {};
  /** 最近的语速评分（用于自适应倒计时） */
  private recentPaceScore: number | undefined;

  constructor(ws: WebSocket, sessionId: string, userId: string) {
    this.ws = ws;
    this.sessionId = sessionId;
    this.userId = userId;
    this.sessionStartTime = Date.now();

    this.paceCalc = new PaceCalculator();
    this.stutterDetector = new StutterDetector();
    this.llm = new StepLlmAnalyzer();
    this.relay = new AnalysisRelay(
      (msg) => this.handleRelayOutbound(msg),
      this.sessionStartTime,
    );
    this.relay.setPaceCalc(this.paceCalc);
  }

  /**
   * 初始化会话
   * 架构：讯飞实时ASR → Step-3.7-Flash LLM分析 + 本地算法兜底
   */
  async start(): Promise<void> {
    try {
      await this.relay.connect();
      console.log(
        `[Orchestrator] 会话 ${this.sessionId} 已启动 (讯飞ASR + Step LLM + 本地算法)`,
      );
    } catch (err) {
      console.warn(
        '[Orchestrator] Relay 连接失败，降级本地模式:',
        (err as Error).message,
      );
      this.sendToClient({
        type: 'error',
        message: `连接失败，已降级本地模式: ${(err as Error).message}`,
      });
      // 降级本地
      this.realScoreTimer = setInterval(() => this.tryGenerateRealScore(), 2000);
    }
  }

  /**
   * 路由客户端 WS 消息
   */
  handleClientMessage(msg: ClientMessage): void {
    switch (msg.type) {
      case 'audio':
        this.relay.sendAudio(msg.data);
        break;

      case 'energy':
        this.handleEnergyReport(msg);
        break;

      case 'transcript':
        this.handleTranscript(msg);
        break;

      case 'ring':
        this.handleRingCommand(msg);
        break;

      case 'session_control':
        if (msg.action === 'end') {
          this.endSession().catch((err) =>
            console.error('[Orchestrator] 结束会话失败:', err),
          );
        }
        break;
    }
  }

  /**
   * 结束会话：关闭 relay、计算汇总、持久化到 DB、检查里程碑
   */
  async endSession(): Promise<void> {
    if (this.ended) return;
    this.ended = true;

    if (this.realScoreTimer) {
      clearInterval(this.realScoreTimer);
      this.realScoreTimer = null;
    }
    this.relay.close();

    const endTime = new Date();
    const duration = Math.round((Date.now() - this.sessionStartTime) / 1000);

    // 计算最终评分
    const avgFluency = this.avg(
      this.pendingSegments.map((s) => s.fluencyScore),
    );
    const avgLogic = this.avg(
      this.pendingSegments.map((s) => s.logicScore),
    );
    const avgPace = this.avg(
      this.pendingSegments.map((s) => s.paceScore),
    );
    const overallScore =
      this.pendingSegments.length > 0
        ? Math.round((avgFluency + avgLogic + avgPace) / 3)
        : null;

    try {
      // 1. 更新会话记录
      await prisma.session.update({
        where: { id: this.sessionId },
        data: {
          endTime,
          duration,
          overallScore,
          transcript: this.transcriptText || null,
        },
      });

      // 2. 保存段记录
      for (const seg of this.pendingSegments) {
        await prisma.segment.create({
          data: {
            sessionId: this.sessionId,
            ts: seg.ts,
            duration: seg.duration,
            fluencyScore: seg.fluencyScore,
            logicScore: seg.logicScore,
            paceScore: seg.paceScore,
            fillerCount: seg.fillerCount,
            pauseCount: seg.pauseCount,
            text: seg.text,
          },
        });
      }

      // 3. 保存钩子事件
      for (const hook of this.hookEvents) {
        await prisma.hookEvent.create({
          data: {
            sessionId: this.sessionId,
            ts: hook.ts,
            hookType: hook.hookType,
            hookText: hook.hookText,
            countdown: hook.countdown,
            responseTimeMs: hook.responseTimeMs,
            recovered: hook.recovered,
            feedback: hook.feedback,
          },
        });
      }

      // 4. 计算并保存画像快照
      const profileEngine = new ProfileEngine();
      const profile = await profileEngine.computeProfile(this.userId);
      await profileEngine.saveSnapshot(this.userId, profile);

      // 5. 检查里程碑并铸证
      await this.checkMilestones(this.sessionId, this.userId);

      // 6. 发送会话结束确认
      const endAck: SessionEndAckMessage = {
        type: 'session_ended',
        sessionId: this.sessionId,
        reportUrl: `/api/session/${this.sessionId}`,
      };
      this.sendToClient(endAck);

      console.log(
        `[Orchestrator] 会话 ${this.sessionId} 已结束 (时长: ${duration}s, 评分: ${overallScore ?? 'N/A'})`,
      );
    } catch (err) {
      console.error('[Orchestrator] 持久化失败:', err);
      this.sendToClient({ type: 'error', message: '会话结束处理失败' });
    }
  }

  // ============================================================
  // 私有方法 — 消息处理
  // ============================================================

  /**
   * 处理能量报告：喂给语速计算器和卡壳检测器，发送语速更新
   */
  private handleEnergyReport(msg: EnergyReportMessage): void {
    this.paceCalc.addEnergyReport(msg.ts, msg.energy, msg.isActive);

    // 注意：server_vad 模式下服务端禁止 input_audio_buffer.commit
    // （会报 "commit when server vad"），句末提交完全交给 StepAudio VAD
    this.lastEnergyActive = msg.isActive;

    // 语速每秒更新一次
    const shouldUpdatePace = msg.ts - this.lastPaceUpdateTs >= PACE_UPDATE_INTERVAL_S;
    let paceScore: number | undefined;

    if (shouldUpdatePace) {
      const paceResult = this.paceCalc.calculate(msg.ts);
      paceScore = paceResult.paceScore;
      this.recentPaceScore = paceScore;
      this.lastPaceUpdateTs = msg.ts;

      const paceMsg: PaceUpdateMessage = {
        type: 'pace_update',
        ts: msg.ts,
        paceScore: paceResult.paceScore,
        charsPerSec: paceResult.charsPerSec,
        pauseRate: paceResult.pauseRate,
      };
      this.sendToClient(paceMsg);
    }

    // 卡壳检测器每次都运行（需要精确的停顿检测）
    // 传入预生成的救场话术和最近语速评分
    const hook = this.stutterDetector.update(
      {
        ts: msg.ts,
        isActive: msg.isActive,
        energy: msg.energy,
        paceScore,
        baselinePace: DEFAULT_BASELINE_PACE,
      },
      this.rescueCache,
      this.recentPaceScore,
    );

    if (hook) {
      this.sendHookToClient(hook);
    }
  }

  /**
   * 处理浏览器 Web Speech API 转写结果
   * 喂给语速计算器，累积文本，触发真实评分
   */
  private handleTranscript(msg: TranscriptMessage): void {
    // 喂给语速计算器（用真实转写文本）
    this.paceCalc.addTranscriptText(msg.text, msg.ts);

    // 累积转写文本
    if (msg.isFinal) {
      this.transcriptText += msg.text;
      this.transcriptProcessor.addText(msg.text, true, msg.ts);

      // 喂给卡壳检测器（让思路/衔接规则能拿到文本）
      this.stutterDetector.update({
        ts: msg.ts,
        isActive: true,
        energy: 0.5,
        text: msg.text,
      });

      // 累积最近转写文本并异步预生成救场话术
      this.recentTranscript = (this.recentTranscript + msg.text).slice(-120);
      this.maybeRefreshRescuePhrases();
    }

    // 尝试生成评分
    this.tryGenerateRealScore();
  }

  /**
   * 尝试基于真实转写文本生成评分
   * 使用本地评分引擎（非 mock），基于实际语速、填充词、停顿率
   */
  private tryGenerateRealScore(): void {
    const ts = (Date.now() - this.sessionStartTime) / 1000;
    const textToScore = this.transcriptProcessor.shouldScore(ts);
    if (!textToScore) return;

    const score = computeRealScore(textToScore, this.paceCalc, ts, this.pauseCounter);
    if (!score) return;

    // 发送给客户端
    this.sendToClient(score);

    // 处理评分消息（累积段数据）
    this.handleScoreMessage(score);

    console.log(
      `[Orchestrator] 真实评分: fluency=${score.fluency} logic=${score.logic} pace=${score.pace} fillers=${score.fillers} text="${textToScore.slice(0, 30)}..."`,
    );
  }

  /**
   * 处理指环命令：发送确认，双击确认恢复
   */
  private handleRingCommand(msg: RingCommandMessage): void {
    // 发送确认反馈
    this.sendToClient({
      type: 'ring_feedback',
      cmd: msg.cmd,
      ts: msg.ts,
      acknowledged: true,
    });

    // 双击确认恢复
    if (msg.cmd === 'double_click') {
      const lastHook = this.hookEvents[this.hookEvents.length - 1];
      if (lastHook && lastHook.recovered === null) {
        const responseTimeMs = Math.round((msg.ts - lastHook.ts) * 1000);
        lastHook.recovered = true;
        lastHook.responseTimeMs = responseTimeMs;

        this.sendToClient({
          type: 'recovery',
          ts: msg.ts,
          responseTimeMs,
          recovered: true,
        });

        console.log(
          `[Orchestrator] 恢复确认: ${lastHook.hookType} (${responseTimeMs}ms)`,
        );
      }
    }
  }

  /**
   * 处理 StepAudio relay 出站事件（ASR / 评分 / 钩子 / 状态）
   */
  private handleRelayOutbound(msg: RelayOutbound): void {
    switch (msg.type) {
      case 'asr_transcript':
        this.handleAsrTranscript(msg);
        break;

      case 'score':
        // LLM 评分：转发给前端 + 落段
        this.sendToClient(msg);
        this.handleScoreMessage(msg);
        break;

      case 'score_update':
        // LLM 逻辑性补丁：转发给前端 + 更新最近一段的 logicScore
        this.sendToClient(msg);
        this.applyLogicUpdate(msg);
        break;

      case 'hook':
        this.sendToClient(msg);
        this.recordHookEvent({
          ts: msg.ts,
          hookType: msg.hookType,
          hookText: msg.hookText,
          countdown: msg.countdown,
        });
        break;

      case 'segment_end':
      case 'recovery':
        this.sendToClient(msg);
        break;

      case 'relay_status':
        this.sendToClient({
          type: 'relay_status',
          status: msg.status,
          message: msg.message,
        });
        break;

      case 'error':
        this.sendToClient(msg);
        break;

      default:
        break;
    }
  }

  /** ASR 用户转写 → 前端转写流 + 本地语速/评分兜底 */
  private handleAsrTranscript(msg: { type: 'asr_transcript'; ts: number; text: string; isFinal: boolean; speaker?: number }): void {
    // 转发给前端转写流（保留 speaker 字段）
    this.sendToClient({
      type: 'asr_transcript',
      ts: msg.ts,
      text: msg.text,
      isFinal: msg.isFinal,
      speaker: msg.speaker ?? 0,
    });

    if (!msg.text.trim()) return;

    if (msg.isFinal) {
      // 喂语速计算器 & 累积全文（relay 内部已喂，但 orchestrator 的 paceCalc 也需要）
      this.paceCalc.addTranscriptText(msg.text, msg.ts);
      this.transcriptText += msg.text;
      this.transcriptProcessor.addText(msg.text, true, msg.ts);

      // 喂给卡壳检测器（让思路/衔接规则能拿到文本）
      this.stutterDetector.update({
        ts: msg.ts,
        isActive: true,
        energy: 0.5,
        text: msg.text,
      });

      // 累积最近转写文本并异步预生成救场话术
      this.recentTranscript = (this.recentTranscript + msg.text).slice(-120);
      this.maybeRefreshRescuePhrases();
    }

    // 本地评分兜底
    this.tryGenerateRealScore();
  }

  /**
   * 处理评分消息：累积段数据（转写文本已由 ASR 路径写入）
   */
  private handleScoreMessage(msg: ScoreMessage): void {
    // 若 ASR 尚未写入该段文本，用 score.text 补全
    if (msg.text && !this.transcriptText.includes(msg.text)) {
      this.paceCalc.addTranscriptText(msg.text, msg.ts);
      this.transcriptText += msg.text;
    }

    const duration =
      this.lastScoreTs > 0
        ? Math.max(1, Math.round(msg.ts - this.lastScoreTs))
        : 3;
    this.lastScoreTs = msg.ts;

    this.pendingSegments.push({
      ts: Math.round(msg.ts),
      duration,
      fluencyScore: msg.fluency,
      logicScore: msg.logic,
      paceScore: msg.pace,
      fillerCount: msg.fillers,
      pauseCount: msg.pauses,
      text: msg.text,
    });
  }

  /**
   * 应用 LLM 逻辑性评分补丁：更新最近一段的 logicScore
   */
  private applyLogicUpdate(msg: ScoreUpdateMessage): void {
    const lastSeg = this.pendingSegments[this.pendingSegments.length - 1];
    if (lastSeg) {
      const oldLogic = lastSeg.logicScore;
      lastSeg.logicScore = msg.logic;
      console.log(
        `[Orchestrator] 逻辑性补丁: ${oldLogic} -> ${msg.logic}${msg.reason ? ` (${msg.reason})` : ''}`,
      );
    }
  }

  // ============================================================
  // 私有方法 - 里程碑与铸证
  // ============================================================

  /**
   * 检查里程碑并铸造凭证
   * - 首次演讲：用户首个完成的会话
   * - 高分演讲：overallScore >= 8
   * - 持续进步：3+ 个完成的会话
   */
  private async checkMilestones(
    sessionId: string,
    userId: string,
  ): Promise<void> {
    const injective = new InjectiveService();

    try {
      const sessions = await prisma.session.findMany({
        where: { userId, endTime: { not: null } },
        orderBy: { startTime: 'asc' },
      });

      const currentSession = sessions.find((s) => s.id === sessionId);

      // 首次演讲
      if (sessions.length === 1) {
        await this.mintCredential(injective, userId, '首次演讲', { sessionId });
      }

      // 高分演讲
      if (
        currentSession?.overallScore !== null &&
        currentSession?.overallScore !== undefined &&
        currentSession.overallScore >= 8
      ) {
        await this.mintCredential(injective, userId, '高分演讲', {
          sessionId,
          score: currentSession.overallScore,
        });
      }

      // 持续进步
      if (sessions.length >= 3) {
        await this.mintCredential(injective, userId, '持续进步', {
          sessionId,
          sessionCount: sessions.length,
        });
      }
    } catch (err) {
      console.error('[Orchestrator] 检查里程碑失败:', err);
    }
  }

  /**
   * 铸造凭证（检查重复，调用 InjectiveService）
   */
  private async mintCredential(
    injective: InjectiveService,
    userId: string,
    milestone: string,
    metadata: Record<string, unknown>,
  ): Promise<void> {
    try {
      // 检查是否已有该里程碑的凭证
      const existing = await prisma.credential.findFirst({
        where: { userId, milestone },
      });
      if (existing) {
        return;
      }

      const result = await injective.mint(userId, milestone, metadata);

      await prisma.credential.create({
        data: {
          id: uuidv4(),
          userId,
          chainTxHash: result.txHash,
          milestone,
          metadata: JSON.stringify(metadata),
        },
      });

      console.log(
        `[Orchestrator] 里程碑达成: ${milestone} (用户: ${userId}, 交易: ${result.txHash})`,
      );
    } catch (err) {
      console.error(`[Orchestrator] 铸证失败 (${milestone}):`, err);
    }
  }

  // ============================================================
  // 私有方法 — 工具
  // ============================================================

  /** 发送钩子消息给客户端并记录 */
  private sendHookToClient(hook: HookTrigger): void {
    this.sendToClient({
      type: 'hook',
      ts: hook.ts,
      hookType: hook.hookType,
      hookText: hook.hookText,
      countdown: hook.countdown,
    });

    this.recordHookEvent({
      ts: hook.ts,
      hookType: hook.hookType,
      hookText: hook.hookText,
      countdown: hook.countdown,
    });
  }

  /**
   * 异步预生成救场话术：基于最近转写内容，调用 LLM 生成四种卡壳场景的话术
   * - 在每段 final 转写到达时调用，但内置限流
   * - LLM 不可用或超时时不阻塞，降级到 StutterDetector 的本地兜底文案
   * - 生成结果缓存到 rescueCache，下次卡壳触发时直接使用
   */
  private maybeRefreshRescuePhrases(): void {
    if (!this.llm.isAvailable) return;
    if (this.recentTranscript.trim().length < 8) return;

    // 异步调用，不阻塞当前转写流
    this.llm.generateRescuePhrases(this.recentTranscript)
      .then((phrases) => {
        if (phrases) {
          this.rescueCache = phrases;
          console.log(
            `[Orchestrator] 救场话术已更新: 开口="${phrases['开口']}" 思路="${phrases['思路']}" 衔接="${phrases['衔接']}" 节奏="${phrases['节奏']}"`,
          );
        }
      })
      .catch((err) => {
        console.warn('[Orchestrator] 救场话术生成失败:', (err as Error).message);
      });
  }

  /** 记录钩子事件（用于恢复追踪和持久化） */
  private recordHookEvent(hook: {
    ts: number;
    hookType: string;
    hookText: string;
    countdown: number;
  }): void {
    this.hookEvents.push({
      ts: hook.ts,
      hookType: hook.hookType,
      hookText: hook.hookText,
      countdown: hook.countdown,
      responseTimeMs: null,
      recovered: null,
      feedback: null,
    });
  }

  /** 安全发送 WS 消息 */
  private sendToClient(msg: ServerMessage): void {
    if (this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  /** 计算数组平均值 */
  private avg(nums: number[]): number {
    if (nums.length === 0) return 0;
    return nums.reduce((a, b) => a + b, 0) / nums.length;
  }
}
