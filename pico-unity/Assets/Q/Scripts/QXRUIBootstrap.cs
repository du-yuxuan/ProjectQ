// ============================================================
// QXRUIBootstrap.cs
// Q (Cue) — 手柄射线操控 HUD
//
// 完整链路：
//   手柄 Trigger → XRRayInteractor (enableUIInteraction)
//   → XRUIInputModule → TrackedDeviceGraphicRaycaster
//   → World Space Canvas 按钮
//
// 场景若已有 Left/Right Controller 则复用并强制打开 UI 交互；
// 否则运行时创建 XRController + Ray + 线可视化。
// 持续扫描新 Canvas，自动补 TrackedDeviceGraphicRaycaster。
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

namespace Q.Pico
{
    public class QXRUIBootstrap : MonoBehaviour
    {
        [Header("配置")]
        public bool autoSetup = true;
        public float rayMaxDistance = 12f;
        public Color rayValidColor = new Color(0.35f, 0.85f, 1f, 0.95f);
        public Color rayInvalidColor = new Color(1f, 0.35f, 0.35f, 0.55f);
        public float rayWidth = 0.006f;
        public bool createMissingControllers = true;
        public bool rescanCanvasesEveryFrame = true;
        public float canvasRescanInterval = 0.5f;
        public bool debugLog = true;

        XRInteractionManager interactionManager;
        XRUIInputModule uiInputModule;
        bool setupDone;
        float rescanTimer;
        readonly HashSet<int> patchedCanvasIds = new HashSet<int>();

        void Start()
        {
            if (autoSetup) Setup();
        }

        void Update()
        {
            if (!setupDone) return;
            // 控制器可能晚一帧出现
            EnsureControllers();
            if (rescanCanvasesEveryFrame)
            {
                rescanTimer -= Time.unscaledDeltaTime;
                if (rescanTimer <= 0f)
                {
                    rescanTimer = canvasRescanInterval;
                    PatchAllWorldCanvases();
                }
            }
        }

        public void Setup()
        {
            EnsureEventSystem();
            EnsureInteractionManager();
            EnsureControllers();
            PatchAllWorldCanvases();
            setupDone = true;
            if (debugLog)
                Debug.Log("[XRUI] Bootstrap 完成：EventSystem/XRUIInputModule/射线/Canvas 射线检测");
        }

        // ============================================================
        // EventSystem + XRUIInputModule
        // ============================================================

        void EnsureEventSystem()
        {
            var es = FindObjectOfType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
                DontDestroyOnLoad(go);
            }

            // 移除会抢占 UI 的旧模块
            var standalone = es.GetComponent<StandaloneInputModule>();
            if (standalone != null) Destroy(standalone);

            var inputSysType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSysType != null)
            {
                var m = es.GetComponent(inputSysType);
                if (m != null) Destroy(m);
            }

            uiInputModule = es.GetComponent<XRUIInputModule>();
            if (uiInputModule == null)
                uiInputModule = es.gameObject.AddComponent<XRUIInputModule>();

            // XR 射线点击；Editor 保留鼠标方便调试
            uiInputModule.enableMouseInput = true;
            uiInputModule.enableTouchInput = false;
            // 部分 XRI 版本字段
            try
            {
                var f = typeof(XRUIInputModule).GetField("m_ClickSpeed",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                // ignore if absent
            }
            catch { }

            if (debugLog) Debug.Log("[XRUI] EventSystem + XRUIInputModule 就绪");
        }

        void EnsureInteractionManager()
        {
            interactionManager = FindObjectOfType<XRInteractionManager>();
            if (interactionManager == null)
            {
                var go = new GameObject("XR Interaction Manager");
                interactionManager = go.AddComponent<XRInteractionManager>();
                DontDestroyOnLoad(go);
            }
        }

        // ============================================================
        // Controllers + Ray
        // ============================================================

        void EnsureControllers()
        {
            var existingRays = FindObjectsOfType<XRRayInteractor>(true);
            if (existingRays != null && existingRays.Length > 0)
            {
                foreach (var ray in existingRays)
                    ConfigureRayInteractor(ray, ensureVisual: true);
                // 确保左右控制器的 UI Press = Trigger
                ConfigureControllerUiPress(existingRays);
                return;
            }

            if (!createMissingControllers) return;

            Transform origin = FindControllerParent();
            CreateController(origin, "Q_RightController", XRNode.RightHand, true);
            CreateController(origin, "Q_LeftController", XRNode.LeftHand, false);
        }

        Transform FindControllerParent()
        {
            var xrOrigin = FindObjectOfType<XROrigin>();
            if (xrOrigin != null)
            {
                if (xrOrigin.CameraFloorOffsetObject != null)
                    return xrOrigin.CameraFloorOffsetObject.transform;
                return xrOrigin.transform;
            }
            var cam = Camera.main;
            if (cam != null && cam.transform.parent != null)
                return cam.transform.parent;
            var go = GameObject.Find("Q_XRControllers");
            if (go == null)
            {
                go = new GameObject("Q_XRControllers");
                DontDestroyOnLoad(go);
            }
            return go.transform;
        }

        void CreateController(Transform parent, string name, XRNode node, bool isRight)
        {
            if (parent.Find(name) != null) return;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = isRight
                ? new Vector3(0.15f, -0.1f, 0.05f)
                : new Vector3(-0.15f, -0.1f, 0.05f);

            // Device-based：不依赖 Input Action 资产，PICO 更稳
            var controller = go.AddComponent<XRController>();
            controller.controllerNode = node;
            controller.selectUsage = InputHelpers.Button.Trigger;
            controller.activateUsage = InputHelpers.Button.Grip;
            controller.uiPressUsage = InputHelpers.Button.Trigger; // HUD 点击键

            var ray = go.AddComponent<XRRayInteractor>();
            ConfigureRayInteractor(ray, ensureVisual: true);

            // 简易手柄 tip
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.name = "Model";
            model.transform.SetParent(go.transform, false);
            model.transform.localScale = new Vector3(0.025f, 0.025f, 0.10f);
            model.transform.localPosition = new Vector3(0, 0, 0.05f);
            var col = model.GetComponent<Collider>();
            if (col != null) Destroy(col);

            if (debugLog) Debug.Log($"[XRUI] 创建控制器 {name} ({node}) UI=Trigger");
        }

        void ConfigureControllerUiPress(XRRayInteractor[] rays)
        {
            foreach (var ray in rays)
            {
                if (ray == null) continue;
                // Device-based
                var dev = ray.GetComponent<XRController>();
                if (dev != null)
                {
                    dev.uiPressUsage = InputHelpers.Button.Trigger;
                    dev.selectUsage = InputHelpers.Button.Trigger;
                }
                // ActionBased：无法在无资产时改 binding，至少保证 ray UI 开
            }
        }

        void ConfigureRayInteractor(XRRayInteractor ray, bool ensureVisual)
        {
            if (ray == null) return;
            if (interactionManager == null) EnsureInteractionManager();
            ray.interactionManager = interactionManager;
            ray.maxRaycastDistance = rayMaxDistance;
            ray.raycastMask = ~0;
            ray.enableUIInteraction = true; // 关键：允许点 UI
            try { ray.lineType = XRRayInteractor.LineType.StraightLine; } catch { }

            // 射线起点：手柄前方
            try
            {
                ray.rayOriginTransform = ray.transform;
            }
            catch { }

            if (ensureVisual)
                EnsureRayVisual(ray);
        }

        void EnsureRayVisual(XRRayInteractor ray)
        {
            var lineVisual = ray.GetComponent<XRInteractorLineVisual>();
            if (lineVisual == null)
                lineVisual = ray.gameObject.AddComponent<XRInteractorLineVisual>();

            lineVisual.lineWidth = rayWidth;
            lineVisual.validColorGradient = MakeGradient(rayValidColor);
            lineVisual.invalidColorGradient = MakeGradient(rayInvalidColor);
            lineVisual.overrideInteractorLineLength = true;
            lineVisual.lineLength = rayMaxDistance;

            // 需要 LineRenderer
            var lr = ray.GetComponent<LineRenderer>();
            if (lr == null)
            {
                lr = ray.gameObject.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    lr.material = new Material(shader);
                    lr.material.color = rayValidColor;
                }
                lr.startWidth = rayWidth;
                lr.endWidth = rayWidth * 0.35f;
            }

            // 可选 reticle
            EnsureReticle(ray);
        }

        void EnsureReticle(XRRayInteractor ray)
        {
            const string reticleName = "Q_UIReticle";
            Transform existing = ray.transform.Find(reticleName);
            GameObject reticleGo;
            if (existing != null) reticleGo = existing.gameObject;
            else
            {
                reticleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                reticleGo.name = reticleName;
                reticleGo.transform.SetParent(ray.transform, false);
                reticleGo.transform.localScale = Vector3.one * 0.015f;
                var col = reticleGo.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var r = reticleGo.GetComponent<Renderer>();
                if (r != null)
                {
                    var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                    if (sh != null)
                    {
                        r.material = new Material(sh);
                        r.material.color = rayValidColor;
                    }
                }
                reticleGo.SetActive(false);
            }

            // 用简单跟随脚本把 reticle 贴到 UI 命中点
            var follow = reticleGo.GetComponent<RayReticleFollower>();
            if (follow == null) follow = reticleGo.AddComponent<RayReticleFollower>();
            follow.ray = ray;
            follow.reticle = reticleGo;
        }

        static Gradient MakeGradient(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(c.a * 0.25f, 1f) });
            return g;
        }

        // ============================================================
        // Canvas patch：所有 World Space Canvas 必须能被 XR 射线点到
        // ============================================================

        public void PatchAllWorldCanvases()
        {
            var canvases = FindObjectsOfType<Canvas>(true);
            int n = 0;
            foreach (var c in canvases)
            {
                if (c == null) continue;
                if (c.renderMode != RenderMode.WorldSpace && c.renderMode != RenderMode.ScreenSpaceCamera)
                {
                    // Overlay 无法被 XR 射线点，跳过
                    continue;
                }
                if (PatchCanvas(c)) n++;
            }
            if (debugLog && n > 0)
                Debug.Log($"[XRUI] 已为 {n} 个 Canvas 补齐 TrackedDeviceGraphicRaycaster");
        }

        public static bool PatchCanvas(Canvas canvas)
        {
            if (canvas == null) return false;
            bool changed = false;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                changed = true;
            }

            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                changed = true;
            }

            // 绑定相机，避免射线排序异常
            if (canvas.worldCamera == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    var cams = Object.FindObjectsOfType<Camera>();
                    foreach (var c in cams)
                    {
                        if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                        { cam = c; break; }
                    }
                }
                if (cam != null)
                {
                    canvas.worldCamera = cam;
                    changed = true;
                }
            }

            // 保证可点：根 Image 可 raycast；按钮 targetGraphic 打开
            var graphics = canvas.GetComponentsInChildren<Graphic>(true);
            foreach (var g in graphics)
            {
                // 文本默认不挡射线；按钮背景需要
                if (g is Text || g is TMPro.TMP_Text) continue;
                // 不强制改所有，只确保 Button 的 target 可点
            }
            var buttons = canvas.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b.targetGraphic != null)
                    b.targetGraphic.raycastTarget = true;
            }

            return changed;
        }

        // ============================================================
        // Public helpers used by Workspace / HUD
        // ============================================================

        public static Canvas CreateWorldSpaceCanvas(Transform parent, string name, float distance = 1.6f, float scale = 0.0012f, int sortingOrder = 100)
        {
            var root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, false);
            Object.DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;
            scaler.referencePixelsPerUnit = 100f;

            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<TrackedDeviceGraphicRaycaster>();

            MakeWorldSpaceHUD(root, distance, scale);
            return canvas;
        }

        public static Canvas MakeWorldSpaceHUD(GameObject root, float distance = 1.6f, float scale = 0.0012f)
        {
            if (root == null) return null;

            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;
            scaler.referencePixelsPerUnit = 100f;

            if (root.GetComponent<GraphicRaycaster>() == null)
                root.AddComponent<GraphicRaycaster>();
            if (root.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                root.AddComponent<TrackedDeviceGraphicRaycaster>();

            var rt = root.GetComponent<RectTransform>();
            if (rt == null) rt = root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            if (rt.sizeDelta.sqrMagnitude < 1f)
                rt.sizeDelta = new Vector2(1920f, 1080f);
            root.transform.localScale = Vector3.one * scale;

            var follower = root.GetComponent<HudFollowHead>();
            if (follower == null) follower = root.AddComponent<HudFollowHead>();
            follower.distance = distance;
            follower.heightOffset = -0.05f;

            Camera cam = Camera.main;
            if (cam == null)
            {
                var cams = Object.FindObjectsOfType<Camera>();
                foreach (var c in cams)
                {
                    if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                    { cam = c; break; }
                }
            }
            if (cam != null)
            {
                canvas.worldCamera = cam;
                Vector3 flat = cam.transform.forward;
                flat.y = 0f;
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                flat.Normalize();
                root.transform.position = cam.transform.position + flat * distance + Vector3.up * follower.heightOffset;
                root.transform.rotation = Quaternion.LookRotation(root.transform.position - cam.transform.position, Vector3.up);
            }
            else
            {
                root.transform.position = new Vector3(0f, 1.3f, distance);
            }

            return canvas;
        }
    }

    /// <summary>HUD 平滑跟随头显前方。</summary>
    public class HudFollowHead : MonoBehaviour
    {
        public float distance = 1.6f;
        public float heightOffset = -0.05f;
        public float followLerp = 8f;
        public float yawOnly = 1f;

        Transform head;

        void LateUpdate()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    var cams = Object.FindObjectsOfType<Camera>();
                    foreach (var c in cams)
                    {
                        if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                        { cam = c; break; }
                    }
                }
                if (cam != null) head = cam.transform;
                else return;
            }

            Vector3 flatForward = head.forward;
            if (yawOnly > 0.5f)
            {
                flatForward.y = 0f;
                if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
                flatForward.Normalize();
            }

            Vector3 targetPos = head.position + flatForward * distance + Vector3.up * heightOffset;
            Quaternion targetRot = Quaternion.LookRotation(targetPos - head.position, Vector3.up);

            if ((transform.position - targetPos).sqrMagnitude > 25f)
            {
                transform.position = targetPos;
                transform.rotation = targetRot;
                return;
            }

            float t = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
        }
    }

    /// <summary>射线命中 UI 时显示光点。</summary>
    public class RayReticleFollower : MonoBehaviour
    {
        public XRRayInteractor ray;
        public GameObject reticle;

        void LateUpdate()
        {
            if (ray == null || reticle == null) return;
            if (ray.TryGetCurrentUIRaycastResult(out var uiHit))
            {
                reticle.SetActive(true);
                reticle.transform.position = uiHit.worldPosition;
                if (uiHit.worldNormal.sqrMagnitude > 1e-4f)
                    reticle.transform.rotation = Quaternion.LookRotation(-uiHit.worldNormal);
            }
            else if (ray.TryGetCurrent3DRaycastHit(out var hit))
            {
                reticle.SetActive(true);
                reticle.transform.position = hit.point;
            }
            else
            {
                reticle.SetActive(false);
            }
        }
    }
}
