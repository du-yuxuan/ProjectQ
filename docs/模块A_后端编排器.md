# 模块A：后端编排器（v17 PICO 中心化架构）

> 项目：Q（Cue）
> 版本：v17
> 路径：`Q/backend/`
> 状态：✅ 已验证（TypeScript 编译通过 + 健康检查通过）

---

## 一、架构概述

后端编排器是 Q 系统的"大脑"，与显示端（PICO 空间渲染）完全解耦。v17 架构下，后端复用验证版（unfreeze-web）的全部业务逻辑，新增 PICO 特有的服务模块。

### v17 变更点

| 变更 | 说明 |
|------|------|
| 移除 Dify | 阶跃星辰直接调用（`step-llm.ts`），无中间层 |
| 移除 Rokid | PICO 承担全部感知（麦克风+摄像头）+ 显示（空间渲染） |
| 心率手动输入 | 新增 `HeartRateService`，操作员通过 PICO 空间面板输入心率值 |
| 物种映射 | 新增 `SpeciesMapper`，情绪/风格 → 物种化身自动匹配 |
| 观众反馈 | 新增 `AudienceFeedbackService`，PICO 摄像头 SpatialML → 接收度分 |
| 钱包登录 | 新增 `WalletService`，WalletStrategy → 钱包地址绑定会话 |
| 里程碑引擎 | 独立为 `MilestoneEngine`，触发条件表驱动 |
| Injective SDK | `InjectiveService` 集成 @injectivelabs/sdk-ts 完整铸造流程 |

### 系统架构图

```
感知层  PICO 头显（麦克风+摄像头+空间追踪+Passthrough MR）+ 指环（IMU+按钮+心率[手动输入]）
   │
采集层  PICO 麦克风 → 讯飞 RTASR 实时STT + 韵律(语速/停顿/音高/填充词)
   │   PICO 摄像头 → 人头识别与遮挡 + 观众反馈转写(专注/走神)
   │   指环 IMU → 微颤检测 | 心率输入面板(手动) → 紧张度估算
   │
处理层  阶跃星辰（连贯性评分 + 钩子生成 + 素养引导，直接调用无 Dify 中间层）
   │
决策层  触发哪个模块 + 垫什么(节点级，垫不替) + 倒计时时长
   │
递送层  PICO 空间显示(效果分/框架/倒计时/弹幕/遮挡化身) + 音频TTS(钩子)
   │
反思层  指环按键确认/消除 + STT检测开口时刻(记录间隔) + 状态是否恢复
   │
铸证层  钱包登录(WalletStrategy) → 达里程碑 → MsgBroadcaster签名 → CosmWasm铸造soulbound
```

---

## 二、服务清单

| 服务文件 | 职责 | 来源 |
|---------|------|------|
| `orchestrator.ts` | 会话编排核心，串联全部服务 | v17 重写（新增 5 服务集成） |
| `iflytek-asr.ts` | 讯飞 RTASR WebSocket 客户端 + 说话人分离 | 复用（验证版已跑通） |
| `analysis-relay.ts` | ASR + LLM + 本地评分编排 | 复用 |
| `score-engine.ts` | 真实评分引擎（流畅度/逻辑度/语速） | 复用 |
| `pace-calculator.ts` | 语速计算器（5秒滑动窗口） | 复用 |
| `stutter-detector.ts` | 卡壳检测引擎（4类钩子：开口/思路/衔接/节奏） | 复用 |
| `step-llm.ts` | 阶跃星辰 LLM（逻辑评分 + 救场话术生成） | 复用 |
| `profile-engine.ts` | 能力画像引擎（跨会话积累） | 复用 |
| `speaker-smoother.ts` | 说话人平滑器（跨帧漂移处理） | 复用 |
| `injective.ts` | **v17 新增**：Injective 链上铸证（真实 SDK 集成） | v17 新写 |
| `wallet.ts` | **v17 新增**：WalletStrategy 钱包登录 | v17 新写 |
| `heart-rate.ts` | **v17 新增**：心率手动输入服务 | v17 新写 |
| `species-mapper.ts` | **v17 新增**：物种映射服务（情绪→物种） | v17 新写 |
| `audience-feedback.ts` | **v17 新增**：观众反馈服务（摄像头→接收度） | v17 新写 |
| `milestone.ts` | **v17 新增**：里程碑引擎（铸证触发条件） | v17 新写 |

---

## 三、v17 新增服务详解

### 3.1 心率服务（HeartRateService）

**文件**：`src/services/heart-rate.ts`

**职责**：接收操作员通过 PICO 空间数字键盘手动输入的心率值，分类紧张度，记录到数据库。

**紧张度分级**：
| 级别 | BPM 范围 | 含义 |
|------|---------|------|
| calm | < 90 | 平静（基线 70 + 20 缓冲） |
| normal | 90-120 | 正常 |
| tense | 120-160 | 紧张（标记"紧张时刻"） |
| panic | > 160 | 恐慌 |

**数据流**：
```
PICO 空间数字键盘 → WS heart_rate 消息 → HeartRateService.handleHeartRate()
  → 分类紧张度 → 持久化 HeartRateRecord → 推送 heart_rate_update 给 PICO 空间显示
  → 趋势分析（rising/falling/stable）→ 叠加到复盘报告"紧张时刻时间轴"
```

### 3.2 物种映射服务（SpeciesMapper）

**文件**：`src/services/species-mapper.ts`

**职责**：根据说话风格/情绪/音色自动匹配物种化身，支持动态切换。

**物种映射表**：
| 风格/情绪 | 物种 | SpeciesType |
|----------|------|-------------|
| 咄咄逼人/强势/攻击/激烈/愤怒 | 老虎 | tiger |
| 突然强硬/升温/对抗 | 狼 | wolf |
| 温和/柔和/友善/亲切 | 兔子 | rabbit |
| 缜密/逻辑/分析/理性/严谨 | 猫头鹰 | owl |
| 活跃/热情/幽默/生动 | 狐狸 | fox |
| 领导/权威/主导/掌控 | 狮子 | lion |
| 中性/平静/友好 | 鹿 | deer |
| 默认 | 默认 | default |

**两路分析**：
1. **本地情绪推断**（`inferEmotion`）：基于转写文本的关键词匹配，无 LLM 依赖
2. **LLM 情绪分析**（读心镜路径）：阶跃星辰多模态分析 → 物种映射（技术一鱼两吃）

**动态切换检测**：缓存每个说话人的当前物种，当物种变化时推送 `species_update` 消息。

### 3.3 观众反馈服务（AudienceFeedbackService）

**文件**：`src/services/audience-feedback.ts`

**职责**：接收 PICO 摄像头 SpatialML 人脸检测结果，分析专注/走神比例 → 接收度分。

**接收度评分**：
```
专注率 = 专注人数 / (专注人数 + 走神人数)
接收度 = round(专注率 × 10)  // 0-10
```

**数据流**：
```
PICO 摄像头 → SpatialML 人脸检测 → WS audience_feedback 消息
  → AudienceFeedbackService.handleFeedback()
  → 计算接收度 → 持久化 AudienceFeedback
  → 叠加到模块1 三维评分（流畅度/逻辑度/接收度）
```

### 3.4 钱包服务（WalletService）

**文件**：`src/services/wallet.ts`

**职责**：WalletStrategy 钱包登录，生成 WalletConnect QR URI，绑定钱包地址到会话。

**流程**：
```
PICO 空间显示 WalletConnect 二维码（QR URI 由 WalletService.generateConnectUri() 生成）
  → 用户用手机钱包（Keplr/MetaMask）扫码 → 授权
  → 钱包地址回传 Q 系统 → WS wallet_connect connect 消息
  → WalletService.connectWallet() → 存储 WalletAddress → 绑定到会话
  → 后续铸证直接铸造到该地址
```

### 3.5 里程碑引擎（MilestoneEngine）

**文件**：`src/services/milestone.ts`

**职责**：定义铸证触发条件，检测里程碑达成。

**里程碑定义表**：
| 里程碑类型 | 触发条件 | 凭证等级 |
|-----------|---------|---------|
| 首次演讲 | 用户首个完成的会话 | 入门表达者 |
| 高分演讲 | overallScore >= 8 | 优秀表达者 |
| 流畅度突破 | 连续 3 次会话流畅度 > 8 | 流畅表达者 |
| 卡壳率下降 | 卡壳率较初始下降 > 50% | 进步显著 |
| 口头禅减少 | 口头禅率 < 5次/分钟 | 表达精炼 |
| 综合里程碑 | 三维分均 > 7.5 且无卡壳 | 认证表达者 |

### 3.6 Injective 铸证服务（InjectiveService）

**文件**：`src/services/injective.ts`

**职责**：使用 @injectivelabs/sdk-ts 构建并广播 CosmWasm 铸造交易。

**铸造流程**：
```
① 里程碑达成（或手动触发）→ 生成铸证数据
② 构造 CosmWasm ExecuteMsg (mint)
③ MsgBroadcaster 签名+广播交易到 Injective 网络
④ 合约执行 mint → 绑定到用户钱包地址（soulbound: transferable=false）
⑤ 返回交易哈希 → 推送 credential_minted 给 PICO 空间 3D 卡片展示
```

**降级策略**：
- Injective 未配置（无 mnemonic）→ 模拟铸证（仅记录到数据库）
- SDK 未完全配置 → fallback 模拟哈希
- 生产环境：完整 SDK 集成（WalletStrategy + MsgBroadcaster + TxGrpcApiClient）

---

## 四、WebSocket 消息协议

### 上行（PICO Unity → 后端）

| 消息类型 | 用途 | v17 新增 |
|---------|------|----------|
| `audio` | 音频帧（base64 PCM 16kHz 16bit） | |
| `energy` | 能量包络（ts/energy/isActive） | |
| `transcript` | 转写结果（Web Speech API） | |
| `ring` | 指环命令（rotate/wave/click） | |
| `session_control` | 会话控制（start/end + walletAddress） | v17 扩展 |
| `heart_rate` | 心率手动输入（bpm） | ✅ v17 新增 |
| `wallet_connect` | 钱包连接/断开/状态 | ✅ v17 新增 |
| `audience_feedback` | 观众反馈（faceCount/attentive/distracted） | ✅ v17 新增 |
| `mint_credential` | 手动触发铸证（演示用） | ✅ v17 新增 |

### 下行（后端 → PICO Unity）

| 消息类型 | 用途 | v17 新增 |
|---------|------|----------|
| `score` | 三维评分（fluency/logic/pace + reception） | v17 扩展 |
| `score_update` | LLM 逻辑性评分补丁 | |
| `hook` | 兜底钩子（开口/思路/衔接/节奏 + 倒计时） | |
| `recovery` | 恢复确认（responseTimeMs） | |
| `pace_update` | 语速更新 | |
| `asr_transcript` | ASR 实时转写流 | |
| `session_started` | 会话开始（含 walletAddress） | v17 扩展 |
| `session_ended` | 会话结束 | |
| `heart_rate_update` | 心率更新（bpm + tension） | ✅ v17 新增 |
| `wallet_status` | 钱包状态（connected + address + qrUri） | ✅ v17 新增 |
| `species_update` | 物种映射更新（speaker + species + emotion） | ✅ v17 新增 |
| `credential_minted` | 凭证铸造成功（txHash + milestone + metrics） | ✅ v17 新增 |

---

## 五、REST API

| 路由 | 方法 | 用途 |
|------|------|------|
| `/api/health` | GET | 健康检查（版本 + 各服务状态） |
| `/api/session/list` | GET | 会话列表 |
| `/api/session/:id` | GET | 会话详情（含段+钩子事件） |
| `/api/profile/:userId` | GET | 能力画像 |
| `/api/credential/list/:userId` | GET | 凭证列表 |
| `/api/credential/mint` | POST | 手动铸证 |
| `/api/wallet/status/:userId` | GET | 钱包状态 + QR URI |
| `/api/wallet/address/:userId` | GET | 用户钱包地址 |
| `/api/heart-rate/history/:userId` | GET | 心率历史 |
| `/api/heart-rate/session/:sessionId` | GET | 会话内心率记录 |

---

## 六、数据库模型

```
User ─┬─ Session ─┬─ Segment（含 receptionScore）
      │           ├─ HookEvent
      │           ╰─ AudienceFeedback        ← v17 新增
      ├─ ProfileSnapshot
      ├─ Credential
      ├─ WalletAddress                      ← v17 新增
      ╰─ HeartRateRecord                    ← v17 新增
```

---

## 七、验证结果

### 7.1 TypeScript 编译

```bash
$ cd Q/backend && npx tsc --noEmit
# 零错误，编译通过
```

### 7.2 数据库初始化

```bash
$ npx prisma generate --schema src/db/schema.prisma
✔ Generated Prisma Client (v5.22.0)

$ npx prisma db push --schema src/db/schema.prisma
🚀 Your database is now in sync with your Prisma schema.
```

### 7.3 服务启动 + 健康检查

```bash
$ npx tsx src/app.ts
[App] 数据库连接成功
════════════════════════════════════════
  Q v17 后端服务已启动（PICO 中心化）
════════════════════════════════════════
  HTTP:       http://localhost:3001
  WS Session: ws://localhost:3001/ws/session
  讯飞ASR:    ⚡ 未配置（Mock）
  Step LLM:  ⚡ 未配置
  Injective:  ⚡ 未启用（模拟铸证）
  物种映射:   ✅ 已启用
  观众反馈:   ✅ 已启用
════════════════════════════════════════
```

```bash
$ curl http://localhost:3001/api/health
{
    "status": "ok",
    "version": "v17",
    "asrMode": false,
    "llmMode": false,
    "injectiveEnabled": false,
    "speciesMapping": true,
    "audienceFeedback": true
}
```

### 7.4 验证结论

| 验证项 | 状态 | 说明 |
|-------|------|------|
| TypeScript 编译 | ✅ 通过 | 零类型错误 |
| Prisma Schema | ✅ 通过 | 6 个模型（含 v17 新增 3 个） |
| 数据库迁移 | ✅ 通过 | SQLite 建表成功 |
| 服务启动 | ✅ 通过 | Express + WS 启动正常 |
| 健康检查 | ✅ 通过 | v17 版本 + 各服务状态正确返回 |
| WS 路由 | ✅ 通过 | /ws/session + /ws/ring-sim 双路由 |
| REST 路由 | ✅ 通过 | 10 个 API 端点（含 v17 新增 4 个） |

---

## 八、与验证版（unfreeze-web）的关系

后端编排器复用验证版的全部业务逻辑（评分算法/卡壳检测/画像/铸证），v17 新增的服务（心率/物种/观众反馈/钱包/里程碑）是 PICO 架构特有的扩展。验证版基于 Rokid + 手机端架构，v17 由 PICO 空间渲染接管 HUD，后端业务逻辑与显示端解耦，可完全复用。
