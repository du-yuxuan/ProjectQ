# Q 验证版 Web 实现方案

> 项目：Q / 表达指引眼镜
> 版本：v1（2026-07-24）
> 目标：无需硬件的验证版网站，在浏览器中模拟完整 Q 体验

---

## 一、体系结构总览

### 三端硬件分配

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Rokid 眼镜（Android 12）                      │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────┐  ┌───────────┐   │
│  │ BLE Central  │  │  摄像头       │  │  HUD     │  │ 音频播放   │   │
│  │ ← 指环数据   │  │ 听众专注分类   │  │评分/钩子  │  │ TTS 钩子  │   │
│  └─────────────┘  └──────────────┘  └──────────┘  └───────────┘   │
│  核心计算：StepAudio WS（中转后）→ 云端                         │
└─────────────────────────────────────────────────────────────────────┘
        ↑ BLE NUS                          ↓ 评分/复盘
        │                                   │
┌───────┴──────────────────┐    ┌──────────┴─────────────────────────┐
│   Zilo Whisper 指环       │    │         手机 APP                    │
│  ┌─────┐ ┌────┐ ┌─────┐  │    │  ┌──────────┐  ┌───────────────┐  │
│  │双麦  │ │IMU │ │按钮  │  │    │  │ 复盘报告  │  │ 链上凭证显示   │  │
│  │录音  │ │手势 │ │单击  │  │    │  │ 能力画像  │  │ (Injective)  │  │
│  │Speex│ │6轴 │ │双击  │  │    │  │ 素养引导  │  └───────────────┘  │
│  └─────┘ └────┘ └─────┘  │    │  └──────────┘                     │
└───────────────────────────┘    └───────────────────────────────────┘
```

| 设备 | 职责 | 验证版模拟方式 |
|------|------|--------------|
| **Rokid 眼镜** | 主运算中枢 + HUD 显示 + 摄像头感知 + BLE 指环管理 | 浏览器作为 HUD 替代，Session 页模拟眼镜端 UI |
| **Zilo 指环** | 双麦录音 / IMU 手势 / 按钮交互 / 振动反馈 | 键盘快捷键 + RingPanel 面板模拟 |
| **手机 APP** | 复盘报告 / 能力画像 / Injective 铸证 / 参观者入口 | 同一套 Web，响应式适配移动端 |

---

## 二、技术栈

| 层 | 选型 | 理由 |
|----|------|------|
| **前端框架** | React 18 + Vite + TypeScript | 组件化 HUD 模拟，生态成熟 |
| **状态管理** | Zustand | 轻量，适合实时流状态 |
| **前端音频** | Web Audio API（MediaStream + AnalyserNode） | 浏览器原生，采集 + VAD 能量计算 |
| **后端** | Node.js + Express + ws（WebSocket） | 与已有 xiaozhi-mcphub 技术栈一致 |
| **数据库** | SQLite via Prisma ORM | 零运维，验证版够用 |
| **语音输入** | 阶跃星辰 StepAudio 2.5 Realtime | 实时语音对话 API，一条 WS 连接完成 STT + LLM |
| **链上** | Injective 测试网（CosmWasm） | soulbound 凭证铸造 |
| **容器化** | Docker（可选） | 现场部署统一环境 |

---

## 三、全链路数据流

```
  浏览器                            后端（Node.js）                       StepAudio
  ┌────────┐                        ┌─────────────┐                    ┌──────────┐
  │AudioCap│                        │StepAudio    │                    │ 2.5     │
  │ture.ts │  16kHz PCM 分片 WS     │Relay.ts     │  WebSocket         │ Realtime│
  │ ───────┼──────────────────────► │ ────────────┼──────────────────► │  ──────  │
  │ 麦克风 │                        │ 中转(即收   │                    │ STT→LLM │
  │       │                        │ 即发)       │                    │ 推理    │
  │       │                        │             │                    │         │
  │       │  能量包络(100ms一次)    │             │                    │         │
  │       │ ───────────────────────►│             │                    │         │
  │       │                        │             │  JSON 响应流        │         │
  │       │ ◄───────────────────── │ ◄─────────────────────────────── │         │
  │       │  score/hook/recovery   │             │                    │         │
  │       │  segment_end           │             │                    │         │
  │       │                        │             │                    │         │
  │       │                        ▼             │                    │         │
  │       │                  Orchestrator.ts     │                    │         │
  │       │           ┌──────────────────┐       │                    │         │
  │       │           │ PaceCalculator   │       │                    │         │
  │       │           │ StutterDetector  │       │                    │         │
  │       │           │ ProfileEngine    │       │                    │         │
  │       │           │ InjectiveService │       │                    │         │
  │       │           │ SQLite 持久化    │       │                    │         │
  │       │           └──────────────────┘       │                    │         │
  └────────┘                        └─────────────┘                    └──────────┘
```

### WS 消息协议

**上行（浏览器 → 后端）**：

```json
// 音频帧
{"type":"audio","data":"<base64 PCM 16kHz 16bit>","seq":0}

// 能量报告（客户端 AudioContext 算）
{"type":"energy","ts":12.5,"energy":0.32,"isActive":true}

// 指环模拟命令
{"type":"ring","cmd":"rotate_back","ts":30.1"}
{"type":"ring","cmd":"rotate_front","ts":45.2"}
{"type":"ring","cmd":"wave","ts":60.3"}
{"type":"ring","cmd":"single_click","ts":90.4"}
{"type":"ring","cmd":"double_click","ts":95.5"}
```

**下行（后端 → 浏览器）**：

```json
// StepAudio 响应转发
{"type":"score","ts":12.5,"fluency":7,"logic":8,"pace":6,"fillers":2,"pauses":1,"text":"今天我要分享的是..."}
{"type":"hook","ts":45.2,"hookType":"思路","hookText":"核心是","countdown":3}
{"type":"recovery","ts":47.8,"responseTimeMs":2600,"recovered":true}
{"type":"segment_end","ts":60.0,"summary":{"avgFluency":7.2,"avgLogic":7.8,"duration":60,"fillers":12,"pauses":8}}

// 本地计算的语速更新
{"type":"pace_update","ts":15.0,"paceScore":7,"charsPerSec":3.5,"pauseRate":0.12}
```

---

## 四、语速检测算法

### 4.1 双源融合

| 数据源 | 提供什么 | 采集频率 |
|--------|---------|---------|
| 客户端 `AnalyserNode` | 能量包络（有声/无声判断） | 每 100ms |
| StepAudio 返回流 | 转写文本 + 时间戳 `ts` | 实时 |

### 4.2 中文语速计算

```
滑动窗口：5秒，每秒更新

1. 纯说话时长 = 窗口内 isActive=true 的时间段总和
   （排除停顿、对方插话、环境安静）

2. 有效字数 = 窗口内总汉字数 - 填充词计数
   填充词列表：嗯/啊/呃/那个/这个/就是/然后/反正/其实/就是说/基本上

3. 净语速 = 有效字数 / 纯说话时长（chars/sec）

4. 归一化评分
   ┌──────────────┬──────────┬──────┐
   │   语速描述    │ chars/s  │ 分   │
   ├──────────────┼──────────┼──────┤
   │ 过慢（几乎逐字）│  < 1.5  │ 0-2  │
   │ 偏慢（念稿感） │ 1.5-2.5 │ 3-5  │
   │ 正常偏慢     │ 2.5-3.3  │ 6-7  │
   │ 舒适区间     │ 3.3-4.7  │ 8-9  │ ← 中文演讲推荐
   │ 正常偏快     │ 4.7-5.5  │ 6-7  │
   │ 偏快（赶场）  │ 5.5-6.5  │ 3-5  │
   │ 过快（听不清） │  > 6.5   │ 0-2  │
   └──────────────┴──────────┴──────┘
   中文演讲舒适语速约为 200-280 字/分钟 ≈ 3.3-4.7 chars/s

5. 综合语速分 = 净语速分 × 0.6 + (1 - 停顿率) × 10 × 0.4
   停顿率 = 窗口内停顿总时长 / 窗口时长
   权衡：语速本身占 60%，流畅度（不停顿）占 40%
```

### 4.3 停顿检测（卡壳触发规则）

```
规则引擎（纯 if-else，无模型）：

停顿 > 2s 且最近 5s 无 hook 触发：
  → 触发"开口兜底"（hookType: "开口"）

填充词密度 > 3次/10字 且 最近 3s 有 >1.5s 停顿：
  → 触发"思路兜底"（hookType: "思路"）

句中断裂模式（有声→无声>1.5s→有声，前后分词不连贯）：
  → 触发"衔接兜底"（hookType: "衔接"）

语速偏离 > 用户基准 40%（过快或过慢持续 3s 以上）：
  → 触发"节奏兜底"（hookType: "节奏"）

限频：同一类型钩子 8s 内不重复触发
```

### 4.4 StepAudio 未返回时间戳时的降级

如果 StepAudio 的 `ts` 字段不带精准的时间边界，纯用客户端能量检测：

```
有声区间 = energy > 阈值（-40dB）的连续帧
无声区间 = energy < 阈值且持续 > 200ms

文本到有声的对齐：
  每收到一条 StepAudio 文本行，取前一个有声区间作为该段的时间范围
  （近似，验证版可接受）
```

---

## 五、StepAudio System Prompt

```
你是 Q —— 一个表达效果实时分析与兜底 AI。

用户正在进行演讲或表达。你的任务是：
1. 持续监听语音输入，实时进行转写
2. 每 3-5 秒输出一次评分 JSON
3. 检测到卡壳信号时，输出 ≤5 字的兜底钩子

输出格式（严格每行一条独立的 JSON）：

评分：
{"type":"score","ts":<当前秒数>,"fluency":<0-10>,"logic":<0-10>,"pace":<0-10>,"fillers":<填充词次数>,"pauses":<停顿次数>,"text":"<转写片段>"}

兜底钩子（仅在检测到卡壳时输出）：
{"type":"hook","ts":<秒数>,"hookType":"开口|思路|衔接|节奏","hookText":"<≤5字>","countdown":<倒计时秒数>}

恢复确认（用户成功接话后）：
{"type":"recovery","ts":<秒数>,"responseTimeMs":<从钩子到开口的毫秒数>,"recovered":true}

段结束（每 60 秒或会话自然断点）：
{"type":"segment_end","ts":<秒数>,"summary":{"avgFluency":<平均>, "avgLogic":<平均>, "duration":<段时长>, "fillers":<总计>, "pauses":<总计>}}

评分标准：
- fluency（流畅度）：基于停顿频率、填充词密度、重复/修正
- logic（逻辑连贯性）：论点是否连贯、是否有漂移、是否前后呼应
- pace（语速）：是否在舒适区间（200-280 字/分钟）

兜底规则：
- 停顿 > 2s → 开口兜底（如"接着说""我在听""你继续"）
- 填充词密集 + 逻辑漂移 → 思路兜底（如"核心是""你说的""关键是"）
- 句中断裂 → 衔接兜底（如"换句话说""试着说""但重点是"）
- 语速异常 → 节奏兜底（如"放慢说""不着急""深呼吸"）

钩子要求：≤5 字，中性鼓励，不替代用户表达。
```

---

## 六、项目目录结构

```
unfreeze-web/
│
├── frontend/                          # React + Vite + TypeScript
│   ├── index.html
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   └── src/
│       ├── main.tsx
│       ├── App.tsx
│       ├── pages/
│       │   ├── SessionPage.tsx        # 核心演讲会话页
│       │   ├── ReportPage.tsx         # 会话复盘
│       │   ├── CredentialsPage.tsx    # 链上凭证
│       │   ├── ProfilePage.tsx        # 能力画像
│       │   └── LandingPage.tsx        # 入口/扫码
│       ├── components/
│       │   ├── hud/
│       │   │   ├── ScoreMeter.tsx     # 三维仪表盘
│       │   │   ├── HookOverlay.tsx    # 钩子+倒计时动画
│       │   │   ├── Transcript.tsx     # 实时转写流
│       │   │   └── Timeline.tsx       # 时间轴标注
│       │   ├── ring/
│       │   │   ├── RingPanel.tsx      # 手势/按钮模拟面板
│       │   │   └── GestureHint.tsx    # 操作提示浮层
│       │   ├── profile/
│       │   │   ├── RadarChart.tsx     # 能力雷达图
│       │   │   └── TrendCurve.tsx     # 进步曲线
│       │   └── common/
│       │       ├── AudioCapture.tsx   # 麦克风管理器
│       │       ├── WSProvider.tsx     # WebSocket 上下文
│       │       └── Layout.tsx         # 全局布局
│       ├── stores/
│       │   ├── sessionStore.ts        # 会话状态
│       │   └── profileStore.ts        # 画像状态
│       └── lib/
│           ├── api.ts                 # REST 客户端
│           └── types.ts               # 全部 TypeScript 类型
│
├── backend/
│   ├── package.json
│   ├── tsconfig.json
│   └── src/
│       ├── app.ts                     # Express + WS 入口
│       ├── config.ts                  # 环境变量
│       ├── ws/
│       │   ├── session-handler.ts     # 浏览器 WS → StepAudio 中转
│       │   └── ring-sim-handler.ts    # 指环模拟通道
│       ├── services/
│       │   ├── stepaudio-relay.ts     # StepAudio WS 代理
│       │   ├── orchestrator.ts        # 编排器（逻辑核心）
│       │   ├── pace-calculator.ts     # 语速计算引擎
│       │   ├── stutter-detector.ts    # 卡壳检测引擎
│       │   ├── profile-engine.ts      # 能力画像计算
│       │   └── injective.ts           # Injective SDK 交互
│       ├── routes/
│       │   ├── session.ts
│       │   ├── profile.ts
│       │   └── credential.ts
│       └── db/
│           ├── index.ts
│           └── schema.prisma
│
├── injective/                         # CosmWasm 智能合约
│   ├── Cargo.toml
│   └── src/
│       └── contract.rs
│
├── scripts/
│   ├── seed.ts                        # 示例数据
│   └── build.sh                       # 构建脚本
│
├── docker-compose.yml                 # 可选：现场部署
├── Dockerfile
└── README.md
```

---

## 七、数据库模型

```prisma
model User {
  id        String   @id @default(uuid())
  name      String?
  createdAt DateTime @default(now())

  sessions     Session[]
  credentials  Credential[]
}

model Session {
  id        String   @id @default(uuid())
  userId    String
  startTime DateTime
  endTime   DateTime?
  duration  Int?     // 秒
  overallScore Int?  // 0-10
  transcript String? // 完整转写文本

  user       User       @relation(fields: [userId], references: [id])
  segments   Segment[]
  hookEvents HookEvent[]
}

model Segment {
  id        String @id @default(uuid())
  sessionId String
  ts        Int    // 相对于会话起始的秒数
  duration  Int    // 本段持续秒数

  fluencyScore  Int? // 0-10
  logicScore    Int? // 0-10
  paceScore     Int? // 0-10
  fillerCount   Int?
  pauseCount    Int?
  text          String?

  session Session @relation(fields: [sessionId], references: [id])
}

model HookEvent {
  id        String @id @default(uuid())
  sessionId String
  ts        Int
  hookType  String  // "开口"|"思路"|"衔接"|"节奏"
  hookText  String
  countdown Int     // 倒计时秒数
  responseTimeMs Int? // 用户接花时间
  recovered Boolean? // 是否恢复
  feedback  Int?    // 用户评价 -1/0/1

  session Session @relation(fields: [sessionId], references: [id])
}

model ProfileSnapshot {
  id           String   @id @default(uuid())
  userId       String
  snapshotDate DateTime

  // JSON: { fluencyAvg, logicAvg, paceAvg, sessionsCount, totalDuration }
  metrics      String

  // JSON: [ "语气词太多", "停顿过长" ]
  weaknesses   String

  // JSON: [ "选题清晰", "语速控制好" ]
  strengths    String

  // JSON: [ {date, fluencyAvg, logicAvg, paceAvg}, ... ]
  trendData    String
}

model Credential {
  id          String   @id @default(uuid())
  userId      String
  chainTxHash String   // Injective 交易哈希
  milestone   String   // 里程碑名称
  mintedAt    DateTime
  metadata    String?  // JSON

  user User @relation(fields: [userId], references: [id])
}
```

---

## 八、路由设计

### WebSocket（主实时通道）

| WS 路径 | 方向 | 说明 |
|---------|------|------|
| `/ws/session` | 双向 | 音频上行 + 评分/钩子下行 |
| `/ws/ring-sim` | 双向 | 指环命令上行 + 状态反馈下行（调试用） |

### REST API

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/session/end` | 结束会话，生成复盘 |
| GET | `/api/session/:id` | 获取会话详情（含 Segments + HookEvents）|
| GET | `/api/session/list` | 用户会话列表 |
| GET | `/api/profile/:userId` | 用户能力画像 |
| GET | `/api/profile/:userId/trend` | 进步曲线数据 |
| GET | `/api/credential/list/:userId` | 用户凭证列表 |
| POST | `/api/credential/mint` | 手动铸证（里程碑自动调用） |
| POST | `/api/feedback` | 提交钩子评价 |

---

## 九、StepAudio 与部署

### StepAudio WS 连接生命周期

```
1. 用户点击"开始演讲"
2. 前端 WS 连接后端 /ws/session
3. 后端建立 StepAudio 2.5 Realtime WS 连接（携带 API Key）
4. 后端发送 system prompt 到 StepAudio
5. 前端音频流 → 后端 → StepAudio
6. StepAudio 响应 → 后端编排器处理 → 前端 HUD
7. 点击"结束"→ 后端关闭 StepAudio WS → 生成复盘 → 持久化 → 响应前端
```

### 配置（环境变量）

```
STEPAUDIO_API_KEY=sk-xxxxx
STEPAUDIO_WS_URL=wss://api.stepfun.com/v1/audio/realtime
DATABASE_URL=file:./dev.db
PORT=3001
INJECTIVE_MNEMONIC=...       # 可选（铸证用）
INJECTIVE_RPC=https://testnet-rpc.injective.dev
```

### 构建与运行

```bash
# 后端
cd backend
pnpm install
npx prisma generate && npx prisma db push
pnpm dev

# 前端
cd frontend
pnpm install
pnpm dev

# 浏览器访问 http://localhost:5173
```

---

## 十、构建顺序

```
Day 0-1：骨架
  ├─ 初始化前后端项目，跑通 WS 握手
  ├─ Prisma 模型初始化 + SQLite
  └─ AudioCapture 采集 → 后端中转 → 前端日志输出

Day 1-2：StepAudio 集成
  ├─ stepaudio-relay.ts 代理转接
  ├─ StepAudio system prompt 调试
  ├─ HUD 评分仪表盘（ScoreMeter）
  └─ 实时转写流（Transcript）

Day 2-3：兜底钩子
  ├─ pace-calculator.ts + stutter-detector.ts
  ├─ HookOverlay 组件（钩子 + 倒计时动画）
  ├─ RingPanel 模拟面板（键盘快捷键映射）
  └─ Timeline 时间轴标注

Day 3-4：复盘 + 画像
  ├─ 会话结束 → ReportPage
  ├─ ProfileEngine → RadarChart + TrendCurve
  └─ SQLite 全部持久化

Day 4-5：链上铸证 + 部署
  ├─ Injective 测试网合约部署
  ├─ 里程碑检测 → 自动铸证
  ├─ CredentialsPage 凭证展示
  └─ Docker 化 / 现场部署准备
```

---

## 十一、指环模拟快捷键映射

| 键盘快捷键 | 对应指环操作 | 效果 |
|-----------|------------|------|
| `←` | rotate_back（向后旋转） | 切换上一个钩子 |
| `→` | rotate_front（向前旋转） | 切换下一个钩子 |
| `↑` 或 `Space` | wave（挥手） | 划掉当前钩子 |
| `Enter` | 双击 | 确认/已恢复 |
| `Shift` | 单击 | 切换模式 |

---

## 十二、不在此版本验证的模块

| 模块 | 验证版做法 | 后续实现在 |
|------|-----------|-----------|
| Rokid HUD 渲染 | 浏览器全屏模拟 | Rokid 原生 Android App |
| 摄像头听众二分类 | 跳过（眼镜端专用） | Rokid 端 TensorFlow Lite |
| Zilo 真实 BLE 协议 | 键盘模拟 | RingDebugApp（已有） |
| 指环振动反馈 | 跳过（固件不支持） | 待 Zilo 固件更新 |
| Speex 录音下载 | 跳过（验证版不保存音频） | 眼镜端原生实现 |

