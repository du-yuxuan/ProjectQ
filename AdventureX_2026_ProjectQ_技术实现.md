# Q 技术实现（独立文档）

> 项目：Q（Cue）
> 版本：v17（2026-07-25）-- 架构重构：完全使用 PICO，移除 Dify/Rokid，指环加心率（手动输入面板），移除参观者手机 App
> 主文档：`AdventureX_2026_ProjectQ.md`
> PICO Spatial SDK 文档：https://developer-cn.picoxr.com/document/spatial-sdk/learn-about-pico-spatial-sdk-and-spatial-apps/

---

## 一、系统架构（PICO 中心化）

```
感知层  PICO 头显（麦克风+摄像头+空间追踪+Passthrough MR）+ 指环（IMU+按钮+心率[手动输入]）
   │
采集层  PICO 麦克风 -> 讯飞 RTASR 实时STT + 韵律(语速/停顿/音高/填充词)
   │   PICO 摄像头 -> 人头识别与遮挡 + 观众反馈转写(专注/走神)
   │   指环 IMU -> 微颤检测 | 心率输入面板(手动) -> 紧张度估算
   │
处理层  阶跃星辰（连贯性评分 + 钩子生成 + 素养引导，直接调用无 Dify 中间层）
   │
决策层  触发哪个模块 + 垫什么(节点级，垫不替) + 倒计时时长
   │
递送层  PICO 空间显示(效果分/框架/倒计时/遮挡化身) + 音频TTS(钩子) + 指环震动(口头禅/心率超频)
   │
反思层  指环按键确认/消除 + STT检测开口时刻(记录间隔) + 状态是否恢复
   │
铸证层  钱包登录(WalletStrategy) -> 达里程碑 -> MsgBroadcaster签名 -> CosmWasm铸造soulbound(详见五)

```

> 变更说明（v17）：
> - **移除 Dify** -> 阶跃星辰直接调用，跨会话记忆用本地/云端存储
> - **移除 Rokid** -> PICO 承担全部感知（麦克风+摄像头）+ 显示（空间渲染）
> - **心率手动输入面板** -> 演示用，操作员通过面板手动输入心率值（如 70=平静/120=紧张/160=恐慌），模拟不同场景下的心率变化
> - **移除参观者手机 App** -> 不再开发手机端，PICO 承担全部功能

---

## 二、技术栈

| 层 | 技术/接口 | 用途 |
| --- | --- | --- |
| 空间 | PICO Spatial SDK（Passthrough MR + 空间渲染 + 摄像头 + 麦克风 + 手部追踪） | 人脸遮挡与识别 + 音频采集 + HUD渲染 |
| 语音 | 讯飞 RTASR（流式实时STT）+ 韵律特征API | 转写 + 语速/停顿/音高/填充词 |
| LLM | 阶跃星辰 | 连贯性评分、兜底钩子生成、素养小引导 |
| 链上 | Injective CosmWasm/EVM + SDK | soulbound 凭证铸造（非转移） |
| 指环 | IMU + 按钮 | 微颤检测、确认/长按输入 |
| 心率面板 | 手动输入 UI（演示用） | 操作员输入心率值，模拟紧张度变化 |
| 填充词检测 | 本地填充词词典 + 规则匹配（可自定义） | 实时统计填充词频率、超阈提醒 |

---

## 三、各模块实现

### 模块1 表达效果实时监测和反馈

- **三维打分**：讯飞韵律+STT -> 流畅度分（含填充词密度）；阶跃星辰对转写窗口打连贯性分 -> 逻辑度分；PICO摄像头观众反馈转写（专注/走神二分类） -> 接收度分
- PICO 空间面板实时显示三维分 + 会话结束生成复盘报告
- **口头禅/语气词实时监测**：词典+规则匹配填充词（嗯、啊、这个、那个、然后、就是、其实、like、you know…），滑动窗口统计频率
- 超阈值即时提醒：PICO 空间角标计数变红 + 指环轻震，只提醒不递词
- **心率[手动输入]叠加**：通过心率输入面板手动输入心率值，飙升时标记"紧张时刻"，复盘报告中标注时间轴

### 模块2 表达过程兜底

- 检测：停顿(>2s)+填充词密度+心率(手动输入)飙升->开口兜底；连贯性↓/漂移->思路兜底；句中断裂/打断->衔接兜底；语速偏离/观众走神->节奏兜底
- 生成：阶跃星辰 prompt 强约束"≤5字节点级"（开头三字/PREP骨架/过渡钩/指令）
- 递送：PICO 空间显示文字 + 音频TTS + 倒计时

### 模块3 表达能力评判

- 本地/云端存储积累模块1效果分 -> 能力画像（强项/弱项/进步曲线）
- 达里程碑 -> 触发 Injective 上链铸造 soulbound 凭证（完整流程详见五、Injective 上链全流程）
- 凭证在 PICO 空间以 3D 卡片形式展示（非手机端）


### 模块4 表达素养培养

- 接话事件（你开口接住钩子）触发 -> 阶跃星辰生成 +1 小引导
- 素养库存储 + 优质演讲片段学习入库
- 〔Future〕回检闭环：下次同类场景用 STT+韵律+口头禅统计验证该建议是否被做到

### 〔Future〕场景自适应（跨模块1+2）

- **场景识别**：根据演讲者实时讲话内容与风格，自动判断当前表达场景（路演 / 答辩 / 谈判 / 致辞 / 汇报 / 闲聊…）
- **场景化评判**：按场景制定差异化评判标准
- **场景化递钩**：灵活生成适配场景的钩子

### 〔Future〕心率联动

- 真实心率传感器 -> 心率+停顿+填充词+微颤多模态融合，卡壳前提前感知（预测式兜底）
- 主动缓解心率：指环震动引导呼吸（4-7-8 呼吸法节拍），主动降心率、缓解紧张
- 扩展"双向减压"至生理端，三维减压

---

## 四、PICO 空间层实现（核心）

> PICO Spatial SDK 文档：https://developer-cn.picoxr.com/document/spatial-sdk/learn-about-pico-spatial-sdk-and-spatial-apps/

### 4.1 人头遮挡与识别（遮脸减压核心）

通过 **PICO SpatialML**（端侧 ML 框架 + Qualcomm NPU 加速）实现：
文档：https://developer-cn.picoxr.com/document/spatial-sdk/spatialml-scenario-tutorial-style-camera/

```
SpatialML Session (请求相机权限, RELAX_MR_CONFIRMED 模式)
   ↓
SpatialML Pipeline:
   相机帧 -> 人脸检测 ML 模型 (Qualcomm NPU 加速) -> JavaScript 后处理 -> 人脸坐标输出
   ↓
读取算法输出: 人脸位置 [{x, y, z, w, h}...]
   ↓
每个人脸 -> 创建/更新 Spatial Anchor
   ↓
空间渲染: 在锚点位置渲染物种化身 Prefab (遮挡真实面部)
   ↓
动态跟随: Pipeline 每帧执行 -> 化身随人脸同步移动
   ↓
效果: 表达者看向听众时，看到的是中性物种化身而非真人表情，降低被审视感
```

- **SpatialML**：PICO 官方端侧 ML 框架，Pipeline 部署模型 + NPU 加速 + 输出驱动渲染
- **人脸检测模型**：YOLO-tiny / MobileFace 等轻量模型，Qualcomm NPU 加速推理
- **遮挡渲染**：在检测到的人脸位置渲染 3D 物种化身 Prefab，遮住真实面部表情
- **动态跟随**：Pipeline 每帧执行，化身随人脸位置实时更新
- **观众反馈转写**：复用同一 Pipeline，人脸表情/朝向分析 -> 专注/走神判断 -> 接收度分
- **官方示例**：PICO 提供「超级分辨率相机」教程，Session->Pipeline->NPU推理->输出渲染->读回 流程完全一致
- **SpatialML 核心组件**：Session / Pipeline / Tensor(Structured/Global/Local/Placeholder) / 空间容器

### 4.3 递钩时序自适应

```
递钩完成事件 T1（PICO空间显示钩子 + 启动倒计时）
   ↓
STT 实时检测你开口时刻 T2
   ↓
间隔 = T2 - T1（你的"释放到开口"距离）
   ↓
更新间隔基准（EMA 滑动平均）-> 下次倒计时时长自适应
```

- 纯事件记录 + 阈值更新，无需训练框架
- STT 检测开口：复用讯飞 RTASR 的 VAD

### 4.4 自训练 Loop（规则式个性化，不赌ML）

```
事件记录 -> 能力画像 -> 自适应（阈值/垫法/间隔）-> 可见进化 -> 铸证
```

技术栈：本地/云端存储 + 前端可视化。不用 GPU/训练框架/调参。

---


## 五、Injective 上链全流程

> 全场唯一把"表达能力"铸成链上凭证。
> Injective 官方文档：https://docs.injective.network/cn
> 钱包连接文档：https://docs.injective.network/cn/developers-native/wallets/connections

### 5.1 钱包登录（用户认证，上链前置）

```
用户连接 Injective 钱包
   ↓
WalletStrategy 抽象层（支持 Keplr / MetaMask / WalletConnect / Leap / Ledger）
   ↓
获取用户钱包地址 -> 绑定到当前 PICO 会话
   ↓
登录状态在 PICO 空间显示（钱包地址 + 已连接标识）
```

- **WalletStrategy**：Injective 官方推荐的钱包抽象层，统一接口支持多种钱包（Keplr/MetaMask/WalletConnect/Leap/Ledger），一套代码适配所有钱包
- **登录方式**：PICO 空间显示 WalletConnect 二维码 -> 用户用自己手机钱包（Keplr / MetaMask 等）扫码 -> 授权连接 -> 钱包地址绑定到当前 PICO 会话
- **登录后**：钱包地址绑定到当前会话，后续铸证直接铸造到该地址
- 参考文档：https://docs.injective.network/cn/developers-native/wallets/connections

### 5.2 数据采集（每次会话）

```
讯飞 RTASR  ->  STT 转写 + 韵律(语速/停顿/音高/填充词)
PICO 摄像头 ->  观众反馈转写(专注/走神二分类) + 人头遮挡
指环 IMU    ->  微颤检测(紧张度信号)
心率面板    ->  心率值(手动输入, 演示用)
事件记录    ->  {卡壳事件, 递钩事件, 接话事件, 恢复事件, 间隔时长}
```

### 5.3 评分与能力画像

```
单次会话:
  阶跃星辰 -> 连贯性分(逻辑度) + 钩子质量评估
  讯飞韵律 -> 流畅度分(含填充词密度)
  PICO摄像头 -> 接收度分(观众专注/走神比)
  -> 三维聚合分 + 会话报告(存本地/云端)

跨会话积累:
  多次会话三维分 -> 能力画像(强项/弱项/进步曲线)
  卡壳率趋势 / 口头禅率趋势 / 心率峰值趋势
```

### 5.4 里程碑定义（铸证触发条件）

| 里程碑类型 | 触发条件 | 凭证等级 |
| --------- | ------- | ------- |
| 流畅度突破 | 流畅度分连续 3 次会话 > 80 | 流畅表达者 |
| 卡壳率下降 | 卡壳率较初始下降 > 50% | 进步显著 |
| 口头禅减少 | 口头禅率下降至 < 5次/分钟 | 表达精炼 |
| 综合里程碑 | 三维分均 > 75 且卡壳率 < 5次/小时 | 认证表达者 |

> 演示模式：可手动触发铸证（跳过里程碑等待），展示完整上链流程。

### 5.5 链上铸造流程（MsgBroadcaster + CosmWasm）

```
① 里程碑达成(或手动触发)
   Q 系统检测到里程碑
   -> 生成铸证数据: {里程碑类型, 指标快照, 会话历史摘要, 时间戳}

② 构造 Mint 消息
   组装 CosmWasm ExecuteMsg:
   {
     "mint": {
       "recipient": "<用户钱包地址>",  // 来自 5.1 登录
       "credential_type": "表达能力认证",
       "level": "认证表达者",
       "metrics": {fluency: 82, logic: 78, reception: 80, stall_rate: 3.2},
       "improvement": "卡壳率从12次/h降至3次/h"
     }
   }

③ MsgBroadcaster 签名+广播
   使用 WalletStrategy 获取用户签名授权
   -> 或 Q 系统用授权 Minter 私钥签名（演示用，用户无需每次签名）
   -> MsgBroadcaster 广播交易到 Injective 网络
   -> 交易上链确认

④ CosmWasm 合约执行
   合约接收 ExecuteMsg
   -> 验证调用者权限（Q 系统 Minter 地址白名单）
   -> 验证数据结构完整性
   -> 铸造 soulbound token（设置 transferable: false）
   -> 绑定到用户钱包地址
   -> 返回交易哈希

⑤ Token 元数据（链上存储）
   {
     credential_type: "表达能力认证",
     level: "认证表达者",
     metrics: {fluency: 82, logic: 78, reception: 80, stall_rate: 3.2},
     improvement_curve: "卡壳率从12次/h降至3次/h",
     issued_at: "2026-07-25T...",
     soulbound: true,  // 不可转移
     minter: "Q系统授权地址"
   }
```

### 5.6 展示与验证

```
PICO 空间展示:
  凭证以 3D 卡片形式悬浮在 PICO 空间中
  -> 显示: 凭证类型 / 等级 / 指标数据 / 进步曲线 / 链上交易哈希
  -> 铸造瞬间: PICO 空间播放铸造动画(粒子效果 + 凭证卡片生成)
  -> 用户可"拿起"卡片翻转查看背面详情

链上验证:
  Injective 区块链浏览器查询交易哈希
  -> 验证凭证真实性(链上不可篡改)
  -> soulbound 标记确保不可转移/伪造

B 端 API 验证(商业化阶段二):
  企业 HR / 猎头通过 Injective API 查询用户钱包地址
  -> 返回凭证列表 + 指标数据
  -> 替代面试中"沟通能力"的主观评价
```

### 5.7 技术实现要点

- **钱包连接**: Injective WalletStrategy（支持 Keplr/MetaMask/WalletConnect/Leap/Ledger），统一接口
- **交易签名**: MsgBroadcaster（一次函数调用完成签名+广播），与 WalletStrategy 配合
- **合约语言**: CosmWasm (Rust)，部署到 Injective 链
- **soulbound 实现**: mint 时设置 `transferable: false`，合约拒绝所有 transfer 调用
- **授权铸造**: Q 系统后端持有 Minter 授权私钥，只有 Q 系统可调用 mint（用户无需每次签名）
- **数据上链范围**: 只上链里程碑摘要 + 指标快照，原始会话数据不上链（隐私保护）
- **演示**: 预部署合约到 Injective 测试网，现场演示登录 -> 采集 -> 铸造 -> PICO展示 全流程
- **SDK**: @injectivelabs/sdk-ts（TypeScript）或 @injectivelabs/sdk-go（Go）

---

## 六、PICO Spatial SDK 能力验证（v0.11.x）

> 验证依据：https://developer-cn.picoxr.com/document/spatial-sdk/learn-about-pico-spatial-sdk-and-spatial-apps/?v=0.11.x
> 验证目标：逐项确认 Q 所需功能在 PICO SDK 中是否有对应能力 + 具体实现路径

### 6.1 总览：Q 功能 vs PICO 能力映射

| # | Q 功能 | PICO SDK 能力 | 实现路径 | 状态 |
| - | ------ | ----------- | ------- | ---- |
| 1 | 人头遮挡与识别 | **SpatialML**（相机数据+ML模型+NPU加速）+ 空间锚点 + 空间渲染 | 见 6.2 | ✅ 可实现（SpatialML） |
| 3 | 麦克风采集 (STT) | Audio API + Unity 音频 | PICO 麦克风 -> Unity AudioCapture -> WebSocket -> 讯飞 RTASR | ✅ 可实现 |
| 4 | 空间显示 (递钩/效果分/倒计时) | 空间渲染 + Unity UI | World Space Canvas 渲染 2D/3D 面板 | ✅ 可实现 |
| 5 | 观众反馈转写 | **SpatialML**（人脸检测+表情分析）+ 空间锚点 | SpatialML 检测观众面部/姿态 -> 判断专注/走神 | ✅ 可实现（SpatialML） |
| 6 | 物种化身 | Avatar SDK + 空间渲染 | PICO Avatar SDK 或自定义 3D Prefab | ✅ 可实现 |
| 7 | 心率输入面板 | Unity UI + 手部追踪 + 空间渲染 | World Space Canvas + 射线交互输入 | ✅ 可实现 |
| 8 | Injective 凭证 3D 卡片 | 空间渲染 + 3D 对象 | 在 PICO 空间渲染 3D 卡片 + 粒子动画 | ✅ 可实现 |
| 9 | WalletConnect 二维码 | Unity UI + 空间渲染 | World Space Canvas 显示二维码图片 | ✅ 可实现 |
| 10 | 指环 IMU/按钮/心率 | BLE + Unity 输入 | 指环 BLE -> Android BLE 插件 -> Unity | ✅ 可实现(外部硬件) |

> 结论：10 项功能全部 ✅ 确认可实现。人头遮挡与识别通过 PICO SpatialML（端侧 ML 框架 + NPU 加速）实现。

### 6.2 人头遮挡与识别（SpatialML 实现，已确认）

**PICO SpatialML** 是 PICO 官方端侧 ML 框架，文档：https://developer-cn.picoxr.com/document/spatial-sdk/spatialml-scenario-tutorial-style-camera/

SpatialML 核心能力（文档确认）：
- ✅ **SpatialML Session**：管理 ML 推理会话
- ✅ **SpatialML Pipeline**：ML 模型部署管线，支持「在 Pipeline 中部署机器学习模型并利用 Qualcomm NPU 加速模型推理」
- ✅ **SpatialML Tensor**：多维张量（Structured/Global/Local/Placeholder），支持切片赋值
- ✅ **JavaScript 自定义运算**：在 Pipeline 中通过 JavaScript 添加自定义运算
- ✅ **相机数据接入**：Session 使用相机数据（需用户相机权限授权，RELAX_MR_CONFIRMED 模式）
- ✅ **算法输出驱动渲染**：步骤五「把算法输出驱动的场景渲染到 SpatialML 空间容器」
- ✅ **算法输出读回**：步骤六「从 SpatialML 中读取算法输出」
- ✅ **动态贴图**：在容器中使用动态贴图并实时更新
- ✅ **文字渲染**：在 Spatial ML 容器中渲染文字

**完整实现路径**：

```
① 创建 SpatialML Session
   -> 请求相机权限 (RELAX_MR_CONFIRMED 模式)
   -> 获取 PICO 前置彩色摄像头数据流

② 构建 SpatialML Pipeline
   -> 输入 Tensor: 相机帧数据
   -> 部署人脸检测 ML 模型 (Qualcomm NPU 加速推理)
   -> JavaScript 自定义运算: 后处理检测结果 (NMS/坐标转换)
   -> 输出 Tensor: 人脸位置坐标列表 [{x, y, z, w, h}...]

③ 执行 Pipeline (每帧)
   -> 提交执行 -> NPU 加速推理 -> 返回人脸坐标

④ 读取算法输出 + 场景渲染
   -> 从 SpatialML 读取人脸位置坐标
   -> 每个人脸位置 -> 创建/更新 Spatial Anchor
   -> 在锚点位置渲染物种化身 Prefab (遮挡真实面部)
   -> 人脸移动时化身随锚点同步更新

⑤ 观众反馈转写 (复用同一 Pipeline)
   -> 人脸检测结果 -> 表情/朝向分析 (专注/走神/皱眉/点头)
   -> 输出观众接收度分 -> 叠加到模块1 三维评分
```

> 官方示例参考：PICO 文档提供了「示例：超级分辨率相机」教程，展示完整的 Session -> Pipeline -> NPU 推理 -> 输出渲染 -> 读回 流程，与人脸检测路径完全一致。

### 6.3 各功能实现路径详解

**功能 2：麦克风采集**
```
PICO 内置麦克风
  -> Unity Microphone.Start() / AudioCapture
  -> 音频流 -> WebSocket -> 讯飞 RTASR
  -> 实时返回 STT + 韵律特征
```
PICO 能力依据：PICO 有内置麦克风 + Audio API（含 Spatial Audio API）；Unity 标准音频 API 可用

**功能 3：空间显示（递钩/效果分/倒计时）**
```
Unity World Space Canvas
  -> 在 PICO 空间中渲染 UI 面板
  -> 显示：效果分 / 递钩文字(≤5字) / 倒计时 / 口头禅计数 / 能力画像
  -> 面板固定在视野角落 或 锚定到空间位置
```
PICO 能力依据：空间渲染是 SDK 核心能力，"支持 2D/3D 内容布局、渲染效果叠加"

**功能 5：物种化身**
```
方案 1: PICO Avatar SDK -> "虚拟形象开发套件，支持自定义形象、动画和表情控制、实时IK和渲染"
方案 2: 自定义 3D 物种 Prefab（低模+骨骼张嘴动画）
  -> 在 Spatial Anchor 位置实例化
  -> 说话时触发张嘴动画
  -> 物种由阶跃星辰情绪分析自动匹配
```
PICO 能力依据：Avatar SDK（官方提供）+ 空间渲染

**功能 6：心率输入面板**
```
Unity World Space Canvas
  -> 渲染数字键盘 UI
  -> PICO 手部追踪 -> XR Interaction Toolkit Ray Interactor
  -> 操作员通过射线点击数字输入心率值 (70/120/160)
  -> 值推送到系统 -> 紧张度估算 -> 叠加到效果分
```
PICO 能力依据：手部追踪（6DoF + 裸手识别）+ XR Interaction Toolkit 兼容

**功能 8：WalletConnect 二维码**
```
Q 系统生成 WalletConnect 连接 URI
  -> 编码为二维码图片
  -> 在 PICO 空间 World Space Canvas 中显示
  -> 用户用自己手机钱包(Keplr/MetaMask)扫码 -> 授权
  -> 钱包地址回传 Q 系统 -> 绑定 PICO 会话
```
PICO 能力依据：空间渲染 + Unity UI（显示图片）

### 6.4 风险与缓解

| 风险 | 影响 | 缓解方案 |
| ---- | ---- | ------- |
| ~~面部追踪仅追踪佩戴者本人~~ | ~~已通过 SpatialML 解决~~ | SpatialML 直接使用相机数据+ML模型检测视野中所有人脸 |
| SpatialML NPU 推理性能 | 人脸多时帧率下降 | 使用轻量模型(YOLO-tiny/MobileFace) + 降低检测频率(5-10fps) |
| ~~透视纹理不开放给应用层~~ | ~~SpatialML 已提供相机数据接入~~ | 已通过 SpatialML Session 相机权限解决 |
| 指环 BLE 连接 | IMU/按钮/心率数据无法传入 | PICO 运行 Android，可用 Android BLE 插件；或 WiFi 转发 |

### 6.5 结论

**PICO Spatial SDK v0.11.x 可实现 Q 的全部核心功能**：
- 9/9 项功能全部确认可实现
- 人头遮挡与识别通过 **SpatialML** 实现（SpatialML Session + Pipeline + NPU 加速 ML 推理 + 输出驱动渲染）
- 官方提供「超级分辨率相机」示例教程，Pipeline 流程与人脸检测完全一致
- SpatialML 支持相机数据接入（需相机权限）、Qualcomm NPU 加速、JavaScript 自定义运算、算法输出读回
- 唯一性能风险：多人脸时帧率，可用轻量模型+降低检测频率缓解
