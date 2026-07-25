// Unfreeze — 分析中继：讯飞 ASR + Step LLM + 本地算法
// 替代原 stepaudio-relay.ts

import { config } from '../config.js';
import type { ServerMessage, ScoreMessage, AsrTranscriptMessage, RelayStatusMessage } from '../types.js';
import { IflytekAsr } from './iflytek-asr.js';
import { StepLlmAnalyzer } from './step-llm.js';
import { TranscriptProcessor } from './score-engine.js';
import { PaceCalculator } from './pace-calculator.js';

/** 中继出站消息 */
export type RelayOutbound =
  | ServerMessage
  | AsrTranscriptMessage
  | RelayStatusMessage;

/** Mock 文本 */
const MOCK_TEXTS = [
  '今天我想和大家分享一个关于产品设计的想法。',
  '在这个过程中，我们遇到了很多挑战，但团队始终坚持用户至上。',
  '我认为最重要的不是速度，而是质量。',
  '嗯，然后我们就是基本上做了一个原型来验证。',
  '其实关键在于，我们需要更好地理解用户的需求。',
  '让我举个例子来说明这个问题。',
  '这个方案的核心是减少用户的认知负担。',
  '换句话说，我们希望让交互更加自然流畅。',
  '呃，那个，我觉得我们可以从用户反馈中学习。',
  '总结来说，好的设计应该让用户无意识地完成任务。',
];

export class AnalysisRelay {
  private onMessage: (msg: RelayOutbound) => void;
  private sessionStartTime: number;
  private asr: IflytekAsr | null = null;
  private llm: StepLlmAnalyzer;
  private paceCalc: PaceCalculator;
  private transcriptProcessor = new TranscriptProcessor();
  private connected = false;
  private mockTimer: ReturnType<typeof setInterval> | null = null;
  private lastLlmTs = -Infinity;

  constructor(onMessage: (msg: RelayOutbound) => void, sessionStartTime: number) {
    this.onMessage = onMessage;
    this.sessionStartTime = sessionStartTime;
    this.llm = new StepLlmAnalyzer();
    this.paceCalc = new PaceCalculator();
  }

  get isConnected(): boolean {
    return this.connected;
  }

  /** 暴露给 orchestrator 的语速计算器 */
  getPaceCalc(): PaceCalculator {
    return this.paceCalc;
  }

  /** 暴露给 orchestrator 的 transcript processor */
  getTranscriptProcessor(): TranscriptProcessor {
    return this.transcriptProcessor;
  }

  getTs(): number {
    return Math.round(((Date.now() - this.sessionStartTime) / 1000) * 10) / 10;
  }

  /** 设置外部 paceCalc（与 orchestrator 共享） */
  setPaceCalc(pc: PaceCalculator): void {
    this.paceCalc = pc;
  }

  /**
   * 连接讯飞 ASR + 启动本地评分
   */
  async connect(): Promise<void> {
    if (!config.iflytek.appId) {
      console.log('[AnalysisRelay] 无讯飞配置，Mock 模式');
      this.connected = true;
      this.onMessage({ type: 'relay_status', status: 'connected', message: 'mock' });
      this.mockTimer = setInterval(() => this.generateMock(), 3000);
      return;
    }

    this.asr = new IflytekAsr(
      (result) => this.handleAsr(result),
      (status, msg) => {
        this.onMessage({ type: 'relay_status', status, message: msg });
        if (status === 'connected') this.connected = true;
        if (status === 'closed' || status === 'error') this.connected = false;
      },
      this.sessionStartTime,
    );

    try {
      await this.asr.connect();
      console.log('[AnalysisRelay] 已启动 (讯飞ASR + Step LLM 逻辑评分)');
    } catch (err) {
      console.warn('[AnalysisRelay] 讯飞连接失败，降级 mock:', (err as Error).message);
      this.connected = true;
      this.mockTimer = setInterval(() => this.generateMock(), 3000);
    }
  }

  /** 发送音频帧到讯飞 ASR */
  sendAudio(base64Pcm: string): void {
    if (this.asr?.isConnected) {
      this.asr.sendAudio(base64Pcm);
    }
  }

  close(): void {
    if (this.mockTimer) { clearInterval(this.mockTimer); this.mockTimer = null; }
    if (this.asr) { this.asr.close(); this.asr = null; }
    this.connected = false;
  }

  // ============================================================
  // 内部
  // ============================================================

  /** 处理讯飞 ASR 转写结果 */
  private handleAsr(result: { text: string; isFinal: boolean; ts: number; speaker: number }): void {
    if (!result.text?.trim()) return;

    // 发送给前端转写流
    this.onMessage({
      type: 'asr_transcript',
      ts: result.ts,
      text: result.text,
      isFinal: result.isFinal,
      speaker: result.speaker,
    });

    // 喂给语速计算器
    this.paceCalc.addTranscriptText(result.text, result.ts);

    if (result.isFinal) {
      // 喂给本地评分引擎
      this.transcriptProcessor.addText(result.text, true, result.ts);
      // 逻辑性：只用当前这一段文本，按固定间隔评判
      this.callLlm(result.ts, result.text);
    }
  }

  /**
   * 调用 Step LLM 分析当前段转写文本（逻辑性评分 + 钩子检测）
   * 策略：不累积历史，每段时间评判一次，距上次不足 logicEvalIntervalS 则跳过
   */
  private async callLlm(ts: number, text: string): Promise<void> {
    if (!this.llm.isAvailable) return;
    if (text.trim().length < 5) return;

    // 限流：按配置间隔
    if (ts - this.lastLlmTs < config.stepLlm.logicEvalIntervalS) return;
    this.lastLlmTs = ts;

    const paceResult = this.paceCalc.calculate(ts);

    const result = await this.llm.analyze({
      text, // 仅当前段文本
      ts,
      charsPerSec: paceResult.charsPerSec,
      pauseRate: paceResult.pauseRate,
    });

    // LLM 返回逻辑性补丁 -> 发给前端
    if (result?.logicUpdate) {
      this.onMessage(result.logicUpdate);
      console.log(`[AnalysisRelay] LLM logic update: logic=${result.logicUpdate.logic}`);
    }
    // 卡壳检测由 orchestrator 的 StutterDetector 负责，这里不再处理 hook
  }

  /** Mock 生成 */
  private generateMock(): void {
    const ts = this.getTs();
    const text = MOCK_TEXTS[Math.floor(Math.random() * MOCK_TEXTS.length)];
    this.onMessage({ type: 'asr_transcript', ts, text, isFinal: true });
    const score: ScoreMessage = {
      type: 'score',
      ts,
      fluency: 5 + Math.floor(Math.random() * 5),
      logic: 5 + Math.floor(Math.random() * 5),
      pace: 5 + Math.floor(Math.random() * 5),
      fillers: Math.floor(Math.random() * 3),
      pauses: Math.floor(Math.random() * 3),
      text,
    };
    this.onMessage(score);
  }
}

