// ============================================================
// QSpatialWorkspace.cs
// 按 q-pico-ui.json 重构的双面板 HUD
//
// 左：control  演讲控制台（mic-stage + 转写手风琴 + 设置手风琴）
// 右：live-hud 评分与钩子（metrics + AI 钩子列表）
// 中间大留白看真实空间
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Q.Pico
{
    public class QSpatialWorkspace : MonoBehaviour
    {
        [Header("布局（World Space）")]
        public float distance = 1.55f;
        public float worldScale = 0.00115f;
        [Tooltip("左右面板中心 X 偏移（设计像素）。越大中间空白越多。映射：px * worldScale = 米。\n"
                 + "默认 560：面板内边距中线 ~374px，中间留约 0.86m 空白。")]
        public float sideOffsetX = 560f;
        public float foldAnimDuration = 0.28f;

        [Header("功能")]
        public bool muteAudienceDefault = true;
        public int maxTranscriptLines = 60;

        // runtime
        QWebSocketClient wsClient;
        GameObject root;
        Canvas leftCanvas;
        Canvas rightCanvas;
        RectTransform leftPanelRt;
        RectTransform rightPanelRt;

        // control panel
        Component livePillLeft;
        Component titleSubLeft;
        Component sessionLabel;
        Component transcriptView;
        ScrollRect transcriptScrollRect;
        RectTransform transcriptContentRt;
        Component recBadge;
        GameObject accTranscriptBody;
        GameObject accSettingsBody;
        GameObject micWaveRow;
        QPicoButton startBtn;
        QPicoButton foldBtn;
        QPicoButton settingsFoldBtn;
        QPicoButton muteBtn;
        Toggle muteToggle;
        Component muteValueLabel;

        // live hud
        Component livePillRight;
        Component titleSubRight;
        Component fluencyValue;
        Component fluencyBar;
        Component fluencyNote;
        Component fillerValue;
        Component fillerBar;
        Component fillerNote;
        Component structureValue;
        Component structureBar;
        Component structureNote;
        Component hooksMeta;
        Component hookFreshType;
        Component hookFreshTime;
        Component hookFreshText;
        Component hookFreshCountdown;
        Image hookFreshFill;
        Component hook2Type;
        Component hook2Time;
        Component hook2Text;
        Component hook3Type;
        Component hook3Time;
        Component hook3Text;
        Component statusFooter;
        QPicoButton confirmBtn;
        QPicoButton dismissBtn;
        GameObject hookFreshCard;
        GameObject hooksIdle;

        // 成长属性面板（覆盖层）
        GameObject growthOverlay;
        Component growthListContent;
        Component growthEmptyHint;
        Component growthWalletLabel;
        QPicoButton growthCloseBtn;
        QPicoButton growthMintBtn;

        // 钱包与分数记录
        string currentWalletAddress = "";
        string currentWalletType = "";
        // 本轮会话累积的最终评分快照
        double _lastFinalFluency, _lastFinalLogic, _lastFinalPace, _lastFinalReception;
        int _lastFinalFillers;

        bool sessionActive;
        bool transcriptOpen = true;
        bool settingsOpen;
        bool muteAudience;
        bool hasActiveHook;
        float sessionStartTime;
        float endRequestTime = -1f;  // 发出 end 请求的时间，-1 表示未等待
        float hookRemaining;
        bool hookCounting;
        float hookTotal = 8f;
        Coroutine foldCo;

        readonly List<TranscriptLine> lines = new List<TranscriptLine>();
        double _fluency = 86, _logic = 78, _pace = 80, _reception;
        int _fillers = 7;
        string _paceNote = "停顿节奏健康";
        string _fillerDetail = "—";

        class TranscriptLine
        {
            public int speaker;
            public string text;
            public bool isFinal;
            public string time;
        }

        void Start()
        {
            muteAudience = muteAudienceDefault;
            var xrui = FindObjectOfType<QXRUIBootstrap>();
            if (xrui != null) xrui.Setup();

            BuildUI();
            BindNetwork();
            HideLegacyHud();

            if (xrui != null) xrui.PatchAllWorldCanvases();
            if (muteAudience)
                ApplyMuteAudience(true, silent: true);

            Debug.Log("[Workspace] 已用 q-pico-ui.json 重构双面板 HUD");
        }

        void Update()
        {
            if (sessionActive)
            {
                float e = Time.time - sessionStartTime;
                int m = Mathf.FloorToInt(e / 60f);
                int s = Mathf.FloorToInt(e % 60f);
                string clock = $"{m:00}:{s:00}";
                QUIFactory.SetText(livePillLeft, "LIVE " + clock);
                QUIFactory.SetText(livePillRight, "LIVE " + clock);
                QUIFactory.SetText(recBadge, "REC " + clock);
                QUIFactory.SetText(sessionLabel, "演讲中 · " + clock);
            }

            if (hookCounting)
            {
                hookRemaining -= Time.deltaTime;
                if (hookRemaining <= 0f)
                {
                    hookRemaining = 0f;
                    hookCounting = false;
                    // 倒计时走完自动恢复（视为用户已恢复）
                    OnConfirmHook();
                }
                QUIFactory.SetText(hookFreshCountdown, hookRemaining > 0 ? $"{hookRemaining:0}s 后自动收起" : "");
                if (hookFreshFill != null && hookTotal > 0.01f)
                {
                    float p = Mathf.Clamp01(hookRemaining / hookTotal);
                    var rt = hookFreshFill.rectTransform;
                    // 从左向右缩短
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(p, 1f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }

            // 会话结束超时：若 endRequestTime >= 0 且超过 5 秒未收到 ack，强制结束
            if (endRequestTime >= 0f && Time.time - endRequestTime > 5f)
            {
                Debug.LogWarning("[Workspace] 结束请求超时 (5s)，强制关闭本地会话状态");
                ForceSessionEnd();
            }

            // 波形动画
            if (micWaveRow != null && sessionActive)
            {
                for (int i = 0; i < micWaveRow.transform.childCount; i++)
                {
                    var t = micWaveRow.transform.GetChild(i) as RectTransform;
                    if (t == null) continue;
                    float h = 8f + 26f * Mathf.Abs(Mathf.Sin(Time.time * 3.2f + i * 0.55f));
                    t.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
                }
            }
        }

        void OnDestroy() => UnbindNetwork();

        void HideLegacyHud()
        {
            // 旧版 SpatialHUDManager 已废弃（改用 QSpatialWorkspace）。
            // 彻底禁用：组件 + 已构建的 UI 根。
            var oldHud = FindObjectOfType<SpatialHUDManager>();
            if (oldHud != null)
            {
                oldHud.autoCreateUI = false;
                // 禁用 MonoBehaviour，确保其 Awake/Start/Update 不再执行
                // （若此前已被场景启用，订阅事件与 BuildRuntimeUI 会与 Workspace 打架）
                oldHud.enabled = false;
            }
            var go = GameObject.Find("Q_SpatialHUD");
            if (go != null) go.SetActive(false);
            // 隐藏旧 workspace canvas 名
            var old = GameObject.Find("Q_WorkspaceCanvas");
            // 若是我们自己新建的同名，不要隐藏：用 root 下的
        }

        // ============================================================
        // Build from design tokens
        // ============================================================

        void BuildUI()
        {
            // 复用场景中已有的 Q_SpatialWorkspace 根节点（若有），避免产生重复屏幕
            root = GameObject.Find("Q_SpatialWorkspace");
            if (root == null)
            {
                root = new GameObject("Q_SpatialWorkspace");
            }
            else
            {
                // 清空旧版序列化进来的子物体（旧的 Q_Panel_Control / Q_Panel_LiveHud 等），
                // 否则它们会在世界空间里残留为"多余的两个屏幕"。
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    Destroy(root.transform.GetChild(i).gameObject);
                }
            }
            DontDestroyOnLoad(root);

            // 固定在世界空间默认位置 (0, 0, 1.55)，支持拖动
            root.transform.position = new Vector3(0, 0, 1.55f);
            root.transform.rotation = Quaternion.identity;

            // 左 control 面板
            leftCanvas = QXRUIBootstrap.CreateWorldSpaceCanvas(
                root.transform, "Q_Panel_Control", distance, worldScale, 120);
            leftPanelRt = leftCanvas.GetComponent<RectTransform>();
            leftPanelRt.sizeDelta = new Vector2(QDesign.DesignW, QDesign.DesignH);
            SafeDisableFollow(leftCanvas.gameObject);

            // 右 live-hud 面板
            rightCanvas = QXRUIBootstrap.CreateWorldSpaceCanvas(
                root.transform, "Q_Panel_LiveHud", distance, worldScale, 121);
            rightPanelRt = rightCanvas.GetComponent<RectTransform>();
            rightPanelRt.sizeDelta = new Vector2(QDesign.DesignW, QDesign.DesignH);
            SafeDisableFollow(rightCanvas.gameObject);

            // 设计像素 → 米：在 root 本地坐标系下水平推开
            // sideOffsetX 是面板中心距中线的像素偏移
            float offsetMeters = sideOffsetX * worldScale;
            leftCanvas.transform.localPosition = new Vector3(-offsetMeters, 0f, 0f);
            rightCanvas.transform.localPosition = new Vector3(offsetMeters, 0f, 0f);
            leftCanvas.transform.localRotation = Quaternion.identity;
            rightCanvas.transform.localRotation = Quaternion.identity;

            BuildControlPanel(leftCanvas.transform);
            BuildLiveHudPanel(rightCanvas.transform);

            var xrui = FindObjectOfType<QXRUIBootstrap>();
            if (xrui != null) xrui.PatchAllWorldCanvases();
            else
            {
                QXRUIBootstrap.PatchCanvas(leftCanvas);
                QXRUIBootstrap.PatchCanvas(rightCanvas);
            }
        }

        static void SafeDisableFollow(GameObject go)
        {
            // CreateWorldSpaceCanvas 自动加 HudFollowHead；直接销毁避免被重新激活
            var f = go.GetComponent<HudFollowHead>();
            if (f != null) Destroy(f);
        }

        // ---------- Control panel (left) ----------

        void BuildControlPanel(Transform panel)
        {
            // 背景
            var bg = MakeImage(panel, "Bg", QDesign.Panel, QDesign.RadiusPanel);
            Stretch(bg.rectTransform);

            float pad = QDesign.SafePad;
            float y = -pad;

            // header
            y = BuildHeader(panel, ref y, "Q", "CONTROL", out livePillLeft, "STANDBY", "cyan");

            // title
            var title = QUIFactory.CreateText(panel, "Title", "演讲控制台",
                PicoTypeRole.Headline, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            QUIFactory.SetFontStyleBold(title);
            SetTop(title.transform as RectTransform, pad, y, -pad, 36);
            y -= 34;

            titleSubLeft = QUIFactory.CreateText(panel, "Sub", "等待开始 · PICO 空间渲染",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt2);
            SetTop(titleSubLeft.transform as RectTransform, pad, y, -pad, 22);
            y -= 28;

            // mic-stage card
            var micCard = MakeCard(panel, "MicStage", QDesign.CardBg);
            SetTop(micCard.rectTransform, pad, y, -pad, 168);
            // left accent bar cyan
            var accent = MakeImage(micCard.transform, "Accent", QDesign.Cyan, 4);
            var art = accent.rectTransform;
            art.anchorMin = new Vector2(0, 0);
            art.anchorMax = new Vector2(0, 1);
            art.pivot = new Vector2(0, 0.5f);
            art.sizeDelta = new Vector2(4, 0);
            art.anchoredPosition = new Vector2(2, 0);

            // waveform
            micWaveRow = new GameObject("Waveform", typeof(RectTransform));
            micWaveRow.transform.SetParent(micCard.transform, false);
            var wrt = micWaveRow.GetComponent<RectTransform>();
            SetTop(wrt, 20, -18, -20, 34);
            BuildWaveform(micWaveRow.transform, 11, QDesign.Cyan);

            startBtn = QPicoButton.Create(micCard.transform, "StartSpeech", "开始辅助表达",
                PicoButtonRole.Passable, PicoButtonSize.Max, OnToggleSession);
            // 用 design primary 色覆盖
            StylePrimaryButton(startBtn);
            Place(startBtn.transform as RectTransform, 0.5f, 1f, 0, -64, 300, 52);

            // meta row
            var meta = QUIFactory.CreateText(micCard.transform, "Meta", "STT  讯飞   ·   指环  —   ·   延迟  —",
                PicoTypeRole.Caption, TextAnchor.MiddleCenter, TMPro.TextAlignmentOptions.Center, QDesign.Txt3);
            SetTop(meta.transform as RectTransform, 16, -130, -16, 22);
            sessionLabel = meta;

            y -= 168 + 14;

            // accordion: 原始录音
            y = BuildAccordion(panel, ref y, "AccTranscript", "原始录音内容", "实时转写 · 填充词已标记",
                true, out accTranscriptBody, out foldBtn, OnToggleTranscript);

            // body of transcript — ScrollRect + ContentSizeFitter 实现自动滚底
            recBadge = QUIFactory.CreateText(accTranscriptBody.transform, "RecBadge", "REC 00:00",
                PicoTypeRole.Caption, TextAnchor.MiddleRight, TMPro.TextAlignmentOptions.Right, QDesign.Cyan);
            SetTop(recBadge.transform as RectTransform, 0, -4, -4, 20);

            // ScrollRect outer
            var scrollGo = new GameObject("TranscriptScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(accTranscriptBody.transform, false);
            transcriptScrollRect = scrollGo.GetComponent<ScrollRect>();
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            QUIFactory.SetRect(scrollRt, 0, 1, 0, 1, 4, 8, -4, -28);

            // Viewport (Mask)
            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            Stretch(vpRt);
            var vpImg = vpGo.GetComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.01f); // 几乎透明，但需要存在以启用 Mask
            vpImg.raycastTarget = false;

            // Content
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(vpGo.transform, false);
            transcriptContentRt = contentGo.GetComponent<RectTransform>();
            transcriptContentRt.anchorMin = new Vector2(0, 1);
            transcriptContentRt.anchorMax = new Vector2(1, 1);
            transcriptContentRt.pivot = new Vector2(0.5f, 1);
            transcriptContentRt.sizeDelta = new Vector2(0, 0);
            // ContentSizeFitter: 垂直方向自适应文本高度
            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            transcriptView = QUIFactory.CreateText(contentGo.transform, "Transcript",
                "等待演讲开始…",
                PicoTypeRole.Body, TextAnchor.UpperLeft, TMPro.TextAlignmentOptions.TopLeft, QDesign.Txt, wrap: true);
            var tvRt = transcriptView.transform as RectTransform;
            tvRt.anchorMin = Vector2.zero;
            tvRt.anchorMax = Vector2.one;
            tvRt.offsetMin = Vector2.zero;
            tvRt.offsetMax = Vector2.zero;
            tvRt.sizeDelta = new Vector2(0, 0);
            // ContentSizeFitter on text too, for proper height calculation
            var tvCsf = (transcriptView as Component)?.gameObject?.AddComponent<ContentSizeFitter>();
            if (tvCsf == null)
                tvCsf = (transcriptView as Component)?.GetComponent<ContentSizeFitter>();
            // Actually add to the content's child text gameObject
            var tvGo = (transcriptView as Component)?.gameObject;
            if (tvGo != null)
            {
                var tCsf = tvGo.AddComponent<ContentSizeFitter>();
                tCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            // Wire ScrollRect
            transcriptScrollRect.content = transcriptContentRt;
            transcriptScrollRect.viewport = vpRt;
            transcriptScrollRect.horizontal = false;
            transcriptScrollRect.vertical = true;
            transcriptScrollRect.movementType = ScrollRect.MovementType.Clamped;
            transcriptScrollRect.scrollSensitivity = 12f;

            y -= 12;

            // accordion: 设置
            y = BuildAccordion(panel, ref y, "AccSettings", "设置", "递钩策略 · 遮脸减压 · 场景",
                false, out accSettingsBody, out settingsFoldBtn, OnToggleSettings);
            BuildSettingsBody(accSettingsBody.transform);
            accSettingsBody.SetActive(false);

            // footer
            var footL = QUIFactory.CreateText(panel, "FootL", "PICO SPATIAL",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt3);
            SetBottom(footL.transform as RectTransform, pad, 14, 160, 20);
            var footR = QUIFactory.CreateText(panel, "FootR", "Q · v17",
                PicoTypeRole.Caption, TextAnchor.MiddleRight, TMPro.TextAlignmentOptions.Right, QDesign.Txt3);
            SetBottom(footR.transform as RectTransform, -pad - 100, 14, 100, 20);
            var frt = footR.transform as RectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(1, 0);
            frt.pivot = new Vector2(1, 0);
            frt.anchoredPosition = new Vector2(-pad, 14);
            frt.sizeDelta = new Vector2(100, 20);
        }

        void BuildSettingsBody(Transform body)
        {
            float yy = -8;

            // 听众遮脸减压 switch row
            var row = MakeCard(body, "MuteRow", HexA("#FFFFFF", 0.03f));
            SetTop(row.rectTransform, 0, yy, 0, 64);

            var lab = QUIFactory.CreateText(row.transform, "L", "听众遮脸减压",
                PicoTypeRole.Label, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            SetTop(lab.transform as RectTransform, 14, -10, -90, 22);

            var desc = QUIFactory.CreateText(row.transform, "D", "冬瓜模型遮挡真实表情",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt3);
            SetTop(desc.transform as RectTransform, 14, -34, -90, 20);

            muteBtn = QPicoButton.Create(row.transform, "MuteSwitch",
                muteAudience ? "开" : "关",
                muteAudience ? PicoButtonRole.Passable : PicoButtonRole.Secondary,
                PicoButtonSize.Min, OnToggleMuteAudience);
            Place(muteBtn.transform as RectTransform, 1f, 0.5f, -14, 0, 64, 32);
            StyleSwitchButton(muteBtn, muteAudience);

            yy -= 76;

            var row2 = MakeCard(body, "HintRow", HexA("#FFFFFF", 0.02f));
            SetTop(row2.rectTransform, 0, yy, 0, 56);
            muteValueLabel = QUIFactory.CreateText(row2.transform, "Hint",
                "开启后：识别人脸 → 冬瓜遮挡 · 停止观众反馈",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt2, wrap: true);
            QUIFactory.SetRect(muteValueLabel.transform as RectTransform, 0, 1, 0, 1, 12, 8, -12, -8);

            yy -= 68;

            // 成长属性入口（Injective 链上成长凭证）
            var row3 = MakeCard(body, "GrowthRow", HexA("#9D8CFF", 0.06f));
            SetTop(row3.rectTransform, 0, yy, 0, 64);

            var gLab = QUIFactory.CreateText(row3.transform, "L", "成长属性",
                PicoTypeRole.Label, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            SetTop(gLab.transform as RectTransform, 14, -10, -90, 22);

            var gDesc = QUIFactory.CreateText(row3.transform, "D", "历次辅助表达评分 · Injective",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt3);
            SetTop(gDesc.transform as RectTransform, 14, -34, -90, 20);

            var growthBtn = QPicoButton.Create(row3.transform, "GrowthOpen", "查看",
                PicoButtonRole.Passable, PicoButtonSize.Min, OnOpenGrowthPanel);
            Place(growthBtn.transform as RectTransform, 1f, 0.5f, -14, 0, 64, 32);
            growthBtn.background.color = QDesign.Violet;
            QUIFactory.SetColor(growthBtn.label, Color.white);
        }

        // ---------- Live HUD (right) ----------

        void BuildLiveHudPanel(Transform panel)
        {
            var bg = MakeImage(panel, "Bg", QDesign.Panel, QDesign.RadiusPanel);
            Stretch(bg.rectTransform);

            float pad = QDesign.SafePad;
            float y = -pad;

            y = BuildHeader(panel, ref y, "Q", "LIVE HUD", out livePillRight, "STANDBY", "cyan");

            var title = QUIFactory.CreateText(panel, "Title", "评分与钩子",
                PicoTypeRole.Headline, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            QUIFactory.SetFontStyleBold(title);
            SetTop(title.transform as RectTransform, pad, y, -pad, 36);
            y -= 34;

            titleSubRight = QUIFactory.CreateText(panel, "Sub", "阶跃星辰实时评判 · 卡壳即递 Cue",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt2);
            SetTop(titleSubRight.transform as RectTransform, pad, y, -pad, 22);
            y -= 28;

            // metrics
            y = BuildMetric(panel, ref y, "流畅度", "FLUENCY", "cyan", out fluencyValue, out fluencyBar, out fluencyNote);
            y = BuildMetric(panel, ref y, "口头禅", "FILLER", "amber", out fillerValue, out fillerBar, out fillerNote);
            y = BuildMetric(panel, ref y, "结构清晰度", "STRUCTURE", "violet", out structureValue, out structureBar, out structureNote);
            RefreshScores();

            // hooks section header
            var hLab = QUIFactory.CreateText(panel, "HooksH", "AI 钩子",
                PicoTypeRole.Label, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            SetTop(hLab.transform as RectTransform, pad, y, -140, 24);
            hooksMeta = QUIFactory.CreateText(panel, "HooksMeta", "EMA —",
                PicoTypeRole.Caption, TextAnchor.MiddleRight, TMPro.TextAlignmentOptions.Right, QDesign.Txt3);
            var hm = hooksMeta.transform as RectTransform;
            hm.anchorMin = hm.anchorMax = new Vector2(1, 1);
            hm.pivot = new Vector2(1, 1);
            hm.anchoredPosition = new Vector2(-pad, y);
            hm.sizeDelta = new Vector2(120, 24);
            y -= 30;

            // fresh hook card (自适应高度)
            hookFreshCard = MakeCard(panel, "HookFresh", QDesign.CyanDim).gameObject;
            var hfr = hookFreshCard.GetComponent<RectTransform>();
            // 用 VerticalLayoutGroup + ContentSizeFitter 自适应内容高度
            SetTop(hfr, pad, y, -pad, 148);
            var hfVLG = hookFreshCard.AddComponent<VerticalLayoutGroup>();
            hfVLG.padding = new RectOffset(14, 14, 10, 8);
            hfVLG.spacing = 6;
            hfVLG.childAlignment = TextAnchor.UpperLeft;
            hfVLG.childForceExpandWidth = true;
            hfVLG.childForceExpandHeight = false;
            hfVLG.childControlWidth = true;
            hfVLG.childControlHeight = true;
            var hfCSF = hookFreshCard.AddComponent<ContentSizeFitter>();
            hfCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var border = hookFreshCard.GetComponent<Image>();
            border.color = HexA("#3AE3D2", 0.10f);

            // type row
            var hTypeRow = new GameObject("TypeRow", typeof(RectTransform));
            hTypeRow.transform.SetParent(hookFreshCard.transform, false);
            var hTypeRowRt = hTypeRow.GetComponent<RectTransform>();
            var hTypeRowLE = hTypeRow.AddComponent<LayoutElement>();
            hTypeRowLE.minHeight = 20;
            hTypeRowLE.preferredHeight = 20;

            hookFreshType = QUIFactory.CreateText(hTypeRow.transform, "Type", "衔接钩",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Cyan);
            Stretch((hookFreshType as Component).GetComponent<RectTransform>());

            hookFreshTime = QUIFactory.CreateText(hTypeRow.transform, "Time", "—",
                PicoTypeRole.Caption, TextAnchor.MiddleRight, TMPro.TextAlignmentOptions.Right, QDesign.Txt3);
            var ht = hookFreshTime.transform as RectTransform;
            ht.anchorMin = ht.anchorMax = new Vector2(1, 0.5f);
            ht.pivot = new Vector2(1, 0.5f);
            ht.anchoredPosition = Vector2.zero;
            ht.sizeDelta = new Vector2(120, 20);

            // hook text (自适应高度)
            var hTextBox = new GameObject("TextRow", typeof(RectTransform));
            hTextBox.transform.SetParent(hookFreshCard.transform, false);
            var hTextBoxLE = hTextBox.AddComponent<LayoutElement>();
            hTextBoxLE.minHeight = 40;
            hTextBoxLE.flexibleHeight = 1f;

            hookFreshText = QUIFactory.CreateText(hTextBox.transform, "Text", "等待卡壳递钩…",
                PicoTypeRole.Body, TextAnchor.UpperLeft, TMPro.TextAlignmentOptions.TopLeft, QDesign.Txt, wrap: true);
            var htfRt = hookFreshText.transform as RectTransform;
            htfRt.anchorMin = Vector2.zero;
            htfRt.anchorMax = Vector2.one;
            htfRt.offsetMin = Vector2.zero;
            htfRt.offsetMax = Vector2.zero;
            // ContentSizeFitter on text for auto-height
            var htfCsf = (hookFreshText as Component).gameObject.AddComponent<ContentSizeFitter>();
            htfCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // countdown bar
            var hBarRow = new GameObject("BarRow", typeof(RectTransform));
            hBarRow.transform.SetParent(hookFreshCard.transform, false);
            var hBarRowLE = hBarRow.AddComponent<LayoutElement>();
            hBarRowLE.minHeight = 5;
            hBarRowLE.preferredHeight = 5;

            var cbg = MakeImage(hBarRow.transform, "CBarBg", HexA("#FFFFFF", 0.08f), 4);
            var cbgRt = cbg.rectTransform;
            cbgRt.anchorMin = Vector2.zero;
            cbgRt.anchorMax = Vector2.one;
            cbgRt.offsetMin = Vector2.zero;
            cbgRt.offsetMax = Vector2.zero;
            hookFreshFill = MakeImage(cbg.transform, "CBar", QDesign.Cyan, 4);
            Stretch(hookFreshFill.rectTransform);

            // countdown text
            var hCdRow = new GameObject("CountdownRow", typeof(RectTransform));
            hCdRow.transform.SetParent(hookFreshCard.transform, false);
            var hCdRowLE = hCdRow.AddComponent<LayoutElement>();
            hCdRowLE.minHeight = 16;
            hCdRowLE.preferredHeight = 16;

            hookFreshCountdown = QUIFactory.CreateText(hCdRow.transform, "CD", "",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt3);
            Stretch((hookFreshCountdown as Component).GetComponent<RectTransform>());

            y -= 156;

            // secondary hooks
            y = BuildHookRow(panel, ref y, "Hook2", "amber", out hook2Type, out hook2Time, out hook2Text);
            y = BuildHookRow(panel, ref y, "Hook3", "violet", out hook3Type, out hook3Time, out hook3Text);
            QUIFactory.SetText(hook2Type, "思路钩");
            QUIFactory.SetText(hook2Text, "—");
            QUIFactory.SetText(hook3Type, "开口钩");
            QUIFactory.SetText(hook3Text, "—");

            // actions
            confirmBtn = QPicoButton.Create(panel, "Confirm", "确认恢复",
                PicoButtonRole.Primary, PicoButtonSize.Regular, OnConfirmHook);
            StylePrimaryButton(confirmBtn);
            Place(confirmBtn.transform as RectTransform, 0f, 0f, pad, 44, 150, 48);

            dismissBtn = QPicoButton.Create(panel, "Dismiss", "忽略",
                PicoButtonRole.Ghost, PicoButtonSize.Regular, OnDismissHook);
            Place(dismissBtn.transform as RectTransform, 1f, 0f, -pad, 44, 120, 48);

            statusFooter = QUIFactory.CreateText(panel, "Foot", "STEPFUN · RTASR",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt3);
            SetBottom(statusFooter.transform as RectTransform, pad, 14, 200, 18);

            var fr = QUIFactory.CreateText(panel, "FootR", "Q · v17",
                PicoTypeRole.Caption, TextAnchor.MiddleRight, TMPro.TextAlignmentOptions.Right, QDesign.Txt3);
            var frr = fr.transform as RectTransform;
            frr.anchorMin = frr.anchorMax = new Vector2(1, 0);
            frr.pivot = new Vector2(1, 0);
            frr.anchoredPosition = new Vector2(-pad, 14);
            frr.sizeDelta = new Vector2(80, 18);

            SetHookActiveVisual(false);
        }

        float BuildMetric(Transform panel, ref float y, string name, string en, string color,
            out Component value, out Component bar, out Component note)
        {
            float pad = QDesign.SafePad;
            var card = MakeCard(panel, "M_" + en, QDesign.CardBg);
            SetTop(card.rectTransform, pad, y, -pad, 92);

            var accent = MakeImage(card.transform, "A", QDesign.Accent(color), 4);
            var art = accent.rectTransform;
            art.anchorMin = new Vector2(0, 0.2f);
            art.anchorMax = new Vector2(0, 0.8f);
            art.pivot = new Vector2(0, 0.5f);
            art.sizeDelta = new Vector2(3, 0);
            art.anchoredPosition = new Vector2(8, 0);

            var n = QUIFactory.CreateText(card.transform, "N", name,
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt2);
            SetTop(n.transform as RectTransform, 18, -12, -100, 18);

            var e = QUIFactory.CreateText(card.transform, "E", en,
                PicoTypeRole.Caption, TextAnchor.MiddleRight, TMPro.TextAlignmentOptions.Right, QDesign.Txt3);
            var er = e.transform as RectTransform;
            er.anchorMin = er.anchorMax = new Vector2(1, 1);
            er.pivot = new Vector2(1, 1);
            er.anchoredPosition = new Vector2(-14, -12);
            er.sizeDelta = new Vector2(100, 18);

            value = QUIFactory.CreateText(card.transform, "V", "—",
                PicoTypeRole.Title, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            QUIFactory.SetFontStyleBold(value);
            SetTop(value.transform as RectTransform, 18, -34, -14, 28);

            // bar bg
            var bbg = MakeImage(card.transform, "BarBg", HexA("#FFFFFF", 0.08f), 4);
            SetTop(bbg.rectTransform, 18, -68, -18, 5);
            var fill = MakeImage(bbg.transform, "Bar", QDesign.Accent(color), 4);
            bar = fill;
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(0.5f, 1);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            note = QUIFactory.CreateText(card.transform, "Note", "",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt3);
            SetTop(note.transform as RectTransform, 18, -76, -18, 14);

            y -= 100;
            return y;
        }

        float BuildHookRow(Transform panel, ref float y, string id, string color,
            out Component type, out Component time, out Component text)
        {
            float pad = QDesign.SafePad;
            var card = MakeCard(panel, id, QDesign.CardBg);
            SetTop(card.rectTransform, pad, y, -pad, 78);

            type = QUIFactory.CreateText(card.transform, "T", "钩",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Accent(color));
            SetTop(type.transform as RectTransform, 14, -8, -90, 18);

            time = QUIFactory.CreateText(card.transform, "Time", "",
                PicoTypeRole.Caption, TextAnchor.MiddleRight, TMPro.TextAlignmentOptions.Right, QDesign.Txt3);
            var tr = time.transform as RectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(1, 1);
            tr.pivot = new Vector2(1, 1);
            tr.anchoredPosition = new Vector2(-12, -8);
            tr.sizeDelta = new Vector2(80, 18);

            text = QUIFactory.CreateText(card.transform, "Tx", "—",
                PicoTypeRole.Body, TextAnchor.UpperLeft, TMPro.TextAlignmentOptions.TopLeft, QDesign.Txt, wrap: true);
            SetTop(text.transform as RectTransform, 14, -30, -12, 42);

            y -= 86;
            return y;
        }

        // ---------- shared builders ----------

        float BuildHeader(Transform panel, ref float y, string icon, string brand,
            out Component pill, string pillText, string pillColor)
        {
            float pad = QDesign.SafePad;
            var iconBg = MakeImage(panel, "BrandIcon", QDesign.CyanDim, 10);
            SetTop(iconBg.rectTransform, pad, y, 0, 32);
            iconBg.rectTransform.anchorMin = iconBg.rectTransform.anchorMax = new Vector2(0, 1);
            iconBg.rectTransform.pivot = new Vector2(0, 1);
            iconBg.rectTransform.anchoredPosition = new Vector2(pad, y);
            iconBg.rectTransform.sizeDelta = new Vector2(32, 32);
            var ic = QUIFactory.CreateText(iconBg.transform, "I", icon,
                PicoTypeRole.Label, TextAnchor.MiddleCenter, TMPro.TextAlignmentOptions.Center, QDesign.Cyan);
            Stretch(ic.transform as RectTransform);

            var br = QUIFactory.CreateText(panel, "Brand", brand,
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt2);
            var brt = br.transform as RectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 1);
            brt.pivot = new Vector2(0, 1);
            brt.anchoredPosition = new Vector2(pad + 40, y - 6);
            brt.sizeDelta = new Vector2(120, 20);

            var pillBg = MakeImage(panel, "Pill", QDesign.AccentDim(pillColor), QDesign.RadiusPill);
            pillBg.rectTransform.anchorMin = pillBg.rectTransform.anchorMax = new Vector2(1, 1);
            pillBg.rectTransform.pivot = new Vector2(1, 1);
            pillBg.rectTransform.anchoredPosition = new Vector2(-pad, y);
            pillBg.rectTransform.sizeDelta = new Vector2(110, 28);
            pill = QUIFactory.CreateText(pillBg.transform, "PT", pillText,
                PicoTypeRole.Caption, TextAnchor.MiddleCenter, TMPro.TextAlignmentOptions.Center, QDesign.Accent(pillColor));
            Stretch(pill.transform as RectTransform);

            y -= 44;
            return y;
        }

        float BuildAccordion(Transform panel, ref float y, string id, string title, string subtitle,
            bool open, out GameObject body, out QPicoButton toggleBtn, UnityEngine.Events.UnityAction onToggle)
        {
            float pad = QDesign.SafePad;
            var head = MakeCard(panel, id + "Head", QDesign.CardBg);
            SetTop(head.rectTransform, pad, y, -pad, 64);

            var t = QUIFactory.CreateText(head.transform, "T", title,
                PicoTypeRole.Label, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            SetTop(t.transform as RectTransform, 16, -10, -70, 22);
            var s = QUIFactory.CreateText(head.transform, "S", subtitle,
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt3);
            SetTop(s.transform as RectTransform, 16, -34, -70, 18);

            toggleBtn = QPicoButton.Create(head.transform, "Toggle", open ? "▾" : "▸",
                PicoButtonRole.Ghost, PicoButtonSize.Min, onToggle);
            Place(toggleBtn.transform as RectTransform, 1f, 0.5f, -12, 0, 44, 36);

            // click whole header
            var hb = head.gameObject.AddComponent<Button>();
            hb.transition = Selectable.Transition.None;
            hb.onClick.AddListener(onToggle);

            y -= 70;

            body = new GameObject(id + "Body", typeof(RectTransform));
            body.transform.SetParent(panel, false);
            var br = body.GetComponent<RectTransform>();
            // body 区域高度：转写用较大
            float bodyH = id.Contains("Transcript") ? 260f : 240f;
            SetTop(br, pad, y, -pad, bodyH);
            body.SetActive(open);
            if (open) y -= bodyH + 8;
            return y;
        }

        void BuildWaveform(Transform parent, int count, Color color)
        {
            var h = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = 4;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlHeight = false;
            h.childControlWidth = false;
            for (int i = 0; i < count; i++)
            {
                var bar = MakeImage(parent, "B" + i, color, 3);
                var le = bar.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 5;
                le.preferredHeight = 10 + (i % 5) * 4;
                bar.rectTransform.sizeDelta = new Vector2(5, le.preferredHeight);
            }
        }

        // ---------- visual helpers ----------

        static Image MakeImage(Transform parent, string name, Color color, float radius = 8f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = QDesign.Round(radius);
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        static Image MakeCard(Transform parent, string name, Color color)
        {
            return MakeImage(parent, name, color, QDesign.RadiusCard);
        }

        static void Stretch(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void SetTop(RectTransform rt, float left, float top, float right, float height)
        {
            if (rt == null) return;
            // top/right: if right negative, treat as inset from right when anchors stretch
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, top);
            rt.sizeDelta = new Vector2(-(Mathf.Abs(left) + Mathf.Abs(right)), height);
            rt.offsetMin = new Vector2(left, top - height);
            rt.offsetMax = new Vector2(right, top);
        }

        static void SetBottom(RectTransform rt, float left, float bottom, float width, float height)
        {
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(left, bottom);
            rt.sizeDelta = new Vector2(width, height);
        }

        static void Place(RectTransform rt, float ax, float ay, float x, float y, float w, float h)
        {
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax <= 0.01f ? 0 : ax >= 0.99f ? 1 : 0.5f,
                                  ay <= 0.01f ? 0 : ay >= 0.99f ? 1 : 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static Color HexA(string hex, float a) => QDesign.HexA(hex, a);

        void StylePrimaryButton(QPicoButton b)
        {
            if (b == null || b.background == null) return;
            // 用 cyan 渐变近似 primary
            b.role = PicoButtonRole.Passable;
            b.background.color = QDesign.Cyan;
            QUIFactory.SetColor(b.label, QDesign.PrimaryOn);
            QUIFactory.SetFontStyleBold(b.label);
        }

        void StyleSwitchButton(QPicoButton b, bool on)
        {
            if (b == null) return;
            b.SetText(on ? "开" : "关");
            b.SetRole(on ? PicoButtonRole.Passable : PicoButtonRole.Secondary);
            if (b.background != null)
                b.background.color = on ? QDesign.Cyan : HexA("#FFFFFF", 0.12f);
            QUIFactory.SetColor(b.label, on ? QDesign.PrimaryOn : QDesign.Txt);
        }

        // ============================================================
        // Data / network
        // ============================================================

        void BindNetwork()
        {
            wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient == null) { SetStatus("未找到 WebSocket"); return; }
            wsClient.OnConnectionChanged.AddListener(OnConn);
            wsClient.OnSessionStart.AddListener(OnSessionStart);
            wsClient.OnSessionEndAck.AddListener(OnSessionEnd);
            wsClient.OnAsrTranscript.AddListener(OnTranscript);
            wsClient.OnHook.AddListener(OnHook);
            wsClient.OnRecovery.AddListener(OnRecovery);
            wsClient.OnScore.AddListener(OnScore);
            wsClient.OnScoreUpdate.AddListener(OnScoreUpdate);
            wsClient.OnPaceUpdate.AddListener(OnPace);
            wsClient.OnSegmentEnd.AddListener(OnSegmentEnd);
            wsClient.OnWalletStatus.AddListener(OnWalletStatus);
            wsClient.OnCredentialMinted.AddListener(OnCredentialMinted);
            wsClient.OnRelayStatus.AddListener(OnRelayStatus);
            wsClient.OnError.AddListener(e => SetStatus("错误: " + e.message));
            SetStatus("连接中…");
        }

        void UnbindNetwork()
        {
            if (wsClient == null) return;
            wsClient.OnConnectionChanged.RemoveListener(OnConn);
            wsClient.OnSessionStart.RemoveListener(OnSessionStart);
            wsClient.OnSessionEndAck.RemoveListener(OnSessionEnd);
            wsClient.OnAsrTranscript.RemoveListener(OnTranscript);
            wsClient.OnHook.RemoveListener(OnHook);
            wsClient.OnRecovery.RemoveListener(OnRecovery);
            wsClient.OnScore.RemoveListener(OnScore);
            wsClient.OnScoreUpdate.RemoveListener(OnScoreUpdate);
            wsClient.OnPaceUpdate.RemoveListener(OnPace);
            wsClient.OnSegmentEnd.RemoveListener(OnSegmentEnd);
            wsClient.OnWalletStatus.RemoveListener(OnWalletStatus);
            wsClient.OnCredentialMinted.RemoveListener(OnCredentialMinted);
            wsClient.OnRelayStatus.RemoveListener(OnRelayStatus);
        }

        void OnConn(WSConnectionState s)
        {
            if (s == WSConnectionState.Connected) SetStatus("已连接后端");
            else if (s == WSConnectionState.Connecting || s == WSConnectionState.Reconnecting) SetStatus("连接中…");
            else SetStatus("未连接");
        }

        void OnSessionStart(SessionStartMessage msg)
        {
            Debug.Log($"[Workspace] OnSessionStart ack received: sessionId={msg.sessionId}");
            endRequestTime = -1f;
            sessionActive = true;
            sessionStartTime = Time.time;
            lines.Clear();
            RefreshTranscript();
            startBtn?.SetText("结束辅助表达");
            startBtn?.SetRole(PicoButtonRole.Danger);
            if (startBtn != null && startBtn.background != null)
                startBtn.background.color = QDesign.Danger;
            QUIFactory.SetColor(startBtn?.label, Color.white);
            QUIFactory.SetText(livePillLeft, "LIVE 00:00");
            QUIFactory.SetText(livePillRight, "LIVE 00:00");
            QUIFactory.SetText(titleSubLeft, "进行中 · 第 1 次会话 · PICO 空间渲染");
            SetStatus("会话已开始");
        }

        void OnSessionEnd(SessionEndAckMessage msg)
        {
            Debug.Log($"[Workspace] OnSessionEnd ack received: reportUrl={msg.reportUrl}");
            endRequestTime = -1f;
            sessionActive = false;
            startBtn?.SetText("开始辅助表达");
            StylePrimaryButton(startBtn);
            QUIFactory.SetText(livePillLeft, "STANDBY");
            QUIFactory.SetText(livePillRight, "STANDBY");
            QUIFactory.SetText(sessionLabel, "STT  讯飞   ·   已结束");
            SetStatus("会话已结束");
            hasActiveHook = false;
            SetHookActiveVisual(false);

            // 记录本次辅助表达分数（链下 + 链上）
            RecordSessionScore();

            // 会话结束后后端会关闭 relay（code=1006），当前 WSS 连接虽仍 Open 但后端
            // 会话状态已失效，导致下次 session_control start 不再回 session_started ack。
            // 延迟 0.6s（等待 mint_credential 发送完成）后强制重连，获得全新后端会话。
            StartCoroutine(ReconnectAfterSessionEnd(0.6f));
        }

        System.Collections.IEnumerator ReconnectAfterSessionEnd(float delay)
        {
            if (wsClient == null) yield break;
            yield return new WaitForSeconds(delay);
            Debug.Log("[Workspace] 会话结束，强制重连 WSS 以刷新后端 relay 状态");
            SetStatus("正在重连后端…");
            wsClient.Reconnect();
        }

        // ============================================================
        // 分数记录（链下 PlayerPrefs + Injective 链上铸证）
        // ============================================================

        void OnWalletStatus(WalletStatusMessage msg)
        {
            if (msg.connected && !string.IsNullOrEmpty(msg.address))
            {
                currentWalletAddress = msg.address;
                currentWalletType = msg.walletType ?? "keplr";
            }
            else
            {
                currentWalletAddress = "";
                currentWalletType = "";
            }
            if (growthOverlay != null && growthOverlay.activeSelf)
                RefreshGrowthPanel();
        }

        void OnCredentialMinted(CredentialMintedMessage msg)
        {
            SetStatus("已上链: " + (msg.chainTxHash ?? msg.milestone));
        }

        /// <summary>
        /// 会话结束时记录分数：
        /// 1. 链下：写入 PlayerPrefs（历次分数 JSON）
        /// 2. 链上：若已连接 Injective 钱包，调用 SendMintCredential 上链铸证
        /// </summary>
        void RecordSessionScore()
        {
            double overall = ComputeOverallScore();
            var entry = new SessionScoreEntry
            {
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                fluency = _lastFinalFluency,
                logic = _lastFinalLogic,
                pace = _lastFinalPace,
                reception = _lastFinalReception,
                fillers = _lastFinalFillers,
                overall = overall,
                onChain = !string.IsNullOrEmpty(currentWalletAddress),
                txHash = ""
            };

            AppendScoreHistory(entry);
            RefreshGrowthPanel();
            SetStatus($"本次辅助表达评分: {overall:0} / 100");

            // 若已连接 Injective 钱包，上链铸证成长凭证
            if (!string.IsNullOrEmpty(currentWalletAddress) && wsClient != null && wsClient.IsConnected)
            {
                var metrics = new Dictionary<string, double>
                {
                    { "overall", overall },
                    { "fluency", _lastFinalFluency },
                    { "logic", _lastFinalLogic },
                    { "pace", _lastFinalPace },
                    { "reception", _lastFinalReception },
                    { "fillers", _lastFinalFillers }
                };
                wsClient.SendMintCredential("session_score", metrics);
                SetStatus($"本次评分: {overall:0} / 100 · 已提交 Injective 铸证");
            }
        }

        double ComputeOverallScore()
        {
            // 综合分：流畅 30% + 逻辑 30% + 语速 20% + 观众接收度 20%
            double o = _lastFinalFluency * 0.3 + _lastFinalLogic * 0.3 + _lastFinalPace * 0.2 + _lastFinalReception * 0.2;
            return Mathf.Clamp((float)o, 0, 100);
        }

        const string ScoreHistoryKey = "Q.Growth.ScoreHistory.v1";

        [System.Serializable]
        class SessionScoreEntry
        {
            public long ts;
            public double fluency;
            public double logic;
            public double pace;
            public double reception;
            public int fillers;
            public double overall;
            public bool onChain;
            public string txHash;
        }

        [System.Serializable]
        class SessionScoreEntryList { public List<SessionScoreEntry> items = new List<SessionScoreEntry>(); }

        List<SessionScoreEntry> LoadScoreHistory()
        {
            try
            {
                var json = PlayerPrefs.GetString(ScoreHistoryKey, "");
                if (string.IsNullOrEmpty(json)) return new List<SessionScoreEntry>();
                var list = JsonUtility.FromJson<SessionScoreEntryList>(json);
                return list != null && list.items != null ? list.items : new List<SessionScoreEntry>();
            }
            catch { return new List<SessionScoreEntry>(); }
        }

        void AppendScoreHistory(SessionScoreEntry entry)
        {
            var list = LoadScoreHistory();
            list.Add(entry);
            // 仅保留最近 50 条
            if (list.Count > 50) list.RemoveRange(0, list.Count - 50);
            try
            {
                var wrapper = new SessionScoreEntryList { items = list };
                PlayerPrefs.SetString(ScoreHistoryKey, JsonUtility.ToJson(wrapper));
                PlayerPrefs.Save();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Growth] 保存分数历史失败: " + e.Message);
            }
        }

        void OnTranscript(AsrTranscriptMessage msg)
        {
            if (string.IsNullOrEmpty(msg.text)) return;
            int sp = msg.speaker ?? 0;
            string time = sessionActive
                ? FormatTime(Time.time - sessionStartTime)
                : "--:--";
            if (!msg.isFinal && lines.Count > 0 && !lines[0].isFinal && lines[0].speaker == sp)
            {
                lines[0].text = msg.text;
                lines[0].time = time;
            }
            else
            {
                lines.Insert(0, new TranscriptLine { speaker = sp, text = msg.text, isFinal = msg.isFinal, time = time });
            }
            RefreshTranscript();
        }

        void OnHook(HookMessage msg)
        {
            hasActiveHook = true;
            SetHookActiveVisual(true);
            string ht = string.IsNullOrEmpty(msg.hookType) ? "开口钩" : msg.hookType + (msg.hookType.EndsWith("钩") ? "" : "钩");
            QUIFactory.SetText(hookFreshType, ht);
            QUIFactory.SetText(hookFreshTime, "刚刚 · 卡壳");
            QUIFactory.SetText(hookFreshText, string.IsNullOrEmpty(msg.hookText) ? "接着说" : msg.hookText);
            hookTotal = (float)(msg.countdown > 0 ? msg.countdown : 8);
            hookRemaining = hookTotal;
            hookCounting = true;
            QUIFactory.SetText(hooksMeta, $"EMA {hookTotal:0.0}s");

            // shift previous into history slots
            string t2 = QUIFactory.GetText(hookFreshType);
            // keep secondary static sample until next
            SetStatus("递钩: " + ht);
        }

        void OnRecovery(RecoveryMessage msg)
        {
            hasActiveHook = false;
            hookCounting = false;
            SetHookActiveVisual(false);
            SetStatus(msg.recovered ? $"已恢复 ({msg.responseTimeMs}ms)" : "未恢复");
        }

        /// <summary>
        /// 后端 relay 关闭（code=1005）且 sessionActive 时，视为隐式会话结束。
        /// 解决后端返回 error "会话结束处理失败" 后不发 session_ended ack 的问题。
        /// </summary>
        void OnRelayStatus(RelayStatusMessage msg)
        {
            if (msg.status == "closed" && sessionActive)
            {
                Debug.Log($"[Workspace] relay 已关闭 (msg={msg.message})，强制结束会话");
                ForceSessionEnd();
            }
        }

        /// <summary>
        /// 强制本地结束会话（无需后端 ack）：重置 UI，
        /// 记录分数，停止录音（由 QAudioCapture 的 OnSessionEndAck 不再触发，故此处显式断开）。
        /// </summary>
        void ForceSessionEnd()
        {
            if (!sessionActive) return;
            endRequestTime = -1f;
            sessionActive = false;
            hasActiveHook = false;
            hookCounting = false;
            SetHookActiveVisual(false);
            startBtn?.SetText("开始辅助表达");
            StylePrimaryButton(startBtn);
            QUIFactory.SetText(livePillLeft, "STANDBY");
            QUIFactory.SetText(livePillRight, "STANDBY");
            QUIFactory.SetText(sessionLabel, "STT  讯飞   ·   已结束");
            SetStatus("会话已结束（后端异常）");
            RecordSessionScore();
            // 强制重连以刷新后端状态
            StartCoroutine(ReconnectAfterSessionEnd(0.3f));
        }

        void OnScore(ScoreMessage msg)
        {
            // design 用 0-100；后端可能是 0-10
            _fluency = NormalizeScore(msg.fluency);
            _logic = NormalizeScore(msg.logic);
            _pace = NormalizeScore(msg.pace);
            _reception = NormalizeScore(msg.reception ?? 0);
            _fillers = msg.fillers;
            // 追踪本轮最终评分快照（每次收到 score 都更新，结束时取最近值）
            _lastFinalFluency = _fluency;
            _lastFinalLogic = _logic;
            _lastFinalPace = _pace;
            _lastFinalReception = _reception;
            _lastFinalFillers = _fillers;
            if (!string.IsNullOrEmpty(msg.text))
                lines.Add(new TranscriptLine { speaker = 0, text = msg.text, isFinal = true, time = FormatTime(sessionActive ? Time.time - sessionStartTime : 0) });
            RefreshScores();
            RefreshTranscript();
        }

        void OnScoreUpdate(ScoreUpdateMessage msg)
        {
            _logic = NormalizeScore(msg.logic);
            _lastFinalLogic = _logic;
            RefreshScores();
        }

        void OnPace(PaceUpdateMessage msg)
        {
            _pace = NormalizeScore(msg.paceScore);
            _lastFinalPace = _pace;
            _paceNote = $"{msg.charsPerSec:0} 字/秒";
            RefreshScores();
        }

        void OnSegmentEnd(SegmentEndMessage msg)
        {
            // 段结束汇总：更新填充词计数和段时长，若有评分则同步刷新
            if (msg.summary.fillers > 0) _fillers = msg.summary.fillers;
            _lastFinalFillers = _fillers;
            if (msg.summary.avgFluency > 0) _fluency = NormalizeScore(msg.summary.avgFluency);
            if (msg.summary.avgLogic > 0) _logic = NormalizeScore(msg.summary.avgLogic);
            _lastFinalFluency = _fluency;
            _lastFinalLogic = _logic;
            RefreshScores();

            // 在转写顶部插入段摘要行
            if (msg.summary.duration > 0)
            {
                string note = $"[段结束 - {msg.summary.duration:0.0}s - 填充词 {msg.summary.fillers} - 停顿 {msg.summary.pauses}]";
                lines.Insert(0, new TranscriptLine { speaker = -1, text = note, isFinal = true,
                    time = FormatTime(sessionActive ? Time.time - sessionStartTime : 0) });
                RefreshTranscript();
            }
        }

        static double NormalizeScore(double v)
        {
            if (v <= 0) return 0;
            if (v <= 10) return v * 10.0; // 0-10 → 0-100
            return v;
        }

        void RefreshScores()
        {
            SetMetric(fluencyValue, fluencyBar, fluencyNote, _fluency, 100,
                $"▲ 语速 {_paceNote}");
            // filler: value is per-min count, bar max 12
            double fillerBarV = Mathf.Clamp((float)_fillers, 0, 12);
            QUIFactory.SetText(fillerValue, $"{_fillers}");
            SetBar(fillerBar, fillerBarV / 12.0);
            QUIFactory.SetText(fillerNote, _fillers > 6 ? "已超频" : "正常");
            QUIFactory.SetColor(fillerValue, _fillers > 6 ? QDesign.Amber : QDesign.Txt);

            SetMetric(structureValue, structureBar, structureNote, _logic, 100,
                "论点推进 · 结构清晰度");
        }

        void SetMetric(Component value, Component bar, Component note, double v, double max, string noteText)
        {
            QUIFactory.SetText(value, v <= 0 ? "—" : v.ToString("0"));
            SetBar(bar, max > 0 ? v / max : 0);
            QUIFactory.SetText(note, noteText);
        }

        void SetBar(Component bar, double pct)
        {
            var img = bar as Image;
            if (img == null) return;
            float p = Mathf.Clamp01((float)pct);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(p, 1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void RefreshTranscript()
        {
            if (!transcriptOpen) return;
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                string t = line.time ?? "--:--";
                string who = line.speaker == 0 ? "" : line.speaker < 0 ? "" : $"[说话人{line.speaker}] ";
                // 简单填充词高亮：用 ASCII 括号标记（避免 TMP SDF 缺字显示白块）
                string text = line.text
                    .Replace("呃", "[呃]")
                    .Replace("那个", "[那个]")
                    .Replace("就是", "[就是]");
                sb.Append(t).Append("  ").Append(who).Append(text);
                if (!line.isFinal) sb.Append("...");
                sb.Append('\n');
            }
            string s = sb.Length == 0 ? "等待演讲开始..." : sb.ToString();
            // 移除旧的字符限制，交给 ScrollRect 处理溢出
            QUIFactory.SetText(transcriptView, s);

            // 自动滚到顶部（最新内容在最上面）
            if (transcriptScrollRect != null && transcriptContentRt != null)
            {
                Canvas.ForceUpdateCanvases();
                transcriptScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        void SetHookActiveVisual(bool active)
        {
            if (hookFreshCard != null)
            {
                var img = hookFreshCard.GetComponent<Image>();
                if (img != null)
                    img.color = active ? HexA("#3AE3D2", 0.12f) : QDesign.CardBg;
            }
            if (confirmBtn != null) confirmBtn.SetInteractable(active);
            if (dismissBtn != null) dismissBtn.SetInteractable(active);
            if (!active)
            {
                QUIFactory.SetText(hookFreshText, "等待卡壳递钩…");
                QUIFactory.SetText(hookFreshCountdown, "");
                QUIFactory.SetText(hookFreshTime, "—");
            }
        }

        void SetStatus(string s)
        {
            QUIFactory.SetText(statusFooter, s);
        }

        static string FormatTime(float sec)
        {
            if (sec < 0) sec = 0;
            int m = Mathf.FloorToInt(sec / 60f);
            int s = Mathf.FloorToInt(sec % 60f);
            return $"{m:00}:{s:00}";
        }

        // ============================================================
        // Actions
        // ============================================================

        public void OnToggleSession()
        {
            Debug.Log($"[Workspace] OnToggleSession sessionActive={sessionActive} wsConnected={(wsClient != null ? wsClient.IsConnected : false)}");
            if (wsClient == null) wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient == null) { SetStatus("无 WebSocket"); return; }
            // 如果正在等待结束 ack，忽略重复点击
            if (endRequestTime >= 0f) { SetStatus("正在等待结束确认…"); return; }
            if (!sessionActive)
            {
                if (!wsClient.IsConnected) { SetStatus("尚未连接后端"); return; }
                endRequestTime = -1f;
                wsClient.SendSessionStart("pico-user", "PICO 用户");
                SetStatus("正在开始辅助表达…");
            }
            else
            {
                endRequestTime = Time.time;
                wsClient.SendSessionEnd();
                SetStatus("正在结束辅助表达…");
            }
        }

        public void OnToggleTranscript()
        {
            transcriptOpen = !transcriptOpen;
            if (accTranscriptBody != null) accTranscriptBody.SetActive(transcriptOpen);
            foldBtn?.SetText(transcriptOpen ? "▾" : "▸");

            // 折叠时压缩 body 占位，让下方设置手风琴上移（简易：改 body 高度）
            var br = accTranscriptBody != null ? accTranscriptBody.GetComponent<RectTransform>() : null;
            if (br != null)
            {
                if (transcriptOpen)
                {
                    br.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 260f);
                    RefreshTranscript();
                }
                else
                {
                    br.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
                }
            }
            // 重新排版左侧下方块：用 settings head 的 anchored 位置近似上移
            RelayoutLeftAccordions();
        }

        public void OnToggleSettings()
        {
            settingsOpen = !settingsOpen;
            if (accSettingsBody != null) accSettingsBody.SetActive(settingsOpen);
            settingsFoldBtn?.SetText(settingsOpen ? "▾" : "▸");
            RelayoutLeftAccordions();
        }

        void RelayoutLeftAccordions()
        {
            // 简易重排：transcript head 固定，body 高度随 open，settings 紧跟其后
            if (leftCanvas == null) return;
            var panel = leftCanvas.transform;
            float pad = QDesign.SafePad;
            // mic stage 约到 -24-44-34-28-168 = 估算
            float y = -pad - 44 - 34 - 28 - 168 - 14;

            var tHead = panel.Find("AccTranscriptHead") as RectTransform;
            var tBody = panel.Find("AccTranscriptBody") as RectTransform;
            var sHead = panel.Find("AccSettingsHead") as RectTransform;
            var sBody = panel.Find("AccSettingsBody") as RectTransform;

            if (tHead != null) SetTop(tHead, pad, y, -pad, 64);
            y -= 70;
            float tBodyH = transcriptOpen ? 260f : 0f;
            if (tBody != null)
            {
                SetTop(tBody, pad, y, -pad, Mathf.Max(tBodyH, 1f));
                tBody.gameObject.SetActive(transcriptOpen);
            }
            if (transcriptOpen) y -= tBodyH + 8;

            if (sHead != null) SetTop(sHead, pad, y, -pad, 64);
            y -= 70;
            float sBodyH = settingsOpen ? 240f : 0f;
            if (sBody != null)
            {
                SetTop(sBody, pad, y, -pad, Mathf.Max(sBodyH, 1f));
                sBody.gameObject.SetActive(settingsOpen);
            }
        }

        public void OnToggleMuteAudience()
        {
            ApplyMuteAudience(!muteAudience, silent: false);
        }

        void ApplyMuteAudience(bool on, bool silent)
        {
            muteAudience = on;
            StyleSwitchButton(muteBtn, on);
            var face = FindObjectOfType<FaceOcclusionManager>();
            if (face != null) face.SetAudienceOcclusion(on);
            else
            {
                var donggua = FindObjectOfType<DongguaOcclusionController>();
                if (donggua == null)
                {
                    var go = new GameObject("DongguaOcclusion");
                    donggua = go.AddComponent<DongguaOcclusionController>();
                }
                donggua.SetOcclusionEnabled(on);
            }
            if (!silent)
                SetStatus(on ? "听众遮脸减压：开（冬瓜）" : "听众遮脸减压：关");
        }

        public void OnConfirmHook()
        {
            if (!hasActiveHook) { SetStatus("当前无激活钩子"); return; }
            hasActiveHook = false;
            hookCounting = false;
            SetHookActiveVisual(false);
            var bridge = FindObjectOfType<RingInputBridge>();
            if (bridge != null) bridge.SimulateCommand(RingCommand.double_click);
            SetStatus("已确认恢复");
        }

        public void OnDismissHook()
        {
            if (!hasActiveHook) { SetStatus("当前无激活钩子"); return; }
            hasActiveHook = false;
            hookCounting = false;
            SetHookActiveVisual(false);
            var bridge = FindObjectOfType<RingInputBridge>();
            if (bridge != null) bridge.SimulateCommand(RingCommand.single_click);
            SetStatus("已忽略钩子");
        }

        // ============================================================
        // 成长属性面板（覆盖层）
        // ============================================================

        public void OnOpenGrowthPanel()
        {
            if (growthOverlay == null) BuildGrowthOverlay();
            if (growthOverlay == null) return;
            growthOverlay.SetActive(true);
            RefreshGrowthPanel();
            SetStatus("成长属性 · 历次评分");
        }

        public void OnCloseGrowthPanel()
        {
            if (growthOverlay != null) growthOverlay.SetActive(false);
        }

        void BuildGrowthOverlay()
        {
            if (leftCanvas == null) return;
            // 全屏覆盖在左面板上
            growthOverlay = new GameObject("GrowthOverlay", typeof(RectTransform));
            growthOverlay.transform.SetParent(leftCanvas.transform, false);
            var ort = growthOverlay.GetComponent<RectTransform>();
            Stretch(ort);

            // 遮罩背景
            var mask = MakeImage(growthOverlay.transform, "Mask", HexA("#000000", 0.55f), 0);
            Stretch(mask.rectTransform);

            // 卡片容器
            var card = MakeImage(growthOverlay.transform, "Card", QDesign.PanelSolid, QDesign.RadiusPanel);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(QDesign.DesignW - 24f, QDesign.DesignH - 120f);

            float pad = QDesign.SafePad;
            float y = -pad;

            // header
            var title = QUIFactory.CreateText(card.transform, "Title", "成长属性",
                PicoTypeRole.Headline, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt);
            QUIFactory.SetFontStyleBold(title);
            SetTop(title.transform as RectTransform, pad, y, -pad, 32);
            y -= 34;

            var sub = QUIFactory.CreateText(card.transform, "Sub", "历次辅助表达评分 · Injective 链上成长凭证",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Txt2);
            SetTop(sub.transform as RectTransform, pad, y, -pad, 20);
            y -= 26;

            // 钱包状态
            growthWalletLabel = QUIFactory.CreateText(card.transform, "Wallet", "钱包: 未连接",
                PicoTypeRole.Caption, TextAnchor.MiddleLeft, TMPro.TextAlignmentOptions.Left, QDesign.Amber);
            SetTop((growthWalletLabel.transform as RectTransform), pad, y, -pad, 20);
            y -= 28;

            // 列表容器（可滚动）
            var listGo = new GameObject("ScoreList", typeof(RectTransform));
            listGo.transform.SetParent(card.transform, false);
            var lrt = listGo.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(1, 1);
            lrt.pivot = new Vector2(0.5f, 1);
            lrt.offsetMin = new Vector2(pad, 64);
            lrt.offsetMax = new Vector2(-pad, y);

            growthListContent = QUIFactory.CreateText(lrt, "Items", "",
                PicoTypeRole.Caption, TextAnchor.UpperLeft, TMPro.TextAlignmentOptions.TopLeft,
                QDesign.Txt, wrap: true);
            var irt = growthListContent.transform as RectTransform;
            Stretch(irt);

            growthEmptyHint = QUIFactory.CreateText(card.transform, "Empty", "尚无评分记录\n开始一次辅助表达后会自动记录",
                PicoTypeRole.Caption, TextAnchor.MiddleCenter, TMPro.TextAlignmentOptions.Center, QDesign.Txt3, wrap: true);
            Stretch((growthEmptyHint.transform as RectTransform));

            // 底部按钮：关闭 + 手动铸证
            growthMintBtn = QPicoButton.Create(card.transform, "Mint", "上链铸证",
                PicoButtonRole.Passable, PicoButtonSize.Min, OnManualMint);
            Place(growthMintBtn.transform as RectTransform, 0f, 0f, pad, 16, 120, 36);
            growthMintBtn.background.color = QDesign.Violet;
            QUIFactory.SetColor(growthMintBtn.label, Color.white);

            growthCloseBtn = QPicoButton.Create(card.transform, "Close", "关闭",
                PicoButtonRole.Secondary, PicoButtonSize.Min, OnCloseGrowthPanel);
            Place(growthCloseBtn.transform as RectTransform, 1f, 0f, -pad, 16, 80, 36);

            growthOverlay.SetActive(false);
        }

        void RefreshGrowthPanel()
        {
            if (growthOverlay == null || !growthOverlay.activeSelf) return;
            var list = LoadScoreHistory();

            // 钱包状态
            if (!string.IsNullOrEmpty(currentWalletAddress))
                QUIFactory.SetText(growthWalletLabel, $"钱包: {ShortenAddr(currentWalletAddress)} ({currentWalletType}) · 已连接");
            else
                QUIFactory.SetText(growthWalletLabel, "钱包: 未连接（评分仅本地保存）");

            if (list.Count == 0)
            {
                QUIFactory.SetText(growthListContent, "");
                if (growthEmptyHint != null) (growthEmptyHint.transform as RectTransform)?.gameObject.SetActive(true);
                return;
            }
            if (growthEmptyHint != null) (growthEmptyHint.transform as RectTransform)?.gameObject.SetActive(false);

            var sb = new StringBuilder();
            // 倒序：最近在上
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                var dt = DateTimeOffset.FromUnixTimeSeconds(e.ts).LocalDateTime;
                string chainTag = e.onChain ? "  ⛓ 链上" : "  · 本地";
                sb.Append($"<b>{e.overall:0}</b> / 100{chainTag}\n");
                sb.Append($"  {dt:MM-dd HH:mm}  流畅 {e.fluency:0}  逻辑 {e.logic:0}  语速 {e.pace:0}\n");
                if (e.reception > 0)
                    sb.Append($"  接收度 {e.reception:0}  口头禅 {e.fillers}\n");
                sb.Append("\n");
            }
            QUIFactory.SetText(growthListContent, sb.ToString().TrimEnd());
        }

        string ShortenAddr(string a)
        {
            if (string.IsNullOrEmpty(a)) return "无";
            return a.Length <= 12 ? a : a.Substring(0, 6) + "…" + a.Substring(a.Length - 4);
        }

        void OnManualMint()
        {
            var list = LoadScoreHistory();
            if (list.Count == 0) { SetStatus("暂无评分可铸证"); return; }
            if (string.IsNullOrEmpty(currentWalletAddress)) { SetStatus("请先连接 Injective 钱包"); return; }
            if (wsClient == null || !wsClient.IsConnected) { SetStatus("未连接后端，无法上链"); return; }
            var last = list[list.Count - 1];
            var metrics = new Dictionary<string, double>
            {
                { "overall", last.overall },
                { "fluency", last.fluency },
                { "logic", last.logic },
                { "pace", last.pace },
                { "reception", last.reception },
                { "fillers", last.fillers }
            };
            wsClient.SendMintCredential("session_score", metrics);
            SetStatus("已提交 Injective 铸证请求…");
        }
    }
}
