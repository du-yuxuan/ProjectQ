# Q（Cue）— 真实表达辅助+成长系统

> **Slogan：Q —— 卡壳时，递你一句 Cue。**
> 副标：真实开口的双向减压器——语言兜底不翻车，视觉遮脸不紧张。

Q 是面向**频繁表达需求、但容易卡壳或开口关键**的创业者 / 销售 / 内容创作者的**真实表达辅助+成长系统**，通过**实时监测表达效果、卡壳时兜底递钩、跨会话评判能力并铸成链上凭证、渐进培养表达素养**，解决**当众表达的核心痛点：效果无评判、过程无兜底、能力无证明、素材难培养、开口太紧张**。

## 硬件

| 硬件 | 职责 |
|------|------|
| **PICO 头显** | 核心设备（麦克风+摄像头+空间追踪+Passthrough MR，承担全部感知与显示） |
| **指环（Zilo）** | 身体接口（IMU 微颤检测+按钮确认/消除+心率[手动输入]） |

## 系统架构（v17 PICO 中心化）

```
Q/
├── backend/          # 后端编排器（Node.js v17）
│   └── src/
│       ├── app.ts                   # Express + WS 入口
│       ├── config.ts                # v17 配置
│       ├── types.ts                 # WS 消息协议（含 v17 新增类型）
│       ├── services/
│       │   ├── orchestrator.ts      # 会话编排核心（串联全部服务）
│       │   ├── iflytek-asr.ts       # 讯飞 RTASR WebSocket 客户端
│       │   ├── step-llm.ts          # 阶跃星辰 LLM（逻辑评分+救场话术）
│       │   ├── score-engine.ts      # 真实评分引擎（流畅度/逻辑度/语速）
│       │   ├── stutter-detector.ts  # 卡壳检测（4类钩子）
│       │   ├── pace-calculator.ts   # 语速计算器
│       │   ├── profile-engine.ts    # 能力画像引擎
│       │   ├── injective.ts         # v17: Injective 链上铸证
│       │   ├── wallet.ts            # v17: WalletStrategy 钱包登录
│       │   ├── heart-rate.ts        # v17: 心率手动输入
│       │   ├── species-mapper.ts    # v17: 物种映射
│       │   ├── audience-feedback.ts # v17: 观众反馈
│       │   └── milestone.ts         # v17: 里程碑引擎
│       ├── routes/                  # REST API（10 端点）
│       ├── ws/                      # WebSocket handlers
│       └── db/                      # Prisma + SQLite（6 模型）
├── pico-unity/       # PICO Unity 空间应用
│   └── Assets/Q/Scripts/           # C# 脚本（8 模块）
│       ├── QWebSocketClient.cs     # WebSocket 客户端
│       ├── QMessageTypes.cs        # 消息类型定义
│       ├── SpatialHUDManager.cs    # 空间 HUD
│       ├── CountdownTimer.cs       # 自适应倒计时
│       ├── FaceOcclusionManager.cs # SpatialML 人头遮挡
│       ├── SpeciesAvatarController.cs # 物种化身控制
│       ├── BulletScreenManager.cs  # 空间弹幕
│       ├── SpeciesMapper.cs        # 物种映射
│       ├── WalletConnectPanel.cs   # 钱包登录
│       ├── CredentialCardSpawner.cs # 凭证 3D 卡片
│       ├── HeartRateInputPanel.cs  # 心率输入面板
│       └── RingInputBridge.cs     # 指环输入桥接
├── injective/        # CosmWasm soulbound 凭证合约（Rust）
│   └── src/lib.rs
└── docs/             # 模块文档
│   ├── 模块A_后端编排器.md
│   ├── 模块B_PICO空间应用.md
│   ├── 模块C_Injective铸证合约.md
│   ├── 模块D_指环感知层.md
│   ├── 模块E_集成联调.md
│   └── 模块F_总体验证报告.md
```

## 五大核心亮点

| 亮点 | 说明 |
|------|------|
| 🪝 **卡壳递钩** | 卡壳瞬间递钩救场（开口/思路/衔接/节奏），倒计时自适应你的节奏 |
| 🔗 **链上凭证** | 软技能铸成 Injective 防篡改 soulbound 凭证（全场唯一"表达能力"上链） |
| 🛡️ **双向减压** | 语言端兜底不翻车 + 视觉端遮脸不紧张 |
| ⚔️ **主动进攻** | 读心镜（看穿对方）+ 弹药库（临场喂反击角度+金句） |
| 🌌 **PICO 空间围观层** | 说话人→物种化身、发言→空间弹幕（最出片、最可传播） |

## 四个模块 + 双向减压

| 痛点 | 模块 | 做什么 |
|------|------|--------|
| ①表达效果无评判 | **表达效果实时监测和反馈** | 实时多维打分 + 口头禅监测 + 超阈提醒 + 复盘报告 |
| ②表达过程无兜底 | **表达过程兜底** | 卡壳时递钩救场（开口/思路/衔接/节奏） |
| ③表达能力无证明 | **表达能力评判** | 能力画像 + Injective 链上凭证 |
| ④表达素材难培养 | **表达素养培养** | 每次接话 +1 小引导 |
| ⑤表达时太紧张 | **双向减压** | 语言端兜底不翻车 + 视觉端遮脸不紧张 |

## 赞助商深用映射

| 赞助商 | 用在哪 | 深度 |
|--------|--------|------|
| 讯飞 | RTASR + 韵律 + VAD 开口检测 | 语音传感器 |
| 阶跃星辰 | 评分 + 钩子 + 素养引导（直接调用，无 Dify） | 持续推理 |
| Injective | soulbound 凭证铸证 | 链上可证 |
| PICO | Spatial SDK：人头遮挡 + 空间弹幕 + 麦克风 + 摄像头 + 空间显示 | 核心设备 |
| 指环(Zilo) | IMU 微颤 + 按钮 + 心率[手动输入] | 身体接口 |

## 快速开始

### 后端

```bash
cd Q/backend
cp .env.example .env        # 配置 API Key
npm install
npx prisma db push --schema src/db/schema.prisma
npm run dev                 # → http://localhost:3001
```

### Injective 合约

```bash
cd Q/injective
rustup target add wasm32-unknown-unknown
cargo build --target wasm32-unknown-unknown --release
# 部署到 Injective 测试网
```

### PICO Unity

1. Unity Hub 创建项目（Unity 2022.3+）
2. 导入 PICO Unity Integration SDK
3. 将 `Q/pico-unity/Assets/Q/` 复制到项目 Assets 目录
4. 配置 WebSocket 连接地址为后端地址
5. 在 Simulator 中运行调试
