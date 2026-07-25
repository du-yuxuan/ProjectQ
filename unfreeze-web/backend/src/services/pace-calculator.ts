// Unfreeze — 中文语速检测引擎
// 滑动窗口 5 秒，每秒更新；基于活跃说话时长与有效字符数计算净语速

/** 能量报告记录 */
interface EnergyRecord {
  ts: number;
  energy: number;
  isActive: boolean;
}

/** 转写文本记录 */
interface TranscriptRecord {
  ts: number;
  text: string;
}

/** 填充词列表 */
const FILLER_WORDS = [
  '嗯', '啊', '呃', '那个', '这个', '就是', '然后', '反正', '其实', '就是说', '基本上',
];

/** 滑动窗口大小（秒） */
const WINDOW_SIZE = 5;

/** 中文字符 Unicode 范围（CJK 统一汉字） */
const CHINESE_CHAR_REGEX = /[\u4e00-\u9fff]/g;

/** 历史数据保留时长（秒），避免内存无限增长 */
const RETENTION_SECONDS = 60;

export class PaceCalculator {
  private energyReports: EnergyRecord[] = [];
  private transcripts: TranscriptRecord[] = [];

  /**
   * 添加能量报告（来自客户端 AudioContext）
   */
  addEnergyReport(ts: number, energy: number, isActive: boolean): void {
    this.energyReports.push({ ts, energy, isActive });
    this.pruneOld(ts);
  }

  /**
   * 添加转写文本片段（来自 StepAudio 评分消息）
   */
  addTranscriptText(text: string, ts: number): void {
    if (text.trim().length === 0) return;
    this.transcripts.push({ ts, text });
    this.pruneOld(ts);
  }

  /**
   * 计算当前语速评分
   * 使用最近 WINDOW_SIZE 秒的数据
   */
  calculate(ts: number): { paceScore: number; charsPerSec: number; pauseRate: number } {
    const windowStart = ts - WINDOW_SIZE;

    // 1. 计算窗口内有效说话时长
    const activeDuration = this.getActiveSpeechDuration(windowStart, ts);

    // 2. 收集窗口内转写文本
    const windowText = this.transcripts
      .filter((t) => t.ts >= windowStart && t.ts <= ts)
      .map((t) => t.text)
      .join('');

    // 3. 统计中文字符数
    const chineseChars = windowText.match(CHINESE_CHAR_REGEX) ?? [];
    const totalChars = chineseChars.length;

    // 4. 统计填充词出现次数
    const fillerCount = this.countFillerWords(windowText);

    // 5. 有效字符数 = 中文字符数 - 填充词次数
    const validChars = Math.max(0, totalChars - fillerCount);

    // 6. 计算净语速（字符/秒）
    const charsPerSec = activeDuration > 0 ? validChars / activeDuration : 0;

    // 7. 计算停顿率 = 1 - 活跃时长 / 窗口时长
    const pauseRate = activeDuration > 0 ? Math.max(0, 1 - activeDuration / WINDOW_SIZE) : 1;

    // 8. 根据净语速映射到 0-10 评分
    const rawPaceScore = this.paceToScore(charsPerSec);

    // 9. 综合评分 = 语速分 * 0.7 + (1 - 停顿率) * 10 * 0.3
    const paceScore = rawPaceScore * 0.7 + (1 - pauseRate) * 10 * 0.3;

    return {
      paceScore: Math.round(paceScore * 10) / 10,
      charsPerSec: Math.round(charsPerSec * 100) / 100,
      pauseRate: Math.round(pauseRate * 100) / 100,
    };
  }

  /**
   * 获取指定时间范围内有效说话时长
   * 通过遍历相邻能量报告，累加 isActive=true 的间隔
   */
  getActiveSpeechDuration(startTs: number, endTs: number): number {
    const records = this.energyReports.filter((r) => r.ts >= startTs && r.ts <= endTs);
    if (records.length < 2) return 0;

    let activeDuration = 0;
    for (let i = 1; i < records.length; i++) {
      const prev = records[i - 1];
      const curr = records[i];
      if (prev.isActive) {
        activeDuration += curr.ts - prev.ts;
      }
    }
    return activeDuration;
  }

  // ============================================================
  // 私有方法
  // ============================================================

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
   * 根据字符/秒映射到 0-10 原始评分
   * 评分表（对称于舒适区 4-7 chars/s）：
   *   < 2.0     -> 0-2
   *   2.0-3.0   -> 3-5
   *   3.0-4.0   -> 6-7
   *   4.0-7.0   -> 8-10（舒适区，中文正常语速）
   *   7.0-8.0   -> 7-8
   *   8.0-9.0   -> 5-6
   *   > 9.0     -> 0-4
   */
  private paceToScore(charsPerSec: number): number {
    if (charsPerSec < 2.0) {
      return (charsPerSec / 2.0) * 2;
    } else if (charsPerSec < 3.0) {
      return 3 + ((charsPerSec - 2.0) / 1.0) * 2;
    } else if (charsPerSec < 4.0) {
      return 6 + ((charsPerSec - 3.0) / 1.0) * 1;
    } else if (charsPerSec < 7.0) {
      return 8 + ((charsPerSec - 4.0) / 3.0) * 2;
    } else if (charsPerSec < 8.0) {
      return 8 - ((charsPerSec - 7.0) / 1.0) * 1;
    } else if (charsPerSec < 9.0) {
      return 6 - ((charsPerSec - 8.0) / 1.0) * 1;
    } else {
      return Math.max(0, 4 - ((charsPerSec - 9.0) / 1.0) * 4);
    }
  }

  /** 清理过期数据，防止内存泄漏 */
  private pruneOld(currentTs: number): void {
    const cutoff = currentTs - RETENTION_SECONDS;
    if (this.energyReports.length > 0 && this.energyReports[0].ts < cutoff) {
      this.energyReports = this.energyReports.filter((r) => r.ts >= cutoff);
    }
    if (this.transcripts.length > 0 && this.transcripts[0].ts < cutoff) {
      this.transcripts = this.transcripts.filter((t) => t.ts >= cutoff);
    }
  }
}
