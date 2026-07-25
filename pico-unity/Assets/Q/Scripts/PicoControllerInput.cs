// ============================================================
// PicoControllerInput.cs
// Q (Cue) — PICO 手柄输入
//
// 使用 Unity XR InputDevices（兼容 PICO XR / OpenXR）：
//   右 Trigger / A  → 确认恢复（等同指环双击）
//   右 Grip / B     → 忽略钩子（等同指环单击）
//   右摇杆 ←/→     → 切换钩子类型
//   右摇杆 ↑       → 切换钩子（wave）
//   左菜单 / X     → 心率面板
//   左 Y           → 钱包面板
//   左摇杆点击     → 开始/结束会话
//   右菜单         → 演示递钩
//
// 同时保留键盘回退（编辑器调试）。
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace Q.Pico
{
    public class PicoControllerInput : MonoBehaviour
    {
        [Header("引用（可空，自动查找）")]
        public SpatialHUDManager hudManager;
        public RingInputBridge ringBridge;
        public HeartRateInputPanel heartRatePanel;
        public WalletConnectPanel walletPanel;
        public QWebSocketClient wsClient;

        [Header("开关")]
        public bool enableControllerInput = true;
        public bool enableKeyboardFallback = true;
        public bool preferRightHand = true;
        public bool debugLog = false;

        [Header("摇杆死区")]
        [Range(0.1f, 0.9f)]
        public float stickDeadzone = 0.6f;
        public float stickRepeatDelay = 0.35f;

        [Header("事件（可选额外监听）")]
        public UnityEvent OnConfirm = new UnityEvent();
        public UnityEvent OnDismiss = new UnityEvent();
        public UnityEvent OnNextHook = new UnityEvent();
        public UnityEvent OnPrevHook = new UnityEvent();
        public UnityEvent OnToggleHeartRate = new UnityEvent();
        public UnityEvent OnToggleWallet = new UnityEvent();
        public UnityEvent OnToggleSession = new UnityEvent();
        public UnityEvent OnDemoHook = new UnityEvent();

        // 边沿检测
        bool prevPrimaryR, prevSecondaryR, prevTriggerR, prevGripR, prevMenuR;
        bool prevPrimaryL, prevSecondaryL, prevTriggerL, prevGripL, prevMenuL;
        bool prevStickClickL, prevStickClickR;
        Vector2 prevStickR;
        float stickCooldown;

        // 键盘
        public KeyCode keyConfirm = KeyCode.Return;
        public KeyCode keyDismiss = KeyCode.Space;
        public KeyCode keyNext = KeyCode.RightArrow;
        public KeyCode keyPrev = KeyCode.LeftArrow;
        public KeyCode keyWave = KeyCode.UpArrow;
        public KeyCode keyHeart = KeyCode.H;
        public KeyCode keyWallet = KeyCode.W;
        public KeyCode keySession = KeyCode.S;
        public KeyCode keyDemoHook = KeyCode.D;

        void Start()
        {
            ResolveRefs();
        }

        void Update()
        {
            if (hudManager == null || ringBridge == null || heartRatePanel == null || walletPanel == null)
                ResolveRefs();

            if (enableControllerInput)
                PollControllers();

            if (enableKeyboardFallback)
                PollKeyboard();

            if (stickCooldown > 0f)
                stickCooldown -= Time.unscaledDeltaTime;
        }

        void ResolveRefs()
        {
            if (hudManager == null) hudManager = FindObjectOfType<SpatialHUDManager>();
            if (ringBridge == null) ringBridge = FindObjectOfType<RingInputBridge>();
            if (heartRatePanel == null) heartRatePanel = FindObjectOfType<HeartRateInputPanel>();
            if (walletPanel == null) walletPanel = FindObjectOfType<WalletConnectPanel>();
            if (wsClient == null) wsClient = FindObjectOfType<QWebSocketClient>();
        }

        // ============================================================
        // XR Controllers
        // ============================================================

        void PollControllers()
        {
            var right = GetDevice(XRNode.RightHand);
            var left = GetDevice(XRNode.LeftHand);

            // ---- Right: hook actions ----
            bool primaryR = ReadButton(right, CommonUsages.primaryButton);     // A
            bool secondaryR = ReadButton(right, CommonUsages.secondaryButton); // B
            bool triggerR = ReadButton(right, CommonUsages.triggerButton);
            bool gripR = ReadButton(right, CommonUsages.gripButton);
            bool menuR = ReadButton(right, CommonUsages.menuButton);
            Vector2 stickR = ReadAxis(right, CommonUsages.primary2DAxis);
            bool stickClickR = ReadButton(right, CommonUsages.primary2DAxisClick);

            if (WasPressed(triggerR, prevTriggerR) || WasPressed(primaryR, prevPrimaryR))
                DoConfirm();
            if (WasPressed(gripR, prevGripR) || WasPressed(secondaryR, prevSecondaryR))
                DoDismiss();
            if (WasPressed(menuR, prevMenuR))
                DoDemoHook();

            // stick horizontal / vertical with cooldown
            if (stickCooldown <= 0f)
            {
                if (stickR.x >= stickDeadzone && prevStickR.x < stickDeadzone)
                {
                    DoNextHook();
                    stickCooldown = stickRepeatDelay;
                }
                else if (stickR.x <= -stickDeadzone && prevStickR.x > -stickDeadzone)
                {
                    DoPrevHook();
                    stickCooldown = stickRepeatDelay;
                }
                else if (stickR.y >= stickDeadzone && prevStickR.y < stickDeadzone)
                {
                    DoNextHook(); // wave / next
                    stickCooldown = stickRepeatDelay;
                }
            }

            prevPrimaryR = primaryR;
            prevSecondaryR = secondaryR;
            prevTriggerR = triggerR;
            prevGripR = gripR;
            prevMenuR = menuR;
            prevStickR = stickR;
            prevStickClickR = stickClickR;

            // ---- Left: panel / session ----
            bool primaryL = ReadButton(left, CommonUsages.primaryButton);     // X
            bool secondaryL = ReadButton(left, CommonUsages.secondaryButton); // Y
            bool triggerL = ReadButton(left, CommonUsages.triggerButton);
            bool gripL = ReadButton(left, CommonUsages.gripButton);
            bool menuL = ReadButton(left, CommonUsages.menuButton);
            bool stickClickL = ReadButton(left, CommonUsages.primary2DAxisClick);

            if (WasPressed(primaryL, prevPrimaryL) || WasPressed(menuL, prevMenuL))
                DoToggleHeartRate();
            if (WasPressed(secondaryL, prevSecondaryL))
                DoToggleWallet();
            if (WasPressed(stickClickL, prevStickClickL))
                DoToggleSession();

            // 左手 Trigger 也可确认（便于惯用左手）
            if (!preferRightHand && WasPressed(triggerL, prevTriggerL))
                DoConfirm();
            if (!preferRightHand && WasPressed(gripL, prevGripL))
                DoDismiss();

            prevPrimaryL = primaryL;
            prevSecondaryL = secondaryL;
            prevTriggerL = triggerL;
            prevGripL = gripL;
            prevMenuL = menuL;
            prevStickClickL = stickClickL;
        }

        void PollKeyboard()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // 仅新 Input System：用 Keyboard API
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb[UnityEngine.InputSystem.Key.Enter].wasPressedThisFrame ||
                kb[UnityEngine.InputSystem.Key.NumpadEnter].wasPressedThisFrame) DoConfirm();
            if (kb[UnityEngine.InputSystem.Key.Space].wasPressedThisFrame) DoDismiss();
            if (kb[UnityEngine.InputSystem.Key.RightArrow].wasPressedThisFrame ||
                kb[UnityEngine.InputSystem.Key.UpArrow].wasPressedThisFrame) DoNextHook();
            if (kb[UnityEngine.InputSystem.Key.LeftArrow].wasPressedThisFrame) DoPrevHook();
            if (kb[UnityEngine.InputSystem.Key.H].wasPressedThisFrame) DoToggleHeartRate();
            if (kb[UnityEngine.InputSystem.Key.W].wasPressedThisFrame) DoToggleWallet();
            if (kb[UnityEngine.InputSystem.Key.S].wasPressedThisFrame) DoToggleSession();
            if (kb[UnityEngine.InputSystem.Key.D].wasPressedThisFrame) DoDemoHook();
#else
            if (Input.GetKeyDown(keyConfirm)) DoConfirm();
            if (Input.GetKeyDown(keyDismiss)) DoDismiss();
            if (Input.GetKeyDown(keyNext) || Input.GetKeyDown(keyWave)) DoNextHook();
            if (Input.GetKeyDown(keyPrev)) DoPrevHook();
            if (Input.GetKeyDown(keyHeart)) DoToggleHeartRate();
            if (Input.GetKeyDown(keyWallet)) DoToggleWallet();
            if (Input.GetKeyDown(keySession)) DoToggleSession();
            if (Input.GetKeyDown(keyDemoHook)) DoDemoHook();
#endif
        }

        // ============================================================
        // Actions
        // ============================================================

        public void DoConfirm()
        {
            if (debugLog) Debug.Log("[Ctrl] Confirm");
            OnConfirm?.Invoke();
            // 优先走 RingInputBridge，保持与指环一致的上报逻辑
            if (ringBridge != null)
                ringBridge.SimulateCommand(RingCommand.double_click);
            else if (hudManager != null)
                hudManager.OnLocalConfirmRecovery();
            Haptic(XRNode.RightHand, 0.4f, 0.05f);
        }

        public void DoDismiss()
        {
            if (debugLog) Debug.Log("[Ctrl] Dismiss");
            OnDismiss?.Invoke();
            if (ringBridge != null)
                ringBridge.SimulateCommand(RingCommand.single_click);
            else if (hudManager != null)
                hudManager.OnLocalDismissHook();
            Haptic(XRNode.RightHand, 0.25f, 0.04f);
        }

        public void DoNextHook()
        {
            if (debugLog) Debug.Log("[Ctrl] NextHook");
            OnNextHook?.Invoke();
            if (ringBridge != null)
                ringBridge.SimulateCommand(RingCommand.rotate_front);
            Haptic(XRNode.RightHand, 0.2f, 0.03f);
        }

        public void DoPrevHook()
        {
            if (debugLog) Debug.Log("[Ctrl] PrevHook");
            OnPrevHook?.Invoke();
            if (ringBridge != null)
                ringBridge.SimulateCommand(RingCommand.rotate_back);
            Haptic(XRNode.RightHand, 0.2f, 0.03f);
        }

        public void DoToggleHeartRate()
        {
            if (debugLog) Debug.Log("[Ctrl] Toggle HR");
            OnToggleHeartRate?.Invoke();
            if (heartRatePanel == null) heartRatePanel = FindObjectOfType<HeartRateInputPanel>();
            if (heartRatePanel != null) heartRatePanel.TogglePanel();
            Haptic(XRNode.LeftHand, 0.3f, 0.04f);
        }

        public void DoToggleWallet()
        {
            if (debugLog) Debug.Log("[Ctrl] Toggle Wallet");
            OnToggleWallet?.Invoke();
            if (walletPanel == null) walletPanel = FindObjectOfType<WalletConnectPanel>();
            if (walletPanel != null) walletPanel.TogglePanel();
            Haptic(XRNode.LeftHand, 0.3f, 0.04f);
        }

        public void DoToggleSession()
        {
            if (debugLog) Debug.Log("[Ctrl] Toggle Session");
            OnToggleSession?.Invoke();
            // 优先多窗口 Workspace
            var workspace = FindObjectOfType<QSpatialWorkspace>();
            if (workspace != null)
            {
                // 通过模拟点击：直接找 start 逻辑 — 调用公共方法
                workspace.SendMessage("OnToggleSession", SendMessageOptions.DontRequireReceiver);
            }
            else if (hudManager != null)
            {
                hudManager.ToggleSessionFromUI();
            }
            Haptic(XRNode.LeftHand, 0.45f, 0.06f);
        }

        public void DoDemoHook()
        {
            if (debugLog) Debug.Log("[Ctrl] Demo Hook");
            OnDemoHook?.Invoke();
            if (hudManager != null)
                hudManager.TriggerDemoHook();
            Haptic(XRNode.RightHand, 0.5f, 0.08f);
        }

        // ============================================================
        // XR helpers
        // ============================================================

        static InputDevice GetDevice(XRNode node)
        {
            return InputDevices.GetDeviceAtXRNode(node);
        }

        static bool ReadButton(InputDevice device, InputFeatureUsage<bool> usage)
        {
            if (!device.isValid) return false;
            return device.TryGetFeatureValue(usage, out bool v) && v;
        }

        static Vector2 ReadAxis(InputDevice device, InputFeatureUsage<Vector2> usage)
        {
            if (!device.isValid) return Vector2.zero;
            return device.TryGetFeatureValue(usage, out Vector2 v) ? v : Vector2.zero;
        }

        static bool WasPressed(bool now, bool prev) => now && !prev;

        static void Haptic(XRNode node, float amplitude, float duration)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;
            // channel 0
            if (device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }

        /// <summary>调试：列出当前已连接 XR 设备。</summary>
        public void LogDevices()
        {
            var list = new List<InputDevice>();
            InputDevices.GetDevices(list);
            foreach (var d in list)
                Debug.Log($"[Ctrl] Device: {d.name} chars={d.characteristics}");
        }
    }
}
