// Unfreeze - Step-3.7-Flash LLM 表达分析
// 混合评分：本地引擎出 fluency+pace，LLM 出 logic + 救场话术

import { config } from '../config.js';
import type { ScoreUpdateMessage } from '../types.js';

/** LLM 分析请求的上下文 */
export interface AnalyzeContext {
  text: string; // 本次转写文本
  ts: number; // 会话内秒数
  paceScore?: number; // 本地语速评分
  charsPerSec?: number;
  pauseRate?: number;
  fillerCount?: number;
  pauseCount?: number;
}

/** LLM 逻辑性分析结果 */
export interface AnalyzeResult {
  logicUpdate?: ScoreUpdateMessage;
}

/** 救场话术缓存：四种钩子类型各一句 */
export interface RescuePhrases {
  '开口': string;
  '思路': string;
  '衔接': string;
  '节奏': string;
}

/** 逻辑性评分专用 prompt（轻量、聚焦语义连贯） */
const LOGIC_PROMPT = `你是演讲逻辑性评估专家。用户正在演讲，你收到一段转写文本。

请仅评估"逻辑性"这一维度，输出严格 JSON（不要 Markdown，不要解释）：

{"logic": 0到10, "reason": "不超过15字的理由"}

评分标准：
- 9-10：论点清晰连贯，有明确的结构（如总分总、因果递进），前后呼应
- 7-8：整体连贯，偶有跳跃但能自圆其说
- 5-6：有一定逻辑但不够清晰，存在部分跳跃或重复
- 3-4：逻辑混乱，频繁漂移，缺少连贯线索
- 0-2：语无伦次，无法理解表达意图

注意：只评估逻辑性，不考虑流畅度、语速或填充词。`;

/** 救场话术生成 prompt（基于最近转写内容，生成上下文相关的救场话） */
const RESCUE_SYSTEM_PROMPT = `你是 Unfreeze 演讲救场教练。用户正在演讲，我会给你最近一段转写内容，请为四种卡壳场景各生成一句救场话。

要求：
1. 每句不超过 10 个字
2. 接着用户刚才说的内容自然延续或引导
3. 口语化、简洁有力、能帮用户接上话

场景说明：
- 开口：用户停顿太久无法开口，需要鼓励继续或提示刚才说到哪
- 思路：用户填充词密集、思路混乱，需要点明核心方向
- 衔接：用户句子断裂不连贯，需要提供过渡衔接
- 节奏：用户语速过快或过慢，需要提示调整节奏

输出严格 JSON（不要 Markdown，不要解释，只输出一行）：
{"开口":"救场话","思路":"救场话","衔接":"救场话","节奏":"救场话"}`;

/** 逻辑评分底层限流（毫秒）：与 AnalysisRelay 的 logicEvalIntervalS 协同 */
const LOGIC_MIN_INTERVAL_MS = 1500;
/** 救场话术生成限流（毫秒）：避免每段转写都调 LLM */
const RESCUE_MIN_INTERVAL_MS = 4000;
/** 救场话术最小输入字符数 */
const RESCUE_MIN_CHARS = 8;

export class StepLlmAnalyzer {
  private apiKey: string;
  private model: string;
  private url: string;
  /** 逻辑评分限流时间戳 */
  private lastLogicCallAt = 0;
  /** 救场话术生成限流时间戳 */
  private lastRescueCallAt = 0;

  constructor() {
    this.apiKey = config.stepLlm.apiKey;
    this.model = config.stepLlm.model;
    this.url = config.stepLlm.url;
  }

  get isAvailable(): boolean {
    return !!this.apiKey;
  }

  /**
   * 分析一段转写文本：调用 LLM 评估逻辑性 -> 返回 score_update
   * 卡壳检测由本地 StutterDetector 负责，这里只做逻辑评分
   */
  async analyze(ctx: AnalyzeContext): Promise<AnalyzeResult | null> {
    if (!this.apiKey) return null;
    if (ctx.text.trim().length < 5) return null;

    const now = Date.now();
    if (now - this.lastLogicCallAt < LOGIC_MIN_INTERVAL_MS) {
      return null; // 限流
    }
    this.lastLogicCallAt = now;

    const logicResult = await this.callLlm(
      LOGIC_PROMPT,
      `转写文本：${ctx.text}\n请评估逻辑性。`,
    );

    const result: AnalyzeResult = {};

    if (logicResult) {
      const parsed = this.parseLogicResponse(logicResult, ctx.ts);
      if (parsed) result.logicUpdate = parsed;
    }

    return Object.keys(result).length > 0 ? result : null;
  }

  /**
   * 基于最近转写内容，预生成四种卡壳场景的救场话术
   * - 在 ASR final 段到达时异步调用，结果缓存供 StutterDetector 触发时使用
   * - 内置限流，避免高频调用
   * - LLM 不可用或解析失败时返回 null，调用方降级到本地话术
   */
  async generateRescuePhrases(recentText: string): Promise<RescuePhrases | null> {
    if (!this.apiKey) return null;
    const trimmed = recentText.trim();
    if (trimmed.length < RESCUE_MIN_CHARS) return null;

    const now = Date.now();
    if (now - this.lastRescueCallAt < RESCUE_MIN_INTERVAL_MS) {
      return null; // 限流
    }
    this.lastRescueCallAt = now;

    const userMsg = `用户最近说的内容：${trimmed.slice(-60)}\n请为四种卡壳场景各生成一句救场话。`;

    const result = await this.callLlm(RESCUE_SYSTEM_PROMPT, userMsg);
    if (!result) return null;

    return this.parseRescueResponse(result);
  }

  /** 底层 LLM 调用 */
  private async callLlm(
    systemPrompt: string,
    userContent: string,
  ): Promise<string | null> {
    try {
      const resp = await fetch(this.url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${this.apiKey}`,
        },
        body: JSON.stringify({
          model: this.model,
          messages: [
            { role: 'system', content: systemPrompt },
            { role: 'user', content: userContent },
          ],
          temperature: 0.3,
          max_tokens: 2048, // step-3.7-flash 是推理模型，需要足够 token 完成推理+输出
        }),
      });

      if (!resp.ok) {
        const body = await resp.text().catch(() => '');
        console.error(`[StepLlm] HTTP ${resp.status}: ${body.slice(0, 200)}`);
        return null;
      }

      const data = (await resp.json()) as {
        choices?: Array<{ message?: { content?: string } }>;
      };
      const content = data.choices?.[0]?.message?.content || '';
      if (!content) return null;

      console.log(`[StepLlm] 响应: "${content.slice(0, 120)}"`);
      return content;
    } catch (err) {
      console.error('[StepLlm] 调用失败:', (err as Error).message);
      return null;
    }
  }

  /** 解析逻辑性评分响应 */
  private parseLogicResponse(
    content: string,
    ts: number,
  ): ScoreUpdateMessage | null {
    try {
      const jsonMatch = content.match(/\{[^{}]*\}/);
      if (!jsonMatch) return null;

      const obj = JSON.parse(jsonMatch[0]) as Record<string, unknown>;
      const logic = clampScore(obj.logic);
      const reason = obj.reason
        ? String(obj.reason).slice(0, 20)
        : undefined;

      return {
        type: 'score_update',
        ts,
        logic,
        reason,
      };
    } catch {
      return null;
    }
  }

  /** 解析救场话术响应 */
  private parseRescueResponse(content: string): RescuePhrases | null {
    try {
      const jsonMatch = content.match(/\{[^{}]*\}/);
      if (!jsonMatch) return null;

      const obj = JSON.parse(jsonMatch[0]) as Record<string, unknown>;

      return {
        '开口': String(obj['开口'] || '接着说').slice(0, 10),
        '思路': String(obj['思路'] || '核心是').slice(0, 10),
        '衔接': String(obj['衔接'] || '换句话说').slice(0, 10),
        '节奏': String(obj['节奏'] || '放慢说').slice(0, 10),
      };
    } catch {
      return null;
    }
  }
}

function clampScore(v: unknown): number {
  const n = typeof v === 'number' ? v : parseFloat(String(v ?? 0));
  if (Number.isNaN(n)) return 0;
  return Math.max(0, Math.min(10, Math.round(n)));
}
