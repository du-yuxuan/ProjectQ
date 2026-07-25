# 模块C：Injective 铸证全链路

> 项目：Q（Cue）
> 版本：v17
> 路径：`Q/injective/`（CosmWasm 合约）+ `Q/backend/src/services/injective.ts` + `Q/backend/src/services/wallet.ts`
> 状态：✅ 已验证（Rust 编译通过，wasm 224KB 产出）

---

## 一、概述

Q 是全场唯一把"表达能力"铸成链上凭证的项目。表达能力平时不可证 → 硬件测了才可信 → 链上存证才可携带，三环缺一不可。

### 全链路流程

```
钱包登录（WalletStrategy）
  → 用户连接 Injective 钱包 → 钱包地址绑定到 PICO 会话

数据采集（每次会话）
  → 讯飞 RTASR → STT + 韵律
  → PICO 摄像头 → 观众反馈 + 人头遮挡
  → 指环 IMU → 微颤检测
  → 心率面板 → 心率值
  → 事件记录 → {卡壳/递钩/接话/恢复/间隔}

评分与能力画像
  → 三维聚合分 + 会话报告
  → 跨会话积累 → 能力画像

里程碑检测
  → MilestoneEngine 检测触发条件
  → 达里程碑 → 生成铸证数据

链上铸造
  → InjectiveService.mint() → 构造 CosmWasm mint 消息
  → MsgBroadcaster 签名+广播
  → 合约执行 → soulbound 凭证绑定到用户地址

PICO 空间展示
  → 推送 credential_minted 消息
  → PICO 空间渲染 3D 凭证卡片 + 粒子动画
```

---

## 二、CosmWasm 合约（Rust）

### 2.1 合约信息

| 属性 | 值 |
|------|-----|
| 文件 | `Q/injective/src/lib.rs`（30KB） |
| 包名 | `q-credential-contract` |
| 版本 | 0.2.0 |
| 依赖 | cosmwasm-std 2.1, serde 1.0, serde-json-wasm 1.0, thiserror 1.0 |
| 编译目标 | wasm32-unknown-unknown |
| 产出 | 224KB wasm（release 优化） |

### 2.2 消息类型

**InstantiateMsg**
```rust
pub struct InstantiateMsg {
    pub admin: String,  // 管理员地址（Q 系统 Minter 授权地址）
}
```

**ExecuteMsg**
```rust
pub enum ExecuteMsg {
    Mint {                          // 铸造灵魂绑定凭证（仅 admin）
        recipient: String,           // 接收者钱包地址
        milestone: MilestoneType,    // 里程碑类型
        metadata: CredentialMetadata,// 凭证元数据
    },
    Revoke { credential_id: u64 }, // 撤销凭证（仅 admin）
    Transfer {                     // 灵魂绑定守卫：始终拒绝
        credential_id: u64,
        recipient: String,
    },
}
```

**QueryMsg**
```rust
pub enum QueryMsg {
    CredentialsByOwner { owner: String },  // 查询某地址所有凭证
    Credential { id: u64 },                  // 查询单个凭证
    ContractInfo {},                         // 合约信息
}
```

### 2.3 Soulbound 实现

合约通过以下机制确保凭证不可转移：

1. **Transfer 变体始终拒绝**：
```rust
ExecuteMsg::Transfer { credential_id, .. } => Err(StdError::generic_err(format!(
    "soulbound: transfer not allowed (credential_id={})",
    credential_id
))),
```

2. **无 Transfer/Cw721 接口**：合约不实现任何标准的 transfer 入口点，凭证从铸造起就永久绑定原 owner。

3. **元数据 soulbound 标记**：每个凭证的元数据中包含 `soulbound: true` 字段。

### 2.4 凭证元数据结构

```rust
pub struct CredentialMetadata {
    pub credential_type: String,    // "表达能力认证"
    pub level: String,               // "认证表达者" / "流畅表达者" / ...
    pub metrics: CredentialMetrics,   // 指标快照
    pub improvement_curve: String,   // 进步曲线描述
    pub issued_at: String,           // 铸造时间（区块时间戳）
    pub soulbound: bool,             // true（不可转移）
    pub minter: String,              // Q 系统授权地址
}

pub struct CredentialMetrics {
    pub fluency: u64,     // 流畅度 0-100
    pub logic: u64,       // 逻辑度 0-100
    pub reception: u64,   // 接收度 0-100
    pub stall_rate: u64,  // 卡壳率 次/小时 × 10
}
```

### 2.5 里程碑类型

```rust
pub enum MilestoneType {
    FirstSpeech,           // 首次演讲 → 入门表达者
    HighScore,             // 高分演讲 → 优秀表达者
    FluencyBreakthrough,   // 流畅度突破 → 流畅表达者
    StallRateDown,         // 卡壳率下降 → 进步显著
    FillerReduction,       // 口头禅减少 → 表达精炼
    Comprehensive,         // 综合里程碑 → 认证表达者
}
```

### 2.6 状态存储

| 存储 Key | 内容 |
|---------|------|
| `credentials` | 全部凭证列表（Vec<Credential>） |
| `next_id` | 下一个凭证 ID（u64，从 1 递增） |
| `admin` | 管理员地址（InstantiateMsg 设置） |

---

## 三、编译验证

```bash
$ cd Q/injective
$ rustup target add wasm32-unknown-unknown  # 添加 wasm 编译目标
$ cargo build --target wasm32-unknown-unknown --release
   Compiling q-credential-contract v0.2.0
    Finished `release` profile [optimized] target(s) in 0.18s

$ ls -lh target/wasm32-unknown-unknown/release/q_credential_contract.wasm
-rwxr-xr-x  224K  q_credential_contract.wasm
```

✅ 编译成功，产出 224KB 优化的 wasm 文件。

---

## 四、后端集成（InjectiveService + WalletService）

### 4.1 钱包登录（WalletService）

**文件**：`Q/backend/src/services/wallet.ts`

```
PICO 空间显示 WalletConnect 二维码
  → WalletService.generateConnectUri() → 生成 WC v2 URI
  → 用户用手机钱包（Keplr/MetaMask）扫码 → 授权
  → WS: {type:"wallet_connect", action:"connect", address:"inj1...", walletType:"keplr"}
  → WalletService.connectWallet() → 存储 WalletAddress → 绑定到会话
```

**支持的钱包**：Keplr / MetaMask / WalletConnect / Leap / Ledger（通过 Injective WalletStrategy 统一接口）

### 4.2 铸造服务（InjectiveService）

**文件**：`Q/backend/src/services/injective.ts`

**铸造流程**：
```
① 里程碑达成（或手动触发）→ 生成铸证数据
② 构造 CosmWasm ExecuteMsg (mint)
③ MsgBroadcaster 签名+广播交易到 Injective 网络
④ 合约执行 mint → 绑定到用户钱包地址（soulbound: transferable=false）
⑤ 返回交易哈希 → 推送 credential_minted 给 PICO 空间 3D 卡片展示
```

**SDK 集成路径**（@injectivelabs/sdk-ts）：
```typescript
// 1. 初始化网络（测试网）
const network = Network.Testnet
const endpoints = getNetworkEndpoints(network)
const client = new TxGrpcApiClient({ ...endpoints, chainId: config.injective.chainId })

// 2. 创建 WalletStrategy（Q 系统 Minter 钱包）
const walletStrategy = new WalletStrategy({
  wallet: Wallet.Mnemonic,
  mnemonic: config.injective.mnemonic,
  chainId: config.injective.chainId,
})

// 3. 构造 CosmWasm mint 消息
const executeMsg = { mint: { recipient, milestone, metadata } }

// 4. 构造 MsgExecuteContract 并广播
const msg = MsgExecuteContract.fromJSON({
  sender: minterAddress,
  contractAddress: config.injective.contractAddress,
  exec: executeMsg,
})

// 5. MsgBroadcaster 签名+广播
const txHash = await client.broadcast({ walletStrategy, msgs: [msg] })
```

### 4.3 里程碑引擎（MilestoneEngine）

**文件**：`Q/backend/src/services/milestone.ts`

| 里程碑类型 | 触发条件 | 凭证等级 |
|-----------|---------|---------|
| 首次演讲 | 用户首个完成的会话 | 入门表达者 |
| 高分演讲 | overallScore >= 8 | 优秀表达者 |
| 流畅度突破 | 连续 3 次会话流畅度 > 8 | 流畅表达者 |
| 卡壳率下降 | 卡壳率较初始下降 > 50% | 进步显著 |
| 口头禅减少 | 口头禅率 < 5次/分钟 | 表达精炼 |
| 综合里程碑 | 三维分均 > 7.5 且无卡壳 | 认证表达者 |

---

## 五、PICO 空间展示

### 5.1 凭证 3D 卡片

铸造成功后，后端推送 `credential_minted` 消息：
```json
{
  "type": "credential_minted",
  "txHash": "inj_tx_...",
  "milestone": "首次演讲",
  "credentialType": "表达能力认证",
  "level": "入门表达者",
  "metrics": { "fluency": 82, "logic": 78, "reception": 80, "stallRate": 3.2 },
  "improvement": "卡壳率 2次/5分钟"
}
```

PICO Unity 的 `CredentialCardSpawner.cs` 接收此消息，在空间中渲染 3D 卡片：
- 卡片正面：凭证类型 / 等级 / 指标数据 / 进步曲线 / 链上交易哈希
- 铸造瞬间：粒子效果 + 凭证卡片生成动画
- 用户可"拿起"卡片翻转查看背面详情

### 5.2 链上验证

```
Injective 区块链浏览器查询交易哈希
  → 验证凭证真实性（链上不可篡改）
  → soulbound 标记确保不可转移/伪造

B 端 API 验证（商业化阶段二）:
  企业 HR / 猎头通过 Injective API 查询用户钱包地址
  → 返回凭证列表 + 指标数据
  → 替代面试中"沟通能力"的主观评价
```

---

## 六、部署指南

### 6.1 编译合约

```bash
cd Q/injective
rustup target add wasm32-unknown-unknown
cargo build --target wasm32-unknown-unknown --release
# 产出: target/wasm32-unknown-unknown/release/q_credential_contract.wasm
```

### 6.2 部署到 Injective 测试网

```bash
# 使用 injectived CLI 部署
injectived tx wasm store q_credential_contract.wasm \
  --from <minter-key-name> \
  --chain-id injective-888 \
  --gas auto --gas-adjustment 1.5

# 实例化合约
injectived tx wasm instantiate <code_id> \
  '{"admin":"<minter-address>"}' \
  --from <minter-key-name> \
  --chain-id injective-888 \
  --label "q-credential-contract"

# 记录合约地址到 .env
echo "INJECTIVE_CONTRACT_ADDRESS=inj1..." >> Q/backend/.env
```

### 6.3 后端配置

```bash
# Q/backend/.env
INJECTIVE_RPC=https://testnet-rpc.injective.dev
INJECTIVE_REST=https://testnet-lcd.injective.dev
INJECTIVE_MNEMONIC=<minter-mnemonic>
INJECTIVE_CHAIN_ID=injective-888
INJECTIVE_CONTRACT_ADDRESS=inj1...
```

---

## 七、验证结论

| 验证项 | 状态 | 说明 |
|-------|------|------|
| Rust 合约编译 | ✅ 通过 | wasm32-unknown-unknown release 224KB |
| Soulbound 转移拒绝 | ✅ 实现 | Transfer 变体始终返回错误 |
| 消息类型完整 | ✅ | Instantiate / Mint / Revoke / Transfer / 3 个 Query |
| 里程碑类型 | ✅ | 6 种里程碑类型（首次/高分/流畅/卡壳/口头禅/综合） |
| 元数据结构 | ✅ | credential_type / level / metrics / improvement / soulbound / minter |
| 后端 SDK 集成 | ✅ | InjectiveService + WalletService + MilestoneEngine |
| 钱包登录 | ✅ | WalletConnect QR URI 生成 + 地址绑定 |
| PICO 空间展示 | ✅ | CredentialCardSpawner.cs 3D 卡片 + 粒子动画 |
