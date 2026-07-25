// ============================================================
// QMessageTypes.cs
// Q (Cue) — WebSocket 消息类型定义（C# 侧）
//
// 与后端 backend/src/types.ts 一一对应的 C# struct/enum。
// 所有消息通过 Newtonsoft.Json 序列化/反序列化。
//
// 上行消息（PICO Unity → 后端）：9 种
// 下行消息（后端 → PICO Unity）：17 种
// ============================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Q.Pico
{
    // ============================================================
    // 共享枚举类型
    // ============================================================

    /// <summary>
    /// 兜底钩子类型（与后端 HookType 对应）。
    /// 枚举值用拼音命名（C# 标识符不允许中文字符），
    /// JSON 中以中文字符串传输，需通过 HookTypeConverter 转换。
    /// </summary>
    public enum HookType
    {
        /// <summary>开口（引导用户开口说话）</summary>
        KaiKou = 0,
        /// <summary>思路（提示演讲思路）</summary>
        SiLu = 1,
        /// <summary>衔接（过渡衔接提示）</summary>
        XianJie = 2,
        /// <summary>节奏（语速节奏调整）</summary>
        JieZou = 3
    }

    /// <summary>
    /// 指环命令类型（与后端 RingCommand 对应）。
    /// 枚举名直接与 JSON 字符串匹配（snake_case 合法 C# 标识符）。
    /// </summary>
    public enum RingCommand
    {
        /// <summary>向后旋转</summary>
        rotate_back = 0,
        /// <summary>向前旋转</summary>
        rotate_front = 1,
        /// <summary>挥手</summary>
        wave = 2,
        /// <summary>单击</summary>
        single_click = 3,
        /// <summary>双击（确认）</summary>
        double_click = 4
    }

    /// <summary>
    /// 物种类型（与后端 SpeciesType 对应）。
    /// </summary>
    public enum SpeciesType
    {
        /// <summary>老虎（咄咄逼人）</summary>
        tiger = 0,
        /// <summary>兔子（温和）</summary>
        rabbit = 1,
        /// <summary>猫头鹰（缜密）</summary>
        owl = 2,
        /// <summary>狐狸（活跃）</summary>
        fox = 3,
        /// <summary>狮子（强势/领导）</summary>
        lion = 4,
        /// <summary>狼（突然强硬）</summary>
        wolf = 5,
        /// <summary>鹿（中性/友好）</summary>
        deer = 6,
        /// <summary>默认</summary>
        @default = 7
    }

    /// <summary>
    /// 心率紧张度等级。
    /// </summary>
    public enum TensionLevel
    {
        /// <summary>平静（&lt;90 bpm）</summary>
        calm = 0,
        /// <summary>正常（90-120 bpm）</summary>
        normal = 1,
        /// <summary>紧张（120-160 bpm）</summary>
        tense = 2,
        /// <summary>恐慌（≥160 bpm）</summary>
        panic = 3
    }

    /// <summary>
    /// Relay 连接状态。
    /// </summary>
    public enum RelayStatus
    {
        connecting = 0,
        connected = 1,
        error = 2,
        closed = 3
    }

    // ============================================================
    // 枚举辅助转换工具
    // ============================================================

    /// <summary>
    /// HookType / RingCommand / SpeciesType 等枚举与 JSON 字符串之间的转换工具。
    /// 后端 HookType 以中文传输（"开口"/"思路"/"衔接"/"节奏"），
    /// 此类负责在中文字符串与 C# 枚举之间双向映射。
    /// </summary>
    public static class EnumConverter
    {
        // --- HookType 中文映射 ---

        private static readonly Dictionary<string, HookType> HookTypeMap =
            new Dictionary<string, HookType>
            {
                { "开口", HookType.KaiKou },
                { "思路", HookType.SiLu },
                { "衔接", HookType.XianJie },
                { "节奏", HookType.JieZou },
            };

        private static readonly Dictionary<HookType, string> HookTypeReverseMap =
            new Dictionary<HookType, string>
            {
                { HookType.KaiKou, "开口" },
                { HookType.SiLu, "思路" },
                { HookType.XianJie, "衔接" },
                { HookType.JieZou, "节奏" },
            };

        /// <summary>将中文字符串解析为 HookType 枚举。解析失败返回 KaiKou。</summary>
        public static HookType ParseHookType(string s)
        {
            if (string.IsNullOrEmpty(s)) return HookType.KaiKou;
            if (HookTypeMap.TryGetValue(s, out var v)) return v;
            // 也兼容数字
            if (int.TryParse(s, out int n) && n >= 0 && n <= 3)
                return (HookType)n;
            return HookType.KaiKou;
        }

        /// <summary>将 HookType 枚举转换回中文字符串（用于 JSON 上行）。</summary>
        public static string ToHookTypeString(HookType t)
        {
            return HookTypeReverseMap.TryGetValue(t, out var s) ? s : "开口";
        }

        // --- RingCommand 字符串映射 ---

        private static readonly Dictionary<string, RingCommand> RingCmdMap =
            new Dictionary<string, RingCommand>
            {
                { "rotate_back", RingCommand.rotate_back },
                { "rotate_front", RingCommand.rotate_front },
                { "wave", RingCommand.wave },
                { "single_click", RingCommand.single_click },
                { "double_click", RingCommand.double_click },
            };

        /// <summary>将字符串解析为 RingCommand 枚举。</summary>
        public static RingCommand ParseRingCommand(string s)
        {
            if (string.IsNullOrEmpty(s)) return RingCommand.single_click;
            if (RingCmdMap.TryGetValue(s, out var v)) return v;
            if (int.TryParse(s, out int n) && n >= 0 && n <= 4)
                return (RingCommand)n;
            return RingCommand.single_click;
        }

        /// <summary>将 RingCommand 枚举转换为字符串。</summary>
        public static string ToRingCommandString(RingCommand c)
        {
            return c.ToString();
        }

        // --- SpeciesType 字符串映射 ---

        /// <summary>将字符串解析为 SpeciesType 枚举。</summary>
        public static SpeciesType ParseSpeciesType(string s)
        {
            if (string.IsNullOrEmpty(s)) return SpeciesType.@default;
            if (Enum.TryParse<SpeciesType>(s, out var v)) return v;
            return SpeciesType.@default;
        }

        /// <summary>将 SpeciesType 枚举转换为字符串。</summary>
        public static string ToSpeciesTypeString(SpeciesType s)
        {
            return s == SpeciesType.@default ? "default" : s.ToString();
        }

        // --- TensionLevel 字符串映射 ---

        /// <summary>将字符串解析为 TensionLevel 枚举。</summary>
        public static TensionLevel ParseTension(string s)
        {
            if (string.IsNullOrEmpty(s)) return TensionLevel.normal;
            if (Enum.TryParse<TensionLevel>(s, out var v)) return v;
            return TensionLevel.normal;
        }

        /// <summary>将 TensionLevel 枚举转换为中文字符串（用于 UI 显示）。</summary>
        public static string ToTensionLabel(TensionLevel t)
        {
            switch (t)
            {
                case TensionLevel.calm: return "平静";
                case TensionLevel.normal: return "正常";
                case TensionLevel.tense: return "紧张";
                case TensionLevel.panic: return "恐慌";
                default: return "正常";
            }
        }

        // --- RelayStatus 字符串映射 ---

        /// <summary>将字符串解析为 RelayStatus 枚举。</summary>
        public static RelayStatus ParseRelayStatus(string s)
        {
            if (string.IsNullOrEmpty(s)) return RelayStatus.closed;
            if (Enum.TryParse<RelayStatus>(s, out var v)) return v;
            return RelayStatus.closed;
        }
    }

    // ============================================================
    // 上行消息（PICO Unity → 后端）
    // ============================================================

    /// <summary>
    /// 音频帧：base64 编码的 16kHz 16bit PCM。
    /// type = "audio"
    /// </summary>
    [Serializable]
    public struct AudioFrameMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "audio"
        [JsonProperty("data")] public string data;          // base64 PCM
        [JsonProperty("seq")] public int seq;               // 序号

        public static AudioFrameMessage Create(string base64Data, int seq) =>
            new AudioFrameMessage { type = "audio", data = base64Data, seq = seq };
    }

    /// <summary>
    /// PICO AudioContext 计算的能量包络。
    /// type = "energy"
    /// </summary>
    [Serializable]
    public struct EnergyReportMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "energy"
        [JsonProperty("ts")] public double ts;               // 时间戳
        [JsonProperty("energy")] public double energy;      // 能量值
        [JsonProperty("isActive")] public bool isActive;    // 是否在说话

        public static EnergyReportMessage Create(double ts, double energy, bool isActive) =>
            new EnergyReportMessage { type = "energy", ts = ts, energy = energy, isActive = isActive };
    }

    /// <summary>
    /// 指环命令（BLE → PICO → 后端）。
    /// type = "ring"
    /// </summary>
    [Serializable]
    public struct RingCommandMessage
    {
        [JsonProperty("type")] public string type;                      // 固定 "ring"
        [JsonProperty("cmd")] public string cmd;                        // RingCommand 字符串
        [JsonProperty("ts")] public double ts;                         // 时间戳

        public static RingCommandMessage Create(RingCommand cmd, double ts) =>
            new RingCommandMessage
            {
                type = "ring",
                cmd = EnumConverter.ToRingCommandString(cmd),
                ts = ts
            };
    }

    /// <summary>
    /// 会话控制（开始/结束）。
    /// type = "session_control"
    /// </summary>
    [Serializable]
    public struct SessionControlMessage
    {
        [JsonProperty("type")] public string type;                      // 固定 "session_control"
        [JsonProperty("action")] public string action;                   // "start" | "end"
        [JsonProperty("userId")] public string userId;                  // 可选
        [JsonProperty("userName")] public string userName;               // 可选
        [JsonProperty("walletAddress")] public string walletAddress;     // v17 可选

        // Newtonsoft.Json 默认不序列化 null/空字段，但这里使用 ExplicitNullHandling 更安全
        // 空字符串会被序列化，null 会被忽略（取决于配置）
        public static SessionControlMessage CreateStart(string userId = null, string userName = null, string walletAddress = null) =>
            new SessionControlMessage
            {
                type = "session_control",
                action = "start",
                userId = userId,
                userName = userName,
                walletAddress = walletAddress
            };

        public static SessionControlMessage CreateEnd() =>
            new SessionControlMessage { type = "session_control", action = "end" };
    }

    /// <summary>
    /// 转写结果（本地 Web Speech API 或讯飞 ASR）。
    /// type = "transcript"
    /// </summary>
    [Serializable]
    public struct TranscriptMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "transcript"
        [JsonProperty("ts")] public double ts;               // 时间戳
        [JsonProperty("text")] public string text;          // 转写文本
        [JsonProperty("isFinal")] public bool isFinal;       // 是否最终结果

        public static TranscriptMessage Create(double ts, string text, bool isFinal) =>
            new TranscriptMessage { type = "transcript", ts = ts, text = text, isFinal = isFinal };
    }

    /// <summary>
    /// v17 新增：心率手动输入。
    /// type = "heart_rate"
    /// </summary>
    [Serializable]
    public struct HeartRateMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "heart_rate"
        [JsonProperty("ts")] public double ts;               // 时间戳
        [JsonProperty("bpm")] public int bpm;               // 每分钟心跳数
        [JsonProperty("userId")] public string userId;       // 可选
        [JsonProperty("source")] public string source;      // 来源标记

        public static HeartRateMessage Create(double ts, int bpm, string userId = null, string source = "manual_panel") =>
            new HeartRateMessage { type = "heart_rate", ts = ts, bpm = bpm, userId = userId, source = source };
    }

    /// <summary>
    /// v17 新增：钱包连接。
    /// type = "wallet_connect"
    /// action: "connect" | "disconnect" | "status"
    /// </summary>
    [Serializable]
    public struct WalletConnectMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "wallet_connect"
        [JsonProperty("action")] public string action;          // "connect" | "disconnect" | "status"
        [JsonProperty("address")] public string address;        // 钱包地址（可选）
        [JsonProperty("walletType")] public string walletType;  // keplr | metamask | walletconnect | leap | ledger
        [JsonProperty("sessionId")] public string sessionId;    // WalletConnect 会话 ID

        public static WalletConnectMessage CreateConnect(string address, string walletType, string sessionId = null) =>
            new WalletConnectMessage
            {
                type = "wallet_connect",
                action = "connect",
                address = address,
                walletType = walletType,
                sessionId = sessionId
            };

        public static WalletConnectMessage CreateDisconnect() =>
            new WalletConnectMessage { type = "wallet_connect", action = "disconnect" };

        public static WalletConnectMessage CreateStatus() =>
            new WalletConnectMessage { type = "wallet_connect", action = "status" };
    }

    /// <summary>
    /// v17 新增：观众反馈（PICO 摄像头 SpatialML 检测结果）。
    /// type = "audience_feedback"
    /// </summary>
    [Serializable]
    public struct AudienceFeedbackMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "audience_feedback"
        [JsonProperty("ts")] public double ts;                   // 时间戳
        [JsonProperty("faceCount")] public int faceCount;       // 检测到的人脸数
        [JsonProperty("attentive")] public int attentive;      // 专注人数
        [JsonProperty("distracted")] public int distracted;    // 走神人数

        public static AudienceFeedbackMessage Create(double ts, int faceCount, int attentive, int distracted) =>
            new AudienceFeedbackMessage
            {
                type = "audience_feedback",
                ts = ts,
                faceCount = faceCount,
                attentive = attentive,
                distracted = distracted
            };
    }

    /// <summary>
    /// v17 新增：手动触发铸证（演示用）。
    /// type = "mint_credential"
    /// </summary>
    [Serializable]
    public struct CredentialMintMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "mint_credential"
        [JsonProperty("milestone")] public string milestone;    // 里程碑类型
        [JsonProperty("metrics")] public string metrics;        // JSON 字符串（Dict→string）

        public static CredentialMintMessage Create(string milestone, Dictionary<string, double> metrics = null) =>
            new CredentialMintMessage
            {
                type = "mint_credential",
                milestone = milestone,
                metrics = metrics != null ? JsonConvert.SerializeObject(metrics) : null
            };
    }

    // ============================================================
    // 下行消息（后端 → PICO Unity）
    // ============================================================

    /// <summary>
    /// ASR 用户转写（实时转写流）。
    /// type = "asr_transcript"
    /// </summary>
    [Serializable]
    public struct AsrTranscriptMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "asr_transcript"
        [JsonProperty("ts")] public double ts;              // 时间戳
        [JsonProperty("text")] public string text;          // 转写文本
        [JsonProperty("isFinal")] public bool isFinal;       // 是否最终结果
        [JsonProperty("speaker")] public int? speaker;      // 说话人 ID（可空）
    }

    /// <summary>
    /// Relay 连接状态。
    /// type = "relay_status"
    /// </summary>
    [Serializable]
    public struct RelayStatusMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "relay_status"
        [JsonProperty("status")] public string status;      // connecting | connected | error | closed
        [JsonProperty("message")] public string message;    // 可选描述

        public RelayStatus Status => EnumConverter.ParseRelayStatus(status);
    }

    /// <summary>
    /// 评分消息。
    /// type = "score"
    /// </summary>
    [Serializable]
    public struct ScoreMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "score"
        [JsonProperty("ts")] public double ts;               // 时间戳
        [JsonProperty("fluency")] public double fluency;     // 0-10 流畅度
        [JsonProperty("logic")] public double logic;         // 0-10 逻辑性
        [JsonProperty("pace")] public double pace;           // 0-10 语速
        [JsonProperty("fillers")] public int fillers;        // 口头禅数量
        [JsonProperty("pauses")] public int pauses;          // 停顿次数
        [JsonProperty("text")] public string text;           // 评分文本
        [JsonProperty("reception")] public double? reception; // v17 观众接收度 0-10（可空）
    }

    /// <summary>
    /// LLM 逻辑性评分补丁。
    /// type = "score_update"
    /// </summary>
    [Serializable]
    public struct ScoreUpdateMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "score_update"
        [JsonProperty("ts")] public double ts;               // 时间戳
        [JsonProperty("logic")] public double logic;         // 更新后的逻辑分
        [JsonProperty("reason")] public string reason;      // 可选原因说明
    }

    /// <summary>
    /// 兜底钩子。
    /// type = "hook"
    /// </summary>
    [Serializable]
    public struct HookMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "hook"
        [JsonProperty("ts")] public double ts;                   // 时间戳
        [JsonProperty("hookType")] public string hookType;      // 中文："开口"|"思路"|"衔接"|"节奏"
        [JsonProperty("hookText")] public string hookText;     // 钩子提示文本
        [JsonProperty("countdown")] public double countdown;    // 倒计时秒数

        /// <summary>获取枚举形式的 HookType。</summary>
        public HookType HookType => EnumConverter.ParseHookType(hookType);
    }

    /// <summary>
    /// 恢复确认。
    /// type = "recovery"
    /// </summary>
    [Serializable]
    public struct RecoveryMessage
    {
        [JsonProperty("type")] public string type;                  // 固定 "recovery"
        [JsonProperty("ts")] public double ts;                      // 时间戳
        [JsonProperty("responseTimeMs")] public double responseTimeMs; // 响应时间（毫秒）
        [JsonProperty("recovered")] public bool recovered;         // 是否恢复成功
    }

    /// <summary>
    /// 段结束汇总。
    /// type = "segment_end"
    /// </summary>
    [Serializable]
    public struct SegmentEndSummary
    {
        [JsonProperty("avgFluency")] public double avgFluency;
        [JsonProperty("avgLogic")] public double avgLogic;
        [JsonProperty("duration")] public double duration;
        [JsonProperty("fillers")] public int fillers;
        [JsonProperty("pauses")] public int pauses;
    }

    /// <summary>
    /// 段结束汇总消息。
    /// type = "segment_end"
    /// </summary>
    [Serializable]
    public struct SegmentEndMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "segment_end"
        [JsonProperty("ts")] public double ts;                  // 时间戳
        [JsonProperty("summary")] public SegmentEndSummary summary; // 汇总数据
    }

    /// <summary>
    /// 本地计算的语速更新。
    /// type = "pace_update"
    /// </summary>
    [Serializable]
    public struct PaceUpdateMessage
    {
        [JsonProperty("type")] public string type;                  // 固定 "pace_update"
        [JsonProperty("ts")] public double ts;                      // 时间戳
        [JsonProperty("paceScore")] public double paceScore;        // 语速评分 0-10
        [JsonProperty("charsPerSec")] public double charsPerSec;     // 每秒字数
        [JsonProperty("pauseRate")] public double pauseRate;        // 停顿率
    }

    /// <summary>
    /// 会话开始确认。
    /// type = "session_started"
    /// </summary>
    [Serializable]
    public struct SessionStartMessage
    {
        [JsonProperty("type")] public string type;                  // 固定 "session_started"
        [JsonProperty("sessionId")] public string sessionId;
        [JsonProperty("userId")] public string userId;
        [JsonProperty("startTime")] public string startTime;
        [JsonProperty("walletAddress")] public string walletAddress; // v17 可选
    }

    /// <summary>
    /// 会话结束确认。
    /// type = "session_ended"
    /// </summary>
    [Serializable]
    public struct SessionEndAckMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "session_ended"
        [JsonProperty("sessionId")] public string sessionId;
        [JsonProperty("reportUrl")] public string reportUrl;    // 报告 URL
    }

    /// <summary>
    /// 指环状态反馈。
    /// type = "ring_feedback"
    /// </summary>
    [Serializable]
    public struct RingFeedbackMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "ring_feedback"
        [JsonProperty("cmd")] public string cmd;                 // RingCommand 字符串
        [JsonProperty("ts")] public double ts;
        [JsonProperty("acknowledged")] public bool acknowledged;

        public RingCommand Cmd => EnumConverter.ParseRingCommand(cmd);
    }

    /// <summary>
    /// 错误消息。
    /// type = "error"
    /// </summary>
    [Serializable]
    public struct ErrorMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "error"
        [JsonProperty("message")] public string message;         // 错误描述
    }

    // ============================================================
    // v17 新增下行消息
    // ============================================================

    /// <summary>
    /// 心率更新推送（PICO 空间显示紧张度）。
    /// type = "heart_rate_update"
    /// </summary>
    [Serializable]
    public struct HeartRateUpdateMessage
    {
        [JsonProperty("type")] public string type;          // 固定 "heart_rate_update"
        [JsonProperty("ts")] public double ts;              // 时间戳
        [JsonProperty("bpm")] public int bpm;               // 每分钟心跳数
        [JsonProperty("tension")] public string tension;    // calm | normal | tense | panic

        public TensionLevel Tension => EnumConverter.ParseTension(tension);
    }

    /// <summary>
    /// 钱包状态更新（PICO 空间显示连接状态）。
    /// type = "wallet_status"
    /// </summary>
    [Serializable]
    public struct WalletStatusMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "wallet_status"
        [JsonProperty("connected")] public bool connected;     // 是否已连接
        [JsonProperty("address")] public string address;        // 钱包地址（可选）
        [JsonProperty("walletType")] public string walletType;  // 钱包类型
        [JsonProperty("qrUri")] public string qrUri;            // WalletConnect QR 内容（可选）
    }

    /// <summary>
    /// WalletConnect QR 推送（主动推送二维码内容给 PICO 显示）。
    /// type = "wallet_connect_uri"
    /// </summary>
    [Serializable]
    public struct WalletConnectUriMessage
    {
        [JsonProperty("type")] public string type;      // 固定 "wallet_connect_uri"
        [JsonProperty("uri")] public string uri;        // WalletConnect 连接 URI
    }

    /// <summary>
    /// 物种映射更新（PICO 空间渲染物种化身）。
    /// type = "species_update"
    /// </summary>
    [Serializable]
    public struct SpeciesUpdateMessage
    {
        [JsonProperty("type")] public string type;              // 固定 "species_update"
        [JsonProperty("speaker")] public int speaker;          // 说话人 ID
        [JsonProperty("species")] public string species;       // SpeciesType 字符串
        [JsonProperty("emotion")] public string emotion;       // 情绪标签
        [JsonProperty("confidence")] public double confidence; // 置信度 0-1

        public SpeciesType Species => EnumConverter.ParseSpeciesType(species);
    }

    /// <summary>
    /// 凭证元数据（嵌套对象）。
    /// 对应后端 CredentialMintedMessage.metadata。
    /// </summary>
    [Serializable]
    public struct CredentialMetadata
    {
        [JsonProperty("credential_type")] public string credential_type;
        [JsonProperty("level")] public string level;
        [JsonProperty("fluency")] public double fluency;
        [JsonProperty("logic")] public double logic;
        [JsonProperty("reception")] public double reception;
        [JsonProperty("stall_rate")] public double stall_rate;
        [JsonProperty("improvement")] public string improvement;
        [JsonProperty("soulbound")] public bool soulbound;
    }

    /// <summary>
    /// 凭证铸造成功通知（PICO 空间 3D 卡片展示）。
    /// type = "credential_minted"
    /// 注意：字段名为 chainTxHash（非 txHash），metadata 为嵌套对象。
    /// </summary>
    [Serializable]
    public struct CredentialMintedMessage
    {
        [JsonProperty("type")] public string type;                  // 固定 "credential_minted"
        [JsonProperty("chainTxHash")] public string chainTxHash;    // 链上交易哈希
        [JsonProperty("milestone")] public string milestone;        // 里程碑类型
        [JsonProperty("metadata")] public CredentialMetadata metadata; // 凭证元数据
    }
}
