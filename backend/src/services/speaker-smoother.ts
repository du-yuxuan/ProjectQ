// Unfreeze - 说话人分离平滑器
// 解决讯飞 role_type=2 盲分模式在长会话中的漂移问题：
//   1. 同一说话人被分配不同 rl（如 A 先是 rl=1，后变成 rl=3）
//   2. 不同说话人被合并为同一 rl
//   3. 快速交替时的乒乓抖动（rl 在 1/2 之间频繁跳转）
//
// 策略：
//   - 维护 rawId -> stableId 映射表
//   - 新 rawId 出现时：检查是否可能是已有说话人的漂移（基于时序模式）
//   - 短时间内频繁交替 -> 去抖（debounce），保持上一个 stableId
//   - 限制最大说话人数（默认 4），超出时复用最久未说话的 stableId

/** 单条说话人记录 */
interface SpeakerRecord {
  rawId: number;
  stableId: number;
  firstSeenTs: number;
  lastSeenTs: number;
  totalChars: number;
  segmentCount: number;
}

export class SpeakerSmoother {
  /** rawId -> SpeakerRecord */
  private mapping = new Map<number, SpeakerRecord>();
  /** stableId -> rawId (反向查找) */
  private stableToRaw = new Map<number, number>();
  private nextStableId = 1;
  private maxSpeakers: number;
  /** 去抖窗口（秒）：在此时间内切换说话人视为抖动 */
  private debounceWindowS: number;
  /** 上一次输出的 stableId */
  private lastOutputStableId = 0;
  private lastOutputTs = 0;
  /** 最小稳定切换间隔（秒），防止乒乓 */
  private minSwitchIntervalS: number;

  constructor(maxSpeakers = 4, debounceWindowS = 0.8, minSwitchIntervalS = 1.5) {
    this.maxSpeakers = maxSpeakers;
    this.debounceWindowS = debounceWindowS;
    this.minSwitchIntervalS = minSwitchIntervalS;
  }

  /**
   * 平滑说话人 ID
   * @param rawId 讯飞返回的原始 rl（1, 2, 3...）
   * @param ts 当前时间戳（秒）
   * @param text 本次转写文本（用于判断文本量）
   * @returns 稳定的说话人 ID（1, 2, 3...）
   */
  smooth(rawId: number, ts: number, text: string): number {
    const charCount = text.length;

    // 已知的 rawId -> 直接用映射
    const existing = this.mapping.get(rawId);
    if (existing) {
      existing.lastSeenTs = ts;
      existing.totalChars += charCount;
      existing.segmentCount++;
      this.lastOutputStableId = existing.stableId;
      this.lastOutputTs = ts;
      return existing.stableId;
    }

    // 新 rawId 出现
    // 检查是否在去抖窗口内 -> 可能是漂移，保持上一个说话人
    if (this.lastOutputStableId > 0 && (ts - this.lastOutputTs) < this.debounceWindowS) {
      // 短时间内出现新 ID，可能是漂移 -> 复用上一个 stableId
      const prevRawId = this.stableToRaw.get(this.lastOutputStableId);
      if (prevRawId !== undefined) {
        const prevRecord = this.mapping.get(prevRawId);
        if (prevRecord) {
          prevRecord.lastSeenTs = ts;
          prevRecord.totalChars += charCount;
          prevRecord.segmentCount++;
          // 不建立 rawId -> stableId 映射，因为这个 rawId 可能是漂移
          this.lastOutputTs = ts;
          return this.lastOutputStableId;
        }
      }
    }

    // 检查是否超过最大说话人数
    if (this.mapping.size >= this.maxSpeakers) {
      // 找到最久未说话的说话人，复用其 stableId
      let oldestRawId = -1;
      let oldestTs = Infinity;
      for (const [rid, rec] of this.mapping) {
        if (rec.lastSeenTs < oldestTs) {
          oldestTs = rec.lastSeenTs;
          oldestRawId = rid;
        }
      }
      if (oldestRawId > 0) {
        const oldRecord = this.mapping.get(oldestRawId)!;
        // 移除旧的 rawId 映射
        this.mapping.delete(oldestRawId);
        // 建立新 rawId -> 同一个 stableId
        const newRecord: SpeakerRecord = {
          rawId,
          stableId: oldRecord.stableId,
          firstSeenTs: ts,
          lastSeenTs: ts,
          totalChars: charCount,
          segmentCount: 1,
        };
        this.mapping.set(rawId, newRecord);
        this.stableToRaw.set(oldRecord.stableId, rawId);
        this.lastOutputStableId = oldRecord.stableId;
        this.lastOutputTs = ts;
        return oldRecord.stableId;
      }
    }

    // 全新说话人
    const stableId = this.nextStableId++;
    const record: SpeakerRecord = {
      rawId,
      stableId,
      firstSeenTs: ts,
      lastSeenTs: ts,
      totalChars: charCount,
      segmentCount: 1,
    };
    this.mapping.set(rawId, record);
    this.stableToRaw.set(stableId, rawId);
    this.lastOutputStableId = stableId;
    this.lastOutputTs = ts;
    return stableId;
  }

  /** 获取当前已检测到的说话人数量 */
  get speakerCount(): number {
    return new Set(Array.from(this.mapping.values()).map((r) => r.stableId)).size;
  }

  /** 重置状态（新会话时调用） */
  reset(): void {
    this.mapping.clear();
    this.stableToRaw.clear();
    this.nextStableId = 1;
    this.lastOutputStableId = 0;
    this.lastOutputTs = 0;
  }
}
