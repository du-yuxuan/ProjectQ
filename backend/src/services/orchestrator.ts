// Q v17 — 会话编排核心（PICO 中心化）
// 串联：讯飞ASR → 评分 → 卡壳检测 → 递钩 → 物种映射 → 心率 → 观众反馈 → 画像 → 里程碑 → 铸证
//
// v17 变更：
// - 新增心率服务（手动输入）→ 叠加紧张度到效果分
// - 新增观众反馈服务（PICO 摄像头）→ 接收度分
// - 新增物种映射服务 → 推送 SpeciesUpdate 给 PICO 空间渲染
// - 新增钱包服务 → 会话绑定钱包地址
// - 里程碑引擎独立 → 触发 Injective 铸证 → 推送 CredentialMinted 给 PICO 3D 卡片

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
  HeartRateMessage,
  WalletConnectMessage,
  AudienceFeedbackMessage,
  CredentialMintMessage,
  HeartRateUpdateMessage,
  WalletStatusMessage,
  SpeciesUpdateMessage,
  CredentialMintedMessage,
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
import { WalletService } from './wallet.js';
import { HeartRateService } from './heart-rate.js';
import { AudienceFeedbackService } from './audience-feedback.js';
import { SpeciesMapper } from './species-mapper.js';
import { MilestoneEngine, type SessionStats } from './milestone.js';
import { computeRealScore, TranscriptProcessor } from './score-engine.js';

/** 待持久化的段数据 */
interface PendingSegment {
  ts: number;
  duration: number;
  fluencyScore: number;
  logicScore: number;
  paceScore: number;
  receptionScore: number;
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
  private llm: StepLlmAnalyzer;

  // v17 新增服务
  private walletService: WalletService;
  private heartRateService: HeartRateService;
  private audienceFeedbackService: AudienceFeedbackService;
  private speciesMapper: SpeciesMapper;
  private milestoneEngine: MilestoneEngine;
  private injective: InjectiveService;

  private ended = false;
  /** 会话结束回调（v17.1: 用于通知 session-handler 重置状态，允许同一连接重新启动会话）*/
  onEnded?: () => void;
  private transcriptText = '';
  private pendingSegments: PendingSegment[] = [];
  private hookEvents: PendingHookEvent[] = [];
  private lastPaceUpdateTs = 0;
  private lastScoreTs = 0;
  private transcriptProcessor = new TranscriptProcessor();
  private realScoreTimer: ReturnType<typeof setInterval> | null = null;
  private pauseCounter = 0;
  private lastEnergyActive = false;
  private lastCommitTs = 0;
  private recentTranscript = '';
  private rescueCache: Partial<RescuePhrases> = {};
  private recentPaceScore: number | undefined;
  private walletAddress: string | null = null;

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

    // v17 服务初始化
    this.walletService = new WalletService();
    this.heartRateService = new HeartRateService(userId);
    this.heartRateService.setSessionId(sessionId);
    this.audienceFeedbackService = new AudienceFeedbackService();
    this.audienceFeedbackService.setSessionId(sessionId);
    this.speciesMapper = new SpeciesMapper();
    this.milestoneEngine = new MilestoneEngine();
    this.injective = new InjectiveService();
  }

  async start(): Promise<void> {
    try {
      await this.relay.connect();
      console.log(
        `[Orchestrator] 会话 ${this.sessionId} 已启动 (讯飞ASR + Step LLM + v17 服务)`,
      );

      // v17: 检查用户已绑定的钱包地址
      const walletAddr = await this.walletService.getWalletAddress(this.userId);
      if (walletAddr) {
        this.walletAddress = walletAddr;
      } else {
        // 未连接钱包 → 主动推送 WalletConnect QR URI 给 PICO 显示
        const qrUri = this.walletService.generateConnectUri();
        this.sendToClient({ type: 'wallet_connect_uri', uri: qrUri });
      }
    } catch (err) {
      console.warn(
        '[Orchestrator] Relay 连接失败，降级本地模式:',
        (err as Error).message,
      );
      this.sendToClient({
        type: 'error',
        message: `连接失败，已降级本地模式: ${(err as Error).message}`,
      });
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

      // v17 新增
      case 'heart_rate':
        this.handleHeartRate(msg);
        break;

      case 'wallet_connect':
        this.handleWalletConnect(msg);
        break;

      case 'audience_feedback':
        this.handleAudienceFeedback(msg);
        break;

      case 'mint_credential':
        this.handleManualMint(msg);
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
    const avgFluency = this.avg(this.pendingSegments.map((s) => s.fluencyScore));
    const avgLogic = this.avg(this.pendingSegments.map((s) => s.logicScore));
    const avgPace = this.avg(this.pendingSegments.map((s) => s.paceScore));
    const avgReception = this.avg(this.pendingSegments.map((s) => s.receptionScore));
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
          walletAddress: this.walletAddress,
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
            receptionScore: seg.receptionScore,
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

      // 5. v17: 检查里程碑并铸证
      await this.checkMilestones();

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

      // v17.1: 通知 session-handler 重置状态，允许同一连接重新启动会话
      try {
        this.onEnded?.();
      } catch (cbErr) {
        console.error('[Orchestrator] onEnded 回调失败:', cbErr);
      }
    } catch (err) {
      console.error('[Orchestrator] 持久化失败:', err);
      this.sendToClient({ type: 'error', message: '会话结束处理失败' });
    }
  }

  // ============================================================
  // v17 新增：消息处理
  // ============================================================

  /** 处理心率输入（手动输入面板） */
  private async handleHeartRate(msg: HeartRateMessage): Promise<void> {
    const update = await this.heartRateService.handleHeartRate(msg.ts, msg.bpm);
    this.sendToClient(update);
  }

  /** 处理钱包连接 */
  private async handleWalletConnect(msg: WalletConnectMessage): Promise<void> {
    if (msg.action === 'connect' && msg.address) {
      const status = await this.walletService.connectWallet(
        this.userId,
        msg.address,
        msg.walletType || 'walletconnect',
      );
      this.walletAddress = msg.address;
      this.sendToClient(status);
    } else if (msg.action === 'disconnect' && msg.address) {
      const status = await this.walletService.disconnectWallet(msg.address);
      this.walletAddress = null;
      this.sendToClient(status);
    } else if (msg.action === 'status') {
      const status = await this.walletService.getStatusMessage(this.userId);
      this.sendToClient(status);
    }
  }

  /** 处理观众反馈（PICO 摄像头 SpatialML） */
  private async handleAudienceFeedback(msg: AudienceFeedbackMessage): Promise<void> {
    const result = await this.audienceFeedbackService.handleFeedback(
      msg.ts,
      msg.faceCount,
      msg.attentive,
      msg.distracted,
    );
    // 不单独推送，由评分时叠加
  }

  /** 处理手动铸证（演示模式） */
  private async handleManualMint(msg: CredentialMintMessage): Promise<void> {
    if (!this.walletAddress) {
      this.sendToClient({
        type: 'error',
        message: '请先连接钱包再铸证',
      });
      return;
    }

    const avgFluency = this.avg(this.pendingSegments.map((s) => s.fluencyScore));
    const avgLogic = this.avg(this.pendingSegments.map((s) => s.logicScore));
    const avgReception = this.avg(this.pendingSegments.map((s) => s.receptionScore));
    const hookCount = this.hookEvents.length;
    const duration = Math.round((Date.now() - this.sessionStartTime) / 1000);
    const stallRate = duration > 0 ? (hookCount / duration) * 3600 : 0;

    const metadata = {
      credentialType: '表达能力认证',
      level: msg.milestone === 'comprehensive' ? '认证表达者' : '优秀表达者',
      metrics: {
        fluency: Math.round(avgFluency * 10),
        logic: Math.round(avgLogic * 10),
        reception: Math.round(avgReception * 10),
        stallRate: Math.round(stallRate * 10) / 10,
      },
      improvement: `卡壳率 ${hookCount}次/${Math.round(duration / 60)}分钟`,
    };

    const result = await this.injective.mint(
      this.userId,
      this.walletAddress,
      msg.milestone,
      metadata,
    );

    // 持久化凭证
    await prisma.credential.create({
      data: {
        id: uuidv4(),
        userId: this.userId,
        chainTxHash: result.txHash,
        milestone: msg.milestone,
        metadata: JSON.stringify(metadata),
      },
    });

    // 推送铸造成功消息给 PICO 空间（3D 卡片展示）
    this.sendToClient(result.mintedMessage);
    console.log(`[Orchestrator] 手动铸证成功: ${msg.milestone} (${result.txHash})`);
  }

  // ============================================================
  // 私有方法 — 消息处理（原有 + v17 增强）
  // ============================================================

  private handleEnergyReport(msg: EnergyReportMessage): void {
    this.paceCalc.addEnergyReport(msg.ts, msg.energy, msg.isActive);
    this.lastEnergyActive = msg.isActive;

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

  private handleTranscript(msg: TranscriptMessage): void {
    this.paceCalc.addTranscriptText(msg.text, msg.ts);

    if (msg.isFinal) {
      this.transcriptText += msg.text;
      this.transcriptProcessor.addText(msg.text, true, msg.ts);

      this.stutterDetector.update({
        ts: msg.ts,
        isActive: true,
        energy: 0.5,
        text: msg.text,
      });

      this.recentTranscript = (this.recentTranscript + msg.text).slice(-120);
      this.maybeRefreshRescuePhrases();

      // v17: 物种映射（基于转写文本推断情绪 → 匹配物种）
      this.updateSpeciesMapping(msg.text);
    }

    this.tryGenerateRealScore();
  }

  /** v17: 更新物种映射 */
  private updateSpeciesMapping(text: string): void {
    if (!config.spatial.speciesMapping) return;

    // 使用本地情绪推断（LLM 不可用时的兜底）
    const emotion = this.speciesMapper.inferEmotion(text);
    const result = this.speciesMapper.mapSpecies(0, emotion); // speaker 0 = 当前用户

    if (result.changed || result.species !== 'default') {
      const speciesMsg: SpeciesUpdateMessage = {
        type: 'species_update',
        speaker: 0,
        species: result.species,
        emotion: result.emotion,
        confidence: result.confidence,
      };
      this.sendToClient(speciesMsg);
    }
  }

  private tryGenerateRealScore(): void {
    const ts = (Date.now() - this.sessionStartTime) / 1000;
    const textToScore = this.transcriptProcessor.shouldScore(ts);
    if (!textToScore) return;

    const score = computeRealScore(textToScore, this.paceCalc, ts, this.pauseCounter);
    if (!score) return;

    // v17: 叠加观众接收度
    const reception = this.audienceFeedbackService.getAverageReception();
    score.reception = reception;

    this.sendToClient(score);
    this.handleScoreMessage(score);

    console.log(
      `[Orchestrator] 真实评分: fluency=${score.fluency} logic=${score.logic} pace=${score.pace} reception=${reception} text="${textToScore.slice(0, 30)}..."`,
    );
  }

  private handleRingCommand(msg: RingCommandMessage): void {
    this.sendToClient({
      type: 'ring_feedback',
      cmd: msg.cmd,
      ts: msg.ts,
      acknowledged: true,
    });

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

  private handleRelayOutbound(msg: RelayOutbound): void {
    switch (msg.type) {
      case 'asr_transcript':
        this.handleAsrTranscript(msg);
        break;

      case 'score':
        this.sendToClient(msg);
        this.handleScoreMessage(msg);
        break;

      case 'score_update':
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

  private handleAsrTranscript(msg: { type: 'asr_transcript'; ts: number; text: string; isFinal: boolean; speaker?: number }): void {
    this.sendToClient({
      type: 'asr_transcript',
      ts: msg.ts,
      text: msg.text,
      isFinal: msg.isFinal,
      speaker: msg.speaker ?? 0,
    });

    if (!msg.text.trim()) return;

    if (msg.isFinal) {
      this.paceCalc.addTranscriptText(msg.text, msg.ts);
      this.transcriptText += msg.text;
      this.transcriptProcessor.addText(msg.text, true, msg.ts);

      this.stutterDetector.update({
        ts: msg.ts,
        isActive: true,
        energy: 0.5,
        text: msg.text,
      });

      this.recentTranscript = (this.recentTranscript + msg.text).slice(-120);
      this.maybeRefreshRescuePhrases();

      // v17: 物种映射（使用讯飞 ASR 的 speaker 字段）
      this.updateSpeciesMappingForSpeaker(msg.text, msg.speaker ?? 0);
    }

    this.tryGenerateRealScore();
  }

  /** v17: 为指定说话人更新物种映射 */
  private updateSpeciesMappingForSpeaker(text: string, speaker: number): void {
    if (!config.spatial.speciesMapping) return;

    const emotion = this.speciesMapper.inferEmotion(text);
    const result = this.speciesMapper.mapSpecies(speaker, emotion);

    if (result.species !== 'default') {
      const speciesMsg: SpeciesUpdateMessage = {
        type: 'species_update',
        speaker,
        species: result.species,
        emotion: result.emotion,
        confidence: result.confidence,
      };
      this.sendToClient(speciesMsg);
    }
  }

  private handleScoreMessage(msg: ScoreMessage): void {
    if (msg.text && !this.transcriptText.includes(msg.text)) {
      this.paceCalc.addTranscriptText(msg.text, msg.ts);
      this.transcriptText += msg.text;
    }

    const duration =
      this.lastScoreTs > 0
        ? Math.max(1, Math.round(msg.ts - this.lastScoreTs))
        : 3;
    this.lastScoreTs = msg.ts;

    // v17: 记录接收度分
    const reception = msg.reception ?? this.audienceFeedbackService.getAverageReception();

    this.pendingSegments.push({
      ts: Math.round(msg.ts),
      duration,
      fluencyScore: msg.fluency,
      logicScore: msg.logic,
      paceScore: msg.pace,
      receptionScore: reception,
      fillerCount: msg.fillers,
      pauseCount: msg.pauses,
      text: msg.text,
    });
  }

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
  // v17: 里程碑与铸证
  // ============================================================

  private async checkMilestones(): Promise<void> {
    try {
      const avgFluency = this.avg(this.pendingSegments.map((s) => s.fluencyScore));
      const avgLogic = this.avg(this.pendingSegments.map((s) => s.logicScore));
      const avgReception = this.avg(this.pendingSegments.map((s) => s.receptionScore));
      const avgPace = this.avg(this.pendingSegments.map((s) => s.paceScore));
      const duration = Math.round((Date.now() - this.sessionStartTime) / 1000);

      const stats: SessionStats = {
        fluencyAvg: avgFluency,
        logicAvg: avgLogic,
        receptionAvg: avgReception,
        paceAvg: avgPace,
        overallScore:
          this.pendingSegments.length > 0
            ? Math.round((avgFluency + avgLogic + avgPace) / 3)
            : null,
        duration,
        fillerCount: this.pendingSegments.reduce((a, s) => a + s.fillerCount, 0),
        pauseCount: this.pendingSegments.reduce((a, s) => a + s.pauseCount, 0),
        hookCount: this.hookEvents.length,
        recoveredCount: this.hookEvents.filter((h) => h.recovered).length,
      };

      const milestones = await this.milestoneEngine.checkMilestones(
        this.sessionId,
        this.userId,
        stats,
      );

      for (const milestone of milestones) {
        await this.mintCredential(milestone);
      }
    } catch (err) {
      console.error('[Orchestrator] 检查里程碑失败:', err);
    }
  }

  private async mintCredential(milestone: {
    milestone: string;
    level: string;
    credentialType: string;
    metadata: {
      credentialType: string;
      level: string;
      metrics: { fluency: number; logic: number; reception: number; stallRate: number };
      improvement: string;
    };
  }): Promise<void> {
    try {
      // 检查是否已有该里程碑的凭证
      const existing = await prisma.credential.findFirst({
        where: { userId: this.userId, milestone: milestone.milestone },
      });
      if (existing) return;

      // 需要钱包地址才能铸证
      if (!this.walletAddress) {
        console.warn(`[Orchestrator] 无钱包地址，跳过铸证: ${milestone.milestone}`);
        return;
      }

      const result = await this.injective.mint(
        this.userId,
        this.walletAddress,
        milestone.milestone,
        milestone.metadata,
      );

      await prisma.credential.create({
        data: {
          id: uuidv4(),
          userId: this.userId,
          chainTxHash: result.txHash,
          milestone: milestone.milestone,
          metadata: JSON.stringify(milestone.metadata),
        },
      });

      // 推送铸造成功消息给 PICO 空间（3D 卡片展示）
      this.sendToClient(result.mintedMessage);
      console.log(
        `[Orchestrator] 里程碑达成: ${milestone.milestone} (用户: ${this.userId}, 交易: ${result.txHash})`,
      );
    } catch (err) {
      console.error(`[Orchestrator] 铸证失败 (${milestone.milestone}):`, err);
    }
  }

  // ============================================================
  // 私有方法 — 工具
  // ============================================================

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

  private maybeRefreshRescuePhrases(): void {
    if (!this.llm.isAvailable) return;
    if (this.recentTranscript.trim().length < 8) return;

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

  private sendToClient(msg: ServerMessage): void {
    if (this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  private avg(nums: number[]): number {
    if (nums.length === 0) return 0;
    return nums.reduce((a, b) => a + b, 0) / nums.length;
  }
}
