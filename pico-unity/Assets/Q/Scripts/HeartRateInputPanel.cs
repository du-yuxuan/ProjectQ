// ============================================================
// HeartRateInputPanel.cs — 运行时数字键盘（uGUI 回退）
// ============================================================

using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Q.Pico
{
    public class HeartRateInputPanel : MonoBehaviour
    {
        public Component inputDisplayText;
        public Component tensionLabelText;
        public Image tensionIndicator;
        public Button[] numberButtons = new Button[10];
        public Button submitButton;
        public Button clearButton;
        public Button backspaceButton;
        public Button preset70Button;
        public Button preset120Button;
        public Button preset160Button;
        public GameObject panelContainer;

        public int maxInputDigits = 3;
        public float autoHideDelay = 2f;
        public bool autoCreateUI = true;
        public bool startHidden = true;
        public KeyCode toggleKey = KeyCode.None;

        public Color calmColor = new Color(0.2f, 0.8f, 0.4f);
        public Color normalColor = new Color(0.4f, 0.6f, 1f);
        public Color tenseColor = new Color(1f, 0.7f, 0.2f);
        public Color panicColor = new Color(1f, 0.2f, 0.2f);

        QWebSocketClient wsClient;
        string inputBuffer = "";
        float hideTimer = -1f;
        bool uiBuilt;

        void Awake()
        {
            if (autoCreateUI && panelContainer == null)
                BuildRuntimeUI();
        }

        void Start()
        {
            wsClient = FindObjectOfType<QWebSocketClient>();
            BindButtons();
            ClearInput();
            UpdateTensionDisplay(0);
            if (startHidden) SetPanelVisible(false);
        }

        void Update()
        {
            if (hideTimer > 0f)
            {
                hideTimer -= Time.deltaTime;
                if (hideTimer <= 0f) { SetPanelVisible(false); hideTimer = -1f; }
            }
        }

        void BindButtons()
        {
            for (int i = 0; i < numberButtons.Length; i++)
            {
                int digit = i;
                if (numberButtons[i] != null)
                {
                    numberButtons[i].onClick.RemoveAllListeners();
                    numberButtons[i].onClick.AddListener(() => OnDigitPressed(digit));
                }
            }
            if (submitButton != null) { submitButton.onClick.RemoveAllListeners(); submitButton.onClick.AddListener(OnSubmit); }
            if (clearButton != null) { clearButton.onClick.RemoveAllListeners(); clearButton.onClick.AddListener(OnClear); }
            if (backspaceButton != null) { backspaceButton.onClick.RemoveAllListeners(); backspaceButton.onClick.AddListener(OnBackspace); }
            if (preset70Button != null) { preset70Button.onClick.RemoveAllListeners(); preset70Button.onClick.AddListener(() => OnPresetPressed(70)); }
            if (preset120Button != null) { preset120Button.onClick.RemoveAllListeners(); preset120Button.onClick.AddListener(() => OnPresetPressed(120)); }
            if (preset160Button != null) { preset160Button.onClick.RemoveAllListeners(); preset160Button.onClick.AddListener(() => OnPresetPressed(160)); }
        }

        void BuildRuntimeUI()
        {
            if (uiBuilt) return;
            uiBuilt = true;

            // 直接 World Space Canvas
            var canvas = QXRUIBootstrap.CreateWorldSpaceCanvas(
                transform, "Q_HeartRatePanel", distance: 1.35f, scale: 0.0011f, sortingOrder: 200);
            var root = canvas.gameObject;

            panelContainer = new GameObject("PanelContainer", typeof(RectTransform));
            panelContainer.transform.SetParent(root.transform, false);
            QUIFactory.StretchFull(panelContainer.GetComponent<RectTransform>());

            // 半透明遮罩（不铺满射线阻挡过大时可关掉）
            var dim = QUIFactory.CreateImage(panelContainer.transform, "Dim", new Color(0, 0, 0, 0.35f));
            QUIFactory.StretchFull(dim.rectTransform);
            dim.raycastTarget = true;
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(HidePanel);

            var card = QUIFactory.CreateImage(panelContainer.transform, "Card", new Color(0.1f, 0.12f, 0.18f, 0.97f));
            QUIFactory.SetRect(card.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, -210, -290, 210, 290);

            var title = QUIFactory.CreateText(card.transform, "Title", "心率输入", 26, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(title.transform as RectTransform, 0, 1, 1, 1, 12, -44, -12, -10);
            QUIFactory.SetFontStyleBold(title);

            var displayBg = QUIFactory.CreateImage(card.transform, "DisplayBg", new Color(0.06f, 0.07f, 0.1f, 1f));
            QUIFactory.SetRect(displayBg.rectTransform, 0, 1, 1, 1, 20, -120, -20, -56);
            inputDisplayText = QUIFactory.CreateText(displayBg.transform, "Display", "---", 48, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.StretchFull(inputDisplayText.transform as RectTransform, 8, 8, 8, 8);
            QUIFactory.SetFontStyleBold(inputDisplayText);

            tensionIndicator = QUIFactory.CreateImage(card.transform, "TensionDot", Color.gray);
            QUIFactory.SetRect(tensionIndicator.rectTransform, 0, 0, 1, 1, 28, -150, 52, -126);
            tensionLabelText = QUIFactory.CreateText(card.transform, "Tension", "", 20, TextAnchor.MiddleLeft, TextAlignmentOptions.Left);
            QUIFactory.SetRect(tensionLabelText.transform as RectTransform, 0, 1, 1, 1, 60, -154, -20, -122);

            preset70Button = QUIFactory.CreateButton(card.transform, "P70", "70 平静", calmColor);
            QUIFactory.SetRect(preset70Button.GetComponent<RectTransform>(), 0, 0.33f, 1, 1, 16, -200, -4, -164);
            preset120Button = QUIFactory.CreateButton(card.transform, "P120", "120 紧张", tenseColor);
            QUIFactory.SetRect(preset120Button.GetComponent<RectTransform>(), 0.33f, 0.66f, 1, 1, 4, -200, -4, -164);
            preset160Button = QUIFactory.CreateButton(card.transform, "P160", "160 恐慌", panicColor);
            QUIFactory.SetRect(preset160Button.GetComponent<RectTransform>(), 0.66f, 1f, 1, 1, 4, -200, -16, -164);

            numberButtons = new Button[10];
            float padTop = 220f, btnH = 56f, gap = 8f;
            for (int n = 1; n <= 9; n++)
            {
                int row = (n - 1) / 3, col = (n - 1) % 3;
                numberButtons[n] = QUIFactory.CreateButton(card.transform, $"N{n}", n.ToString(), new Color(0.18f, 0.2f, 0.28f));
                var rt = numberButtons[n].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(16 + col * (128 + gap), -(padTop + row * (btnH + gap)));
                rt.sizeDelta = new Vector2(128, btnH);
            }
            numberButtons[0] = QUIFactory.CreateButton(card.transform, "N0", "0", new Color(0.18f, 0.2f, 0.28f));
            var zrt = numberButtons[0].GetComponent<RectTransform>();
            zrt.anchorMin = zrt.anchorMax = zrt.pivot = new Vector2(0, 1);
            zrt.anchoredPosition = new Vector2(16 + 128 + gap, -(padTop + 3 * (btnH + gap)));
            zrt.sizeDelta = new Vector2(128, btnH);

            backspaceButton = QUIFactory.CreateButton(card.transform, "Back", "←", new Color(0.35f, 0.25f, 0.2f));
            var brt = backspaceButton.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 1);
            brt.anchoredPosition = new Vector2(16, -(padTop + 3 * (btnH + gap)));
            brt.sizeDelta = new Vector2(128, btnH);

            clearButton = QUIFactory.CreateButton(card.transform, "Clear", "清除", new Color(0.3f, 0.22f, 0.22f));
            var crt = clearButton.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0, 1);
            crt.anchoredPosition = new Vector2(16 + 2 * (128 + gap), -(padTop + 3 * (btnH + gap)));
            crt.sizeDelta = new Vector2(128, btnH);

            submitButton = QUIFactory.CreateButton(card.transform, "Submit", "提交心率", new Color(0.15f, 0.45f, 0.3f));
            QUIFactory.SetRect(submitButton.GetComponent<RectTransform>(), 0, 1, 0, 0, 20, 18, -20, 70);

            Debug.Log("[HRPanel] 运行时 UI 已创建");
        }

        void OnDigitPressed(int digit)
        {
            if (inputBuffer.Length >= maxInputDigits) return;
            inputBuffer += digit.ToString();
            UpdateDisplay();
            if (int.TryParse(inputBuffer, out int bpm)) UpdateTensionDisplay(bpm);
        }

        void OnSubmit()
        {
            if (string.IsNullOrEmpty(inputBuffer)) return;
            if (!int.TryParse(inputBuffer, out int bpm) || bpm <= 0 || bpm > 300)
            {
                QUIFactory.SetText(inputDisplayText, "无效!");
                return;
            }
            if (wsClient == null) wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient != null && wsClient.IsConnected)
            {
                wsClient.SendHeartRate(QWebSocketClient.GetTimestamp(), bpm, source: "manual_panel");
                Debug.Log($"[HRPanel] 心率已提交: {bpm} BPM");
            }
            QUIFactory.SetText(inputDisplayText, $"✓ {bpm}");
            hideTimer = autoHideDelay;
        }

        void OnClear() => ClearInput();

        void OnBackspace()
        {
            if (inputBuffer.Length == 0) return;
            inputBuffer = inputBuffer.Substring(0, inputBuffer.Length - 1);
            UpdateDisplay();
            if (int.TryParse(inputBuffer, out int bpm)) UpdateTensionDisplay(bpm);
            else UpdateTensionDisplay(0);
        }

        void OnPresetPressed(int bpm)
        {
            inputBuffer = bpm.ToString();
            UpdateDisplay();
            UpdateTensionDisplay(bpm);
        }

        void ClearInput()
        {
            inputBuffer = "";
            UpdateDisplay();
            UpdateTensionDisplay(0);
        }

        void UpdateDisplay() => QUIFactory.SetText(inputDisplayText, string.IsNullOrEmpty(inputBuffer) ? "---" : inputBuffer);

        void UpdateTensionDisplay(int bpm)
        {
            TensionLevel tension = ClassifyTension(bpm);
            string label = EnumConverter.ToTensionLabel(tension);
            Color color = GetTensionColor(tension);
            QUIFactory.SetText(tensionLabelText, bpm > 0 ? label : "");
            QUIFactory.SetColor(tensionLabelText, color);
            if (tensionIndicator != null) tensionIndicator.color = bpm > 0 ? color : Color.gray;
        }

        public static TensionLevel ClassifyTension(int bpm)
        {
            if (bpm <= 0) return TensionLevel.normal;
            if (bpm < 90) return TensionLevel.calm;
            if (bpm < 120) return TensionLevel.normal;
            if (bpm < 160) return TensionLevel.tense;
            return TensionLevel.panic;
        }

        Color GetTensionColor(TensionLevel tension)
        {
            switch (tension)
            {
                case TensionLevel.calm: return calmColor;
                case TensionLevel.normal: return normalColor;
                case TensionLevel.tense: return tenseColor;
                case TensionLevel.panic: return panicColor;
                default: return Color.gray;
            }
        }

        public void ShowPanel()
        {
            if (panelContainer == null && autoCreateUI) BuildRuntimeUI();
            EnsureWorldSpaceCanvas();
            SetPanelVisible(true);
            ClearInput();
            hideTimer = -1f;
        }

        public void HidePanel() => SetPanelVisible(false);

        public void TogglePanel()
        {
            if (panelContainer == null && autoCreateUI) BuildRuntimeUI();
            bool show = panelContainer == null || !panelContainer.activeSelf;
            if (show) ShowPanel(); else HidePanel();
        }

        public bool IsVisible => panelContainer != null && panelContainer.activeSelf;

        void SetPanelVisible(bool visible)
        {
            if (panelContainer != null) panelContainer.SetActive(visible);
        }

        public void EnsureWorldSpaceCanvas()
        {
            Canvas canvas = panelContainer != null
                ? panelContainer.GetComponentInParent<Canvas>()
                : GetComponentInChildren<Canvas>();
            if (canvas == null) return;
            // 强制 World Space
            canvas.renderMode = RenderMode.WorldSpace;
            QXRUIBootstrap.MakeWorldSpaceHUD(canvas.gameObject, distance: 1.35f, scale: 0.0011f);
        }
    }
}
