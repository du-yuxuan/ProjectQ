// ============================================================
// QRuntimeBootstrap.cs
// 场景加载后强制拉起 Q 子系统与悬浮 HUD
// ============================================================

using UnityEngine;

namespace Q.Pico
{
    public static class QRuntimeBootstrap
    {
        const string RootName = "Q_RuntimeSystems";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad()
        {
            try
            {
                EnsureSystems();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[QBootstrap] 启动失败: {e}");
            }
        }

        public static void EnsureSystems()
        {
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Object.DontDestroyOnLoad(root);
            }

            // 即使场景里有被禁用的组件，也创建一套激活的
            EnsureOn<QSceneManager>(root, "QSceneManager");
            EnsureOn<QWebSocketClient>(root, "QWebSocketClient");
            EnsureOn<RingInputBridge>(root, "RingInputBridge");
            EnsureOn<PicoControllerInput>(root, "PicoControllerInput");
            EnsureOn<QXRUIBootstrap>(root, "QXRUIBootstrap");
            EnsureOn<HeartRateInputPanel>(root, "HeartRateInputPanel");
            EnsureOn<WalletConnectPanel>(root, "WalletConnectPanel");
            EnsureOn<CredentialCardSpawner>(root, "CredentialCardSpawner");
            EnsureOn<QAudioCapture>(root, "QAudioCapture");

            // SpatialHUDManager 已废弃，不再强制显示（见上方注释）。
            // var hud = Object.FindObjectOfType<SpatialHUDManager>();
            // if (hud != null)
            // {
            //     hud.autoCreateUI = true;
            //     hud.ForceShowUI();
            // }

            var xrui = Object.FindObjectOfType<QXRUIBootstrap>();
            if (xrui != null) xrui.Setup();

            Debug.Log("[QBootstrap] 系统已确保运行，HUD 已请求显示");
        }

        static T EnsureOn<T>(GameObject root, string name) where T : Component
        {
            // 优先找激活实例
            var existing = Object.FindObjectOfType<T>();
            if (existing != null && existing.gameObject.activeInHierarchy)
                return existing;

            // 场景里有但被禁用：在 root 上新建一份
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var c = go.AddComponent<T>();
            Debug.Log($"[QBootstrap] 创建 {name}");
            return c;
        }
    }
}
