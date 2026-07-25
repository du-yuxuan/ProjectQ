// ============================================================
// QSceneManager.cs
// Q (Cue) — PICO Unity 场景配置脚本
// 自动设置 PXR_Manager + 核心组件 + 手柄交互
// ============================================================

using UnityEngine;
using UnityEngine.EventSystems;
using Unity.XR.PXR;

namespace Q.Pico
{
    /// <summary>
    /// 场景配置管理器：Awake 配置 PICO，Start 确保全部核心子系统存在。
    /// </summary>
    public class QSceneManager : MonoBehaviour
    {
        [Header("PICO SDK 功能开关")]
        public bool enableVideoSeeThrough = true;
        public bool enableHandTracking = true;
        public bool enableSpatialAnchor = true;
        public bool enableLateLatching = true;
        public bool enableMSAA = true;

        [Header("自动创建子系统")]
        public bool ensureWebSocket = true;
        public bool ensureHUD = false;
        public bool ensureWorkspace = false;
        public bool ensureHeartRatePanel = true;
        public bool ensureWalletPanel = true;
        public bool ensureCredentialSpawner = true;
        public bool ensureRingBridge = true;
        public bool ensureRingBle = true;
        public bool ensureSpeciesMapper = true;
        public bool ensureControllerInput = true;
        public bool ensureAudioCapture = true; // 麦克风录音 + 音频帧上行
        public bool ensureXRUI = true;
        public bool ensureFaceOcclusion = false; // SecureMR 仅真机需要

        private static QSceneManager instance;
        public static QSceneManager Instance => instance;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigurePICO();
        }

        void ConfigurePICO()
        {
            var pxrManager = FindObjectOfType<PXR_Manager>();
            if (pxrManager == null)
            {
                var go = new GameObject("PXR_Manager");
                pxrManager = go.AddComponent<PXR_Manager>();
                Debug.Log("[QScene] PXR_Manager 已自动创建");
            }

            if (enableVideoSeeThrough)
            {
                try
                {
                    PXR_Manager.EnableVideoSeeThrough = true;
                    Debug.Log("[QScene] 视频透视已开启");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[QScene] 视频透视开启失败: {e.Message}");
                }
            }

            Debug.Log("[QScene] PICO 环境配置完成");
        }

        void Start()
        {
            EnsureCoreComponents();
        }

        void EnsureCoreComponents()
        {
            // 强制 XR 射线顺序：先 XRUI，再 Workspace
            if (ensureXRUI)
                Ensure<QXRUIBootstrap>("QXRUIBootstrap");

            if (ensureWebSocket)
                Ensure<QWebSocketClient>("QWebSocketClient");

            // Workspace 前再 Setup 一次
            var xruiEarly = FindObjectOfType<QXRUIBootstrap>();
            if (xruiEarly != null) xruiEarly.Setup();

            if (ensureWorkspace)
                Ensure<QSpatialWorkspace>("QSpatialWorkspace");

            if (ensureHUD)
                Ensure<SpatialHUDManager>("SpatialHUDManager");

            if (ensureHeartRatePanel)
                Ensure<HeartRateInputPanel>("HeartRateInputPanel");

            if (ensureWalletPanel)
                Ensure<WalletConnectPanel>("WalletConnectPanel");

            if (ensureCredentialSpawner)
                Ensure<CredentialCardSpawner>("CredentialCardSpawner");

            if (ensureAudioCapture)
                Ensure<QAudioCapture>("QAudioCapture");

            if (ensureRingBridge)
                Ensure<RingInputBridge>("RingInputBridge");

            if (ensureRingBle)
                Ensure<RingBleManager>("RingBleManager");

            if (ensureSpeciesMapper)
                Ensure<SpeciesMapper>("SpeciesMapper");

            if (ensureControllerInput)
                Ensure<PicoControllerInput>("PicoControllerInput");

            // 冬瓜遮挡控制器（设置里「屏蔽听众」用）
            if (FindObjectOfType<DongguaOcclusionController>() == null)
                Ensure<DongguaOcclusionController>("DongguaOcclusion");

            // 人脸/遮挡管理：设置开关会启用 SecureMR + 冬瓜
            if (FindObjectOfType<FaceOcclusionManager>() == null)
                Ensure<FaceOcclusionManager>("FaceOcclusionManager");

            if (ensureFaceOcclusion)
                Ensure<FaceOcclusionManager>("FaceOcclusionManager");

            // EventSystem：交给 QXRUIBootstrap 配置 XRUIInputModule
            if (!ensureXRUI && FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                Debug.Log("[QScene] 已创建 EventSystem (Standalone)");
            }

            // 互相关联引用
            var hud = FindObjectOfType<SpatialHUDManager>();
            var bridge = FindObjectOfType<RingInputBridge>();
            if (hud != null && bridge != null && bridge.hudManager == null)
                bridge.hudManager = hud;

            var ctrl = FindObjectOfType<PicoControllerInput>();
            if (ctrl != null)
            {
                if (ctrl.hudManager == null) ctrl.hudManager = hud;
                if (ctrl.ringBridge == null) ctrl.ringBridge = bridge;
                if (ctrl.heartRatePanel == null) ctrl.heartRatePanel = FindObjectOfType<HeartRateInputPanel>();
                if (ctrl.walletPanel == null) ctrl.walletPanel = FindObjectOfType<WalletConnectPanel>();
                if (ctrl.wsClient == null) ctrl.wsClient = FindObjectOfType<QWebSocketClient>();
            }

            // 确保 XR UI bootstrap 先于 Workspace 完成射线链路
            var xrui = FindObjectOfType<QXRUIBootstrap>();
            if (xrui == null && ensureXRUI)
                xrui = Ensure<QXRUIBootstrap>("QXRUIBootstrap");
            if (xrui != null)
            {
                xrui.Setup();
                // Workspace 可能刚创建 Canvas，再补一次
                xrui.PatchAllWorldCanvases();
            }

            Debug.Log("[QScene] 核心组件就绪（含手柄射线 UI）");
        }

        static T Ensure<T>(string name) where T : Component
        {
            var existing = FindObjectOfType<T>();
            if (existing != null) return existing;
            var go = new GameObject(name);
            var c = go.AddComponent<T>();
            Debug.Log($"[QScene] 已创建 {name}");
            return c;
        }

        /// <summary>切换物种化身（species_update 触发）。</summary>
        public void OnSpeciesUpdate(string species)
        {
            var faceOcclusion = FindObjectOfType<FaceOcclusionManager>();
            if (faceOcclusion != null)
                faceOcclusion.UpdateSpecies(species);

            var avatar = FindObjectOfType<SpeciesAvatarController>();
            if (avatar != null)
                avatar.UpdateSpecies(species);
        }
    }
}
