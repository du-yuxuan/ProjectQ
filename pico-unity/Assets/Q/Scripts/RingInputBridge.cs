// ============================================================
// RingInputBridge.cs
// Q (Cue) — 指环命令 → 钩子动作映射桥
// ============================================================

using UnityEngine;
using UnityEngine.Events;

namespace Q.Pico
{
    public class RingInputBridge : MonoBehaviour
    {
        [Header("引用")]
        public SpatialHUDManager hudManager;
        public RingBleManager ringBleManager;

        [Header("键盘回退（PicoControllerInput 接管后可关闭）")]
        public bool enableKeyboardFallback = false;
        public KeyCode confirmKey = KeyCode.Return;
        public KeyCode dismissKey = KeyCode.Space;
        public KeyCode nextHookKey = KeyCode.RightArrow;
        public KeyCode prevHookKey = KeyCode.LeftArrow;
        public KeyCode waveKey = KeyCode.UpArrow;

        [Header("钩子类型切换顺序")]
        public HookType[] hookOrder = new HookType[]
        {
            HookType.KaiKou,
            HookType.SiLu,
            HookType.XianJie,
            HookType.JieZou
        };

        [Header("事件")]
        public UnityEvent OnConfirmRecovery = new UnityEvent();
        public UnityEvent OnDismissHook = new UnityEvent();
        public UnityEvent<HookType> OnSwitchHook = new UnityEvent<HookType>();

        [Header("调试")]
        public bool debugLog = false;

        private QWebSocketClient wsClient;
        private bool hasActiveHook;
        private HookType activeHookType = HookType.KaiKou;
        private int currentHookIndex;
        private bool bleSubscribed;

        public bool HasActiveHook => hasActiveHook;
        public HookType ActiveHookType => activeHookType;
        public int CurrentHookIndex => currentHookIndex;

        /// <summary>
        /// 供 PicoControllerInput / 外部模拟指环命令。
        /// </summary>
        public void SimulateCommand(RingCommand cmd)
        {
            HandleRingCommand(cmd, fromKeyboard: true, reportToBackend: true);
        }

        void Start()
        {
            wsClient = FindObjectOfType<QWebSocketClient>();
            if (wsClient != null)
            {
                wsClient.OnRingFeedback.AddListener(OnRingFeedback);
                wsClient.OnHook.AddListener(OnHookReceived);
                wsClient.OnRecovery.AddListener(OnRecoveryReceived);
            }
            else
            {
                Debug.LogWarning("[RingBridge] QWebSocketClient 未找到");
            }

            if (hudManager == null)
                hudManager = FindObjectOfType<SpatialHUDManager>();

            TryBindBle();
        }

        void Update()
        {
            if (!bleSubscribed) TryBindBle();

            if (!enableKeyboardFallback) return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb[UnityEngine.InputSystem.Key.Enter].wasPressedThisFrame ||
                kb[UnityEngine.InputSystem.Key.NumpadEnter].wasPressedThisFrame)
                HandleRingCommand(RingCommand.double_click, fromKeyboard: true);
            else if (kb[UnityEngine.InputSystem.Key.Space].wasPressedThisFrame)
                HandleRingCommand(RingCommand.single_click, fromKeyboard: true);
            else if (kb[UnityEngine.InputSystem.Key.RightArrow].wasPressedThisFrame)
                HandleRingCommand(RingCommand.rotate_front, fromKeyboard: true);
            else if (kb[UnityEngine.InputSystem.Key.LeftArrow].wasPressedThisFrame)
                HandleRingCommand(RingCommand.rotate_back, fromKeyboard: true);
            else if (kb[UnityEngine.InputSystem.Key.UpArrow].wasPressedThisFrame)
                HandleRingCommand(RingCommand.wave, fromKeyboard: true);
#else
            if (Input.GetKeyDown(confirmKey))
                HandleRingCommand(RingCommand.double_click, fromKeyboard: true);
            else if (Input.GetKeyDown(dismissKey))
                HandleRingCommand(RingCommand.single_click, fromKeyboard: true);
            else if (Input.GetKeyDown(nextHookKey))
                HandleRingCommand(RingCommand.rotate_front, fromKeyboard: true);
            else if (Input.GetKeyDown(prevHookKey))
                HandleRingCommand(RingCommand.rotate_back, fromKeyboard: true);
            else if (Input.GetKeyDown(waveKey))
                HandleRingCommand(RingCommand.wave, fromKeyboard: true);
#endif
        }

        void OnDestroy()
        {
            if (wsClient != null)
            {
                wsClient.OnRingFeedback.RemoveListener(OnRingFeedback);
                wsClient.OnHook.RemoveListener(OnHookReceived);
                wsClient.OnRecovery.RemoveListener(OnRecoveryReceived);
            }
        }

        void TryBindBle()
        {
            if (bleSubscribed) return;
            ringBleManager = ringBleManager != null ? ringBleManager : FindObjectOfType<RingBleManager>();
            if (ringBleManager == null) return;

            ringBleManager.OnRingCommand.AddListener((cmd) =>
            {
                HandleRingCommand(cmd, fromKeyboard: false);
                if (debugLog) Debug.Log($"[RingBridge] BLE 直连命令: {cmd}");
            });
            bleSubscribed = true;
            if (debugLog) Debug.Log("[RingBridge] 已连接原生 RingBleManager");
        }

        void OnRingFeedback(RingFeedbackMessage msg)
        {
            if (!msg.acknowledged)
            {
                if (debugLog) Debug.LogWarning("[RingBridge] 指环命令未被后端确认");
                return;
            }
            // 后端 ack 的命令通常已在本地处理过；避免键盘路径重复上报时二次触发
            // 仅当 hasActiveHook 状态仍匹配时执行
            HandleRingCommand(msg.Cmd, fromKeyboard: false, reportToBackend: false);
        }

        void OnHookReceived(HookMessage msg)
        {
            hasActiveHook = true;
            activeHookType = msg.HookType;
            for (int i = 0; i < hookOrder.Length; i++)
            {
                if (hookOrder[i] == activeHookType)
                {
                    currentHookIndex = i;
                    break;
                }
            }
            if (debugLog)
                Debug.Log($"[RingBridge] 钩子激活: {msg.hookType} (index={currentHookIndex})");
        }

        void OnRecoveryReceived(RecoveryMessage msg)
        {
            hasActiveHook = false;
        }

        void HandleRingCommand(RingCommand cmd, bool fromKeyboard, bool reportToBackend = true)
        {
            switch (cmd)
            {
                case RingCommand.double_click:
                    HandleConfirmRecovery();
                    break;
                case RingCommand.single_click:
                    HandleDismissHook();
                    break;
                case RingCommand.wave:
                case RingCommand.rotate_front:
                    HandleSwitchHook(forward: true);
                    break;
                case RingCommand.rotate_back:
                    HandleSwitchHook(forward: false);
                    break;
                default:
                    if (debugLog) Debug.LogWarning($"[RingBridge] 未处理的命令: {cmd}");
                    break;
            }

            if (reportToBackend && fromKeyboard && wsClient != null && wsClient.IsConnected)
                wsClient.SendRingCommand(cmd, QWebSocketClient.GetTimestamp());
        }

        void HandleConfirmRecovery()
        {
            if (!hasActiveHook)
            {
                if (debugLog) Debug.Log("[RingBridge] 确认恢复：当前无激活钩子");
                return;
            }

            hasActiveHook = false;
            OnConfirmRecovery?.Invoke();
            // 若无人订阅事件，直接驱动 HUD
            if (hudManager != null)
                hudManager.HideHookPanel();

            if (debugLog) Debug.Log("[RingBridge] → 确认恢复（double_click）");
        }

        void HandleDismissHook()
        {
            if (!hasActiveHook)
            {
                if (debugLog) Debug.Log("[RingBridge] 忽略钩子：当前无激活钩子");
                return;
            }

            hasActiveHook = false;
            OnDismissHook?.Invoke();
            if (hudManager != null)
                hudManager.HideHookPanel();

            if (debugLog) Debug.Log("[RingBridge] → 忽略钩子（single_click）");
        }

        void HandleSwitchHook(bool forward)
        {
            if (hookOrder == null || hookOrder.Length == 0) return;

            int newIndex = forward
                ? (currentHookIndex + 1) % hookOrder.Length
                : (currentHookIndex - 1 + hookOrder.Length) % hookOrder.Length;

            currentHookIndex = newIndex;
            HookType newHookType = hookOrder[currentHookIndex];
            activeHookType = newHookType;
            OnSwitchHook?.Invoke(newHookType);

            if (debugLog)
                Debug.Log($"[RingBridge] → 切换钩子: {EnumConverter.ToHookTypeString(newHookType)} (index={newIndex})");
        }
    }
}
