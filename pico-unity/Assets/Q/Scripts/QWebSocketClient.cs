// ============================================================
// QWebSocketClient.cs
// Q (Cue) — WebSocket 客户端
//
// 连接后端 ws://<server>/ws/session，负责：
//   开发环境: ws://localhost:3001/ws/session
//   生产环境: wss://<zeabur部署域名>/ws/session
//   1. 后台线程接收循环 → 主线程分发队列（Unity API 线程安全）
//   2. 指数退避自动重连
//   3. 17 个下行消息类型事件（Action 回调）
//   4. 9 个上行消息发送方法
//
// 线程模型：
//   [后台线程] ReceiveLoop → 解析 JSON → 入队 mainThreadQueue
//   [主线程]   Update → 出队 → 触发对应 UnityAction 事件
// ============================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Q.Pico
{
    /// <summary>
    /// WebSocket 连接状态。
    /// </summary>
    public enum WSConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        Error = 4
    }

    /// <summary>
    /// WebSocket 客户端 — 连接 Q 后端，处理消息收发。
    /// 挂载到场景中的空 GameObject 上即可。
    /// </summary>
    public class QWebSocketClient : MonoBehaviour
    {
        [Header("连接配置")]
        [Tooltip("方式一：拖入 QServerConfig asset（推荐）")]
        public QServerConfig serverConfig;
        [Tooltip("方式二：直接填写 WebSocket 地址")]
        public string serverUrl = "wss://qqqq.preview.aliyun-zeabur.cn/ws/session";
        [Tooltip("自动重连")]
        public bool autoReconnect = true;
        [Tooltip("初始重连间隔（秒）")]
        public float initialReconnectDelay = 1f;
        [Tooltip("最大重连间隔（秒）")]
        public float maxReconnectDelay = 30f;
        [Tooltip("重连最大尝试次数（-1 无限）")]
        public int maxReconnectAttempts = -1;

        [Header("发送配置")]
        [Tooltip("发送缓冲区大小")]
        public int sendBufferSize = 65536;

        [Header("调试")]
        [Tooltip("在控制台打印收发的消息")]
        public bool debugLog = false;

        // ============================================================
        // 下行消息事件（17 个 — 对应 ServerMessage 的 17 种类型）
        // ============================================================

        // --- 评分相关 ---
        [Tooltip("ScoreMessage 事件 — 完整评分")]
        public UnityEvent<ScoreMessage> OnScore = new UnityEvent<ScoreMessage>();
        [Tooltip("ScoreUpdateMessage 事件 — LLM 逻辑分补丁")]
        public UnityEvent<ScoreUpdateMessage> OnScoreUpdate = new UnityEvent<ScoreUpdateMessage>();

        // --- 钩子与恢复 ---
        [Tooltip("HookMessage 事件 — 兜底钩子")]
        public UnityEvent<HookMessage> OnHook = new UnityEvent<HookMessage>();
        [Tooltip("RecoveryMessage 事件 — 恢复确认")]
        public UnityEvent<RecoveryMessage> OnRecovery = new UnityEvent<RecoveryMessage>();

        // --- 段落与语速 ---
        [Tooltip("SegmentEndMessage 事件 — 段结束汇总")]
        public UnityEvent<SegmentEndMessage> OnSegmentEnd = new UnityEvent<SegmentEndMessage>();
        [Tooltip("PaceUpdateMessage 事件 — 语速更新")]
        public UnityEvent<PaceUpdateMessage> OnPaceUpdate = new UnityEvent<PaceUpdateMessage>();

        // --- 会话控制 ---
        [Tooltip("SessionStartMessage 事件 — 会话开始确认")]
        public UnityEvent<SessionStartMessage> OnSessionStart = new UnityEvent<SessionStartMessage>();
        [Tooltip("SessionEndAckMessage 事件 — 会话结束确认")]
        public UnityEvent<SessionEndAckMessage> OnSessionEndAck = new UnityEvent<SessionEndAckMessage>();

        // --- 指环反馈 ---
        [Tooltip("RingFeedbackMessage 事件 — 指环状态反馈")]
        public UnityEvent<RingFeedbackMessage> OnRingFeedback = new UnityEvent<RingFeedbackMessage>();

        // --- ASR 与 Relay ---
        [Tooltip("AsrTranscriptMessage 事件 — ASR 转写")]
        public UnityEvent<AsrTranscriptMessage> OnAsrTranscript = new UnityEvent<AsrTranscriptMessage>();
        [Tooltip("RelayStatusMessage 事件 — Relay 连接状态")]
        public UnityEvent<RelayStatusMessage> OnRelayStatus = new UnityEvent<RelayStatusMessage>();

        // --- 错误 ---
        [Tooltip("ErrorMessage 事件 — 后端错误")]
        public UnityEvent<ErrorMessage> OnError = new UnityEvent<ErrorMessage>();

        // --- v17 新增 ---
        [Tooltip("HeartRateUpdateMessage 事件 — 心率更新")]
        public UnityEvent<HeartRateUpdateMessage> OnHeartRateUpdate = new UnityEvent<HeartRateUpdateMessage>();
        [Tooltip("WalletStatusMessage 事件 — 钱包状态")]
        public UnityEvent<WalletStatusMessage> OnWalletStatus = new UnityEvent<WalletStatusMessage>();
        [Tooltip("WalletConnectUriMessage 事件 — WalletConnect QR URI")]
        public UnityEvent<WalletConnectUriMessage> OnWalletConnectUri = new UnityEvent<WalletConnectUriMessage>();
        [Tooltip("SpeciesUpdateMessage 事件 — 物种映射更新")]
        public UnityEvent<SpeciesUpdateMessage> OnSpeciesUpdate = new UnityEvent<SpeciesUpdateMessage>();
        [Tooltip("CredentialMintedMessage 事件 — 凭证铸造成功")]
        public UnityEvent<CredentialMintedMessage> OnCredentialMinted = new UnityEvent<CredentialMintedMessage>();

        // --- 连接状态 ---
        [Tooltip("连接状态变化事件（WSConnectionState）")]
        public UnityEvent<WSConnectionState> OnConnectionChanged = new UnityEvent<WSConnectionState>();

        // ============================================================
        // 内部状态
        // ============================================================

        private ClientWebSocket ws;
        private CancellationTokenSource cts;
        private Task receiveTask;

        /// <summary>主线程消息分发队列（线程安全）</summary>
        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

        private WSConnectionState currentState = WSConnectionState.Disconnected;
        private int reconnectAttempts = 0;
        private float currentReconnectDelay;
        private bool isShuttingDown = false;
        private int audioSeq = 0;

        /// <summary>当前连接状态</summary>
        public WSConnectionState ConnectionState => currentState;

        /// <summary>是否已连接</summary>
        public bool IsConnected => currentState == WSConnectionState.Connected;

        // ============================================================
        // Unity 生命周期
        // ============================================================

        /// <summary>当前生效的服务器地址</summary>
        private string ResolvedUrl
        {
            get
            {
                // 优先使用 ServerConfig（ScriptableObject）
                if (serverConfig != null)
                    return serverConfig.ResolvedWsUrl;
                // 其次使用直接填写的 serverUrl
                if (!string.IsNullOrEmpty(serverUrl))
                    return serverUrl;
                // 最后使用远程生产默认地址（Zeabur 部署）
                return "wss://qqqq.preview.aliyun-zeabur.cn/ws/session";
            }
        }

        void Awake()
        {
            currentReconnectDelay = initialReconnectDelay;
        }

        void Start()
        {
            // 自动连接
            ConnectAsync();
        }

        void Update()
        {
            // 主线程分发：从队列取出所有待处理 Action 并执行
            while (mainThreadQueue.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[QWS] 主线程分发异常: {e}");
                }
            }

            // 自动重连逻辑（在主线程检查状态）
            if (autoReconnect && currentState == WSConnectionState.Disconnected && !isShuttingDown)
            {
                if (reconnectDelayTimer > 0f)
                {
                    reconnectDelayTimer -= Time.deltaTime;
                    if (reconnectDelayTimer <= 0f)
                    {
                        reconnectDelayTimer = 0f;
                        _ = ConnectAsync(reconnect: true);
                    }
                }
            }
        }

        private float reconnectDelayTimer = 0f;

        void OnApplicationQuit()
        {
            isShuttingDown = true;
            Disconnect();
        }

        void OnDestroy()
        {
            isShuttingDown = true;
            Disconnect();
        }

        // ============================================================
        // 连接管理
        // ============================================================

        /// <summary>
        /// 异步连接到后端 WebSocket 服务器。
        /// </summary>
        /// <param name="reconnect">是否为重连调用</param>
        public async Task ConnectAsync(bool reconnect = false)
        {
            if (currentState == WSConnectionState.Connecting ||
                currentState == WSConnectionState.Connected)
            {
                return;
            }

            // 检查重连次数限制
            if (reconnect && maxReconnectAttempts >= 0 && reconnectAttempts >= maxReconnectAttempts)
            {
                Debug.LogWarning($"[QWS] 已达最大重连次数 {maxReconnectAttempts}，停止重连");
                SetConnectionState(WSConnectionState.Disconnected);
                return;
            }

            SetConnectionState(reconnect ? WSConnectionState.Reconnecting : WSConnectionState.Connecting);

            if (reconnect)
            {
                reconnectAttempts++;
                Debug.Log($"[QWS] 重连第 {reconnectAttempts} 次，延迟 {currentReconnectDelay:F1}s");
            }

            try
            {
                // 取消旧的 CTS
                cts?.Cancel();
                cts = new CancellationTokenSource();

                ws?.Dispose();
                ws = new ClientWebSocket();

                // 连接
                var url = ResolvedUrl;
                Debug.Log($"[QWS] 正在连接: {url}");
                await ws.ConnectAsync(new Uri(url), cts.Token);
                Debug.Log($"[QWS] ✅ 已连接: {url}");

                reconnectAttempts = 0;
                currentReconnectDelay = initialReconnectDelay;
                SetConnectionState(WSConnectionState.Connected);

                // 启动后台接收循环
                receiveTask = Task.Run(() => ReceiveLoop(cts.Token));
            }
            catch (Exception e)
            {
                if (!isShuttingDown)
                {
                    Debug.LogWarning($"[QWS] 连接失败: {e.Message}");
                    SetConnectionState(WSConnectionState.Disconnected);

                    if (autoReconnect)
                    {
                        // 指数退避
                        currentReconnectDelay = Math.Min(currentReconnectDelay * 2f, maxReconnectDelay);
                        reconnectDelayTimer = currentReconnectDelay;
                    }
                }
            }
        }

        /// <summary>手动断开连接。</summary>
        public void Disconnect()
        {
            cts?.Cancel();

            if (ws != null && ws.State == WebSocketState.Open)
            {
                try
                {
                    ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None).Wait(1000);
                }
                catch { /* 忽略关闭异常 */ }
            }

            try { ws?.Dispose(); } catch { }
            ws = null;
            SetConnectionState(WSConnectionState.Disconnected);
        }

        /// <summary>
        /// 立即重连：取消旧 socket 并异步发起全新连接（非阻塞）。
        /// 用于会话结束后强制刷新后端 relay 状态，避免旧连接状态导致下次 start 无 ack。
        /// </summary>
        public void Reconnect()
        {
            // 先强制置为 Disconnected，绕过 ConnectAsync 的状态早退判断
            cts?.Cancel();
            try { ws?.Dispose(); } catch { }
            ws = null;
            // 清空主线程队列里可能残留的旧消息回调，避免串扰
            while (mainThreadQueue.TryDequeue(out _)) { }
            SetConnectionState(WSConnectionState.Disconnected);
            reconnectAttempts = 0;
            currentReconnectDelay = initialReconnectDelay;
            // 若首次连接失败，Update 的自动重连会在该延迟后介入
            reconnectDelayTimer = initialReconnectDelay;
            _ = ConnectAsync(reconnect: false);
        }

        // ============================================================
        // 后台接收循环
        // ============================================================

        /// <summary>
        /// 后台线程接收循环：读取 WebSocket 消息 → 解析 JSON → 入队主线程分发。
        /// </summary>
        private async void ReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[sendBufferSize];
            var messageBuilder = new StringBuilder();

            try
            {
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    messageBuilder.Clear();

                    // 分帧读取（消息可能分多个包）
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            // 服务器关闭连接
                            EnqueueOnMain(() =>
                            {
                                Debug.Log("[QWS] 服务器关闭连接");
                                SetConnectionState(WSConnectionState.Disconnected);
                                if (autoReconnect && !isShuttingDown)
                                {
                                    currentReconnectDelay = Math.Min(currentReconnectDelay * 2f, maxReconnectDelay);
                                    reconnectDelayTimer = currentReconnectDelay;
                                }
                            });
                            return;
                        }
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    string json = messageBuilder.ToString();

                    if (debugLog)
                        Debug.Log($"[QWS] ← {json}");

                    // 解析 JSON 并入队分发（在主线程执行事件回调）
                    EnqueueOnMain(() => DispatchMessage(json));
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception e)
            {
                if (!isShuttingDown && !token.IsCancellationRequested)
                {
                    EnqueueOnMain(() =>
                    {
                        Debug.LogWarning($"[QWS] 接收异常: {e.Message}");
                        SetConnectionState(WSConnectionState.Disconnected);
                        if (autoReconnect)
                        {
                            currentReconnectDelay = Math.Min(currentReconnectDelay * 2f, maxReconnectDelay);
                            reconnectDelayTimer = currentReconnectDelay;
                        }
                    });
                }
            }
        }

        // ============================================================
        // 消息分发（主线程）
        // ============================================================

        /// <summary>
        /// 根据消息 type 字段分发到对应事件。
        /// 在主线程调用（从 mainThreadQueue 出队）。
        /// </summary>
        private void DispatchMessage(string json)
        {
            try
            {
                // 先解析 type 字段
                var jObj = JObject.Parse(json);
                string type = jObj.Value<string>("type");

                if (string.IsNullOrEmpty(type))
                {
                    Debug.LogWarning($"[QWS] 消息缺少 type 字段: {json}");
                    return;
                }

                switch (type)
                {
                    // --- 评分 ---
                    case "score":
                        OnScore?.Invoke(JsonConvert.DeserializeObject<ScoreMessage>(json));
                        break;
                    case "score_update":
                        OnScoreUpdate?.Invoke(JsonConvert.DeserializeObject<ScoreUpdateMessage>(json));
                        break;

                    // --- 钩子与恢复 ---
                    case "hook":
                        OnHook?.Invoke(JsonConvert.DeserializeObject<HookMessage>(json));
                        break;
                    case "recovery":
                        OnRecovery?.Invoke(JsonConvert.DeserializeObject<RecoveryMessage>(json));
                        break;

                    // --- 段落与语速 ---
                    case "segment_end":
                        OnSegmentEnd?.Invoke(JsonConvert.DeserializeObject<SegmentEndMessage>(json));
                        break;
                    case "pace_update":
                        OnPaceUpdate?.Invoke(JsonConvert.DeserializeObject<PaceUpdateMessage>(json));
                        break;

                    // --- 会话控制 ---
                    case "session_started":
                        OnSessionStart?.Invoke(JsonConvert.DeserializeObject<SessionStartMessage>(json));
                        break;
                    case "session_ended":
                        OnSessionEndAck?.Invoke(JsonConvert.DeserializeObject<SessionEndAckMessage>(json));
                        break;

                    // --- 指环反馈 ---
                    case "ring_feedback":
                        OnRingFeedback?.Invoke(JsonConvert.DeserializeObject<RingFeedbackMessage>(json));
                        break;

                    // --- ASR 与 Relay ---
                    case "asr_transcript":
                        OnAsrTranscript?.Invoke(JsonConvert.DeserializeObject<AsrTranscriptMessage>(json));
                        break;
                    case "relay_status":
                        OnRelayStatus?.Invoke(JsonConvert.DeserializeObject<RelayStatusMessage>(json));
                        break;

                    // --- 错误 ---
                    case "error":
                        OnError?.Invoke(JsonConvert.DeserializeObject<ErrorMessage>(json));
                        break;

                    // --- v17 新增 ---
                    case "heart_rate_update":
                        OnHeartRateUpdate?.Invoke(JsonConvert.DeserializeObject<HeartRateUpdateMessage>(json));
                        break;
                    case "wallet_status":
                        OnWalletStatus?.Invoke(JsonConvert.DeserializeObject<WalletStatusMessage>(json));
                        break;
                    case "wallet_connect_uri":
                        OnWalletConnectUri?.Invoke(JsonConvert.DeserializeObject<WalletConnectUriMessage>(json));
                        break;
                    case "species_update":
                        OnSpeciesUpdate?.Invoke(JsonConvert.DeserializeObject<SpeciesUpdateMessage>(json));
                        break;
                    case "credential_minted":
                        OnCredentialMinted?.Invoke(JsonConvert.DeserializeObject<CredentialMintedMessage>(json));
                        break;

                    default:
                        Debug.LogWarning($"[QWS] 未知消息类型: {type}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[QWS] 消息分发异常: {e}\nJSON: {json}");
            }
        }

        // ============================================================
        // 上行消息发送方法（9 个）
        // ============================================================

        // --- 1. 会话控制 ---
        /// <summary>发送会话开始消息。</summary>
        public void SendSessionStart(string userId = null, string userName = null, string walletAddress = null)
        {
            var msg = SessionControlMessage.CreateStart(userId, userName, walletAddress);
            SendJson(msg);
        }

        /// <summary>发送会话结束消息。</summary>
        public void SendSessionEnd()
        {
            var msg = SessionControlMessage.CreateEnd();
            SendJson(msg);
        }

        // --- 2. 音频帧 ---
        /// <summary>发送音频帧（base64 PCM）。</summary>
        public void SendAudioFrame(byte[] pcmData)
        {
            if (pcmData == null || pcmData.Length == 0) return;
            string base64 = Convert.ToBase64String(pcmData);
            var msg = AudioFrameMessage.Create(base64, Interlocked.Increment(ref audioSeq));
            SendJson(msg);
        }

        // --- 3. 能量 ---
        /// <summary>发送能量报告。</summary>
        public void SendEnergy(double ts, double energy, bool isActive)
        {
            var msg = EnergyReportMessage.Create(ts, energy, isActive);
            SendJson(msg);
        }

        // --- 4. 指环命令 ---
        /// <summary>发送指环命令。</summary>
        public void SendRingCommand(RingCommand cmd, double ts)
        {
            var msg = RingCommandMessage.Create(cmd, ts);
            SendJson(msg);
        }

        // --- 5. 心率 ---
        /// <summary>发送心率手动输入。</summary>
        public void SendHeartRate(double ts, int bpm, string userId = null, string source = "manual_panel")
        {
            var msg = HeartRateMessage.Create(ts, bpm, userId, source);
            SendJson(msg);
        }

        // --- 6. 钱包连接 ---
        /// <summary>发送钱包连接消息。</summary>
        public void SendWalletConnect(string address, string walletType, string sessionId = null)
        {
            var msg = WalletConnectMessage.CreateConnect(address, walletType, sessionId);
            SendJson(msg);
        }

        /// <summary>发送钱包断开消息。</summary>
        public void SendWalletDisconnect()
        {
            var msg = WalletConnectMessage.CreateDisconnect();
            SendJson(msg);
        }

        /// <summary>发送钱包状态查询。</summary>
        public void SendWalletStatus()
        {
            var msg = WalletConnectMessage.CreateStatus();
            SendJson(msg);
        }

        // --- 7. 观众反馈 ---
        /// <summary>发送观众反馈数据。</summary>
        public void SendAudienceFeedback(double ts, int faceCount, int attentive, int distracted)
        {
            var msg = AudienceFeedbackMessage.Create(ts, faceCount, attentive, distracted);
            SendJson(msg);
        }

        // --- 8. 手动铸证 ---
        /// <summary>发送手动触发铸证请求。</summary>
        public void SendMintCredential(string milestone, Dictionary<string, double> metrics = null)
        {
            var msg = CredentialMintMessage.Create(milestone, metrics);
            SendJson(msg);
        }

        // --- 9. 转写结果 ---
        /// <summary>发送本地转写结果。</summary>
        public void SendTranscript(double ts, string text, bool isFinal)
        {
            var msg = TranscriptMessage.Create(ts, text, isFinal);
            SendJson(msg);
        }

        // ============================================================
        // 底层发送
        // ============================================================

        /// <summary>
        /// 序列化消息对象为 JSON 并发送。
        /// 使用 ConfigureAwait(false) 避免上下文捕获。
        /// </summary>
        private async void SendJson<T>(T message)
        {
            if (!IsConnected || ws == null || ws.State != WebSocketState.Open)
            {
                if (debugLog)
                    Debug.LogWarning("[QWS] 未连接，消息已丢弃");
                return;
            }

            try
            {
                string json = JsonConvert.SerializeObject(message);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                if (debugLog)
                    Debug.Log($"[QWS] → {json}");

                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[QWS] 发送失败: {e.Message}");
            }
        }

        // ============================================================
        // 工具方法
        // ============================================================

        /// <summary>设置连接状态并触发事件（主线程调用）。</summary>
        private void SetConnectionState(WSConnectionState newState)
        {
            if (currentState == newState) return;
            currentState = newState;
            OnConnectionChanged?.Invoke(newState);
        }

        /// <summary>将 Action 入队到主线程分发队列（线程安全）。</summary>
        private void EnqueueOnMain(Action action)
        {
            mainThreadQueue.Enqueue(action);
        }

        /// <summary>获取当前时间戳（Unix 秒）。</summary>
        public static double GetTimestamp()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }
}
