// ============================================================
// SpeciesMapper.cs
// Q (Cue) — 关键词→物种映射器
//
// 职责：
//   1. 基于关键词映射物种（咄咄逼人→老虎, 温和→兔子, ...）
//   2. 轻量级本地情绪推理（InferEmotion）— 不依赖后端 LLM
//   3. 为每个说话人维护当前物种状态
//
// Inspector 中可自定义关键词→物种映射表。
// ============================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Q.Pico
{
    /// <summary>
    /// 情绪分析结果（轻量级本地推理输出）。
    /// </summary>
    [Serializable]
    public struct EmotionAnalysis
    {
        /// <summary>情绪标签（aggressive/gentle/analytical/lively/dominant/sudden_shift/neutral）</summary>
        public string emotion;
        /// <summary>置信度 0-1</summary>
        public float confidence;
        /// <summary>匹配的关键词列表</summary>
        public string matchedKeywords;

        /// <summary>创建默认分析结果</summary>
        public static EmotionAnalysis Default => new EmotionAnalysis
        {
            emotion = "neutral",
            confidence = 0.5f,
            matchedKeywords = ""
        };
    }

    /// <summary>
    /// 物种映射规则（Inspector 中可配置）。
    /// </summary>
    [Serializable]
    public struct SpeciesMappingRule
    {
        [Tooltip("情绪关键词列表（逗号分隔）")]
        [TextArea(1, 2)]
        public string keywords;
        [Tooltip("情绪标签")]
        public string emotionLabel;
        [Tooltip("目标物种")]
        public SpeciesType species;
    }

    /// <summary>
    /// 关键词→物种映射器 — 轻量级本地情绪推理 + 物种映射。
    /// 在 PICO 侧运行，不依赖后端 LLM，作为 species_update 的快速预判。
    /// </summary>
    public class SpeciesMapper : MonoBehaviour
    {
        [Header("物种映射规则（Inspector 可自定义）")]
        [Tooltip("关键词→物种映射规则。默认值覆盖 7 种物种。")]
        public SpeciesMappingRule[] mappingRules = new SpeciesMappingRule[]
        {
            new SpeciesMappingRule
            {
                keywords = "咄咄逼人, 强势, 压迫, 攻击性, 凶猛",
                emotionLabel = "aggressive",
                species = SpeciesType.tiger
            },
            new SpeciesMappingRule
            {
                keywords = "温和, 柔和, 平和, 善意, 友善",
                emotionLabel = "gentle",
                species = SpeciesType.rabbit
            },
            new SpeciesMappingRule
            {
                keywords = "缜密, 严密, 逻辑, 深思, 理性",
                emotionLabel = "analytical",
                species = SpeciesType.owl
            },
            new SpeciesMappingRule
            {
                keywords = "活跃, 灵动, 敏捷, 机智, 幽默",
                emotionLabel = "lively",
                species = SpeciesType.fox
            },
            new SpeciesMappingRule
            {
                keywords = "领导, 引领, 统帅, 主导, 号召",
                emotionLabel = "dominant",
                species = SpeciesType.lion
            },
            new SpeciesMappingRule
            {
                keywords = "突然强硬, 转为强硬, 骤然, 反转",
                emotionLabel = "sudden_shift",
                species = SpeciesType.wolf
            },
            new SpeciesMappingRule
            {
                keywords = "中性, 平衡, 客观, 默认",
                emotionLabel = "neutral",
                species = SpeciesType.deer
            },
        };

        [Header("推理参数")]
        [Tooltip("关键词匹配最小置信度")]
        public float minConfidence = 0.3f;
        [Tooltip("无关键词匹配时的默认物种")]
        public SpeciesType defaultSpecies = SpeciesType.deer;
        [Tooltip("是否将推理结果发送到后端")]
        public bool sendToBackend = false;

        [Header("调试")]
        public bool debugLog = false;

        // ============================================================
        // 内部缓存
        // ============================================================

        /// <summary>关键词→（情绪标签, 物种）查找表</summary>
        private Dictionary<string, (string emotion, SpeciesType species)> keywordMap;

        // ============================================================
        // Unity 生命周期
        // ============================================================

        void Awake()
        {
            BuildKeywordMap();
        }

        /// <summary>从 Inspector 规则构建关键词查找表</summary>
        private void BuildKeywordMap()
        {
            keywordMap = new Dictionary<string, (string, SpeciesType)>(StringComparer.Ordinal);

            foreach (var rule in mappingRules)
            {
                if (string.IsNullOrEmpty(rule.keywords)) continue;
                string[] keywords = rule.keywords.Split(',');
                foreach (var kw in keywords)
                {
                    string trimmed = kw.Trim();
                    if (trimmed.Length > 0 && !keywordMap.ContainsKey(trimmed))
                    {
                        keywordMap[trimmed] = (rule.emotionLabel, rule.species);
                    }
                }
            }

            if (debugLog)
                Debug.Log($"[SpeciesMapper] 关键词表已构建: {keywordMap.Count} 个关键词");
        }

        // ============================================================
        // 公开方法
        // ============================================================

        /// <summary>
        /// 基于情绪分析结果映射物种。
        /// </summary>
        /// <param name="speaker">说话人 ID</param>
        /// <param name="emotion">情绪分析结果</param>
        /// <returns>目标物种类型</returns>
        public SpeciesType MapSpecies(int speaker, EmotionAnalysis emotion)
        {
            // 直接匹配情绪标签
            foreach (var rule in mappingRules)
            {
                if (rule.emotionLabel == emotion.emotion)
                {
                    if (debugLog)
                        Debug.Log($"[SpeciesMapper] speaker={speaker} emotion={emotion.emotion} → {rule.species}");
                    return rule.species;
                }
            }

            // 未匹配 → 默认物种
            return defaultSpecies;
        }

        /// <summary>
        /// 轻量级本地情绪推理 — 基于文本关键词匹配。
        /// 不依赖后端 LLM，用于快速预判。
        /// </summary>
        /// <param name="text">输入文本（ASR 转写或待分析的内容）</param>
        /// <returns>情绪分析结果</returns>
        public EmotionAnalysis InferEmotion(string text)
        {
            if (string.IsNullOrEmpty(text))
                return EmotionAnalysis.Default;

            var matchedKeywords = new List<string>();
            var matchedEmotions = new Dictionary<string, int>();

            // 逐关键词扫描
            foreach (var kvp in keywordMap)
            {
                if (text.IndexOf(kvp.Key, StringComparison.Ordinal) >= 0)
                {
                    matchedKeywords.Add(kvp.Key);
                    string emo = kvp.Value.emotion;
                    if (!matchedEmotions.ContainsKey(emo))
                        matchedEmotions[emo] = 0;
                    matchedEmotions[emo]++;
                }
            }

            // 无匹配 → 默认
            if (matchedEmotions.Count == 0)
                return EmotionAnalysis.Default;

            // 找出匹配次数最多的情绪
            string bestEmotion = "neutral";
            int bestCount = 0;
            foreach (var kvp in matchedEmotions)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestEmotion = kvp.Key;
                }
            }

            // 置信度 = 匹配次数 / 总关键词数比例（简化计算）
            float confidence = Mathf.Clamp01(
                (float)bestCount / Mathf.Max(1, matchedKeywords.Count));
            if (confidence < minConfidence)
                confidence = minConfidence;

            // 拼接匹配关键词
            var sb = new StringBuilder();
            for (int i = 0; i < matchedKeywords.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(matchedKeywords[i]);
            }

            var result = new EmotionAnalysis
            {
                emotion = bestEmotion,
                confidence = confidence,
                matchedKeywords = sb.ToString()
            };

            if (debugLog)
                Debug.Log($"[SpeciesMapper] InferEmotion: \"{text}\" → {bestEmotion} ({confidence:F2}), keywords: {sb}");

            return result;
        }

        /// <summary>
        /// 一步完成推理 + 映射 + 后端上报。
        /// </summary>
        /// <param name="speaker">说话人 ID</param>
        /// <param name="text">输入文本</param>
        /// <returns>映射的物种类型</returns>
        public SpeciesType InferAndMap(int speaker, string text)
        {
            EmotionAnalysis emotion = InferEmotion(text);
            SpeciesType species = MapSpecies(speaker, emotion);

            // 可选：发送到后端（后端会做更精确的 LLM 推理并回推 species_update）
            if (sendToBackend)
            {
                var wsClient = FindObjectOfType<QWebSocketClient>();
                if (wsClient != null && wsClient.IsConnected)
                {
                    wsClient.SendTranscript(
                        QWebSocketClient.GetTimestamp(),
                        text,
                        isFinal: true
                    );
                }
            }

            return species;
        }

        /// <summary>获取物种的中文标签</summary>
        public static string GetSpeciesChineseLabel(SpeciesType species)
        {
            switch (species)
            {
                case SpeciesType.tiger:   return "老虎";
                case SpeciesType.rabbit:  return "兔子";
                case SpeciesType.owl:     return "猫头鹰";
                case SpeciesType.fox:     return "狐狸";
                case SpeciesType.lion:    return "狮子";
                case SpeciesType.wolf:    return "狼";
                case SpeciesType.deer:    return "鹿";
                default:                  return "默认";
            }
        }
    }
}
