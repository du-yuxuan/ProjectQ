// ============================================================
// RingBleManager.cs
// Q (Cue) — 指环 BLE 原生管理器（集成在 Unity 应用内）
//
// 使用 AndroidJavaObject 调用 RingBleHelper.java（原生插件），
// 通过 AndroidJavaProxy 实现 RingBleHelper.Callback 接口。
//
// 数据流：指环 BLE 通知 → RingBleHelper(PacketStream 重组) → C# 回调
//   → 解析 v4 命令 → RingInputBridge / QWebSocketClient
//
// 命令映射：
//   0x0701 双击 → RingCommand.double_click（确认恢复）
//   0x0702 手势 → RingCommand.wave（切换钩子）
//   0x0703 按键双击 → RingCommand.double_click
//   0x0704 按键单击 → RingCommand.single_click（忽略钩子）
//   0x0605 IMU 数据 → 微颤检测（紧张度信号）
// ============================================================

using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Events;

namespace Q.Pico
{
    /// <summary>
    /// 指环 BLE 原生管理器 — 直接在 Unity 应用内连接 Zilo 指环。
    /// 无需独立 App，无需后端中转。
    /// </summary>
    public class RingBleManager : MonoBehaviour
    {
        // ===== v4 命令号（与 RingProtocol.kt 一致）=====
        private const int CMD_EVT_DOUBLE_TAP = 0x0701;
        private const int CMD_EVT_GESTURE     = 0x0702;
        private const int CMD_EVT_KEY_DOUBLE  = 0x0703;
        private const int CMD_EVT_KEY_SINGLE  = 0x0704;
        private const int CMD_SENSOR_DATA     = 0x0605;
        private const int CMD_SENSOR_START    = 0x0601;
        private const int CMD_SENSOR_STOP     = 0x0603;

        // ===== 状态枚举 =====
        public enum BleState { Disconnected = 0, Scanning = 1, Connecting = 2, Connected = 3, NusReady = 4 }

        [Header("自动连接")]
        [Tooltip("启动时自动开始扫描")]
        public bool autoScanOnStart = true;
        [Tooltip("自动开启 IMU（微颤检测）")]
        public bool autoStartImu = true;

        [Header("引用")]
        [Tooltip("WebSocket 客户端（用于上报指环命令）")]
        public QWebSocketClient wsClient;
        [Tooltip("指环输入桥接（用于本地钩子操作）")]
        public RingInputBridge ringBridge;

        [Header("事件")]
        public UnityEvent<BleState> OnStateChanged = new UnityEvent<BleState>();
        public UnityEvent<RingCommand> OnRingCommand = new UnityEvent<RingCommand>();
        public UnityEvent<string> OnLog = new UnityEvent<string>();

        // ===== 内部状态 =====
        private AndroidJavaObject bleHelper;
        private RingBleCallbackProxy callbackProxy;
        private BleState currentState = BleState.Disconnected;
        private bool isImuStarted = false;

        // 主线程分发队列
        internal readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

        // ============================================================
        // Unity 生命周期
        // ============================================================

        void Start()
        {
            if (wsClient == null) wsClient = FindObjectOfType<QWebSocketClient>();
            if (ringBridge == null) ringBridge = FindObjectOfType<RingInputBridge>();

#if UNITY_ANDROID && !UNITY_EDITOR
            InitNative();
            if (autoScanOnStart) StartScan();
#else
            Debug.Log("[RingBLE] 非 Android 平台，BLE 不可用（使用键盘回退）");
#endif
        }

        void Update()
        {
            // 在主线程执行 Android 回调
            while (mainThreadQueue.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }

        void OnDestroy()
        {
            Disconnect();
        }

        // ============================================================
        // 原生初始化
        // ============================================================

        private void InitNative()
        {
            try
            {
                // 获取 Unity 当前 Activity（PICO Unity 应用运行的 Android 上下文）
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                // 创建 C# 回调代理（实现 RingBleHelper.Callback 接口）
                callbackProxy = new RingBleCallbackProxy(this);

                // 使用构造函数创建 RingBleHelper Java 实例
                // （旧代码先 GetStatic("@nullptr") 占位再 new，该占位符在 PICO 上抛 NoSuchFieldError，
                //  虽被 catch 住但使 bleHelper=null，后续 StartScan 被跳过 —— 直接用构造函数即可）
                bleHelper = new AndroidJavaObject("com.q.cue.ble.RingBleHelper",
                    activity, callbackProxy);

                Debug.Log("[RingBLE] 原生 RingBleHelper 已初始化");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RingBLE] 初始化失败: {e.Message}");
            }
        }

        // ============================================================
        // 公共 API
        // ============================================================

        /// <summary>开始 BLE 扫描寻找指环</summary>
        public void StartScan()
        {
            if (bleHelper == null)
            {
                Debug.LogWarning("[RingBLE] 未初始化，跳过扫描");
                return;
            }
            try
            {
                bleHelper.Call("startScan");
                Debug.Log("[RingBLE] 开始扫描...");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RingBLE] 扫描启动失败: {e.Message}");
            }
        }

        /// <summary>手动连接指定 MAC 地址</summary>
        public void Connect(string mac)
        {
            if (bleHelper == null) return;
            try { bleHelper.Call("connectByMac", mac); }
            catch (Exception e) { Debug.LogError($"[RingBLE] 连接失败: {e.Message}"); }
        }

        /// <summary>断开连接</summary>
        public void Disconnect()
        {
            if (bleHelper == null) return;
            try { bleHelper.Call("disconnect"); }
            catch (Exception e) { Debug.LogError($"[RingBLE] 断开失败: {e.Message}"); }
        }

        /// <summary>是否已连接（NUS 就绪）</summary>
        public bool IsConnected()
        {
            if (bleHelper == null) return false;
            try { return bleHelper.Call<bool>("isConnected"); }
            catch { return false; }
        }

        /// <summary>发送 v4 协议命令到指环</summary>
        public void SendCommand(int command, byte[] body = null)
        {
            if (bleHelper == null || !IsConnected()) return;
            try { bleHelper.Call("send", command, body ?? new byte[0]); }
            catch (Exception e) { Debug.LogError($"[RingBLE] 发送失败: {e.Message}"); }
        }

        /// <summary>开启 IMU 传感器（微颤检测）</summary>
        public void StartImu()
        {
            if (!IsConnected() || isImuStarted) return;
            SendCommand(CMD_SENSOR_START);
            isImuStarted = true;
            Debug.Log("[RingBLE] IMU 已开启");
        }

        /// <summary>停止 IMU</summary>
        public void StopImu()
        {
            if (!IsConnected() || !isImuStarted) return;
            SendCommand(CMD_SENSOR_STOP);
            isImuStarted = false;
        }

        // ============================================================
        // 回调处理（由 RingBleCallbackProxy 在 Android 线程调用）
        // ============================================================

        internal void HandleStateChanged(int state)
        {
            mainThreadQueue.Enqueue(() =>
            {
                currentState = (BleState)state;
                OnStateChanged?.Invoke(currentState);

                string stateName = currentState switch
                {
                    BleState.Disconnected => "已断开",
                    BleState.Scanning => "扫描中",
                    BleState.Connecting => "连接中",
                    BleState.Connected => "已连接",
                    BleState.NusReady => "NUS就绪",
                    _ => "未知"
                };
                Debug.Log($"[RingBLE] 状态: {stateName}");

                // NUS 就绪后自动开启 IMU
                if (currentState == BleState.NusReady && autoStartImu)
                {
                    StartImu();
                }
            });
        }

        internal void HandleScanResult(string name, string mac, int rssi)
        {
            mainThreadQueue.Enqueue(() =>
            {
                Debug.Log($"[RingBLE] 发现: {name} [{mac}] RSSI={rssi}");
            });
        }

        internal void HandlePacket(int command, byte[] body)
        {
            mainThreadQueue.Enqueue(() =>
            {
                Debug.Log($"[RingBLE] 收到命令: 0x{command:X4} ({body?.Length ?? 0}B)");

                // 解析 v4 命令 → RingCommand
                RingCommand? ringCmd = null;
                switch (command)
                {
                    case CMD_EVT_DOUBLE_TAP:
                    case CMD_EVT_KEY_DOUBLE:
                        ringCmd = RingCommand.double_click;
                        break;
                    case CMD_EVT_KEY_SINGLE:
                        ringCmd = RingCommand.single_click;
                        break;
                    case CMD_EVT_GESTURE:
                        ringCmd = RingCommand.wave;
                        break;
                    case CMD_SENSOR_DATA:
                        HandleImuData(body);
                        break;
                    default:
                        Debug.Log($"[RingBLE] 未处理的命令: 0x{command:X4}");
                        break;
                }

                if (ringCmd.HasValue)
                {
                    OnRingCommand?.Invoke(ringCmd.Value);

                    // 上报到后端（WebSocket）
                    if (wsClient != null && wsClient.IsConnected)
                    {
                        double ts = QWebSocketClient.GetTimestamp();
                        wsClient.SendRingCommand(ringCmd.Value, ts);
                    }
                }
            });
        }

        /// <summary>处理 IMU 数据（微颤检测）</summary>
        private void HandleImuData(byte[] body)
        {
            if (body == null || body.Length < 16) return;

            // v4 SensorBatch 格式: seq(u32) | count(u16) | sample_size(u16) | [ts(u32) ax(i16) ay(i16) az(i16) gx(i16) gy(i16) gz(i16)]...
            // 每个样本 16 字节
            try
            {
                int pos = 0;
                // 跳过 error code (u16)
                pos = 2;
                long seq = ReadU32(body, ref pos);
                int count = ReadU16(body, ref pos);
                int sampleSize = ReadU16(body, ref pos);

                for (int i = 0; i < count && pos + 16 <= body.Length; i++)
                {
                    long ts = ReadU32(body, ref pos);
                    int ax = ReadI16(body, ref pos);
                    int ay = ReadI16(body, ref pos);
                    int az = ReadI16(body, ref pos);
                    int gx = ReadI16(body, ref pos);
                    int gy = ReadI16(body, ref pos);
                    int gz = ReadI16(body, ref pos);

                    // 微颤检测：加速度变化量（|a - gravity|）
                    float accelMag = Mathf.Sqrt(ax * ax + ay * ay + az * az);
                    // TODO: 累积微颤信号 → 紧张度估算
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RingBLE] IMU 数据解析失败: {e.Message}");
            }
        }

        // 大端序读取工具
        private static long ReadU32(byte[] d, ref int p) {
            long v = ((d[p] & 0xFFL) << 24) | ((d[p+1] & 0xFFL) << 16) | ((d[p+2] & 0xFFL) << 8) | (d[p+3] & 0xFFL);
            p += 4; return v;
        }
        private static int ReadU16(byte[] d, ref int p) {
            int v = ((d[p] & 0xFF) << 8) | (d[p+1] & 0xFF); p += 2; return v;
        }
        private static int ReadI16(byte[] d, ref int p) {
            int v = ReadU16(d, ref p); return v >= 0x8000 ? v - 0x10000 : v;
        }

        public BleState CurrentState => currentState;
    }

    // ============================================================
    // AndroidJavaProxy — 实现 RingBleHelper.Callback 接口
    // ============================================================

    /// <summary>
    /// C# 代理类，实现 Java 的 RingBleHelper.Callback 接口。
    /// Java 侧调用回调方法时，Unity 会在 Android 线程上触发对应的 C# 方法。
    /// </summary>
    public class RingBleCallbackProxy : AndroidJavaProxy
    {
        private readonly RingBleManager manager;

        public RingBleCallbackProxy(RingBleManager manager) : base("com.q.cue.ble.RingBleHelper$Callback")
        {
            this.manager = manager;
        }

        // Java 接口方法实现（方法名必须与 Java 接口完全一致）
        public void onStateChanged(int state)
        {
            manager.HandleStateChanged(state);
        }

        public void onScanResult(string name, string mac, int rssi)
        {
            manager.HandleScanResult(name, mac, rssi);
        }

        public void onPacket(int command, byte[] body)
        {
            manager.HandlePacket(command, body);
        }

        public void onLog(string message)
        {
            manager.mainThreadQueue.Enqueue(() =>
            {
                manager.OnLog?.Invoke(message);
            });
        }
    }
}
