# PICO Unity 项目环境配置指南

> 基于 PICO Unity Integration SDK 3.2.0+ / Unity 2022.3 或 Unity 6

---

## 一、硬件和软件要求

| 项目 | 要求 |
|------|------|
| PICO 设备 | PICO 4 Ultra 系列（SecureMR 需要 5.13.0+） |
| Unity 版本 | Unity 2022.3.22f1 或 Unity 6 |
| Android Build Target | ARM64 (arm64-v8a) |
| Min SDK | Android 10 (API 29) |
| Target SDK | Auto |
| Scripting Backend | IL2CPP |

> **无设备开发**：可使用 [PICO 模拟器 (Beta)](https://developer-cn.picoxr.com/document/unity-integration/pico-emulator) 在 PC 上运行和调试。支持 Windows 和 macOS。

---

## 二、导入 PICO Unity Integration SDK

### 方式 A：Git URL（推荐）

1. 在 Unity Hub 创建 3D 项目（名称不能含中文）
2. 打开 Unity 编辑器 → **Window** → **Package Manager**
3. 点击 **+** → **Add package from git URL**
4. 输入：`https://github.com/Pico-Developer/PICO-Unity-XR-SDK.git`
5. 等待导入完成

### 方式 B：本地 SDK 包

1. 前往 [PICO 开发者资源](https://developer-cn.pico-interactive.com/resources/#pdc) 下载最新 SDK
2. 解压后得到包含 `package.json` 的文件夹
3. **Window** → **Package Manager** → **+** → **Add package from disk**
4. 选择 `package.json` 文件

---

## 三、项目配置

### 3.1 完成项目配置

参考 PICO 官方文档 [完成项目配置](https://developer-cn.picoxr.com/document/unity-integration/complete-project-settings)：

1. **File** → **Build Settings** → 切换到 Android 平台
2. **Player Settings**：
   - **Other Settings** → **Minimum API Level**: 29 (Android 10)
   - **Other Settings** → **Target Architectures**: ARM64
   - **Other Settings** → **Scripting Backend**: IL2CPP
   - **Other Settings** → **Color Space**: Linear
3. **XR Plugin Management** → 安装并启用 PICO OpenXR Provider

### 3.2 XR Interaction Toolkit

PICO SDK 兼容 Unity XR Interaction Toolkit，用于手柄/手势射线交互：

1. **Window** → **Package Manager** → **Unity Registry** → 安装 `XR Interaction Toolkit`
2. 升级 XR Interaction Toolkit 参考 [PICO 文档](https://developer-cn.picoxr.com/document/unity-integration/create-an-xr-scene)

### 3.3 Newtonsoft.Json

Q 项目使用 Newtonsoft.Json 解析 WebSocket JSON：

1. **Window** → **Package Manager** → **Unity Registry** → 安装 `NuGet Newtonsoft.Json`
2. 或通过 `manifest.json` 添加：`"com.unity.nuget.newtonsoft-json": "3.2.1"`

---

## 四、场景结构

每个场景需要以下核心组件：

```
Scene Root
├── QSceneManager (脚本)          # 自动配置 PXR_Manager + 开启 VST
├── XR Origin (VR)                # Unity XR 相机
│   ├── Main Camera               # 头部追踪相机
│   ├── LeftHand Controller       # 左手（替换为 PICO 手模 HandLeft）
│   └── RightHand Controller      # 右手（替换为 PICO 手模 HandRight）
├── PXR_Manager (脚本)            # PICO SDK 核心管理器
│   ├── ✅ Video Seeehrough      # 视频透视（SecureMR 前提）
│   ├── ✅ Hand Tracking         # 手势追踪
│   ├── ✅ Spatial Anchor        # 空间锚点
│   └── ✅ Late Latching         # 延迟锁定
├── QWebSocketClient (脚本)       # WebSocket 连接后端
├── SpatialHUDManager (脚本)      # 空间 HUD 面板
├── FaceOcclusionManager (脚本)   # SecureMR 人脸检测+化身
├── BulletScreenManager (脚本)    # 空间弹幕
├── WalletConnectPanel (脚本)     # 钱包连接面板
├── HeartRateInputPanel (脚本)    # 心率输入面板
├── CredentialCardSpawner (脚本)  # 凭证 3D 卡片
└── RingInputBridge (脚本)        # 指环输入桥接
```

### 添加 PXR_Manager

1. 选中场景中的 **XR Origin**
2. **Inspector** → **Add Component** → 搜索 **PXR_Manager**
3. 勾选需要的功能（Video Seethrough / Hand Tracking / Spatial Anchor 等）

### 添加手部模型

1. 删除 XR Origin 下的手柄模型
2. 从 `Packages/PICO Integration/Assets/Resources/Prefabs` 拖入 `HandLeft` 和 `HandRight`
3. 放到 XR Origin 下与 Main Camera 同级

---

## 五、SecureMR 配置

### 5.1 启用 SecureMR

1. 在 **PXR_Manager (Script)** 面板上勾选 **SecureMR**
2. 确保已开启 **Video Seethrough**（SecureMR 前提）

### 5.2 人脸检测模型

将 UFO 示例的 MediaPipe Face Detection 模型放入 StreamingAssets：

```
Assets/StreamingAssets/
├── facedetector_fp16_qnn229.bin    # MediaPipe 人脸检测模型（QNN 格式）
└── anchors_1.mat                   # 预定义锚点矩阵
```

模型来源：[Qualcomm AI Hub - MediaPipe Face Detection](https://aihub.qualcomm.com/models/mediapipe_face)

### 5.3 物种化身 glTF 文件

将物种 3D 模型放入 StreamingAssets：

```
Assets/StreamingAssets/species/
├── tiger.gltf     # 老虎
├── rabbit.gltf    # 兔子
├── owl.gltf       # 猫头鹰
├── fox.gltf       # 狐狸
├── lion.gltf      # 狮子
├── wolf.gltf      # 狼
└── deer.gltf      # 鹿（默认/中性）
```

---

## 六、SecureMR C# API 架构

Q 项目使用 PICO Unity Integration SDK 的 SecureMR C# API 实现 4 管线人脸检测+化身渲染（基于 UFO 示例）：

### 命名空间

```csharp
using Unity.XR.PXR;
using Unity.XR.PXR.SecureMR;
using Color = Unity.XR.PXR.SecureMR.Color;
```

### 核心类

| 类 | 用途 |
|----|------|
| `Provider` | SecureMR 会话（应用与 SecureMR 服务之间的连接） |
| `Pipeline` | 计算图（operator + tensor 组成的执行链） |
| `Tensor` | 数据块句柄（全局 tensor 跨管线共享，局部 tensor 管线内用） |
| `TensorMapping` | 占位符→全局 tensor 映射（管线执行时绑定） |

### 4 条管线（UFO 架构）

```
Pipeline 1 (VST):     RectifiedVstAccessOperator → 左右眼图像 + 时间戳 + 相机内参
  → ArithmeticComposeOperator → UInt8 转 Float32 (÷255)

Pipeline 2 (推理):    RunModelInferenceOperator(MediaPipe) → 人脸 2D 坐标 + 置信度
  → ArgmaxOperator → 找最高置信度人脸
  → CustomizedCompareOperator → 判断置信度 > 0.55
  → AllOperator → 综合判断是否检测到人脸

Pipeline 3 (2D→3D):  UVTo3DInCameraSpaceOperator → 2D UV 投影到 3D（深度传感器）
  → CameraSpaceToWorldOperator → 相机坐标系 → OpenXR Local 世界坐标系
  → ArithmeticComposeOperator → 坐标轴翻转 + 偏移

Pipeline 4 (渲染):    ArithmeticComposeOperator → 阻尼平滑 (prev×0.95 + curr×0.05)
  → SwitchGltfRenderStatusOperator → 在平滑位置渲染物种化身 glTF
  → ExecuteConditional → 仅当检测到人脸时执行
```

### 关键 Operator

| Operator | 用途 |
|----------|------|
| `RectifiedVstAccessOperator` | 获取 VST 相机画面（左右眼 RGB + 时间戳 + 内参） |
| `ArithmeticComposeOperator` | 算术运算（加减乘除、表达式） |
| `RunModelInferenceOperator` | 运行 ML 模型推理（MediaPipe/YOLO 等 QNN 模型） |
| `ArgmaxOperator` | 找最大值索引 |
| `AssignmentOperator` | 拷贝/切片/类型转换 |
| `CustomizedCompareOperator` | 比较运算 |
| `ElementwiseAndOperator` | 逐元素逻辑与 |
| `AllOperator` | 全部非零判断 |
| `UVTo3DInCameraSpaceOperator` | 2D UV → 相机坐标系 3D 坐标 |
| `CameraSpaceToWorldOperator` | 相机坐标系 → 世界坐标系变换矩阵 |
| `SwitchGltfRenderStatusOperator` | 渲染 glTF 模型到指定位置 |

---

## 七、使用 PICO 模拟器开发

### 下载和安装

1. 前往 [PICO 开发者资源](https://developer-cn.picoxr.com/resources/#emulator) 下载模拟器
2. macOS: 运行 `start-emulator.sh`
3. Windows: 双击 picoemulator 图标

### 构建和安装

```bash
# 在 Unity 中 Build APK
# 然后安装到模拟器
adb install Q-Cue.apk

# 或直接将 APK 拖入模拟器窗口
```

### 模拟器操作

| 操作 | 功能 |
|------|------|
| W/S/A/D 键 | 视角移动 |
| 鼠标左键 | 确认 |
| 鼠标滚轮 | 前后移动 |
| 右键拖动 | 旋转视角 |

---

## 八、参考文档

| 文档 | 链接 |
|------|------|
| PICO Unity Integration SDK 介绍 | https://developer-cn.picoxr.com/document/unity-integration/about-pico-unity-integration-sdk |
| 导入 SDK | https://developer-cn.picoxr.com/document/unity-integration/import-the-sdk |
| 完成项目配置 | https://developer-cn.picoxr.com/document/unity-integration/complete-project-settings |
| SecureMR 概览 | https://developer-cn.picoxr.com/document/unity-integration/securemr-overview |
| SecureMR 核心概念 | https://developer-cn.picoxr.com/document/unity-integration/securemr-key-concepts |
| SecureMR 快速开始 | https://developer-cn.picoxr.com/document/unity-integration/securemr-quickstart |
| SecureMR Operator 类型 | https://developer-cn.picoxr.com/document/unity-integration/use-different-operators |
| SecureMR 场景教学 | https://developer-cn.picoxr.com/document/unity-integration/securemr-use-cases |
| SecureMR Unity 示例 | https://github.com/Pico-Developer/SecureMR-Unity-Sample |
| SecureMR 原生示例 (UFO) | https://github.com/Pico-Developer/SecureMR-Samples/samples/ufo |
| 手势追踪 | https://developer-cn.picoxr.com/document/unity-integration/hand-tracking |
| PXR Manager 介绍 | https://developer-cn.picoxr.com/document/unity-integration/about-pxr-manager |
| PICO 模拟器 | https://developer-cn.picoxr.com/document/unity-integration/pico-emulator |
| SDK Git 仓库 | https://github.com/Pico-Developer/PICO-Unity-XR-SDK |
