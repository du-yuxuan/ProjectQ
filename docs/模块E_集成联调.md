# 模块E：集成联调（WS协议统一 + 端到端数据流）

> 项目：Q（Cue）
> 版本：v17
> 状态：📋 联调规范文档（端到端数据流定义）

---

## 一、端到端架构

```
┌─────────────────────────────────────────────────────────────┐
│                    PICO 头显（Unity 空间应用）                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │ 麦克风    │  │ 摄像头    │  │ 指环BLE  │  │ 空间渲染  │   │
│  │ (音频采集)│  │(SpatialML)│  │(IMU+按钮) │  │ (HUD+化身)│   │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────▲─────┘   │
│       │              │              │              │         │
│       ▼              ▼              ▼              │         │
│  ┌──────────────────────────────────────────────────┐       │
│  │           QWebSocketClient (C#)                  │       │
│  │     WebSocket ←→ 后端 ws://localhost:3001        │       │
│  └──────────────────────┬───────────────────────────┘       │
└─────────────────────────┼───────────────────────────────────┘
                          │ WebSocket JSON
                          ▼
┌─────────────────────────────────────────────────────────────┐
│                  后端编排器（Node.js v17）                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │ 讯飞ASR  │  │ 阶跃星辰  │  │ 卡壳检测  │  │ 评分引擎  │   │
│  │ (STT+韵律)│  │ (LLM)    │  │ (4类钩子) │  │(流畅/逻辑)│   │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘   │
│       │              │              │              │         │
│       ▼              ▼              ▼              ▼         │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │              Orchestrator（编排核心）                      │ │
│  │  + HeartRateService  + SpeciesMapper  + AudienceFeedback │ │
│  │  + WalletService  + MilestoneEngine  + InjectiveService  │ │
│  └──────────────────────┬───────────────────────────────────┘ │
│                         │                                    │
│       ┌─────────────────┼─────────────────┐                  │
│       ▼                 ▼                 ▼                  │
│  ┌─────────┐    ┌──────────────┐   ┌──────────┐             │
│  │ Prisma   │    │ Injective    │   │ WS 推送   │             │
│  │ SQLite   │    │ CosmWasm     │   │ → PICO    │             │
│  └─────────┘    └──────────────┘   └──────────┘             │
└─────────────────────────────────────────────────────────────┘
```

---

## 二、端到端数据流（核心闭环）

### 2.1 "听→判断→递钩"最小闭环（MVP 必跑通）

```
Step 1: PICO 麦克风采集音频
  → Unity Microphone.Start() / AudioCapture
  → base64 PCM 16kHz 16bit
  → WS: {type:"audio", data:"<base64>", seq:0}

Step 2: 后端转发讯飞 RTASR
  → IflytekAsr.sendAudio()
  → 讯飞返回转写结果
  → ASR callback: {text, isFinal, ts, speaker}

Step 3: 评分 + 卡壳检测
  → ScoreEngine.computeRealScore() → {fluency, logic, pace}
  → StutterDetector.update() → 检测停顿/填充词/断裂/语速偏离
  → 触发 HookTrigger: {hookType, hookText, countdown}

Step 4: 递钩 + 倒计时
  → WS 推送: {type:"hook", hookType:"开口", hookText:"接着说", countdown:5}
  → PICO 空间显示钩子文字 + 启动倒计时

Step 5: 用户开口（STT VAD 检测）
  → 讯飞 ASR 返回新转写 → 检测到开口
  → 指环双击确认恢复 → WS: {type:"ring", cmd:"double_click"}
  → 计算间隔 = T2(开口) - T1(递钩)
  → EMA 滑动平均 → 下次倒计时时长自适应

Step 6: 恢复确认
  → WS 推送: {type:"recovery", responseTimeMs:2600, recovered:true}
```

### 2.2 三维评分闭环（模块1）

```
PICO 麦克风 → 讯飞 ASR → 转写文本
  → ScoreEngine: 填充词密度 → 流畅度分
  → StepLlm: LLM 逻辑评分 → 逻辑度分
  → PaceCalculator: 5秒滑动窗口 → 语速分

PICO 摄像头 → SpatialML 人脸检测
  → WS: {type:"audience_feedback", faceCount:5, attentive:4, distracted:1}
  → AudienceFeedbackService: 专注率 → 接收度分

PICO 心率面板 → 手动输入
  → WS: {type:"heart_rate", bpm:120}
  → HeartRateService: 紧张度分级

→ WS 推送: {type:"score", fluency:7, logic:8, pace:6, reception:8, fillers:2}
→ PICO 空间面板实时显示三维分 + 心率 + 口头禅计数
```

### 2.3 物种映射闭环（PICO 空间围观层）

```
讯飞 ASR 转写（带 speaker 分离）
  → SpeciesMapper.inferEmotion(text) → 情绪/风格分析
  → SpeciesMapper.mapSpecies(speaker, emotion) → 物种匹配
  → 物种变化检测

→ WS 推送: {type:"species_update", speaker:1, species:"tiger", emotion:"攻击性"}
→ PICO 空间渲染:
  → 检测到的人脸位置 → Spatial Anchor
  → 在锚点位置渲染物种化身 Prefab（老虎）
  → 说话时触发张嘴动画
  → 转写文字以弹幕从化身"嘴边"飘出
```

### 2.4 人头遮挡闭环（遮脸减压）

```
PICO 摄像头 → SpatialML Pipeline
  → 人脸检测 ML 模型（NPU 加速）→ 人脸坐标 [{x,y,z,w,h}...]
  → 每个人脸 → 创建/更新 Spatial Anchor
  → 在锚点位置渲染物种化身 Prefab（遮挡真实面部）
  → Pipeline 每帧执行 → 化身随人脸同步移动

效果: 表达者看向听众时，看到中性物种化身而非真人表情
```

### 2.5 Injective 铸证闭环（模块3）

```
Step 1: 钱包登录
  → PICO 空间显示 WalletConnect QR
  → 用户手机钱包扫码 → 授权
  → WS: {type:"wallet_connect", action:"connect", address:"inj1...", walletType:"keplr"}
  → WalletService.connectWallet() → 绑定到会话

Step 2: 数据采集（每次会话）
  → 讯飞 ASR + 韵律 → STT + 语速/停顿/音高/填充词
  → PICO 摄像头 → 观众反馈 + 人头遮挡
  → 指环 IMU → 微颤检测
  → 心率面板 → 心率值
  → 事件记录 → {卡壳/递钩/接话/恢复/间隔}

Step 3: 评分与能力画像
  → 三维聚合分 + 会话报告
  → 跨会话积累 → 能力画像

Step 4: 里程碑检测
  → MilestoneEngine.checkMilestones()
  → 达里程碑 → 生成铸证数据

Step 5: 链上铸造
  → InjectiveService.mint() → 构造 CosmWasm mint 消息
  → MsgBroadcaster 签名+广播
  → 合约执行 → soulbound 凭证绑定到用户地址

Step 6: PICO 空间展示
  → WS 推送: {type:"credential_minted", txHash:"...", milestone:"...", metrics:{...}}
  → PICO 空间渲染 3D 凭证卡片 + 粒子动画
  → 用户可"拿起"卡片翻转查看详情
```

---

## 三、WS 连接生命周期

```
1. PICO Unity 启动 → QWebSocketClient 连接 ws://localhost:3001/ws/session

2. 会话开始
  → PICO 发: {type:"session_control", action:"start", userId:"...", walletAddress:"inj1..."}
  → 后端创建 Session 记录 → 绑定钱包地址
  → 后端推: {type:"session_started", sessionId:"...", walletAddress:"..."}

3. 实时数据流（持续）
  → PICO 发: {type:"audio", data:"<base64>"}  ← 音频帧（每 40ms）
  → PICO 发: {type:"energy", ts:12.5, energy:0.32, isActive:true}  ← 能量包络
  → PICO 发: {type:"heart_rate", bpm:120}  ← 心率（手动输入）
  → PICO 发: {type:"audience_feedback", faceCount:5, attentive:4, distracted:1}  ← 摄像头
  → PICO 发: {type:"ring", cmd:"double_click"}  ← 指环命令

  → 后端推: {type:"asr_transcript", text:"..."}  ← ASR 转写
  → 后端推: {type:"score", fluency:7, logic:8, pace:6, reception:8}  ← 评分
  → 后端推: {type:"pace_update", paceScore:7, charsPerSec:3.5}  ← 语速
  → 后端推: {type:"hook", hookType:"开口", hookText:"接着说", countdown:5}  ← 递钩
  → 后端推: {type:"recovery", responseTimeMs:2600}  ← 恢复确认
  → 后端推: {type:"heart_rate_update", bpm:120, tension:"tense"}  ← 心率状态
  → 后端推: {type:"species_update", species:"tiger", emotion:"攻击性"}  ← 物种
  → 后端推: {type:"wallet_status", connected:true, address:"inj1..."}  ← 钱包

4. 会话结束
  → PICO 发: {type:"session_control", action:"end"}
  → 后端: 持久化段/钩子/画像 → 检查里程碑 → 铸证
  → 后端推: {type:"credential_minted", txHash:"...", milestone:"首次演讲"}
  → 后端推: {type:"session_ended", sessionId:"...", reportUrl:"/api/session/..."}
```

---

## 四、递钩时序自适应（EMA 滑动平均）

```
递钩完成事件 T1（PICO空间显示钩子 + 启动倒计时）
   ↓
STT 实时检测你开口时刻 T2（讯飞 RTASR 的 VAD）
   ↓
间隔 = T2 - T1（你的"释放到开口"距离）
   ↓
更新间隔基准（EMA 滑动平均）→ 下次倒计时时长自适应
```

**自适应倒计时规则**（StutterDetector.adaptiveCountdown）：
| 语速评分 | 基础倒计时 | 开口类钩子 | 最终倒计时 |
|---------|-----------|-----------|-----------|
| < 4（偏慢） | 6 秒 | +1 秒 | 7 秒 |
| 4-7（正常） | 5 秒 | +1 秒 | 6 秒 |
| > 7（偏快） | 4 秒 | +1 秒 | 5 秒 |
| 无数据 | 5 秒 | +1 秒 | 6 秒 |

---

## 五、MVP 验证清单

| MVP 必跑通项 | 验证方法 | 状态 |
|-------------|---------|------|
| 模块2 开口兜底 | WS 推送 hook 消息 + PICO 空间显示 | ✅ 后端逻辑就绪 |
| 模块2 衔接兜底 | WS 推送 hook 消息 + PICO 空间显示 | ✅ 后端逻辑就绪 |
| 模块1 实时分 | WS 推送 score 消息 + PICO 空间面板 | ✅ 后端逻辑就绪 |
| 递钩倒计时 | WS countdown 字段 + PICO 倒计时显示 | ✅ 后端逻辑就绪 |
| 间隔记录 | STT VAD 检测开口 → 计算 T2-T1 → EMA 更新 | ✅ 后端逻辑就绪 |
| PICO 人头遮挡 | SpatialML 人脸检测 → 物种化身渲染 | 📋 PICO Unity 脚本就绪 |
