# 模块 B：PICO 空间应用

> 项目：Q（Cue）— 实时表达辅助系统
> 模块：B（PICO Unity 空间应用脚本）
> 代码路径：`Q/pico-unity/Assets/Q/Scripts/`
> 目标平台：Unity 2022.3+ / Unity 6 / .NET Standard 2.1 / PICO Unity Integration SDK
> 后端协议：`Q/backend/src/types.ts`（WebSocket `ws://localhost:3001/ws/session`）

---

## 一、架构总览

模块 B 是运行在 PICO 头显上的 Unity 空间应用，承担全部「感知 → 显示」的端侧职责。它通过 WebSocket 与后端（模块 A，`Q/backend`）通信，把后端下发的评分/钩子/转写/铸证/心率/物种等事件驱动为 PICO 空间中的实时渲染。

### 技术栈

- **PICO Unity Integration SDK**：`Unity.XR.PXR` 命名空间，提供 `PXR_Manager`（视频透视、手部追踪等）
- **PICO SecureMR C# API**：`Unity.XR.PXR.SecureMR` 命名空间，提供 `Provider`、`Pipeline`、`Tensor`、`Operator` 等混合现实推理管线
- **Newtonsoft.Json**：JSON 序列化/反序列化
- **TextMesh Pro (TMPro)**：空间文字渲染
- **System.Net.WebSockets**：`ClientWebSocket` 连接后端

### 系统架构图

```
PICO 头显（Unity 空间应用，模块 B）
  │
  ├── QSceneManager ──────── 场景配置：PXR_Manager 自动创建 + 功能开关
  │
  ├── QWebSocketClient ──────WebSocket────── 后端 /ws/session（模块 A）
  │     │  上行(9种)：audio / energy / ring / session_control / transcript
  │     │            / heart_rate / wallet_connect / audience_feedback / mint_credential
  │     │  下行(17种)：score / score_update / hook / recovery / segment_end / pace_update
  │     │              / session_started / session_ended / ring_feedback / asr_transcript
  │     │              / relay_status / error / heart_rate_update / wallet_status
  │     │              / wallet_connect_uri / species_update / credential_minted
  │     │
  │     └── 17 个 UnityEvent<T> → 各子系统订阅
  │
  ├── QMessageTypes ─────── C# struct/enum 定义，与 types.ts 完全对齐
  │     └── EnumConverter ─ 中文字符串 ↔ 枚举转换（HookType 开口/思路/衔接/节奏）
  │
  ├── SpatialHUDManager ──── World Space Canvas：评分/钩子/口头禅/心率/连接状态
  │     └── CountdownTimer ─ EMA 自适应倒计时（T1 递钩 → T2 ASR 开口 → 间隔 → 基准更新）
  │
  ├── FaceOcclusionManager ─ SecureMR 4 管线人脸检测 → 物种化身遮挡（遮脸减压）
  │     （4 条 Pipeline：VST → 推理 → 2D→3D → 渲染，基于 UFO 示例）
  │
  ├── SpeciesAvatarController ─ 单化身控制：物种类型 / 嘴巴动画 / 情绪切换冷却
  ├── SpeciesMapper ─── 关键词→物种映射（Inspector 可定制，7 种物种）
  │
  ├── BulletScreenManager ──── ASR 转写 → 从化身位置飘出 World Space TMP 弹幕
  │
  ├── WalletConnectPanel ──── WalletConnect 二维码面板
  ├── CredentialCardSpawner ─ Injective 凭证 3D 卡片 + 粒子动画
  │     └── CredentialCardView ─ 卡片正反面文本绑定
  │
  ├── HeartRateInputPanel ── World Space Canvas 数字键盘（手动心率 → 紧张度）
  │
  └── RingInputBridge ─────── 指环按钮 → 钩子动作映射 + 键盘回退
```

---

## 二、脚本清单与职责（13 个）

| # | 脚本 | 职责 | 关键类/方法 |
|---|------|------|-------------|
| 1 | `QMessageTypes.cs` | C# struct/enum，与后端 `types.ts` 完全对齐 | `AudioFrameMessage`...`CredentialMintedMessage` (9+17=26 消息) + `EnumConverter` |
| 2 | `QWebSocketClient.cs` | WebSocket 客户端，自动重连，后台线程接收 → 主线程分发 | `ConnectAsync()` / `ReceiveLoop()` / `DispatchMessage()` / `Send*()` (9 个) |
| 3 | `QSceneManager.cs` | 场景配置：PXR_Manager 自动创建 + 视频透视开启 | `ConfigurePICO()` |
| 4 | `SpatialHUDManager.cs` | 空间 HUD：评分/钩子/口头禅/心率/连接状态 | `OnScoreReceived()` / `OnHookReceived()` / `OnHeartRateUpdateReceived()` |
| 5 | `CountdownTimer.cs` | EMA 自适应倒计时（T1→T2→间隔→基准→下次时长） | `StartCountdown()` / `RecordUserOpening()` / `UpdateEmaBaseline()` |
| 6 | `FaceOcclusionManager.cs` | SecureMR 4 管线人脸检测 → 物种化身遮挡 | `InitializeSecureMR()` / `RunVstPipeline()` / `UpdateSpecies()` |
| 7 | `SpeciesAvatarController.cs` | 单化身：物种切换 + 嘴巴骨骼缩放动画 | `UpdateSpecies()` / `SetSpeakingEnergy()` / `SetPosition()` |
| 8 | `BulletScreenManager.cs` | 空间弹幕：TMP 文字漂浮 + 渐隐（对象池） | `SpawnBullet()` / `GetSpeakerPosition()` |
| 9 | `SpeciesMapper.cs` | 关键词→物种映射（7 种，Inspector 可定制） | `MapSpecies()` / `InferEmotion()` / `InferAndMap()` |
| 10 | `WalletConnectPanel.cs` | WalletConnect QR 面板 + 地址显示 | `OnWalletConnectUri()` / `ReportConnected()` |
| 11 | `CredentialCardSpawner.cs` | 凭证 3D 卡片 + 粒子 + 浮动旋转动画 | `SpawnCard()` / `CredentialCardView.SetData()` |
| 12 | `HeartRateInputPanel.cs` | 心率数字键盘 + 紧张度指示 + 快捷预设 | `OnDigitPressed()` / `OnSubmit()` / `ClassifyTension()` |
| 13 | `RingInputBridge.cs` | 指环命令→钩子动作映射 + 键盘回退 | `HandleRingCommand()` / `HandleConfirmRecovery()` / `HandleSwitchHook()` |

---

## 三、SecureMR 4 管线人脸遮挡架构

`FaceOcclusionManager.cs` 基于 PICO SecureMR C# API 实现，架构参考 [SecureMR-Samples UFO 示例](https://github.com/Pico-Developer/SecureMR-Samples/samples/ufo)。

SecureMR 的核心概念：
- **Provider**：应用与 SecureMR 服务的会话，负责创建 Pipeline 和 Tensor
- **Pipeline**：算子序列，按顺序执行多个 Operator
- **Tensor**：数据容器，有全局 Tensor（跨 Pipeline 共享）和局部 Tensor（Pipeline 内部）
- **Operator**：算子，如 `RectifiedVstAccessOperator`、`RunModelInferenceOperator` 等

### 3.1 四条 Pipeline 架构

```
Pipeline 1 (VST 管线)
  RectifiedVstAccessOperator → 左/右眼相机画面 + 时间戳 + 相机内参
  ArithmeticComposeOperator  → UInt8 → Float32 归一化 (÷255.0)
        ↓
Pipeline 2 (推理管线)
  RunModelInferenceOperator(MediaPipe Face Detection, QNN 格式)
  → face_anchor (896×16) + score (896) 边界框 + 置信度
  ArgmaxOperator → 最高置信度人脸
  AssignmentOperator + Slice → 提取 UV 坐标
  CustomizedCompareOperator → 置信度阈值(0.55) + UV 有效性过滤
  ElementwiseAndOperator + AllOperator → isFaceDetected
        ↓
Pipeline 3 (2D→3D 管线)
  UVTo3DInCameraSpaceOperator → 2D UV → 相机坐标系 3D 点
  ElementwiseMultiplyOperator → Y 轴翻转 (PICO Y↓ → OpenXR Y↑)
  ArithmeticComposeOperator   → 高度偏移 (化身悬浮在人脸上方)
  CameraSpaceToWorldOperator  → 相机坐标系 → OpenXR Local 世界坐标系
  ArithmeticComposeOperator   → 世界坐标 = 左眼变换 × 当前位置
        ↓
Pipeline 4 (渲染管线)
  ArithmeticComposeOperator → EMA 阻尼平滑 (prev×0.95 + curr×0.05)
  AssignmentOperator        → 更新 previousPosition
  SwitchGltfRenderStatusOperator → 在平滑位置渲染物种 glTF 化身
```

### 3.2 全局 Tensor 共享

| 全局 Tensor | 类型 | 用途 |
|------------|------|------|
| `globalLeftImageUint8` | `byte, Matrix (3, 256×256)` | 左眼 RGB 图像 (UInt8) |
| `globalRightImageUint8` | `byte, Matrix (3, 256×256)` | 右眼 RGB 图像 |
| `globalLeftImageFp32` | `float, Matrix (3, 256×256)` | 左眼图像 Float32（推理归一化用） |
| `globalTimestamp` | `int, Timestamp` | 相机时间戳 |
| `globalCameraMatrix` | `float, Matrix (1, 3×3)` | 相机内参矩阵 |
| `globalUv` | `int, Point (2, 1)` | 人脸 2D 中心坐标 |
| `globalIsFaceDetected` | `sbyte, Scalar (1, 1)` | 是否检测到人脸 |
| `globalCurrentPosition` | `float, Matrix (1, 4×4)` | 当前 3D 变换矩阵 |
| `globalPreviousPosition` | `float, Matrix (1, 4×4)` | 上一帧位置（阻尼用） |
| `globalGltfAsset` | `Gltf` | 物种化身 glTF 资产 |

### 3.3 物种化身动态切换

后端 `species_update` 消息 → `QSceneManager.OnSpeciesUpdate()` → `FaceOcclusionManager.UpdateSpecies(string species)`：

1. 检查物种是否变化（避免重复加载）
2. 从 `StreamingAssets/species/{species}.gltf` 加载 glTF 文件
3. 销毁旧的 `globalGltfAsset` Tensor
4. 创建新的 `globalGltfAsset = secureMRProvider.CreateTensor<Gltf>(gltfData)`
5. 渲染管线自动使用新的 glTF 资产

### 3.4 观众反馈

`FaceOcclusionManager` 定时（`feedbackInterval` 秒）向后端发送 `audience_feedback` 消息：
- 当前简化版：检测到人脸 = 专注，未检测到 = 走神
- 实际产品中可在推理管线中增加表情分类 Operator

---

## 四、数据流：WebSocket → 消息解析 → 空间渲染

### 4.1 线程模型

```
[后台线程]  ReceiveLoop()
              → ws.ReceiveAsync() 分帧读取
              → UTF-8 拼接完整 JSON
              → mainThreadQueue.Enqueue(() => DispatchMessage(json))
                         ↓
[主线程]    Update()
              → mainThreadQueue.TryDequeue(out Action)
              → DispatchMessage(json)
                 → JObject.Parse → type 字段路由
                 → JsonConvert.DeserializeObject<T>()
                 → OnScore?.Invoke(msg) / OnHook?.Invoke(msg) / ...
                         ↓
              各子系统事件回调 → Unity 渲染
```

关键设计：
- `ConcurrentQueue<Action>` 保证线程安全的跨线程分发
- 所有 Unity API（Transform/UI）只在主线程调用
- 发送使用 `async void SendJson<T>()` + `ConfigureAwait(false)`

### 4.2 下行消息 → 事件 → 渲染映射

| 后端消息 type | C# struct | C# UnityEvent | 渲染效果 |
|--------------|-----------|----------------|---------|
| `score` | `ScoreMessage` | `OnScore` | 评分面板：流畅/逻辑/语速/接收度 + 口头禅徽章 |
| `score_update` | `ScoreUpdateMessage` | `OnScoreUpdate` | 逻辑分 LLM 补丁 |
| `hook` | `HookMessage` | `OnHook` | 钩子面板：文本 + 类型标签 + 倒计时启动(T1) |
| `recovery` | `RecoveryMessage` | `OnRecovery` | 恢复确认 → 清除钩子面板 |
| `segment_end` | `SegmentEndMessage` | `OnSegmentEnd` | 段落汇总 |
| `pace_update` | `PaceUpdateMessage` | `OnPaceUpdate` | 语速评分更新 |
| `session_started` | `SessionStartMessage` | `OnSessionStart` | 会话激活 |
| `session_ended` | `SessionEndAckMessage` | `OnSessionEndAck` | 报告 URL 显示 |
| `ring_feedback` | `RingFeedbackMessage` | `OnRingFeedback` | 指环命令确认 → 钩子动作 |
| `asr_transcript` | `AsrTranscriptMessage` | `OnAsrTranscript` | ① 弹幕飘字 ② 记录 T2 更新 EMA |
| `relay_status` | `RelayStatusMessage` | `OnRelayStatus` | 中继状态显示 |
| `error` | `ErrorMessage` | `OnError` | 错误提示 |
| `heart_rate_update` | `HeartRateUpdateMessage` | `OnHeartRateUpdate` | 心率 + 紧张度标签显示 |
| `wallet_status` | `WalletStatusMessage` | `OnWalletStatus` | 钱包连接状态 + 地址 |
| `wallet_connect_uri` | `WalletConnectUriMessage` | `OnWalletConnectUri` | QR 码面板显示 |
| `species_update` | `SpeciesUpdateMessage` | `OnSpeciesUpdate` | ① 弹幕物种标签 ② 化身切换 |
| `credential_minted` | `CredentialMintedMessage` | `OnCredentialMinted` | 3D 凭证卡片 + 粒子 |

### 4.3 上行消息发送方法

| 操作 | C# 方法 | 消息 type |
|------|---------|-----------|
| 会话开始 | `SendSessionStart(userId, userName, walletAddress)` | `session_control` (action=start) |
| 会话结束 | `SendSessionEnd()` | `session_control` (action=end) |
| 音频帧 | `SendAudioFrame(byte[] pcmData)` | `audio` (base64 PCM + seq) |
| 能量报告 | `SendEnergy(ts, energy, isActive)` | `energy` |
| 指环命令 | `SendRingCommand(cmd, ts)` | `ring` |
| 心率输入 | `SendHeartRate(ts, bpm, userId, source)` | `heart_rate` |
| 钱包连接 | `SendWalletConnect(address, walletType, sessionId)` | `wallet_connect` (action=connect) |
| 钱包断开 | `SendWalletDisconnect()` | `wallet_connect` (action=disconnect) |
| 观众反馈 | `SendAudienceFeedback(ts, faceCount, attentive, distracted)` | `audience_feedback` |
| 手动铸证 | `SendMintCredential(milestone, metrics)` | `mint_credential` |
| 转写结果 | `SendTranscript(ts, text, isFinal)` | `transcript` |

### 4.4 重连机制

指数退避重连：
- 初始延迟 `initialReconnectDelay`（默认 1 秒）
- 每次失败后 `delay = min(delay × 2, maxReconnectDelay)`（默认上限 30 秒）
- 连接成功后重置延迟和重试计数
- `maxReconnectAttempts = -1` 表示无限重连

---

## 五、EMA 自适应递钩时序

`CountdownTimer.cs` 实现基于指数移动平均的自适应倒计时：

```
HookMessage 到达 → CountdownTimer.StartCountdown(ts, duration)
   ↓ T1 = 钩子推送时刻（服务器时间戳 + 本地 Time.time）
   ↓ 倒计时开始（后端指定 duration）

ASR 转写 isFinal=true → CountdownTimer.RecordUserOpening()
   ↓ T2 = 用户开口时刻（本地 Time.time）
   ↓ interval = T2 - T1
   ↓ EMA 更新：baseline = α·interval + (1-α)·baseline
   ↓ 采样数 +1

下次递钩 → CountdownTimer.GetAdaptiveDuration()
   ↓ 根据 paceScore 调整：
   ↓   pace < slowPaceThreshold(4.0) → duration × slowMultiplier(1.5)
   ↓   pace > fastPaceThreshold(7.0) → duration × fastMultiplier(0.7)
   ↓ 钳制到 [minBaseline, maxBaseline]
```

参数说明：
- `emaAlpha`（默认 0.15）：EMA 平滑系数，越大越偏向新样本
- `initialBaseline`（默认 5.0 秒）：首次采样前的初始基准
- `minBaseline` / `maxBaseline`（2-15 秒）：钳制范围
- `slowPaceThreshold` / `fastPaceThreshold`：pace score 分界线

---

## 六、物种映射逻辑

`SpeciesMapper.cs` 实现关键词→物种的本地轻量级推理：

### 6.1 映射规则（Inspector 可定制）

| 情绪关键词 | 情绪标签 | 物种 | SpeciesType 枚举 |
|-----------|---------|------|-----------------|
| 咄咄逼人, 强势, 压迫, 攻击性, 凶猛 | aggressive | 老虎 | `tiger` |
| 温和, 柔和, 平和, 善意, 友善 | gentle | 兔子 | `rabbit` |
| 缜密, 严密, 逻辑, 深思, 理性 | analytical | 猫头鹰 | `owl` |
| 活跃, 灵动, 敏捷, 机智, 幽默 | lively | 狐狸 | `fox` |
| 领导, 引领, 统帅, 主导, 号召 | dominant | 狮子 | `lion` |
| 突然强硬, 转为强硬, 骤然, 反转 | sudden_shift | 狼 | `wolf` |
| 中性, 平衡, 客观, 默认 | neutral | 鹿 | `deer` |

### 6.2 推理流程

```
InferEmotion(text)
  → 遍历 keywordMap（从 Inspector 规则构建）
  → 文本.IndexOf(keyword) 匹配
  → 统计各情绪命中次数 → 取最高
  → 置信度 = 命中次数 / 总匹配数

MapSpecies(speaker, emotion)
  → emotion.emotion 标签匹配 mappingRules
  → 返回 SpeciesType

InferAndMap(speaker, text)  ← 一步完成
  → InferEmotion → MapSpecies → 可选上报后端
```

---

## 七、PXR_Manager 场景配置

`QSceneManager.cs` 在 `Awake()` 中自动配置 PICO 环境：

```csharp
// 1. 确保 PXR_Manager 存在（不存在则自动创建 GameObject + PXR_Manager 组件）
var pxrManager = FindObjectOfType<PXR_Manager>();
if (pxrManager == null) {
    var go = new GameObject("PXR_Manager");
    pxrManager = go.AddComponent<PXR_Manager>();
}

// 2. 开启视频透视（SecureMR 前提）
PXR_Manager.EnableVideoSeeThrough = true;

// 3. 单例持久化
DontDestroyOnLoad(gameObject);
```

Inspector 功能开关：
- `enableVideoSeeThrough`：视频透视（SecureMR 前提，默认开）
- `enableHandTracking`：手势追踪
- `enableSpatialAnchor`：空间锚点
- `enableLateLatching`：延迟锁定
- `enableMSAA`：抗锯齿（4x MSAA）

---

## 八、Unity 项目搭建

### 8.1 SDK 导入

1. **Unity 2022.3 LTS** 或更高版本，安装时勾选：
   - Android Build Support + OpenJDK + SDK + NDK
   - TextMesh Pro（TMP Essential Resources）

2. **PICO Unity Integration SDK**：
   - 下载：https://developer-cn.picoxr.com/document/unity-integration/
   - 导入 Package：`Pico.Unity.Integration.SDK.unitypackage`
   - 验证：`Assets/Pico/` 目录存在，含 `PXR_Framework`、`SecureMR` 等子模块

3. **依赖包（Package Manager）**：
   - `com.unity.nuget.newtonsoft-json`（JSON 解析，`QMessageTypes` / `QWebSocketClient` 依赖）
   - `com.unity.xr.interaction.toolkit`（手部追踪射线交互，心率面板/卡片翻转）
   - `com.unity.textmeshpro`（空间文字）

4. **二维码库（WalletConnect 需要其一）**：
   - QRCoder（https://github.com/codebude/QRCoder）或 ZXing.Net
   - 在 `WalletConnectPanel.RenderQRCode()` 中接入

### 8.2 场景结构

主场景 `QScene.unity`：

```
QScene
├── XR Origin（PICO 头显，HMD + 手部追踪）
├── EventSystem（XR UI Input Module）
├── QSceneManager（根节点，场景配置）
├── Managers（空物体，挂全部管理器）
│   ├── QWebSocketClient
│   ├── SpatialHUDManager
│   ├── FaceOcclusionManager
│   ├── BulletScreenManager
│   ├── SpeciesMapper
│   ├── WalletConnectPanel
│   ├── CredentialCardSpawner
│   ├── HeartRateInputPanel
│   └── RingInputBridge
├── HUD Canvas（World Space Canvas，固定视野角落）
│   ├── ScorePanel（fluencyText / logicText / paceText / receptionText）
│   ├── HookPanel（hookText + hookTypeLabel + CountdownTimer）
│   ├── FillerBadge（fillerCountText + fillerBadgeBackground Image）
│   ├── HeartRatePanel（heartRateText + heartRateTensionLabel）
│   └── ConnectionIndicator（Image）
├── WalletConnect Canvas（World Space Canvas）
│   └── qrCodeImage + addressText + statusText
├── HeartRate Canvas（World Space Canvas）
│   ├── NumberButtons[0-9]
│   ├── SubmitButton / ClearButton / BackspaceButton
│   ├── Preset70Button / Preset120Button / Preset160Button
│   └── inputDisplayText + tensionLabelText + tensionIndicator
├── BulletContainer（弹幕实例容器）
├── CardContainer（凭证卡片容器）
└── AvatarsRoot（化身实例容器）
```

### 8.3 Prefab 制作

在 `Assets/Q/Prefabs/` 下制作：

| Prefab | 说明 | 关键组件 |
|--------|------|---------|
| `Tiger.prefab` | 老虎低模+骨骼 | `SkinnedMeshRenderer` + `Mouth` 骨骼 |
| `Rabbit.prefab` | 兔子低模+骨骼 | 同上 |
| `Owl.prefab` | 猫头鹰低模+骨骼 | 同上 |
| `Fox.prefab` | 狐狸低模+骨骼 | 同上 |
| `Lion.prefab` | 狮子低模+骨骼 | 同上 |
| `Wolf.prefab` | 狼低模+骨骼 | 同上 |
| `Deer.prefab` | 鹿低模+骨骼（默认） | 同上 |
| `BulletText.prefab` | 弹幕文字 | `TextMeshPro (World Space)` |
| `CredentialCard.prefab` | 凭证 3D 卡片 | `CredentialCardView` + TMP 子物体 |
| `MintParticle.prefab` | 铸造粒子 | `ParticleSystem`（爆发 2-3 秒） |

### 8.4 SecureMR StreamingAssets

在 `Assets/StreamingAssets/` 下放置：

| 文件 | 用途 |
|------|------|
| `facedetector_fp16_qnn229.bin` | MediaPipe 人脸检测模型（Qualcomm AI Hub QNN 格式） |
| `anchors_1.mat` | 人脸检测预定义锚点矩阵（896×4） |
| `species/tiger.gltf` | 老虎 glTF 物种化身 |
| `species/rabbit.gltf` | 兔子 glTF |
| `species/owl.gltf` | 猫头鹰 glTF |
| `species/fox.gltf` | 狐狸 glTF |
| `species/lion.gltf` | 狮子 glTF |
| `species/wolf.gltf` | 狼 glTF |
| `species/deer.gltf` | 鹿 glTF（默认） |

### 8.5 Inspector 绑定清单

1. **QWebSocketClient**：`serverUrl = ws://localhost:3001/ws/session`
2. **SpatialHUDManager**：绑定各 TMP_Text / Image 引用 + `countdownTimer` + `connectionIndicator`
3. **FaceOcclusionManager**：`faceDetectionModelPath`、`anchorMatrixPath` + 7 个物种 Prefab + 调试参数
4. **SpeciesAvatarController**：`speakerId` + 7 个物种 Prefab + `mouthBoneName`
5. **SpeciesMapper**：`mappingRules` 数组（7 条规则，Inspector 可自定义关键词）
6. **BulletScreenManager**：`bulletTextPrefab` + 弹幕参数
7. **WalletConnectPanel**：`qrCodeImage` + `addressText` + `statusText`
8. **CredentialCardSpawner**：`cardPrefab` + `mintParticlePrefab` + 位置参数
9. **HeartRateInputPanel**：数字按钮 0-9 + 操作按钮 + 预设按钮 + 文本引用
10. **RingInputBridge**：`hudManager` 引用 + 键盘映射 + `hookOrder` 数组

---

## 九、指环命令映射

`RingInputBridge.cs` 将指环命令映射到钩子交互动作：

| RingCommand | 动作 | 键盘回退 | 说明 |
|-------------|------|---------|------|
| `double_click` | 确认恢复 | Enter | 用户已响应钩子，主动恢复 |
| `single_click` | 忽略钩子 | Space | 关闭当前钩子面板 |
| `wave` | 切换钩子 | ↑ | 切换到下一类钩子 |
| `rotate_front` | 切换钩子 | → | 向前切换 |
| `rotate_back` | 切换钩子 | ← | 向后切换 |

钩子类型切换顺序（循环）：
`开口(KaiKou)` → `思路(SiLu)` → `衔接(XianJie)` → `节奏(JieZou)` → `开口` ...

---

## 十、已知限制与 TODO

### 10.1 SecureMR 模型与 glTF 资产
- `facedetector_fp16_qnn229.bin` 和 `anchors_1.mat` 需从 Qualcomm AI Hub 下载放入 StreamingAssets
- 7 种物种的 glTF 文件需美术制作或从开源 3D 模型库获取
- `globalGltfAsset` 的初始化当前留空（注释标注），需在 `InitializeSecureMR` 中补充 glTF 加载逻辑

### 10.2 二维码生成库
- `WalletConnectPanel.RenderQRCode()` 当前为占位实现（纯色方块）
- 需导入 QRCoder 或 ZXing.Net Unity Package 后替换为真实 QR 码生成
- 代码中已标注 QRCoder 和 ZXing.Net 的集成示例注释

### 10.3 麦克风采集层
- `QWebSocketClient.SendAudioFrame()` 承载上行音频帧，但采集层（PICO 麦克风 → Unity AudioCapture → base64 PCM）未实现
- 需新增 `MicrophoneCapture.cs`：`Microphone.Start` → `AudioClip.GetData` → base64 → `SendAudioFrame`
- 能量报告 `SendEnergy` 同理需 AudioContext 计算能量包络

### 10.4 指环 BLE 接入
- `RingInputBridge` 当前使用键盘回退（`enableKeyboardFallback = true`）
- 真实指环需 BLE 接入：指环 BLE NUS v4 → Android BLE 插件 → `AndroidJavaProxy` 回调 → `HandleRingCommand`
- 命令来源 A（后端 RingFeedbackMessage）已实现，来源 B（本地键盘）已实现

### 10.5 SecureMR Pipeline 执行细节
- `RunRenderPipeline()` 中 `ExecuteConditional` 的条件 tensor 参数（`globalIsFaceDetected`）需要确认 SDK API 的具体调用方式
- `CreateMap2dTo3dPipeline()` 中 `TransformOperator` 用 `AssignmentOperator` 替代，实际 SDK 如有专用算子应替换
- `RunVstPipeline()` 等管线执行中 `TensorMapping` 的具体 API 需与 SDK 版本匹配

### 10.6 观众反馈
- `FaceOcclusionManager.SendAudienceFeedbackToBackend()` 当前简化为：检测到人脸 = 专注
- 实际产品中需在推理管线中增加表情分类 Operator 区分专注/走神

### 10.7 物种化身 Prefab
- 7 个物种 Prefab 需美术制作（低模 + 骨骼 + 嘴巴动画）
- `SpeciesAvatarController.mouthBone` 需指向 Prefab 的下颌骨骼 Transform
- SecureMR 侧使用 glTF 渲染，Unity 侧 Prefab 作为弹幕定位参考

---

## 十一、文件索引

```
Q/pico-unity/Assets/Q/Scripts/
├── QMessageTypes.cs          # WS 消息类型（26 种，与 types.ts 对齐）+ EnumConverter
├── QWebSocketClient.cs        # WebSocket 客户端 + 重连 + 17 事件 + 9 发送方法
├── QSceneManager.cs           # 场景配置（PXR_Manager 自动创建）
├── SpatialHUDManager.cs       # 空间 HUD 总管（评分/钩子/口头禅/心率/连接）
├── CountdownTimer.cs          # EMA 自适应倒计时
├── FaceOcclusionManager.cs    # SecureMR 4 管线人脸遮挡 + 物种化身
├── SpeciesAvatarController.cs  # 单化身控制（物种/嘴巴/情绪冷却）
├── SpeciesMapper.cs           # 关键词→物种映射（7 种，Inspector 可定制）
├── BulletScreenManager.cs     # 空间弹幕（TMP 漂浮 + 渐隐 + 对象池）
├── WalletConnectPanel.cs      # WalletConnect QR 面板
├── CredentialCardSpawner.cs   # 凭证 3D 卡片 + 粒子 + CredentialCardView
├── HeartRateInputPanel.cs     # 心率数字键盘 + 紧张度 + 预设
└── RingInputBridge.cs          # 指环→钩子动作映射 + 键盘回退
```
