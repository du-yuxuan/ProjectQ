# Q 验证版 Web

> 表达指引眼镜 — 无需硬件的浏览器验证版

## 概述

Q 是一个在真实表达场景里既兜底又成长的系统：实时评判表达效果、卡壳时兜底递钩、跨会话评判能力并铸成链上凭证。本仓库是**验证版**，用浏览器模拟 AR 眼镜 HUD 体验，无需任何硬件。

| 设备 | 职责 | 验证版模拟方式 |
|------|------|--------------|
| Rokid 眼镜 | 主运算中枢 + HUD 显示 + 摄像头感知 | 浏览器作为 HUD 替代，Session 页模拟眼镜端 UI |
| Zilo 指环 | 双麦录音 / IMU 手势 / 按钮交互 | 键盘快捷键 + RingPanel 面板模拟 |
| 手机 APP | 复盘报告 / 能力画像 / Injective 铸证 | 同一套 Web，响应式适配移动端 |

## 技术栈

| 层 | 技术 |
|----|------|
| 前端 | React 18 + Vite + TypeScript + Zustand |
| 后端 | Node.js + Express + ws (WebSocket) |
| 数据库 | SQLite via Prisma ORM |
| 语音 | StepAudio 2.5 Realtime（无 API Key 时自动启用 Mock 模式） |
| 链上 | Injective 测试网 (CosmWasm soulbound 凭证) |

## 快速开始

### 环境要求

- Node.js 18+
- npm 或 pnpm

### 一键构建

```bash
cd unfreeze-web
bash scripts/build.sh
```

### 手动启动

**后端：**

```bash
cd backend
npm install        # 或 pnpm install
npx prisma generate
npx prisma db push
npm run seed       # 播种示例数据
npm run dev        # 启动 → http://localhost:3001
```

**前端：**

```bash
cd frontend
npm install        # 或 pnpm install
npm run dev        # 启动 → http://localhost:5173
```

浏览器访问 `http://localhost:5173` 即可体验。

### 真实数据模式（默认）

当 `STEPAUDIO_API_KEY` 环境变量为空时（默认），后端启用**本地真实评分引擎**：
- 浏览器通过 Web Speech API（`SpeechRecognition`）进行实时中文 STT 转写
- 转写文本通过 WS 发送到后端
- 后端基于实际转写文本计算真实评分：
  - **流畅度**：基于填充词密度（嗯/啊/呃/那个/这个/就是/然后/反正/其实/就是说/基本上）和停顿率
  - **语速**：基于 5 秒滑动窗口的中文字符数 / 纯说话时长
  - **逻辑性**：基于逻辑连接词密度（首先/其次/因此/所以/不过/但是等）和文本完整性
- 卡壳检测基于真实能量数据和停顿模式

配置 StepAudio API Key 后，系统会自动切换到 StepAudio 2.5 Realtime API（正确的 `wss://api.stepfun.com/v1/realtime` 协议），由 LLM 进行更智能的评分和钩子生成。

## 功能模块

### 1. 表达效果评判
- 麦克风实时采集 → WS 传输 → 评分
- 三维仪表盘：流畅度 / 逻辑度 / 语速
- 实时转写流

### 2. 表达过程兜底
- 卡壳检测引擎（停顿/填充词/断裂/语速偏离）
- 四类兜底钩子：开口 / 思路 / 衔接 / 节奏
- 钩子浮层 + 倒计时动画
- 指环模拟（键盘快捷键）

### 3. 表达能力评判
- 能力雷达图
- 进步趋势曲线
- 强项/弱项分析

### 4. 表达素养培养
- 接话事件记录
- 跨会话画像积累
- 里程碑 → Injective 链上凭证铸造

## 指环模拟快捷键

| 快捷键 | 操作 | 效果 |
|--------|------|------|
| `←` | 向后旋转 | 切换上一个钩子 |
| `→` | 向前旋转 | 切换下一个钩子 |
| `↑` / `Space` | 挥手 | 划掉当前钩子 |
| `Enter` | 双击 | 确认/已恢复 |
| `Shift` | 单击 | 切换模式 |

## WS 消息协议

### 上行（浏览器 → 后端）

```json
{"type":"audio","data":"<base64 PCM 16kHz 16bit>","seq":0}
{"type":"energy","ts":12.5,"energy":0.32,"isActive":true}
{"type":"ring","cmd":"rotate_back","ts":30.1}
{"type":"session_control","action":"start","userId":"...","userName":"..."}
```

### 下行（后端 → 浏览器）

```json
{"type":"session_started","sessionId":"...","userId":"...","startTime":"..."}
{"type":"score","ts":12.5,"fluency":7,"logic":8,"pace":6,"fillers":2,"pauses":1,"text":"..."}
{"type":"hook","ts":45.2,"hookType":"思路","hookText":"核心是","countdown":3}
{"type":"recovery","ts":47.8,"responseTimeMs":2600,"recovered":true}
{"type":"segment_end","ts":60.0,"summary":{"avgFluency":7.2,"avgLogic":7.8,"duration":60,"fillers":12,"pauses":8}}
{"type":"pace_update","ts":15.0,"paceScore":7,"charsPerSec":3.5,"pauseRate":0.12}
```

## 数据库模型

```
User ─┬─ Session ─┬─ Segment
      │           ╰─ HookEvent
      ├─ ProfileSnapshot
      ╰─ Credential
```

## Docker 部署

```bash
docker-compose up -d
# 访问 http://localhost:3001
```

## 项目结构

```
unfreeze-web/
├── frontend/          # React + Vite + TypeScript
│   └── src/
│       ├── pages/     # 5 个页面
│       ├── components/ # hud / ring / profile / common
│       ├── stores/    # Zustand stores
│       └── lib/       # API 客户端 + 类型
├── backend/           # Node.js + Express + ws
│   └── src/
│       ├── ws/        # WS handlers
│       ├── services/  # 核心服务（6 个）
│       ├── routes/    # REST API
│       └── db/        # Prisma + SQLite
├── injective/         # CosmWasm soulbound 合约
├── scripts/           # 构建 + 种子数据
├── docker-compose.yml
└── Dockerfile
```

## 不在此版本验证的模块

| 模块 | 验证版做法 | 后续 |
|------|-----------|------|
| Rokid HUD 渲染 | 浏览器全屏模拟 | Rokid 原生 Android App |
| 摄像头听众二分类 | 跳过 | Rokid 端 TensorFlow Lite |
| Zilo 真实 BLE 协议 | 键盘模拟 | RingDebugApp |
| 指环振动反馈 | 跳过 | 待 Zilo 固件更新 |
| Speex 录音下载 | 跳过 | 眼镜端原生实现 |
