// ============================================================
// SpatialHUDManager.cs
// Q (Cue) — 空间 HUD（运行时自动创建，TMP 缺失时回退 uGUI Text）
// ============================================================

using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Q.Pico
{
    public class SpatialHUDManager : MonoBehaviour
    {
        [Header("评分面板（Component = TMP_Text 或 UI.Text）")]
        public Component fluencyText;
        public Component logicText;
        public Component paceText;
        public Component receptionText;

        [Header("钩子面板")]
        public Component hookText;
        public Component hookTypeLabel;
        public GameObject hookPanel;
        public CountdownTimer countdownTimer;

        [Header("口头禅/心率/状态")]
        public Component fillerCountText;
        public Image fillerBadgeBackground;
        public Component heartRateText;
        public Component heartRateTensionLabel;
        public Image connectionIndicator;
        public Color connectedColor = new Color(0.2f, 0.85f, 0.35f);
        public Color disconnectedColor = new Color(0.9f, 0.25f, 0.25f);
        public Color connectingColor = new Color(0.95f, 0.8f, 0.2f);
        public Component transcriptText;
        public Component statusText;
        public Component sessionTimerText;

        [Header("配置")]
        public float fillerAlertThreshold = 5f;
        public Color fillerAlertColor = new Color(0.9f, 0.2f, 0.2f, 0.9f);
        public Color fillerNormalColor = new Color(0.2f, 0.2f, 0.25f, 0.75f);
        public bool autoCreateUI = true;
        public string defaultUserId = "pico-user";
        public string defaultUserName = "PICO 用户";
        [Tooltip("World Space 悬浮距离（米）")]
        public float hudDistance = 1.55f;
        [Tooltip("World Space 缩放（1920x1080 参考分辨率）")]
        public float hudScale = 0.00115f;

        QWebSocketClient wsClient;
        HeartRateInputPanel hrPanel;
        WalletConnectPanel walletPanel;
        RingInputBridge ringBridge;

        int totalFillers;
        float sessionStartTime;
        int lastScoreFillers;
        bool hasActiveHook;
        bool sessionActive;
        Canvas rootCanvas;
        GameObject uiRoot;
        Component countdownLabel;
        Button sessionToggleButton;
        Component sessionToggleLabel;

        public bool HasActiveHook => hasActiveHook;
        public GameObject HookPanel => hookPanel;

        void Awake()
        {
            try
            {
                if (autoCreateUI && fluencyText == null)
                    BuildRuntimeUI();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HUD] Awake BuildRuntimeUI 失败（已忽略）: {e}");
                autoCreateUI = false;
            }
        }

        void Start()
        {
            try
            {
                if (autoCreateUI && (uiRoot == null || fluencyText == null))
                {
                    Debug.LogWarning("[HUD] Start 时 UI 未就绪，强制 BuildRuntimeUI");
                    BuildRuntimeUI();
                }
                else if (uiRoot != null)
                {
                    ConvertToWorldSpace();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HUD] Start UI 构建失败（已忽略）: {e}");
                autoCreateUI = false;
            }

            wsClient = FindObjectOfType<QWebSocketClient>();
            hrPanel = FindObjectOfType<HeartRateInputPanel>();
            walletPanel = FindObjectOfType<WalletConnectPanel>();
            ringBridge = FindObjectOfType<RingInputBridge>();

            if (wsClient == null)
            {
                Debug.LogWarning("[HUD] QWebSocketClient 未找到");
                SetStatus("未找到 WebSocket 客户端", disconnectedColor);
            }
            else
            {
                SubscribeEvents();
                SetStatus("连接中…", connectingColor);
            }

            BindRingBridge();
            sessionStartTime = Time.time;
            UpdateScoreDisplay(0, 0, 0, 0);
            UpdateFillerBadge(0);
            UpdateHeartRate(0, "normal");
            if (hookPanel != null) hookPanel.SetActive(false);

            try
            {
                if (Unity.XR.PXR.PXR_Manager.EnableVideoSeeThrough == false)
                    Unity.XR.PXR.PXR_Manager.EnableVideoSeeThrough = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HUD] 开启视频透视失败: {e.Message}");
            }

            Debug.Log($"[HUD] Start 完成 uiRoot={(uiRoot != null)} tmp={QUIFactory.IsTmpAvailable}");
        }

        public void ForceShowUI()
        {
            if (uiRoot == null || fluencyText == null) BuildRuntimeUI();
            if (uiRoot != null)
            {
                uiRoot.SetActive(true);
                ConvertToWorldSpace();
            }
        }

        void Update()
        {
            if (ringBridge == null) BindRingBridge();

            if (sessionTimerText != null && sessionActive)
            {
                float elapsed = Time.time - sessionStartTime;
                int m = Mathf.FloorToInt(elapsed / 60f);
                int s = Mathf.FloorToInt(elapsed % 60f);
                QUIFactory.SetText(sessionTimerText, $"{m:00}:{s:00}");
            }
        }

        void BindRingBridge()
        {
            if (ringBridge == null) ringBridge = FindObjectOfType<RingInputBridge>();
            if (ringBridge == null) return;
            if (ringBridge.hudManager == null) ringBridge.hudManager = this;
            ringBridge.OnSwitchHook.RemoveListener(OnLocalSwitchHook);
            ringBridge.OnSwitchHook.AddListener(OnLocalSwitchHook);
            ringBridge.OnConfirmRecovery.RemoveAllListeners();
            ringBridge.OnConfirmRecovery.AddListener(() => SetStatus("已确认恢复 ✓", connectedColor));
            ringBridge.OnDismissHook.RemoveAllListeners();
            ringBridge.OnDismissHook.AddListener(() => SetStatus("已忽略钩子", connectingColor));
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            if (ringBridge != null)
                ringBridge.OnSwitchHook.RemoveListener(OnLocalSwitchHook);
        }

        // ============================================================
        // Runtime UI
        // ============================================================

        void BuildRuntimeUI()
        {
            if (uiRoot != null)
            {
                uiRoot.SetActive(true);
                ConvertToWorldSpace();
                return;
            }

            // 直接创建 World Space Canvas（头显前方悬浮）
            rootCanvas = QXRUIBootstrap.CreateWorldSpaceCanvas(
                null, "Q_SpatialHUD", distance: hudDistance, scale: hudScale, sortingOrder: 100);
            uiRoot = rootCanvas.gameObject;
            DontDestroyOnLoad(uiRoot);

            // 内容布局基于 1920x1080 sizeDelta（World Space）
            var bg = QUIFactory.CreateImage(uiRoot.transform, "Background", new Color(0.05f, 0.06f, 0.09f, 0.45f));
            QUIFactory.SetRect(bg.rectTransform, 0, 1, 0, 1, 8, 8, -8, -8);
            bg.raycastTarget = false;

            var topBar = QUIFactory.CreateImage(uiRoot.transform, "TopBar", new Color(0.1f, 0.12f, 0.18f, 0.92f));
            QUIFactory.SetRect(topBar.rectTransform, 0, 1, 1, 1, 0, -56, 0, 0);

            // 标题
            var title = QUIFactory.CreateText(topBar.transform, "Title", "Q  卡壳时，递你一句 Cue",
                PicoTypeRole.Title, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(title.transform as RectTransform, 0, 1, 0, 1, 20, -8, -320, 8);
            QUIFactory.SetFontStyleBold(title);

            sessionTimerText = Txt(topBar.transform, "Timer", "00:00", 22, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(sessionTimerText.transform as RectTransform, 0.5f, 0.5f, 0, 1, -40, -8, 40, 8);
            QUIFactory.SetFontStyleBold(sessionTimerText);
            QUIFactory.SetColor(sessionTimerText, new Color(0.7f, 0.85f, 1f));

            connectionIndicator = QUIFactory.CreateImage(topBar.transform, "ConnDot", disconnectedColor);
            QUIFactory.SetRect(connectionIndicator.rectTransform, 1, 1, 0.5f, 0.5f, -48, -10, -28, 10);

            statusText = Txt(topBar.transform, "Status", "启动中…", 16, TextAnchor.MiddleRight, TextAlignmentOptions.Right);
            QUIFactory.SetRect(statusText.transform as RectTransform, 1, 1, 0, 1, -300, -8, -56, 8);
            QUIFactory.SetColor(statusText, new Color(0.8f, 0.8f, 0.85f));

            var scorePanel = QUIFactory.CreateImage(uiRoot.transform, "ScorePanel", new Color(0.12f, 0.14f, 0.2f, 0.88f));
            QUIFactory.SetRect(scorePanel.rectTransform, 0, 0.38f, 0.42f, 0.88f, 16, 0, -8, 0);

            var scoreTitle = Txt(scorePanel.transform, "ScoreTitle", "表达效果", 22, TextAnchor.UpperLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(scoreTitle.transform as RectTransform, 0, 1, 1, 1, 12, -36, -12, -8);
            QUIFactory.SetFontStyleBold(scoreTitle);

            fluencyText = Txt(scorePanel.transform, "Fluency", "流畅  —", 26, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(fluencyText.transform as RectTransform, 0, 1, 0.72f, 0.9f, 16, 0, -12, 0);
            logicText = Txt(scorePanel.transform, "Logic", "逻辑  —", 26, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(logicText.transform as RectTransform, 0, 1, 0.52f, 0.7f, 16, 0, -12, 0);
            paceText = Txt(scorePanel.transform, "Pace", "语速  —", 26, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(paceText.transform as RectTransform, 0, 1, 0.32f, 0.5f, 16, 0, -12, 0);
            receptionText = Txt(scorePanel.transform, "Reception", "接收  —", 26, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(receptionText.transform as RectTransform, 0, 1, 0.12f, 0.3f, 16, 0, -12, 0);

            hookPanel = QUIFactory.CreateImage(uiRoot.transform, "HookPanel", new Color(0.18f, 0.12f, 0.08f, 0.95f)).gameObject;
            QUIFactory.SetRect(hookPanel.GetComponent<RectTransform>(), 0.4f, 1f, 0.58f, 0.88f, 8, 0, -16, 0);

            hookTypeLabel = Txt(hookPanel.transform, "HookType", "开口兜底", 20, TextAnchor.UpperLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(hookTypeLabel.transform as RectTransform, 0, 1, 1, 1, 14, -34, -14, -8);
            QUIFactory.SetColor(hookTypeLabel, new Color(1f, 0.75f, 0.35f));
            QUIFactory.SetFontStyleBold(hookTypeLabel);

            hookText = Txt(hookPanel.transform, "HookText", "接着说", 42, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(hookText.transform as RectTransform, 0, 1, 0.28f, 0.85f, 10, 0, -10, 0);
            QUIFactory.SetFontStyleBold(hookText);

            countdownLabel = Txt(hookPanel.transform, "Countdown", "倒计时 —", 22, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(countdownLabel.transform as RectTransform, 0, 1, 0.05f, 0.28f, 10, 0, -10, 0);
            QUIFactory.SetColor(countdownLabel, new Color(1f, 0.85f, 0.5f));

            var sidePanel = QUIFactory.CreateImage(uiRoot.transform, "SidePanel", new Color(0.12f, 0.14f, 0.2f, 0.88f));
            QUIFactory.SetRect(sidePanel.rectTransform, 0.4f, 1f, 0.42f, 0.56f, 8, 0, -16, 0);

            fillerBadgeBackground = QUIFactory.CreateImage(sidePanel.transform, "FillerBadge", fillerNormalColor);
            QUIFactory.SetRect(fillerBadgeBackground.rectTransform, 0, 0.48f, 0.1f, 0.9f, 10, 0, -4, 0);
            fillerCountText = Txt(fillerBadgeBackground.transform, "FillerText", "口头禅 0/min", 18, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.StretchFull(fillerCountText.transform as RectTransform, 4, 4, 4, 4);

            heartRateText = Txt(sidePanel.transform, "HRText", "心率 —", 20, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(heartRateText.transform as RectTransform, 0.5f, 1f, 0.5f, 0.95f, 8, 0, -10, 0);
            heartRateTensionLabel = Txt(sidePanel.transform, "HRTension", "正常", 18, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(heartRateTensionLabel.transform as RectTransform, 0.5f, 1f, 0.1f, 0.5f, 8, 0, -10, 0);
            QUIFactory.SetColor(heartRateTensionLabel, new Color(0.7f, 0.85f, 0.7f));

            var transcriptPanel = QUIFactory.CreateImage(uiRoot.transform, "TranscriptPanel", new Color(0.08f, 0.09f, 0.12f, 0.88f));
            QUIFactory.SetRect(transcriptPanel.rectTransform, 0, 1, 0.16f, 0.4f, 16, 0, -16, 0);
            var tTitle = Txt(transcriptPanel.transform, "TTitle", "实时转写", 18, TextAnchor.UpperLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(tTitle.transform as RectTransform, 0, 1, 1, 1, 12, -28, -12, -6);
            QUIFactory.SetColor(tTitle, new Color(0.7f, 0.75f, 0.9f));
            transcriptText = Txt(transcriptPanel.transform, "Transcript", "等待语音…", 22, TextAnchor.UpperLeft, TextAlignmentOptions.TopLeft, wrap: true);
            QUIFactory.SetRect(transcriptText.transform as RectTransform, 0, 1, 0, 1, 12, 10, -12, -34);
            QUIFactory.SetWrap(transcriptText, true);
            QUIFactory.SetColor(transcriptText, new Color(0.9f, 0.9f, 0.95f));

            var controlBar = QUIFactory.CreateImage(uiRoot.transform, "ControlBar", new Color(0.1f, 0.12f, 0.18f, 0.95f));
            QUIFactory.SetRect(controlBar.rectTransform, 0, 1, 0.08f, 0.15f, 16, 0, -16, 0);

            sessionToggleButton = QUIFactory.CreateButton(controlBar.transform, "SessionBtn", "开始会话", new Color(0.15f, 0.5f, 0.35f));
            QUIFactory.SetRect(sessionToggleButton.GetComponent<RectTransform>(), 0, 0.2f, 0.15f, 0.85f, 8, 0, -4, 0);
            sessionToggleLabel = sessionToggleButton.transform.Find("Label")?.GetComponent<Component>();
            sessionToggleButton.onClick.AddListener(OnSessionToggleClicked);

            var hrBtn = QUIFactory.CreateButton(controlBar.transform, "HRBtn", "心率", new Color(0.45f, 0.25f, 0.3f));
            QUIFactory.SetRect(hrBtn.GetComponent<RectTransform>(), 0.2f, 0.36f, 0.15f, 0.85f, 4, 0, -4, 0);
            hrBtn.onClick.AddListener(OnOpenHeartRate);

            var walletBtn = QUIFactory.CreateButton(controlBar.transform, "WalletBtn", "钱包", new Color(0.25f, 0.35f, 0.55f));
            QUIFactory.SetRect(walletBtn.GetComponent<RectTransform>(), 0.36f, 0.52f, 0.15f, 0.85f, 4, 0, -4, 0);
            walletBtn.onClick.AddListener(OnOpenWallet);

            var demoHookBtn = QUIFactory.CreateButton(controlBar.transform, "DemoHook", "演示递钩", new Color(0.5f, 0.35f, 0.15f));
            QUIFactory.SetRect(demoHookBtn.GetComponent<RectTransform>(), 0.52f, 0.7f, 0.15f, 0.85f, 4, 0, -4, 0);
            demoHookBtn.onClick.AddListener(OnDemoHook);

            var dismissBtn = QUIFactory.CreateButton(controlBar.transform, "Dismiss", "忽略钩子", new Color(0.35f, 0.25f, 0.25f));
            QUIFactory.SetRect(dismissBtn.GetComponent<RectTransform>(), 0.7f, 0.85f, 0.15f, 0.85f, 4, 0, -4, 0);
            dismissBtn.onClick.AddListener(OnLocalDismissHook);

            var confirmBtn = QUIFactory.CreateButton(controlBar.transform, "Confirm", "确认恢复", new Color(0.2f, 0.4f, 0.3f));
            QUIFactory.SetRect(confirmBtn.GetComponent<RectTransform>(), 0.85f, 1f, 0.15f, 0.85f, 4, 0, -8, 0);
            confirmBtn.onClick.AddListener(OnLocalConfirmRecovery);

            var tip = Txt(uiRoot.transform, "Tip",
                "手柄：Trigger确认 · Grip忽略 · 摇杆切换 · 左X心率 · 左Y钱包 · 左摇杆点会话 · 右菜单演示递钩",
                15, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(tip.transform as RectTransform, 0, 1, 0, 0.08f, 16, 4, -16, 0);
            QUIFactory.SetColor(tip, new Color(0.55f, 0.58f, 0.65f));

            var ctGo = new GameObject("CountdownTimer");
            ctGo.transform.SetParent(hookPanel.transform, false);
            countdownTimer = ctGo.AddComponent<CountdownTimer>();
            countdownTimer.BindText(countdownLabel);

            ConvertToWorldSpace();
            Debug.Log($"[HUD] 运行时 UI 已创建 mode={rootCanvas?.renderMode} tmp={QUIFactory.IsTmpAvailable}");
        }

        static Component Txt(Transform parent, string name, string content, float size,
            TextAnchor legacy, TextAlignmentOptions tmp, bool wrap = false)
        {
            // 映射到 PICO 文字角色字重
            PicoFontWeight w = PicoFontWeight.Regular;
            if (size >= 34f) w = PicoFontWeight.Bold;
            else if (size >= 24f) w = PicoFontWeight.Medium;
            else if (size >= 18f) w = PicoFontWeight.Medium;
            return QUIFactory.CreateText(parent, name, content, size, w, legacy, tmp, wrap: wrap);
        }

        public void ConvertToWorldSpace()
        {
            if (uiRoot == null) return;
            QXRUIBootstrap.MakeWorldSpaceHUD(uiRoot, distance: hudDistance, scale: hudScale);
            if (rootCanvas != null)
            {
                rootCanvas.renderMode = RenderMode.WorldSpace;
                rootCanvas.sortingOrder = 100;
            }
        }

        public void ToggleSessionFromUI() => OnSessionToggleClicked();
        public void TriggerDemoHook() => OnDemoHook();

        void OnSessionToggleClicked()
        {
            if (wsClient == null) wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient == null) { SetStatus("无 WebSocket 客户端", disconnectedColor); return; }

            if (!sessionActive)
            {
                if (!wsClient.IsConnected) { SetStatus("尚未连接后端，无法开始", disconnectedColor); return; }
                wsClient.SendSessionStart(defaultUserId, defaultUserName);
                SetStatus("正在开始会话…", connectingColor);
            }
            else
            {
                wsClient.SendSessionEnd();
                SetStatus("正在结束会话…", connectingColor);
            }
        }

        void OnOpenHeartRate()
        {
            if (hrPanel == null) hrPanel = FindObjectOfType<HeartRateInputPanel>();
            if (hrPanel != null) hrPanel.ShowPanel();
            else SetStatus("心率面板未就绪", connectingColor);
        }

        void OnOpenWallet()
        {
            if (walletPanel == null) walletPanel = FindObjectOfType<WalletConnectPanel>();
            if (walletPanel != null) walletPanel.ShowPanel();
            else SetStatus("钱包面板未就绪", connectingColor);
        }

        void OnDemoHook()
        {
            OnHook(new HookMessage
            {
                type = "hook",
                ts = QWebSocketClient.GetTimestamp(),
                hookType = "开口",
                hookText = "先说结论",
                countdown = 5.0
            });
        }

        public void HideHookPanel()
        {
            hasActiveHook = false;
            if (hookPanel != null) hookPanel.SetActive(false);
            if (countdownTimer != null) countdownTimer.ClearDisplay();
        }

        public void OnLocalConfirmRecovery()
        {
            if (!hasActiveHook) { SetStatus("当前无激活钩子", connectingColor); return; }
            HideHookPanel();
            SetStatus("已确认恢复 ✓", connectedColor);
        }

        public void OnLocalDismissHook()
        {
            if (!hasActiveHook) { SetStatus("当前无激活钩子", connectingColor); return; }
            HideHookPanel();
            SetStatus("已忽略钩子", connectingColor);
        }

        void OnLocalSwitchHook(HookType type)
        {
            if (hookTypeLabel != null)
                QUIFactory.SetText(hookTypeLabel, $"{EnumConverter.ToHookTypeString(type)}兜底");
            if (hasActiveHook)
                SetStatus($"切换钩子类型 → {EnumConverter.ToHookTypeString(type)}", connectingColor);
        }

        // ============================================================
        // Events
        // ============================================================

        void SubscribeEvents()
        {
            wsClient.OnScore.AddListener(OnScore);
            wsClient.OnScoreUpdate.AddListener(OnScoreUpdate);
            wsClient.OnHook.AddListener(OnHook);
            wsClient.OnRecovery.AddListener(OnRecovery);
            wsClient.OnPaceUpdate.AddListener(OnPaceUpdate);
            wsClient.OnAsrTranscript.AddListener(OnAsrTranscript);
            wsClient.OnRelayStatus.AddListener(OnRelayStatus);
            wsClient.OnError.AddListener(OnError);
            wsClient.OnConnectionChanged.AddListener(OnConnectionChanged);
            wsClient.OnHeartRateUpdate.AddListener(OnHeartRateUpdate);
            wsClient.OnSessionStart.AddListener(OnSessionStart);
            wsClient.OnSessionEndAck.AddListener(OnSessionEnd);
            wsClient.OnCredentialMinted.AddListener(OnCredentialMinted);
            wsClient.OnSpeciesUpdate.AddListener(OnSpeciesUpdate);
        }

        void UnsubscribeEvents()
        {
            if (wsClient == null) return;
            wsClient.OnScore.RemoveListener(OnScore);
            wsClient.OnScoreUpdate.RemoveListener(OnScoreUpdate);
            wsClient.OnHook.RemoveListener(OnHook);
            wsClient.OnRecovery.RemoveListener(OnRecovery);
            wsClient.OnPaceUpdate.RemoveListener(OnPaceUpdate);
            wsClient.OnAsrTranscript.RemoveListener(OnAsrTranscript);
            wsClient.OnRelayStatus.RemoveListener(OnRelayStatus);
            wsClient.OnError.RemoveListener(OnError);
            wsClient.OnConnectionChanged.RemoveListener(OnConnectionChanged);
            wsClient.OnHeartRateUpdate.RemoveListener(OnHeartRateUpdate);
            wsClient.OnSessionStart.RemoveListener(OnSessionStart);
            wsClient.OnSessionEndAck.RemoveListener(OnSessionEnd);
            wsClient.OnCredentialMinted.RemoveListener(OnCredentialMinted);
            wsClient.OnSpeciesUpdate.RemoveListener(OnSpeciesUpdate);
        }

        void OnConnectionChanged(WSConnectionState state)
        {
            // 旧版 SpatialHUDManager 已废弃（改用 QSpatialWorkspace）。
            // 此处仅更新连接指示色，不再自动 SendSessionStart（曾导致"一进来就正在辅助表达"
            // 及与 QSpatialWorkspace 状态打架后按钮失灵）。
            switch (state)
            {
                case WSConnectionState.Connected:
                    SetConnectionColor(connectedColor);
                    SetStatus("已连接后端", connectedColor);
                    break;
                case WSConnectionState.Connecting:
                case WSConnectionState.Reconnecting:
                    SetConnectionColor(connectingColor);
                    SetStatus("连接中…", connectingColor);
                    break;
                default:
                    SetConnectionColor(disconnectedColor);
                    SetStatus("未连接", disconnectedColor);
                    break;
            }
        }

        void OnSessionStart(SessionStartMessage msg)
        {
            sessionActive = true;
            sessionStartTime = Time.time;
            totalFillers = 0;
            lastScoreFillers = 0;
            string sid = msg.sessionId ?? "";
            SetStatus($"会话已开始  {(sid.Length > 8 ? sid.Substring(0, 8) : sid)}…", connectedColor);
            QUIFactory.SetText(transcriptText, "会话已开始，开始说话…");
            QUIFactory.SetText(sessionToggleLabel, "结束会话");
            if (sessionToggleButton != null)
            {
                var img = sessionToggleButton.GetComponent<Image>();
                if (img != null) img.color = new Color(0.55f, 0.2f, 0.2f);
            }
        }

        void OnSessionEnd(SessionEndAckMessage msg)
        {
            sessionActive = false;
            SetStatus("会话已结束", connectingColor);
            HideHookPanel();
            QUIFactory.SetText(sessionToggleLabel, "开始会话");
            if (sessionToggleButton != null)
            {
                var img = sessionToggleButton.GetComponent<Image>();
                if (img != null) img.color = new Color(0.15f, 0.5f, 0.35f);
            }
            if (!string.IsNullOrEmpty(msg.reportUrl))
                AppendTranscript($"\n报告: {msg.reportUrl}");
        }

        void OnScore(ScoreMessage msg)
        {
            UpdateScoreDisplay(msg.fluency, msg.logic, msg.pace, msg.reception ?? 0);
            if (msg.fillers > lastScoreFillers)
                totalFillers += (msg.fillers - lastScoreFillers);
            lastScoreFillers = msg.fillers;
            UpdateFillerBadge(totalFillers);
            if (!string.IsNullOrEmpty(msg.text)) AppendTranscript(msg.text);
        }

        void OnScoreUpdate(ScoreUpdateMessage msg)
        {
            QUIFactory.SetText(logicText, $"逻辑  {msg.logic:0}");
        }

        void OnPaceUpdate(PaceUpdateMessage msg)
        {
            QUIFactory.SetText(paceText, $"语速  {msg.paceScore:0}  ({msg.charsPerSec:0.0}字/秒)");
            if (countdownTimer != null) countdownTimer.UpdatePaceScore(msg.paceScore);
        }

        void OnHook(HookMessage msg)
        {
            hasActiveHook = true;
            if (hookPanel != null) hookPanel.SetActive(true);
            QUIFactory.SetText(hookText, string.IsNullOrEmpty(msg.hookText) ? "接着说" : msg.hookText);
            QUIFactory.SetText(hookTypeLabel, $"{msg.hookType}兜底");
            double duration = msg.countdown > 0 ? msg.countdown : 5.0;
            if (countdownTimer != null) countdownTimer.StartCountdown(msg.ts, duration);
            SetStatus($"递钩：{msg.hookType} — {msg.hookText}", new Color(1f, 0.7f, 0.3f));
        }

        void OnRecovery(RecoveryMessage msg)
        {
            HideHookPanel();
            SetStatus(msg.recovered ? $"已恢复 ({msg.responseTimeMs}ms)" : "未恢复", connectedColor);
        }

        void OnAsrTranscript(AsrTranscriptMessage msg)
        {
            if (string.IsNullOrEmpty(msg.text)) return;
            AppendTranscript(msg.text);
            if (msg.isFinal && countdownTimer != null && countdownTimer.IsRunning())
                countdownTimer.RecordUserOpening();
        }

        void OnRelayStatus(RelayStatusMessage msg) => SetStatus($"ASR: {msg.status} {msg.message}", connectingColor);
        void OnError(ErrorMessage msg) => SetStatus($"错误: {msg.message}", disconnectedColor);
        void OnHeartRateUpdate(HeartRateUpdateMessage msg) => UpdateHeartRate(msg.bpm, msg.tension);

        void OnCredentialMinted(CredentialMintedMessage msg)
        {
            SetStatus($"凭证已铸造: {msg.milestone}", new Color(0.6f, 0.9f, 1f));
        }

        void OnSpeciesUpdate(SpeciesUpdateMessage msg)
        {
            SetStatus($"物种: {msg.species} / {msg.emotion}", new Color(0.5f, 0.85f, 1f));
        }

        void UpdateScoreDisplay(double fluency, double logic, double pace, double reception)
        {
            QUIFactory.SetText(fluencyText, $"流畅  {ScoreOrDash(fluency)}");
            QUIFactory.SetText(logicText, $"逻辑  {ScoreOrDash(logic)}");
            QUIFactory.SetText(paceText, $"语速  {ScoreOrDash(pace)}");
            QUIFactory.SetText(receptionText, $"接收  {ScoreOrDash(reception)}");
            ColorizeScore(fluencyText, fluency);
            ColorizeScore(logicText, logic);
            ColorizeScore(paceText, pace);
            ColorizeScore(receptionText, reception);
        }

        void ColorizeScore(Component t, double v)
        {
            if (t == null || v <= 0) return;
            QUIFactory.SetColor(t, v < 5 ? new Color(1f, 0.35f, 0.4f)
                : v <= 7 ? new Color(1f, 0.8f, 0.3f)
                : new Color(0.3f, 0.95f, 0.5f));
        }

        string ScoreOrDash(double v) => v <= 0 ? "—" : v.ToString("0");

        void UpdateFillerBadge(int count)
        {
            float minutes = Mathf.Max(0.1f, (Time.time - sessionStartTime) / 60f);
            float perMin = count / minutes;
            QUIFactory.SetText(fillerCountText, $"口头禅 {perMin:0.0}/min");
            if (fillerBadgeBackground != null)
                fillerBadgeBackground.color = perMin >= fillerAlertThreshold ? fillerAlertColor : fillerNormalColor;
        }

        void UpdateHeartRate(int bpm, string tension)
        {
            QUIFactory.SetText(heartRateText, bpm > 0 ? $"心率 {bpm}" : "心率 —");
            string label = "正常";
            Color c = new Color(0.7f, 0.85f, 0.7f);
            if (tension == "calm") { label = "平静"; c = new Color(0.5f, 0.9f, 0.6f); }
            else if (tension == "tense") { label = "紧张"; c = new Color(1f, 0.7f, 0.3f); }
            else if (tension == "panic") { label = "恐慌"; c = new Color(1f, 0.3f, 0.3f); }
            QUIFactory.SetText(heartRateTensionLabel, label);
            QUIFactory.SetColor(heartRateTensionLabel, c);
        }

        void AppendTranscript(string text)
        {
            if (transcriptText == null) return;
            string cur = QUIFactory.GetText(transcriptText);
            if (cur == "等待语音…" || cur == "会话已开始，开始说话…") cur = "";
            cur = (cur + " " + text).Trim();
            if (cur.Length > 220) cur = "…" + cur.Substring(cur.Length - 220);
            QUIFactory.SetText(transcriptText, cur);
        }

        void SetStatus(string text, Color color)
        {
            QUIFactory.SetText(statusText, text);
            QUIFactory.SetColor(statusText, color);
        }

        void SetConnectionColor(Color c)
        {
            if (connectionIndicator != null) connectionIndicator.color = c;
        }
    }
}
