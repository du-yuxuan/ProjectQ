// Q v17 — 物种映射服务
// 根据说话风格/情绪/音色自动匹配物种化身
// 咄咄逼人→老虎、温和→兔子、缜密→猫头鹰、活跃→狐狸
// 物种可随对话情绪动态切换（如某人突然强硬，化身从兔子渐变为狼）

import type { SpeciesType } from '../types.js';

export interface EmotionAnalysis {
  /** 情绪标签 */
  emotion: string;
  /** 风格标签 */
  style: string;
  /** 置信度 0-1 */
  confidence: number;
}

/** 物种映射规则表（风格→物种） */
const SPECIES_MAP: Array<{
  keywords: string[];
  species: SpeciesType;
  emotionRange: string;
}> = [
  // 咄咄逼人 → 老虎
  {
    keywords: ['咄咄逼人', '强势', '攻击', '激烈', '愤怒', '强硬', '压迫', 'aggressive'],
    species: 'tiger',
    emotionRange: '攻击性',
  },
  // 突然强硬 → 狼（动态切换中间态）
  {
    keywords: ['突然强硬', '变强硬', '升温', '对抗', 'wolf'],
    species: 'wolf',
    emotionRange: '升温',
  },
  // 温和 → 兔子
  {
    keywords: ['温和', '柔和', '友善', '亲切', '平稳', 'gentle', 'soft'],
    species: 'rabbit',
    emotionRange: '温和',
  },
  // 缜密 → 猫头鹰
  {
    keywords: ['缜密', '逻辑', '分析', '理性', '深思', '严谨', 'precise', 'analytical'],
    species: 'owl',
    emotionRange: '缜密',
  },
  // 活跃 → 狐狸
  {
    keywords: ['活跃', '热情', '幽默', '生动', '活泼', 'energetic', 'lively'],
    species: 'fox',
    emotionRange: '活跃',
  },
  // 强势领导 → 狮子
  {
    keywords: ['领导', '权威', '主导', '掌控', 'dominant', 'leader'],
    species: 'lion',
    emotionRange: '权威',
  },
  // 中性/友好 → 鹿
  {
    keywords: ['中性', '平静', '友好', '默认', 'neutral'],
    species: 'deer',
    emotionRange: '中性',
  },
];

/** 默认物种 */
const DEFAULT_SPECIES: SpeciesType = 'default';

export class SpeciesMapper {
  /** 说话人当前物种缓存（用于动态切换检测） */
  private speakerSpecies: Map<number, SpeciesType> = new Map();

  /**
   * 根据情绪/风格分析结果映射物种
   * @param speaker 说话人 ID
   * @param analysis 情绪分析结果（来自阶跃星辰或读心镜）
   * @returns 物种类型 + 情绪标签 + 置信度
   */
  mapSpecies(speaker: number, analysis: EmotionAnalysis): {
    species: SpeciesType;
    emotion: string;
    confidence: number;
    changed: boolean;
  } {
    const { emotion, style, confidence } = analysis;

    // 合并情绪+风格文本进行关键词匹配
    const combinedText = `${emotion} ${style}`.toLowerCase();

    let matched: SpeciesType = DEFAULT_SPECIES;
    let matchedRange = '中性';

    for (const rule of SPECIES_MAP) {
      if (rule.keywords.some((kw) => combinedText.includes(kw.toLowerCase()))) {
        matched = rule.species;
        matchedRange = rule.emotionRange;
        break;
      }
    }

    // 无匹配时，根据置信度选择默认
    if (matched === DEFAULT_SPECIES && confidence > 0.6) {
      // 有置信度但无关键词匹配 → 用默认鹿（中性友好）
      matched = 'deer';
    }

    // 检测物种是否变化（动态切换）
    const previous = this.speakerSpecies.get(speaker);
    const changed = previous !== undefined && previous !== matched;

    // 更新缓存
    this.speakerSpecies.set(speaker, matched);

    return {
      species: matched,
      emotion: matchedRange,
      confidence,
      changed,
    };
  }

  /**
   * 基于转写文本的轻量情绪推断（无 LLM 时的兜底）
   * 分析文本中的情绪词、标点、长度等特征
   */
  inferEmotion(text: string): EmotionAnalysis {
    const len = text.length;
    const hasExclamation = /[！!]/.test(text);
    const hasQuestion = /[？?]/.test(text);
    const hasEllipsis = /[…。]{2,}|。{2,}/.test(text);

    // 简单关键词情绪检测
    const aggressive = /不行|必须|绝对|当然|肯定|一定|不可能|不对/.test(text);
    const gentle = /谢谢|请|不好意思|也许|可能|或许|希望|建议/.test(text);
    const analytical = /因此|所以|因为|首先|其次|总之|综上|也就是说|核心是|关键是/.test(text);
    const lively = /哈哈|好|太棒了|不错|牛|厉害|哇/.test(text);

    let emotion = '中性';
    let style = '平稳';
    let confidence = 0.5;

    if (aggressive || hasExclamation) {
      emotion = '咄咄逼人';
      style = '强势';
      confidence = 0.7;
    } else if (gentle) {
      emotion = '温和';
      style = '柔和';
      confidence = 0.65;
    } else if (analytical) {
      emotion = '缜密';
      style = '逻辑';
      confidence = 0.68;
    } else if (lively) {
      emotion = '活跃';
      style = '热情';
      confidence = 0.6;
    } else if (hasEllipsis || len < 10) {
      emotion = '犹豫';
      style = '不确定';
      confidence = 0.4;
    }

    return { emotion, style, confidence };
  }

  /** 获取说话人当前物种 */
  getSpecies(speaker: number): SpeciesType {
    return this.speakerSpecies.get(speaker) ?? DEFAULT_SPECIES;
  }

  /** 重置所有说话人物种缓存 */
  reset(): void {
    this.speakerSpecies.clear();
  }
}
