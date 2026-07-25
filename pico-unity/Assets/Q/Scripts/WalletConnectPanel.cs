// ============================================================
// WalletConnectPanel.cs — QR 面板（uGUI 回退）
// ============================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Q.Pico
{
    public class WalletConnectPanel : MonoBehaviour
    {
        public Image qrCodeImage;
        public Component addressText;
        public Component statusText;
        public Component uriHintText;
        public GameObject panelContainer;
        public Button connectDemoButton;
        public Button closeButton;
        public Button requestButton;

        public int qrPixelSize = 8;
        public string defaultWalletType = "keplr";
        public float autoHideDelay = 3f;
        public bool autoCreateUI = true;
        public bool startHidden = true;
        public KeyCode toggleKey = KeyCode.None;
        public string demoAddressPrefix = "inj1qdemo";

        public Color connectedColor = new Color(0.2f, 0.8f, 0.2f);
        public Color disconnectedColor = new Color(0.8f, 0.2f, 0.2f);
        public Color connectingColor = new Color(0.8f, 0.8f, 0.2f);

        QWebSocketClient wsClient;
        string currentUri = "";
        float hideTimer = -1f;
        bool uiBuilt;
        Texture2D qrTexture;

        void Awake()
        {
            if (autoCreateUI && panelContainer == null)
                BuildRuntimeUI();
        }

        void Start()
        {
            wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient != null)
            {
                wsClient.OnWalletConnectUri.AddListener(OnWalletConnectUri);
                wsClient.OnWalletStatus.AddListener(OnWalletStatus);
            }
            if (connectDemoButton != null) { connectDemoButton.onClick.RemoveAllListeners(); connectDemoButton.onClick.AddListener(OnDemoConnectClicked); }
            if (closeButton != null) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(HidePanel); }
            if (requestButton != null) { requestButton.onClick.RemoveAllListeners(); requestButton.onClick.AddListener(RequestConnect); }
            if (startHidden) SetPanelVisible(false);
            UpdateStatusText("等待钱包连接…", connectingColor);
        }

        void Update()
        {
            if (hideTimer > 0f)
            {
                hideTimer -= Time.deltaTime;
                if (hideTimer <= 0f) { SetPanelVisible(false); hideTimer = -1f; }
            }
        }

        void OnDestroy()
        {
            if (wsClient != null)
            {
                wsClient.OnWalletConnectUri.RemoveListener(OnWalletConnectUri);
                wsClient.OnWalletStatus.RemoveListener(OnWalletStatus);
            }
            if (qrTexture != null) Destroy(qrTexture);
        }

        void BuildRuntimeUI()
        {
            if (uiBuilt) return;
            uiBuilt = true;

            // 直接 World Space Canvas
            var canvas = QXRUIBootstrap.CreateWorldSpaceCanvas(
                transform, "Q_WalletPanel", distance: 1.4f, scale: 0.0011f, sortingOrder: 210);
            var root = canvas.gameObject;

            panelContainer = new GameObject("PanelContainer", typeof(RectTransform));
            panelContainer.transform.SetParent(root.transform, false);
            QUIFactory.StretchFull(panelContainer.GetComponent<RectTransform>());

            var dim = QUIFactory.CreateImage(panelContainer.transform, "Dim", new Color(0, 0, 0, 0.4f));
            QUIFactory.StretchFull(dim.rectTransform);
            dim.raycastTarget = true;
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(HidePanel);

            var card = QUIFactory.CreateImage(panelContainer.transform, "Card", new Color(0.1f, 0.12f, 0.18f, 0.98f));
            QUIFactory.SetRect(card.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, -230, -310, 230, 310);

            var title = QUIFactory.CreateText(card.transform, "Title", "WalletConnect", 26, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(title.transform as RectTransform, 0, 1, 1, 1, 12, -44, -12, -10);
            QUIFactory.SetFontStyleBold(title);

            var qrBg = QUIFactory.CreateImage(card.transform, "QRBg", Color.white);
            QUIFactory.SetRect(qrBg.rectTransform, 0.5f, 0.5f, 1, 1, -110, -280, 110, -60);
            qrCodeImage = qrBg;

            statusText = QUIFactory.CreateText(card.transform, "Status", "等待…", 18, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(statusText.transform as RectTransform, 0, 1, 1, 1, 16, -320, -16, -290);

            addressText = QUIFactory.CreateText(card.transform, "Address", "地址: —", 16, TextAnchor.MiddleCenter, TextAlignmentOptions.Center);
            QUIFactory.SetRect(addressText.transform as RectTransform, 0, 1, 1, 1, 16, -350, -16, -322);
            QUIFactory.SetColor(addressText, new Color(0.75f, 0.8f, 0.9f));

            uriHintText = QUIFactory.CreateText(card.transform, "UriHint", "", 12, TextAnchor.MiddleCenter, TextAlignmentOptions.Center, wrap: true);
            QUIFactory.SetRect(uriHintText.transform as RectTransform, 0, 1, 1, 1, 16, -390, -16, -354);
            QUIFactory.SetColor(uriHintText, new Color(0.5f, 0.55f, 0.6f));

            requestButton = QUIFactory.CreateButton(card.transform, "Request", "请求二维码", new Color(0.2f, 0.35f, 0.55f));
            QUIFactory.SetRect(requestButton.GetComponent<RectTransform>(), 0, 0.5f, 0, 0, 16, 70, -6, 118);
            connectDemoButton = QUIFactory.CreateButton(card.transform, "Demo", "演示连接", new Color(0.15f, 0.45f, 0.3f));
            QUIFactory.SetRect(connectDemoButton.GetComponent<RectTransform>(), 0.5f, 1, 0, 0, 6, 70, -16, 118);
            closeButton = QUIFactory.CreateButton(card.transform, "Close", "关闭", new Color(0.3f, 0.25f, 0.25f));
            QUIFactory.SetRect(closeButton.GetComponent<RectTransform>(), 0, 1, 0, 0, 16, 18, -16, 60);

            Debug.Log("[WalletPanel] 运行时 UI 已创建");
        }

        void OnWalletConnectUri(WalletConnectUriMessage msg)
        {
            currentUri = msg.uri;
            SetPanelVisible(true);
            UpdateStatusText("请扫码连接钱包…", connectingColor);
            RenderQRCode(currentUri);
            QUIFactory.SetText(addressText, "地址: 待连接");
            string u = currentUri ?? "";
            QUIFactory.SetText(uriHintText, u.Length > 64 ? u.Substring(0, 64) + "…" : u);
        }

        void OnWalletStatus(WalletStatusMessage msg)
        {
            if (msg.connected)
            {
                QUIFactory.SetText(addressText, $"地址: {ShortenAddress(msg.address ?? "")}");
                UpdateStatusText($"已连接 {msg.walletType ?? ""}", connectedColor);
                if (!string.IsNullOrEmpty(msg.qrUri)) RenderQRCode(msg.qrUri);
                hideTimer = autoHideDelay;
            }
            else
            {
                SetPanelVisible(true);
                UpdateStatusText("未连接", disconnectedColor);
                QUIFactory.SetText(addressText, "地址: 无");
                hideTimer = -1f;
            }
        }

        void RenderQRCode(string uri)
        {
            if (string.IsNullOrEmpty(uri) || qrCodeImage == null) return;
            try
            {
                if (qrTexture != null) { Destroy(qrTexture); qrTexture = null; }
                qrTexture = SimpleQR.CreateTexture(uri, qrPixelSize, 2);
                if (qrTexture != null)
                {
                    qrCodeImage.sprite = Sprite.Create(qrTexture, new Rect(0, 0, qrTexture.width, qrTexture.height), new Vector2(0.5f, 0.5f), 100f);
                    qrCodeImage.color = Color.white;
                    qrCodeImage.preserveAspect = true;
                }
                else qrCodeImage.color = new Color(0.9f, 0.9f, 0.9f);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WalletPanel] QR 渲染异常: {e.Message}");
                qrCodeImage.color = new Color(0.9f, 0.9f, 0.9f);
            }
        }

        public void ReportConnected(string address, string walletType)
        {
            if (wsClient == null) wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient == null) return;
            wsClient.SendWalletConnect(address, walletType);
            QUIFactory.SetText(addressText, $"地址: {ShortenAddress(address)}");
            UpdateStatusText($"已连接 {walletType}", connectedColor);
            hideTimer = autoHideDelay;
        }

        public void RequestConnect()
        {
            SetPanelVisible(true);
            UpdateStatusText("等待 QR 码…", connectingColor);
            if (wsClient == null) wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient != null) wsClient.SendWalletStatus();
            if (string.IsNullOrEmpty(currentUri))
            {
                currentUri = $"wc:demo@{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}@2?relay-protocol=irn&symKey=qcue";
                RenderQRCode(currentUri);
                QUIFactory.SetText(uriHintText, "演示 URI（后端未返回时本地生成）");
                UpdateStatusText("演示二维码已生成", connectingColor);
            }
        }

        void OnDemoConnectClicked()
        {
            string address = demoAddressPrefix + Guid.NewGuid().ToString("N").Substring(0, 20);
            ReportConnected(address, defaultWalletType);
        }

        public void ShowPanel()
        {
            if (panelContainer == null && autoCreateUI) BuildRuntimeUI();
            EnsureWorldSpaceCanvas();
            SetPanelVisible(true);
            hideTimer = -1f;
            if (string.IsNullOrEmpty(currentUri)) RequestConnect();
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

        void UpdateStatusText(string text, Color color)
        {
            QUIFactory.SetText(statusText, text);
            QUIFactory.SetColor(statusText, color);
        }

        string ShortenAddress(string addr)
        {
            if (string.IsNullOrEmpty(addr)) return "无";
            if (addr.Length <= 12) return addr;
            return $"{addr.Substring(0, 8)}…{addr.Substring(addr.Length - 4)}";
        }

        public void EnsureWorldSpaceCanvas()
        {
            Canvas canvas = panelContainer != null
                ? panelContainer.GetComponentInParent<Canvas>()
                : GetComponentInChildren<Canvas>();
            if (canvas == null) return;
            canvas.renderMode = RenderMode.WorldSpace;
            QXRUIBootstrap.MakeWorldSpaceHUD(canvas.gameObject, distance: 1.4f, scale: 0.0011f);
        }
    }
}
