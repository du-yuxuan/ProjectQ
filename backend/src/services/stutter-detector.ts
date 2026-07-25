// Unfreeze — 卡壳/崩溃检测引擎（纯规则，无 ML）
// 基于停顿时长、填充词密度、句子断裂模式、语速偏差检测演讲卡壳

/** 填充词列表（与 pace-calculator 一致） */
const FILLER_WORDS = [
  '嗯', '啊', '呃', '那个', '这个', '就是', '然后', '反正', '其实', '就是说', '基本上',
];

/** 各类钩子的本地兜底文案（≤10 字，LLM 不可用时使用） */
const HOOK_SUGGESTIONS: Record<string, string[]> = {
  '开口': ['接着说', '我在听', '你继续', '慢慢来不急'],
  '思路': ['核心是', '关键是', '你说的重点是', '回到刚才话题'],
  '衔接': ['换句话说', '试着说', '但重点是', '也就是'],
  '节奏': ['放慢说', '不着急', '深呼吸', '稳一稳再说'],
};

// 阈值常量（秒）
const RATE_LIMIT_S = 8;              // 同类钩子 8 秒内不重复触发
const NO_HOOK_WINDOW_S = 5;          // 开口规则：5 秒内无任何钩子
const PAUSE_THRESHOLD_S = 2;         // 开口：停顿 > 2 秒
const SHORT_PAUSE_THRESHOLD_S = 1.5; // 思路/衔接：停顿 > 1.5 秒
const PACE_DEVIATION = 0.4;          // 节奏偏差 > 40%
const PACE_DEVIATION_SUSTAINED_S = 3;// 偏差持续 3 秒+
const FILLER_DENSITY_THRESHOLD = 3;  // 每 10 字填充词 > 3
const HISTORY_RETENTION_S = 30;     // 保留 30 秒历史数据

export interface HookTrigger {
  hookType: '开口' | '思路' | '衔接' | '节奏';
  hookText: string;
  countdown: number;
  ts: number;
}

interface EnergyRecord {
  ts: number;
  isActive: boolean;
  energy: number;
}

interface TextRecord {
  ts: number;
  text: string;
}

export class StutterDetector {
  private energyHistory: EnergyRecord[] = [];
  private textHistory: TextRecord[] = [];

  /** 各类型钩子最近触发时间（用于速率限制） */
  private lastHookTimes: Record<string, number> = {};

  /** 最近任意钩子触发时间（用于开口规则的 5 秒窗口） */
  private lastAnyHookTs = -Infinity;

  // ---- 衔接检测状态 ----
  private wasActive = false;
  private pauseStartTs: number | null = null;
  /** 是否检测到 active→inactive(>1.5s)→active 模式，待确认 */
  private sentenceBreakPending = false;
  /** 恢复说话的时间戳 */
  private restartTs: number | null = null;

  // ---- 节奏检测状态 ----
  /** 语速偏差开始时间（持续 3 秒+ 才触发） */
  private paceDeviationStartTs: number | null = null;

  /**
   * 处理新的检测数据，返回触发的钩子（或 null）
   * @param rescuePhrases 外部预生成的救场话术（来自 LLM），优先于本地兜底
   * @param recentPaceScore 最近的语速评分，用于自适应倒计时
   */
  update(data: {
    ts: number;
    isActive: boolean;
    energy: number;
    text?: string;
    paceScore?: number;
    baselinePace?: number;
  }, rescuePhrases?: Record<string, string>, recentPaceScore?: number): HookTrigger | null {
    const { ts, isActive, energy, text } = data;

    // 记录能量数据
    this.energyHistory.push({ ts, isActive, energy });

    // 记录文本数据
    if (text && text.trim().length > 0) {
      this.textHistory.push({ ts, text });
    }

    this.pruneOld(ts);

    // 更新句子断裂状态追踪
    this.updateSentenceBreakState(ts, isActive);

    // 依次检查各规则（优先级：开口 > 思路 > 衔接 > 节奏）
    let hook = this.checkKaiKou(ts, isActive);
    if (!hook) hook = this.checkSiLu(ts);
    if (!hook) hook = this.checkXianJie(ts, isActive);
    if (!hook) hook = this.checkJieZou(ts, data.paceScore, data.baselinePace);

    if (hook) {
      // 用外部救场话术覆盖本地兜底
      if (rescuePhrases && rescuePhrases[hook.hookType]) {
        hook.hookText = rescuePhrases[hook.hookType];
      }
      // 自适应倒计时：语速越慢，给越多时间
      hook.countdown = this.adaptiveCountdown(hook.hookType, recentPaceScore);
      this.lastHookTimes[hook.hookType] = ts;
      this.lastAnyHookTs = ts;
    }

    return hook;
  }

  /**
   * 自适应倒计时：根据语速评分动态调整
   * - 语速偏慢（paceScore < 4）：倒计时 6 秒，给更多时间
   * - 语速正常（4-7）：倒计时 5 秒
   * - 语速偏快（> 7）：倒计时 4 秒，快速救场
   * - 开口类钩子总是多给 1 秒
   */
  private adaptiveCountdown(hookType: HookTrigger['hookType'], paceScore?: number): number {
    let base: number;
    if (paceScore === undefined) {
      base = 5;
    } else if (paceScore < 4) {
      base = 6;
    } else if (paceScore > 7) {
      base = 4;
    } else {
      base = 5;
    }
    if (hookType === '开口') base += 1;
    return Math.max(3, Math.min(8, base));
  }

  /** 重置所有状态 */
  reset(): void {
    this.energyHistory = [];
    this.textHistory = [];
    this.lastHookTimes = {};
    this.lastAnyHookTs = -Infinity;
    this.wasActive = false;
    this.pauseStartTs = null;
    this.sentenceBreakPending = false;
    this.restartTs = null;
    this.paceDeviationStartTs = null;
  }

  // ============================================================
  // 规则检查
  // ============================================================

  /**
   * 开口规则：停顿 > 2 秒 且 5 秒内无任何钩子
   * 演讲者长时间沉默无法开口时触发
   */
  private checkKaiKou(ts: number, isActive: boolean): HookTrigger | null {
    if (isActive) return null;
    if (this.pauseStartTs === null) return null;

    const pauseDuration = ts - this.pauseStartTs;
    if (pauseDuration < PAUSE_THRESHOLD_S) return null;

    // 5 秒内无任何钩子
    if (ts - this.lastAnyHookTs < NO_HOOK_WINDOW_S) return null;

    if (this.isRateLimited('开口', ts)) return null;

    return this.createHook('开口', ts);
  }

  /**
   * 思路规则：填充词密度 > 3/10字 且 最近 3 秒内有 >1.5 秒停顿
   * 演讲者思路混乱、填充词过多时触发
   */
  private checkSiLu(ts: number): HookTrigger | null {
    // 收集最近 3 秒文本
    const recentText = this.getRecentText(ts - 3, ts);
    if (recentText.length < 5) return null;

    const fillerCount = this.countFillerWords(recentText);
    const density = fillerCount / (recentText.length / 10);
    if (density <= FILLER_DENSITY_THRESHOLD) return null;

    // 最近 3 秒内有 >1.5 秒的停顿
    if (!this.hasPauseInWindow(ts - 3, ts, SHORT_PAUSE_THRESHOLD_S)) return null;

    if (this.isRateLimited('思路', ts)) return null;

    return this.createHook('思路', ts);
  }

  /**
   * 衔接规则：active→inactive(>1.5s)→active 模式 且 词语不连贯
   * 演讲者句子断裂、衔接不畅时触发
   */
  private checkXianJie(ts: number, isActive: boolean): HookTrigger | null {
    if (!this.sentenceBreakPending) return null;

    // 超过 5 秒后清除待定状态
    if (this.restartTs !== null && ts - this.restartTs > 5) {
      this.sentenceBreakPending = false;
      return null;
    }

    // 需要正在说话
    if (!isActive) return null;

    // 检查恢复后的文本是否不连贯（短或含填充词）
    const recentText = this.getRecentText(this.restartTs ?? ts - 2, ts);
    if (recentText.length === 0) return null;

    const isIncoherent =
      recentText.length < 5 || this.countFillerWords(recentText) > 0;
    if (!isIncoherent) {
      this.sentenceBreakPending = false;
      return null;
    }

    if (this.isRateLimited('衔接', ts)) return null;

    this.sentenceBreakPending = false;
    return this.createHook('衔接', ts);
  }

  /**
   * 节奏规则：语速偏差 > 40%（相对于用户基线），持续 3 秒+
   * 演讲者语速过快或过慢时触发
   */
  private checkJieZou(
    ts: number,
    paceScore?: number,
    baselinePace?: number,
  ): HookTrigger | null {
    if (paceScore === undefined || baselinePace === undefined || baselinePace <= 0) {
      // 无语速数据时不处理，也不重置偏差追踪
      return null;
    }

    const deviation = Math.abs(paceScore - baselinePace) / baselinePace;

    if (deviation > PACE_DEVIATION) {
      // 偏差超阈值
      if (this.paceDeviationStartTs === null) {
        this.paceDeviationStartTs = ts;
      }

      if (ts - this.paceDeviationStartTs >= PACE_DEVIATION_SUSTAINED_S) {
        if (this.isRateLimited('节奏', ts)) return null;
        this.paceDeviationStartTs = null;
        return this.createHook('节奏', ts);
      }
    } else {
      // 偏差恢复正常
      this.paceDeviationStartTs = null;
    }

    return null;
  }

  // ============================================================
  // 辅助方法
  // ============================================================

  /** 更新句子断裂检测状态 */
  private updateSentenceBreakState(ts: number, isActive: boolean): void {
    if (isActive && !this.wasActive) {
      // 从停顿恢复说话
      if (this.pauseStartTs !== null) {
        const pauseDuration = ts - this.pauseStartTs;
        if (pauseDuration > SHORT_PAUSE_THRESHOLD_S) {
          // 检测到 active→inactive(>1.5s)→active 模式
          this.sentenceBreakPending = true;
          this.restartTs = ts;
        }
      }
      this.pauseStartTs = null;
    } else if (!isActive && this.wasActive) {
      // 开始停顿
      this.pauseStartTs = ts;
    } else if (!isActive && this.pauseStartTs === null) {
      // 首帧即为静音
      this.pauseStartTs = ts;
    }
    this.wasActive = isActive;
  }

  /** 检查同类钩子是否被速率限制 */
  private isRateLimited(hookType: string, ts: number): boolean {
    const lastTime = this.lastHookTimes[hookType];
    if (lastTime === undefined) return false;
    return ts - lastTime < RATE_LIMIT_S;
  }

  /** 创建钩子触发对象，随机选择本地兜底文案（倒计时由 update() 覆盖） */
  private createHook(hookType: HookTrigger['hookType'], ts: number): HookTrigger {
    const suggestions = HOOK_SUGGESTIONS[hookType];
    const hookText = suggestions[Math.floor(Math.random() * suggestions.length)];
    return {
      hookType,
      hookText,
      countdown: 5, // 默认值，update() 会用 adaptiveCountdown 覆盖
      ts,
    };
  }

  /** 获取指定时间范围内的文本 */
  private getRecentText(startTs: number, endTs: number): string {
    return this.textHistory
      .filter((t) => t.ts >= startTs && t.ts <= endTs)
      .map((t) => t.text)
      .join('');
  }

  /** 统计文本中填充词出现次数 */
  private countFillerWords(text: string): number {
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

  /**
   * 检查指定时间窗口内是否存在 >= minDuration 秒的连续停顿
   */
  private hasPauseInWindow(startTs: number, endTs: number, minDuration: number): boolean {
    const records = this.energyHistory.filter(
      (r) => r.ts >= startTs && r.ts <= endTs,
    );
    if (records.length < 2) return false;

    let pauseStart: number | null = null;
    for (let i = 0; i < records.length; i++) {
      if (!records[i].isActive) {
        if (pauseStart === null) {
          pauseStart = records[i].ts;
        }
      } else {
        if (pauseStart !== null) {
          if (records[i].ts - pauseStart >= minDuration) {
            return true;
          }
          pauseStart = null;
        }
      }
    }

    // 检查停顿是否延续到窗口末尾
    if (pauseStart !== null) {
      const lastTs = records[records.length - 1].ts;
      if (lastTs - pauseStart >= minDuration) {
        return true;
      }
    }

    return false;
  }

  /** 清理过期数据 */
  private pruneOld(currentTs: number): void {
    const cutoff = currentTs - HISTORY_RETENTION_S;
    if (this.energyHistory.length > 0 && this.energyHistory[0].ts < cutoff) {
      this.energyHistory = this.energyHistory.filter((r) => r.ts >= cutoff);
    }
    if (this.textHistory.length > 0 && this.textHistory[0].ts < cutoff) {
      this.textHistory = this.textHistory.filter((t) => t.ts >= cutoff);
    }
  }
}
