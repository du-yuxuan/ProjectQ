// ============================================================
// CredentialCardSpawner.cs
// Q (Cue) — 3D 凭证卡片生成器（含运行时简单卡片）
// ============================================================

using System.Collections;
using TMPro;
using UnityEngine;

namespace Q.Pico
{
    public class CredentialCardSpawner : MonoBehaviour
    {
        [Header("卡片 Prefab")]
        public GameObject cardPrefab;
        public GameObject mintParticlePrefab;
        public float particleDuration = 2.0f;

        [Header("卡片位置")]
        public Vector3 spawnOffset = new Vector3(0.3f, 0.2f, 1.5f);
        public float cardScale = 0.15f;
        public float rotationSpeed = 15f;
        public float floatAmplitude = 0.03f;
        public float floatSpeed = 1.5f;

        [Header("自动隐藏")]
        public float displayDuration = 10f;

        [Header("调试")]
        public bool debugLog = false;

        private QWebSocketClient wsClient;
        private GameObject currentCard;
        private CredentialCardView currentCardView;
        private float displayTimer = -1f;
        private Vector3 basePosition;

        void Start()
        {
            wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient == null)
                Debug.LogWarning("[Credential] QWebSocketClient 未找到");
            else
                wsClient.OnCredentialMinted.AddListener(OnCredentialMinted);
        }

        void Update()
        {
            if (currentCard == null) return;

            currentCard.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
            float yOffset = Mathf.Sin(Time.time * floatSpeed) * floatAmplitude;
            Vector3 pos = basePosition;
            pos.y += yOffset;
            currentCard.transform.position = pos;

            if (displayTimer > 0f)
            {
                displayTimer -= Time.deltaTime;
                if (displayTimer <= 0f) HideCard();
            }
        }

        void OnDestroy()
        {
            if (wsClient != null)
                wsClient.OnCredentialMinted.RemoveListener(OnCredentialMinted);
        }

        void OnCredentialMinted(CredentialMintedMessage msg) => SpawnCard(msg);

        void SpawnCard(CredentialMintedMessage msg)
        {
            if (currentCard != null) Destroy(currentCard);

            if (cardPrefab == null)
            {
                Debug.LogWarning("[Credential] cardPrefab 未配置，使用内置简单卡片");
                CreateSimpleCard(msg);
            }
            else
            {
                var cam = Camera.main;
                Vector3 spawnPos = cam != null
                    ? cam.transform.position + cam.transform.TransformDirection(spawnOffset)
                    : Vector3.forward * 1.5f + spawnOffset;

                currentCard = Instantiate(cardPrefab, spawnPos, Quaternion.identity, transform);
                currentCard.transform.localScale = Vector3.one * cardScale;
                currentCardView = currentCard.GetComponent<CredentialCardView>();
                if (currentCardView == null)
                    currentCardView = currentCard.AddComponent<CredentialCardView>();
                currentCardView.SetData(msg);
            }

            if (currentCard == null) return;
            basePosition = currentCard.transform.position;

            if (mintParticlePrefab != null)
            {
                var particles = Instantiate(mintParticlePrefab, currentCard.transform.position, Quaternion.identity);
                Destroy(particles, particleDuration);
            }
            else
            {
                StartCoroutine(BurstFlash(currentCard.transform.position));
            }

            displayTimer = displayDuration > 0 ? displayDuration : -1f;
            if (debugLog)
                Debug.Log($"[Credential] 凭证卡片已生成: {msg.milestone} (tx: {msg.chainTxHash})");

            StartCoroutine(CardSpawnAnimation(currentCard));
        }

        IEnumerator CardSpawnAnimation(GameObject card)
        {
            if (card == null) yield break;           // 初始空检查
            Vector3 targetScale = card.transform.localScale;
            card.transform.localScale = Vector3.zero;
            float elapsed = 0f;
            float duration = 0.6f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                // 每帧检查 card 是否已被 HideCard 销毁（displayTimer 到期），
                // 避免访问已销毁对象的 transform 触发 NullReferenceException
                if (card == null) yield break;
                float t = Mathf.Clamp01(elapsed / duration);
                float easeT = 1f + 2.70158f * Mathf.Pow(t - 1f, 3) + 1.70158f * Mathf.Pow(t - 1f, 2);
                card.transform.localScale = targetScale * easeT;
                yield return null;
            }
            if (card != null) card.transform.localScale = targetScale;
        }

        IEnumerator BurstFlash(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "MintFlash";
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.05f;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (sh != null)
                {
                    r.material = new Material(sh);
                    r.material.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                }
            }
            float t = 0f;
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                float k = t / 0.6f;
                go.transform.localScale = Vector3.one * (0.05f + k * 0.4f);
                if (r != null && r.material != null)
                {
                    var c = r.material.color;
                    c.a = 1f - k;
                    r.material.color = c;
                }
                yield return null;
            }
            Destroy(go);
        }

        void CreateSimpleCard(CredentialMintedMessage msg)
        {
            var cam = Camera.main;
            Vector3 spawnPos = cam != null
                ? cam.transform.position + cam.transform.TransformDirection(spawnOffset)
                : Vector3.forward * 1.5f;

            currentCard = new GameObject($"Credential_{msg.milestone}");
            currentCard.transform.SetParent(transform);
            currentCard.transform.position = spawnPos;
            currentCard.transform.localScale = Vector3.one * cardScale;

            var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
            board.name = "Board";
            board.transform.SetParent(currentCard.transform, false);
            board.transform.localScale = new Vector3(2.2f, 3.2f, 1f);
            var col = board.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var br = board.GetComponent<Renderer>();
            if (br != null)
            {
                var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (sh != null)
                {
                    br.material = new Material(sh);
                    br.material.color = new Color(0.12f, 0.14f, 0.22f, 1f);
                }
            }

            currentCardView = currentCard.AddComponent<CredentialCardView>();
            currentCardView.SetData(msg);
        }

        void HideCard()
        {
            if (currentCard != null)
            {
                Destroy(currentCard);
                currentCard = null;
                currentCardView = null;
            }
            displayTimer = -1f;
        }

        public void DismissCard() => HideCard();
    }

    public class CredentialCardView : MonoBehaviour
    {
        public TMP_Text credentialTypeText;
        public TMP_Text levelText;
        public TMP_Text metricsText;
        public TMP_Text txHashText;
        public TMP_Text soulboundText;
        public Color soulboundColor = new Color(0.8f, 0.6f, 0.2f);

        public void SetData(CredentialMintedMessage msg)
        {
            var meta = msg.metadata;

            if (credentialTypeText != null)
                credentialTypeText.text = meta.credential_type ?? "Unknown";
            if (levelText != null)
                levelText.text = $"Lv. {meta.level ?? "—"}";
            if (metricsText != null)
            {
                metricsText.text =
                    $"流畅度: {meta.fluency:F1}\n" +
                    $"逻辑性: {meta.logic:F1}\n" +
                    $"接收度: {meta.reception:F1}\n" +
                    $"停滞率: {meta.stall_rate:F1}\n" +
                    $"提升: {meta.improvement ?? "—"}";
            }
            if (txHashText != null)
            {
                string hash = msg.chainTxHash ?? "—";
                txHashText.text = hash.Length > 16
                    ? $"{hash.Substring(0, 8)}...{hash.Substring(hash.Length - 6)}"
                    : hash;
            }
            if (soulboundText != null)
            {
                soulboundText.text = meta.soulbound ? "★ Soulbound" : "Transferable";
                soulboundText.color = meta.soulbound ? soulboundColor : Color.gray;
            }

            if (credentialTypeText == null && levelText == null && metricsText == null)
                AddFloatingText(msg);
        }

        void AddFloatingText(CredentialMintedMessage msg)
        {
            var meta = msg.metadata;
            var go = new GameObject("CardText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 0, -0.02f);
            go.transform.localScale = Vector3.one * 0.02f;

            string hash = msg.chainTxHash ?? "—";
            string shortHash = hash.Length > 16
                ? $"{hash.Substring(0, 8)}…{hash.Substring(hash.Length - 4)}"
                : hash;
            string content =
                $"{meta.credential_type ?? msg.milestone}\n" +
                $"Lv. {meta.level ?? "—"}\n" +
                $"F:{meta.fluency:F1} L:{meta.logic:F1} R:{meta.reception:F1}\n" +
                $"{(meta.soulbound ? "★ Soulbound" : "")}\n" +
                $"{shortHash}";

            if (QUIFactory.IsTmpAvailable)
            {
                try
                {
                    var tmp = go.AddComponent<TextMeshPro>();
                    tmp.fontSize = 8;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = Color.white;
                    tmp.enableWordWrapping = true;
                    var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Chinese SDF");
                    if (font == null)
                        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansSC SDF");
                    if (font == null)
                        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/PICOSansSC SDF");
                    if (font != null)
                    {
                        // 开启动态图集 + 预热，避免凭证卡显示白块
                        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                        font.TryAddCharacters(content);
                        tmp.font = font;
                    }
                    tmp.text = content;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt != null) rt.sizeDelta = new Vector2(100, 140);
                    return;
                }
                catch { /* fallthrough */ }
            }

            var tm = go.AddComponent<TextMesh>();
            tm.text = content;
            tm.characterSize = 0.08f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            if (QUIFactory.CjkFont != null) tm.font = QUIFactory.CjkFont;
        }
    }
}
