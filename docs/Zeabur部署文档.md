# Q (Cue) — Zeabur 部署与 pico-unity 连接配置

## 当前状态

| 项目 | 详情 |
|------|------|
| **Zeabur 项目** | Q1 |
| **后端服务** | `q-backend-over` — ✅ 运行中 |
| **服务 ID** | `6a6490b00dff901d470b69c0` |
| **服务器** | Aliyun Hangzhou 2C 4GB |
| **内网地址** | `q-backend-over.zeabur.internal:8080` |
| **公网域名** | ⚠️ 待实名认证后生成 `q-backend.preview.aliyun-zeabur.cn` |

## ⚠️ 关键操作：完成实名认证获取公网域名

因为服务器在阿里云杭州（中国大陆），绑定域名需要实名认证。

### 步骤 1：在 Dashboard 完成认证

1. 打开 [Zeabur Q1 项目](https://zeabur.com/projects/6a648b06757f7a223e13654e)
2. 点击左侧 **q-backend-over** 服务
3. 切换到 **网络** 标签页
4. 点击 **生成域名**
5. 输入 `q-backend`
6. 在弹出的认证窗口中填写姓名和身份证号
7. 点击 **确认绑定**

### 步骤 2：验证后端可用

```bash
# 健康检查
curl https://q-backend.preview.aliyun-zeabur.cn/api/health

# 预期返回
{"status":"ok","version":"v17","timestamp":"..."}
```

## pico-unity 连接配置

### 新增文件
| 文件 | 说明 |
|------|------|
| `QServerConfig.cs` | 服务器地址配置 ScriptableObject，支持环境切换 |
| `QWebSocketClient.cs` | 已更新：支持 ServerConfig 或直接填写 URL |

### 三种配置方式

**方式一：QServerConfig asset（推荐）**
1. 在 Unity Project 窗口右键 → `Create → Q → Server Config`
2. 设置 `Environment` = `Production`
3. 将 `Prod Ws Url` 设为 `wss://q-backend.preview.aliyun-zeabur.cn/ws/session`
4. 将生成的 asset 拖到 `QWebSocketClient` 的 `Server Config` 字段

**方式二：直接填写 Inspector**
1. 选中场景中的 `QWebSocketClient` GameObject
2. 在 `Server Url` 填入：`wss://q-backend.preview.aliyun-zeabur.cn/ws/session`

**方式三：本地开发**
- 默认连接 `ws://localhost:3001/ws/session`

### WebSocket 端点
| 端点 | 用途 |
|------|------|
| `/ws/session` | PICO Unity 主会话连接 |
| `/ws/ring-sim` | 指环模拟器 |

### REST API 端点
| 端点 | 方法 | 用途 |
|------|------|------|
| `/api/health` | GET | 健康检查 |
| `/api/session/list` | GET | 会话列表 |
| `/api/session/:id` | GET | 会话详情 |
| `/api/profile/:userId` | GET | 用户画像 |
| `/api/credential/*` | GET | 链上凭证 |
| `/api/wallet/*` | GET/POST | 钱包管理 |
| `/api/heart-rate/*` | POST | 心率记录 |

## 接口对齐验证 ✅

全部 **9 个上行消息 + 17 个下行消息**类型已验证前后端一致。

## 环境变量

已配置：讯飞 RTASR、阶跃星辰 LLM、SQLite 数据库。

## 运维命令

```bash
# 查看服务
npx zeabur@0.8.0 service list --project-id 6a648b06757f7a223e13654e -i=false --json

# 重新部署
cd Q/backend
npx zeabur@0.8.0 deploy --project-id 6a648b06757f7a223e13654e --service-id 6a6490b00dff901d470b69c0 -i=false

# 重启
npx zeabur@0.8.0 service restart --id 6a6490b00dff901d470b69c0 -y -i=false

# 构建日志
npx zeabur@0.8.0 deployment log --service-id 6a6490b00dff901d470b69c0 -t build -i=false

# 运行时日志
npx zeabur@0.8.0 deployment log --service-id 6a6490b00dff901d470b69c0 -t runtime -i=false
```
