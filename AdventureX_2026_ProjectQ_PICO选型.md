# Q · PICO 技术方案选型（基于官方文档核实 + 现场通告修订）

> 项目：Q（Cue）
> 版本：v2（2026-07-25 现场通告后修订）
> 数据来源：https://developer.picoxr.com/zh/document/ 与 https://developer-cn.picoxr.com/document/spatial-sdk/
> 提取日期：2026-07-25
> 关联文档：`AdventureX_2026_ProjectQ_技术实现.md`（v17）、`赛道提交/13_PICO_EscapeRectangle/提交.md`

---

## 〇、一句话结论

> **选 PICO Spatial SDK（Android / Kotlin 原生，跑在 OS 6 / Project Swan），对应「Android 空间应用创作」子赛道。**
> 核心卖点人脸遮挡依赖的 **SpatialML 是 Spatial SDK 独有能力、Kotlin 原生调用**，Unity 路线拿不到相机+NPU 的 ML Pipeline。开发与演示用官方 **Spatial Plugin + Spatial Editor + PICO Emulator**（Android Studio 内一站式，PC/Mac 均可），**全程模拟器**——这已被组织方明确背书：模拟器出片不影响评审。
> 现实约束：**Project Swan 未发布，现场借到的 PICO 4/4U 跑旧 OS、跑不了 OS 6 空间应用/SpatialML，故 SpatialML 无真机兜底**。演示主线锚定模拟器稳定的「物种化身+空间弹幕」围观层，人脸遮挡作为 R1 通过后的加分项。

---

## 一、现场通告关键事实（2026-07-25 组织方）

> 来源：AdventureX PICO 赛道群通告。直接影响选型与风险结构。

1. **赛道放宽为三子赛道**，任意 PICO SDK 均可参评：
   - Android 空间应用创作 — Kotlin/Compose/Android Spatial（**我们的落点**）
   - Web 空间应用或沉浸体验创作 — WebSpatial 或 WebXR
   - Unity 沉浸体验创作 — 任意 Unity，模拟器或实机均可
2. **设备现状**：借到 **3 台 PICO 4U / PICO 4**（数量极有限），**仅给 WebXR / Unity OpenXR 项目做实机调优**。
3. **Spatial 路线强制模拟器**：使用 Android Spatial Plugin / WebSpatial / Unity Spatial 模式的同学，「**因为最新的机器（Project Swan）还没发布，目前只能通过 PICO Emulator** 进行测试、调优与路演」。
4. **模拟器不被扣分**：「无论用哪种技术栈、无论用 PICO 4/4U 实机或 Emulator，只要能让项目跑起来，都不影响 PICO 赛道及最终主题评审」。
5. **Project Swan 新机**：8 月 ChinaJoy 闭门会定向邀请 AdvX 选手体验，差旅全额报销（关系/认可角度，非技术）。

> 对本项目的净影响：选型有效且落在命名赛道；模拟器演示被背书；但 **SpatialML 失去真机兜底，R1 成为硬二选一**。

---

## 二、官方文档核实结论（已逐条对齐）

PICO 当前 SDK 与平台全景（官方页面原文核实）：

| 平台 / 设备 | 对应 SDK | 技术栈 | 含 SpatialML | 现场可实机 |
| --- | --- | --- | --- | --- |
| **OS 6 / Project Swan / PICO 4 Ultra 系列**（空间应用） | **PICO Spatial SDK** | **Android 原生（Kotlin + Jetpack Compose + Android Studio）** | **✅** | ❌（Swan 未发布，仅 Emulator） |
| PICO 4 / 4 Pro / Neo 3（传统 VR） | Unity Integration SDK | Unity | ❌ | ✅（借到的 4/4U 属此列） |
| 跨引擎传统 VR | Unity OpenXR SDK / OpenXR SDK | Unity + OpenXR | ❌ | ✅ |
| 跨平台空间 Web | WebSpatial | Web（Chromium/WebXR/WebGPU），多端运行 | ❌ | ❌（Spatial 模式仅 Emulator） |
| UE 路线 | Unreal SDK / Unreal OpenXR Plugin | Unreal | ❌ | 部分 |

**SpatialML 官方原文（核实通过）**：

- 「**基于相机透视数据**部署端侧机器学习模型的框架，**开放 DSP 处理器**」
- 「Pipeline 中部署机器学习模型并利用 **Qualcomm NPU** 加速推理」
- 「Pipeline 中通过 **JavaScript** 添加自定义运算」
- 「**使用 Kotlin 协程异步调用 SpatialML**」
- 构建链：「**Gradle、AGP、Kotlin**」「**Android Studio**」「Android Studio Profiler」
- 官方示例：「基于 SpatialML 实现一个**超级分辨率相机**应用」（Session→Pipeline→NPU→渲染→读回）
- Spatial SDK 文档版本 **0.13.x**（`技术实现.md` v17 的 v0.11.x 已过时，需更新）

> 结论：SpatialML 是 Spatial SDK（Android/Kotlin 原生）独有，Unity 路线拿不到。这是选型第一硬约束。

---

## 三、现有文档的两处矛盾（需统一修正）

| # | 矛盾点 | 现状 | 正确结论 |
| --- | --- | --- | --- |
| 1 | SDK 口径不一致 | `提交.md` 写「Unity Integration SDK + Simulator」；`技术实现.md` v17 写「Spatial SDK + SpatialML」却用 Unity API | **统一为 PICO Spatial SDK 0.13.x（Android/Kotlin）**。Unity Integration SDK 是 Neo3/PICO4 传统 VR，不含 SpatialML；HUD 用 **Spatial UI（Jetpack Compose）** 而非 Unity World Space Canvas |
| 2 | PICO 卖点层不一致 | `提交.md` 主打围观层（易出片）；`技术实现.md` 主打 SpatialML 人脸遮挡（吃相机+NPU） | 两者均为 Spatial SDK 能力，但模拟器可行性不同，应分层排优先级（见第五节） |

> 术语修正：官方工具套件是 **Spatial Plugin + Spatial Editor + PICO Emulator**，在 **Android Studio** 内开发；无「PICO Unity 模拟器」叫法。

---

## 四、选定技术栈

| 层 | 选定方案 | 官方依据 |
| --- | --- | --- |
| SDK | **PICO Spatial SDK 0.13.x**（Android/Kotlin） | 「专为开发空间应用而设计」 |
| 开发环境 | **Android Studio** + Spatial Plugin + Spatial Editor | 「在 Android Studio 一站式完成开发」 |
| 测试/演示 | **PICO Emulator**（PC/Mac，全程模拟器，官方背书） | 「完全模拟 OS 6」「仅支持 Project Swan」 |
| HUD / UI | **PICO Spatial UI**（Jetpack Compose 声明式 + 组件库 + Figma） | 「基于 Jetpack Compose 的声明式 UI」 |
| 人脸遮挡 | **SpatialML**：Session + Pipeline(NPU) + JS 运算 + 渲染 + 读回 | 「超级分辨率相机」示例同人脸检测流程 |
| 3D 物种化身 | 空间锚点 + 3D 渲染 + Spatial Plugin ECS | 「空间锚点」「Volumetric 组合模型/手势/ECS」 |
| 音频采集 | Android `AudioRecord`（麦克风） | PICO 内置麦克风 + Android 音频 API |
| 空间音频 | Spatial SDK 空间音频 | 「空间音频」核心示例之一 |
| 指环 BLE | Android BLE（或 WiFi 转发兜底） | PICO 跑 Android，可用标准 BLE |
| 上链二维码 | Spatial UI 渲染图片 | Spatial UI 组件 + 渲染 |

---

## 五、功能 × 能力 × 模拟器可行性矩阵

> 现实约束：**Spatial 路线全程模拟器**（Project Swan 未发布，借到的 PICO 4/4U 跑旧 OS 不支持 OS 6/SpatialML）。

| 功能 | 用到的能力 | 模拟器可行性 | 说明 |
| --- | --- | --- | --- |
| 麦克风采集 -> 讯飞 RTASR | Android `AudioRecord` + WebSocket | ✅ 高 | 模拟器可注入/直连麦克风 |
| 空间显示 HUD（效果分/递钩≤5字/倒计时/口头禅/能力画像） | **Spatial UI（Compose）** | ✅ 高 | 组件化 UI，最稳 |
| WalletConnect 二维码 | Spatial UI 显示图片 | ✅ 高 | 渲染二维码位图 |
| 心率输入面板（手动） | Spatial UI + 手部追踪射线 | ✅ 高 | 数字键盘 + ray |
| **物种化身（3D）** | 空间锚点 + 3D 渲染 + ECS | ✅ 中高 | 锚定方位，跟随移动 |
| **空间弹幕（围观层·Escape the Rectangle）** | 空间锚点 + 文字渲染 + 空间音频 | ✅ 高 | **最易出片，演示主线** |
| **人脸遮挡（SpatialML 人脸检测）** | **SpatialML Session + 相机数据 + NPU** | ⚠️ **中（硬二选一，无真机兜底）** | 依赖模拟器是否喂相机/透视帧 |
| 观众反馈（专注/走神） | 复用 SpatialML + 后处理 | ⚠️ 中 | 同上，复用同一 Pipeline |
| 指环 BLE（IMU/按钮/心率） | Android BLE | ⚠️ 中 | 模拟器无 BLE，需 WiFi 转发或模拟输入 |

**结论**：6 项高可行、2 项中高、1 项（人脸遮挡）**硬二选一**。**演示主线锚定「空间弹幕+物种化身+效果分 HUD」，人脸遮挡作为 R1 通过后的加分项**。

---

## 六、核心风险与验证计划

### R1（最高·硬二选一）：模拟器是否向 SpatialML 提供相机/透视数据

- **变更**：原方案有「真机兜底」。现 **Project Swan 未发布，借到的 PICO 4/4U 跑旧 OS、不支持 OS 6/SpatialML，无真机救场**。R1 变为硬二选一。
- **影响**：能跑 = 演人脸遮挡；不能跑 = 这功能本现场砍掉，围观层不受影响。
- **验证动作（第一天完成）**：
  1. 按 SpatialML「超级分辨率相机」官方示例在 **Android Studio + Spatial Plugin + Emulator** 跑通；
  2. 看 SpatialML Session 取相机帧是否返回有效数据（非空帧）；
  3. 若空/报错：排查「注入视频文件/虚拟相机」替代输入（文档提到 `视频纹理`、`VideoTextureSampleMode`、`DisplayMode`，疑似支持视频帧作相机替代源）；
  4. 二选一：✅ 可演 / ❌ 降级（围观层先行）。
- **利好**：即使纯模拟器演示，组织方明确「不影响评审」，**R1 失败≠赛道出局**，只丢一个加分项。

### R2：SpatialML NPU 推理性能（多人脸）
- 模拟器无真 NPU，可能 CPU 模拟推理，多人脸帧率降。缓解：轻量模型（YOLO-tiny/MobileFace）+ 降频（5–10fps）+ 仅检测前方主听众。

### R3：术语与版本漂移
- `技术实现.md` v17 用 v0.11.x、Unity API；`提交.md` 用 Unity Integration SDK。修正：统一为 Spatial SDK **0.13.x**、Kotlin/Compose、Spatial UI；HUD 从 Unity World Space Canvas 改 Spatial UI 组件。

### R4：指环 BLE 在模拟器不可用
- 缓解：演示用「心率手动输入面板 + 模拟指环输入」兜底。

---

## 七、备选路径与战略取舍

1. **围观层为主、遮挡层降级（首选缓解）**：演示主线只做「物种化身+空间弹幕+空间音频+效果分 HUD」，全部模拟器高可行；人脸遮挡作为「R1 通过即上」加分项，文书保留设计。**完全契合 `提交.md` 的 Escape the Rectangle 框架。**
2. **Unity + 真机 PICO 4/4U（不推荐切换）**：能上真机、live demo 更直观，但**丢掉 SpatialML（核心差异点）**，且 72h 内 Unity 重写成本高、Sense Pack MR 弱于 SpatialML。**性价比低，不切换。**
3. **WebSpatial 跨平台兜底（仅极端兜底）**：Web 技术栈一次开发多端运行，但**无 SpatialML**、且仍需 Emulator。仅人员/时间极度受限时考虑。
4. **OpenXR 跨设备**：若后续真机非 PICO，可移植，但同样无 SpatialML。

---

## 八、建议的下一步动作

1. **统一口径**：`技术实现.md` v17 与 `提交.md` 的 SDK 表述统一为「PICO Spatial SDK 0.13.x（Android/Kotlin）+ Spatial Plugin/Editor/Emulator」，HUD 改 Spatial UI，版本号 v0.11.x→0.13.x。
2. **第一天验证 R1**：官方「超级分辨率相机」示例在模拟器跑通相机帧输入，确认人脸遮挡可演示性（硬二选一，无兜底）。
3. **分层演示编排**：稳定主线（空间弹幕+物种化身+效果分 HUD）先出片；人脸遮挡按 R1 结果决定是否现场演示。
4. **找组织方要支持**：通告明确「遇到问题务必艾特我们，可到工位解答」。R1 卡住时第一时间艾特 PICO 研发同学确认「Emulator 是否支持相机数据喂入 SpatialML / 是否可用视频帧替代」。

---

## 附：官方文档关键原文索引（核实出处）

- SDK 全景：`/document/spatial-sdk/learn-about-pico-spatial-sdk-and-spatial-apps/`
- SpatialML 概览/Session/Tensor/Pipeline/容器/读回/超级分辨率相机示例：`/document/spatial-sdk/spatialml/`
- 模拟器与工具套件：`/document/spatial-sdk/emulator/`（Spatial Plugin + Spatial Editor + PICO Emulator）
- Spatial UI：`/document/spatial-sdk/`（Jetpack Compose 声明式 UI）
- 设备：PICO 4 Ultra 系列 / Project Swan / OS 6

---

## 九、方向修订（v3）：改用 Unity + SecureMR（2026-07-25 当晚决策）

> 团队决定改用 **PICO Unity Integration SDK 3.4.0 + SecureMR**（`/document/unity/securemr-overview/`）。
> 经官方文档核实，这是比 Android Spatial SDK 更优的路径。

### 9.1 核实结论：SecureMR 确有 Pipeline（QNN/NPU）

官方 `securemr-use-cases` 页面代码原文：
- `pipeline.CreateTensor<T>(...)`、`pipeline.CreateOperator<T>(...)`、`pipeline.Execute(pipelineIOPair)`、`pipeline.CreateTensorMapping()`
- 文档树含「Pipeline 中的 Tensor」「Pipeline 同步」专页
- 算子：`poseOperator`、`RenderTextOperator`、`SwitchGltfRenderStatusOperator`
- 模型：专页 **「使用 QNN 模型来运行算法」**（path `create-a-qnn-model-to-run-algorithms`）-> **Pipeline 跑 QNN 模型，NPU 加速**
- 相机：`PXR_CameraImage` + 「相机图像数据（用户/企业设备）」+「视频透视」页
- 示例：「人体追踪示例」（pose 估计用 Pipeline）

> 结论：SecureMR = SpatialML 的 Unity 等价物（Session→Pipeline→Tensor→Operator→Execute + 相机 + NPU）。

### 9.2 MediaPipe Face Detector 不能直接用，需转 QNN

- MediaPipe Face Detector = **BlazeFace 模型**，格式 `.tflite`（`face_short_range.tflite` / `face_full_range.tflite`，float16）
- SecureMR 文档**无 "mediapipe"/"blazeface"**，无原生 MediaPipe 集成；Pipeline 只吃 **QNN** 模型
- 转换路径：`BlazeFace.tflite` ->(tf2onnx)-> `ONNX` ->(Qualcomm QAIRT / qnn-onnx-converter)-> `.qnn` -> 加载为 Pipeline 模型 Operator -> NPU 推理
- 官方有「转换并调试 SecureMR 模型」页（模型转换是官方支持流程）

### 9.3 为何这是更优路径（修正前文结论）

| 维度 | Android Spatial SDK（前文推荐） | **Unity + SecureMR（现决定）** |
| --- | --- | --- |
| Pipeline/ML | SpatialML（Kotlin） | SecureMR（C#/Unity，等价） |
| 相机+透视 | 有 | 有（PXR_CameraImage + 视频透视） |
| 真机 | ❌ Swan 未发布，仅模拟器 | **✅ PICO 4/4U（组织方借 3 台）** |
| 技术栈 | Kotlin/Compose | **Unity（技术实现文档 v17 全部成立）** |
| 相机喂入风险 | 高（模拟器无真相机） | **低（真机有真摄像头+真 NPU）** |

**核心收益**：原 R1「模拟器相机喂入 SpatialML、无真机兜底」**风险解除**——真机 PICO 4/4U 有前置彩色摄像头 + Snapdragon NPU，SecureMR 直接用真机摄像头跑 BlazeFace 人脸检测。SecureMR 属 Unity Integration SDK 3.4.0，正是组织方借的设备型号，符合「Unity 实机调优」安排。

### 9.4 修订后的功能实现路径

| 功能 | 实现路径 | 真机可行性 |
| --- | --- | --- |
| 人脸遮挡（核心） | SecureMR Pipeline：相机帧-> BlazeFace(QNN) -> 人脸坐标 -> 空间锚点 -> 物种化身 Prefab 遮挡 | ✅ 真机高（真摄像头+真NPU） |
| 麦克风采集->STT | Unity `Microphone.Start` -> 讯飞 RTASR | ✅ |
| 空间显示 HUD | Unity World Space Canvas（XR Interaction Toolkit） | ✅ |
| 物种化身 | 空间锚点 + 3D Prefab + Avatar SDK | ✅ |
| 空间弹幕（围观层） | 空间锚点 + 文字渲染 + 空间音频 | ✅ |
| 心率面板/WalletConnect二维码 | World Space Canvas + 手柄射线 | ✅ |
| 指环 BLE | Android BLE | ✅（真机） |

### 9.5 修订后的风险

- **R1（新·最高）：BlazeFace `.tflite`->QNN 转换是否顺利**。BlazeFace 轻量标准，通常不踩坑，但需 QAIRT/QNN SDK 转换+调试。
- **R2（先验证链路）**：第一天先用官方「人体追踪示例」跑通 SecureMR Pipeline+相机的完整链路（确认相机帧能进 Pipeline、NPU 能推理），再单独攻克 BlazeFace->QNN。
- **R3：FPS**。多人脸时降帧，用 `face_full_range_sparse`（更小）+ 降频（5–10fps）+ 仅前方主听众。
- 原模拟器相机风险（旧 R1）**解除**。

### 9.6 修订后的下一步

1. 拿官方「人体追踪示例」+ SecureMR 在借到的 PICO 4/4U 上跑通 Pipeline+相机链路（第一天）。
2. 拉 MediaPipe BlazeFace `.tflite`，走 tf2onnx->ONNX->QNN 转换，装进 Pipeline 做人脸检测。
3. 卡住第一时间艾特 PICO 研发同学（通告承诺可到工位解答），直接问「SecureMR 是否有现成人脸检测模型/Operator」「BlazeFace 转 QNN 的官方推荐工具」。
4. 文档同步：选型文档统一为 Unity Integration SDK 3.4.0 + SecureMR；技术实现文档 v17 的 Unity API 保留有效，SpatialML 改为 SecureMR。
