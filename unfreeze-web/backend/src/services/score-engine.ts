// Unfreeze — 真实评分引擎
// 基于实际语音指标（语速、填充词、停顿率、文本特征）计算多维评分
// 不依赖外部 LLM API，纯本地启发式计算

import type { ScoreMessage } from '../types.js';
import { PaceCalculator } from './pace-calculator.js';

/** 填充词列表 */
const FILLER_WORDS = [
  '嗯', '啊', '呃', '那个', '这个', '就是', '然后', '反正', '其实', '就是说', '基本上',
];

/** 逻辑连接词（表明结构清晰） */
const LOGIC_CONNECTORS = [
  '首先', '其次', '然后', '最后', '因此', '所以', '因为', '由于', '不过', '但是',
  '然而', '另外', '此外', '总之', '综上', '也就是说', '换句话说', '核心是', '关键是',
];

/** 中文字符正则 */
const CHINESE_CHAR_RE = /[\u4e00-\u9fff]/g;

/** 计算文本中的填充词数量 */
export function countFillers(text: string): number {
  let count = 0;
  for (const filler of FILLER_WORDS) {
    let idx = 0;
    while ((idx = text.indexOf(filler, idx)) !== -1) {
      count++;
      idx += filler.length;
    }
  }
  return count;
}

/** 计算中文字符数 */
export function countChineseChars(text: string): number {
  const matches = text.match(CHINESE_CHAR_RE);
  return matches ? matches.length : 0;
}

/** 检测文本中逻辑连接词数量 */
function countLogicConnectors(text: string): number {
  let count = 0;
  for (const conn of LOGIC_CONNECTORS) {
    let idx = 0;
    while ((idx = text.indexOf(conn, idx)) !== -1) {
      count++;
      idx += conn.length;
    }
  }
  return count;
}

/**
 * 基于实际指标计算评分
 * @param text 转写文本
 * @param paceCalc 语速计算器实例（获取实时语速数据）
 * @param ts 当前时间戳
 * @param pauseCount 当前停顿次数
 * @returns ScoreMessage 或 null（文本太短时返回 null）
 */
export function computeRealScore(
  text: string,
  paceCalc: PaceCalculator,
  ts: number,
  pauseCount: number,
): ScoreMessage | null {
  const charCount = countChineseChars(text);

  // 文本太短，不值得评分
  if (charCount < 5) return null;

  const fillerCount = countFillers(text);
  const logicConnectors = countLogicConnectors(text);

  // 获取语速数据
  const paceResult = paceCalc.calculate(ts);
  const charsPerSec = paceResult.charsPerSec;
  const pauseRate = paceResult.pauseRate;

  // === 流畅度评分 (0-10) ===
  // 因素：填充词密度、停顿率
  const fillerDensity = charCount > 0 ? fillerCount / charCount : 0;
  // 填充词密度：0 → 10分，每 1% 密度扣 1 分（正常 1-2% 扣 1-2 分）
  const fillerScore = Math.max(0, 10 - fillerDensity * 100);
  // 停顿率：0 → 10分，前 15% 免费（自然停顿），之后每 5% 扣 1 分
  const pauseScore = Math.max(0, 10 - Math.max(0, pauseRate - 0.15) * 20);
  const fluency = Math.round(Math.min(10, Math.max(1, fillerScore * 0.6 + pauseScore * 0.4)));

  // === 语速评分 (0-10) ===
  // 直接使用 PaceCalculator 的评分
  const pace = Math.round(Math.min(10, Math.max(0, paceResult.paceScore)));

  // === 逻辑性评分 (0-10) ===
  // 因素：逻辑连接词密度、文本长度（越完整越高）、标点结构
  const connectorDensity = charCount > 0 ? logicConnectors / charCount : 0;
  // 连接词密度：适中 1-3/50字 → 高分
  const connectorScore = Math.min(10, connectorDensity * 150);
  // 文本完整性：短文本扣分
  const completenessScore = Math.min(10, charCount / 20);
  // 填充词过多扣分（逻辑混乱）
  const fillerPenalty = Math.max(0, 10 - fillerDensity * 150);
  const logic = Math.round(Math.min(10, Math.max(0,
    connectorScore * 0.4 + completenessScore * 0.3 + fillerPenalty * 0.3,
  )));

  const score: ScoreMessage = {
    type: 'score',
    ts: Math.round(ts * 10) / 10,
    fluency,
    logic,
    pace,
    fillers: fillerCount,
    pauses: pauseCount,
    text,
  };

  return score;
}

/**
 * 累积转写文本并在合适时机触发评分
 * 当收到一段足够长的最终转写时触发评分
 */
export class TranscriptProcessor {
  private buffer = '';
  private lastScoreTs = 0;
  private minCharsForScore = 15; // 至少 15 个中文字符才评分
  private minIntervalS = 2; // 最少间隔 2 秒
  private totalPauses = 0;

  /** 添加转写文本 */
  addText(text: string, isFinal: boolean, ts: number): void {
    if (isFinal) {
      this.buffer += text;
    }
  }

  /** 记录停顿 */
  addPause(): void {
    this.totalPauses++;
  }

  /**
   * 检查是否应该生成评分
   * 返回需要评分的文本（截取 buffer），或 null
   */
  shouldScore(ts: number): string | null {
    const charCount = countChineseChars(this.buffer);
    if (charCount < this.minCharsForScore) return null;
    if (ts - this.lastScoreTs < this.minIntervalS) return null;

    // 取出 buffer 中的文本进行评分
    const textToScore = this.buffer;
    this.buffer = '';
    this.lastScoreTs = ts;
    return textToScore;
  }

  /** 获取当前缓冲区文本（用于最终转写） */
  getFullText(): string {
    return this.buffer;
  }
}
