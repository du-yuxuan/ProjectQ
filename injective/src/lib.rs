//! Q (Cue) — Soulbound Expression Credential Contract (Module C)
//!
//! 在 Injective 链上铸造不可转移（soulbound）的表达能力凭证。
//! 每个凭证绑定到一个用户钱包地址，永久不可转移，用于证明用户在
//! 表达能力上的里程碑突破（流畅度 / 逻辑度 / 接收度 / 卡壳率等）。
//!
//! 合约遵循 v17 技术规格（详见 `Q/docs/模块C_Injective铸证合约.md`）：
//! - 仅 admin（Q 系统 Minter 授权地址）可调用 `Mint`
//! - 凭证 `soulbound = true`，`Transfer` 始终被拒绝
//! - 元数据包含 credential_type / level / metrics / improvement_curve / issued_at / soulbound / minter
//! - 里程碑类型：流畅度突破 / 卡壳率下降 / 口头禅减少 / 综合里程碑
//!
//! 部署目标：Injective 测试网（CosmWasm，编译目标 `wasm32-unknown-unknown`）

use cosmwasm_std::{
    entry_point, to_json_binary, Addr, Binary, Deps, DepsMut, Env, MessageInfo,
    Response, StdError, StdResult, Storage,
};
use serde::{Deserialize, Serialize};

// ============================================================
// 消息类型
// ============================================================

/// 实例化消息：设置唯一可铸造凭证的 admin（Q 系统 Minter 授权地址）
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct InstantiateMsg {
    /// 管理员地址（可铸造凭证的授权地址，即 Q 系统 Minter）
    pub admin: String,
}

/// 执行消息
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum ExecuteMsg {
    /// 铸造灵魂绑定凭证（仅 admin）
    Mint {
        /// 接收者钱包地址
        recipient: String,
        /// 里程碑类型
        milestone: MilestoneType,
        /// 凭证元数据（合约将强制 soulbound=true、minter=admin、issued_at=区块时间）
        metadata: CredentialMetadata,
    },
    /// 撤销凭证（仅 admin，用于测试 / 修复）
    Revoke { credential_id: u64 },
    /// 灵魂绑定守卫：始终拒绝，返回 `soulbound: transfer not allowed`
    ///
    /// 该变体存在的意义是让调用方明确：本合约不支持任何形式的凭证转移，
    /// 任何 `Transfer` 调用都会以错误终止，凭证永久绑定原 owner。
    Transfer {
        credential_id: u64,
        recipient: String,
    },
}

/// 查询消息
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum QueryMsg {
    /// 查询某地址的所有（未撤销）凭证
    CredentialsByOwner { owner: String },
    /// 查询单个凭证详情（含已撤销，返回体携带 `revoked` 字段）
    Credential { id: u64 },
    /// 查询合约信息：admin / total_minted / active_count
    ContractInfo {},
}

// ============================================================
// 里程碑类型（铸证触发条件，对应技术文档 5.4）
// ============================================================

/// 里程碑类型枚举。serde 序列化为 snake_case（如 `fluency_breakthrough`），
/// 链上存储机器可读的枚举值；中文标签与等级通过方法访问。
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum MilestoneType {
    /// 流畅度突破：流畅度分连续 3 次会话 > 80
    FluencyBreakthrough,
    /// 卡壳率下降：卡壳率较初始下降 > 50%
    StallRateDrop,
    /// 口头禅减少：口头禅率下降至 < 5 次/分钟
    FillerReduction,
    /// 综合里程碑：三维分均 > 75 且卡壳率 < 5 次/小时
    Comprehensive,
}

impl MilestoneType {
    /// 中文标签（里程碑名称）
    pub fn label(&self) -> &'static str {
        match self {
            MilestoneType::FluencyBreakthrough => "流畅度突破",
            MilestoneType::StallRateDrop => "卡壳率下降",
            MilestoneType::FillerReduction => "口头禅减少",
            MilestoneType::Comprehensive => "综合里程碑",
        }
    }

    /// 默认凭证等级（与里程碑类型对应，见技术文档 5.4）
    pub fn default_level(&self) -> &'static str {
        match self {
            MilestoneType::FluencyBreakthrough => "流畅表达者",
            MilestoneType::StallRateDrop => "进步显著",
            MilestoneType::FillerReduction => "表达精炼",
            MilestoneType::Comprehensive => "认证表达者",
        }
    }

    /// 触发条件说明
    pub fn trigger(&self) -> &'static str {
        match self {
            MilestoneType::FluencyBreakthrough => "流畅度分连续 3 次会话 > 80",
            MilestoneType::StallRateDrop => "卡壳率较初始下降 > 50%",
            MilestoneType::FillerReduction => "口头禅率下降至 < 5 次/分钟",
            MilestoneType::Comprehensive => "三维分均 > 75 且卡壳率 < 5 次/小时",
        }
    }
}

// ============================================================
// 元数据结构（对应技术文档 5.5 ⑤ Token 元数据）
// ============================================================

/// 三维能力指标快照
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct Metrics {
    /// 流畅度分（0-100，来自讯飞韵律 + 填充词密度）
    pub fluency: u32,
    /// 逻辑度分（0-100，来自阶跃星辰 连贯性 + 钩子质量）
    pub logic: u32,
    /// 接收度分（0-100，来自 PICO 观众专注/走神比）
    pub reception: u32,
    /// 卡壳率（次/小时）。使用字符串以规避链上浮点序列化的精度与兼容性问题，如 "3.2"
    pub stall_rate: String,
}

/// 凭证元数据（链上存储）。其中 `soulbound` / `minter` / `issued_at` 由合约强制写入，
/// 调用方传入的对应值会被覆盖，确保这三个字段不可伪造。
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct CredentialMetadata {
    /// 凭证类型，如 "表达能力认证"
    pub credential_type: String,
    /// 凭证等级，如 "认证表达者"
    pub level: String,
    /// 三维能力指标快照
    pub metrics: Metrics,
    /// 进步曲线描述，如 "卡壳率从 12 次/h 降至 3 次/h"
    pub improvement_curve: String,
    /// 铸造时间（ISO 8601 UTC，由合约从区块时间生成，如 "2026-07-25T03:20:00Z"）
    pub issued_at: String,
    /// 灵魂绑定标记（合约强制为 true）
    pub soulbound: bool,
    /// 铸造者地址（合约强制为 admin）
    pub minter: String,
}

// ============================================================
// 状态结构与存储
// ============================================================

const CREDENTIALS_KEY: &[u8] = b"credentials";
const NEXT_ID_KEY: &[u8] = b"next_id";
const ADMIN_KEY: &[u8] = b"admin";

/// 凭证主体
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct Credential {
    pub id: u64,
    pub owner: Addr,
    pub milestone: MilestoneType,
    pub metadata: CredentialMetadata,
    /// 铸造时的区块高度
    pub minted_at_height: u64,
    /// 铸造时间戳（Unix 秒）
    pub minted_at_time: u64,
    pub revoked: bool,
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct CredentialsResponse {
    pub credentials: Vec<Credential>,
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct ContractInfoResponse {
    pub admin: String,
    pub total_minted: u64,
    pub active_count: u64,
}

// ============================================================
// 辅助函数：读写状态
// ============================================================

fn read_next_id(storage: &dyn Storage) -> StdResult<u64> {
    Ok(storage.get(NEXT_ID_KEY).map_or(1u64, |v| {
        let mut bytes = [0u8; 8];
        bytes.copy_from_slice(&v);
        u64::from_be_bytes(bytes)
    }))
}

fn write_next_id(storage: &mut dyn Storage, id: u64) -> StdResult<()> {
    storage.set(NEXT_ID_KEY, &id.to_be_bytes());
    Ok(())
}

fn read_admin(storage: &dyn Storage) -> StdResult<Addr> {
    let raw = storage
        .get(ADMIN_KEY)
        .ok_or_else(|| StdError::generic_err("Admin not set"))?;
    let addr_str = String::from_utf8(raw)
        .map_err(|_| StdError::generic_err("Invalid admin encoding"))?;
    Ok(Addr::unchecked(addr_str))
}

fn write_admin(storage: &mut dyn Storage, admin: &str) -> StdResult<()> {
    storage.set(ADMIN_KEY, admin.as_bytes());
    Ok(())
}

fn read_all_credentials(storage: &dyn Storage) -> StdResult<Vec<Credential>> {
    let raw = storage.get(CREDENTIALS_KEY).unwrap_or_default();
    if raw.is_empty() {
        return Ok(vec![]);
    }
    let creds: Vec<Credential> = serde_json_wasm::from_slice(&raw)
        .map_err(|_| StdError::generic_err("Failed to deserialize credentials"))?;
    Ok(creds)
}

fn write_all_credentials(storage: &mut dyn Storage, creds: &[Credential]) -> StdResult<()> {
    let serialized = serde_json_wasm::to_vec(creds)
        .map_err(|_| StdError::generic_err("Failed to serialize credentials"))?;
    storage.set(CREDENTIALS_KEY, &serialized);
    Ok(())
}

/// Unix 秒 -> ISO 8601 UTC 字符串，如 `2026-07-25T03:20:00Z`。
///
/// 使用 Howard Hinnant 的 `civil_from_days` 算法，无外部依赖、可在 wasm 中运行。
/// 用途：为凭证元数据 `issued_at` 生成可读的链上时间戳。
fn format_iso8601(unix_seconds: u64) -> String {
    let days = (unix_seconds / 86_400) as i64;
    let secs = (unix_seconds % 86_400) as u64;
    let (year, month, day) = civil_from_days(days);
    let hour = secs / 3_600;
    let minute = (secs % 3_600) / 60;
    let second = secs % 60;
    format!(
        "{:04}-{:02}-{:02}T{:02}:{:02}:{:02}Z",
        year, month, day, hour, minute, second
    )
}

/// 纪元天数 (1970-01-01 起算) -> (年, 月, 日)。proleptic Gregorian。
fn civil_from_days(z: i64) -> (i64, u32, u32) {
    let z = z + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = (z - era * 146_097) as u64; // [0, 146096]
    let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365; // [0, 399]
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100); // [0, 365]
    let mp = (5 * doy + 2) / 153; // [0, 11]
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32; // [1, 31]
    let m = (if mp < 10 { mp + 3 } else { mp - 9 }) as u32; // [1, 12]
    (if m <= 2 { y + 1 } else { y }, m, d)
}

// ============================================================
// 入口点
// ============================================================

#[entry_point]
pub fn instantiate(
    deps: DepsMut,
    _env: Env,
    _info: MessageInfo,
    msg: InstantiateMsg,
) -> StdResult<Response> {
    let admin_addr = deps.api.addr_validate(&msg.admin)?;
    write_admin(deps.storage, admin_addr.as_str())?;
    write_next_id(deps.storage, 1u64)?;

    Ok(Response::new()
        .add_attribute("method", "instantiate")
        .add_attribute("admin", admin_addr.as_str()))
}

#[entry_point]
pub fn execute(
    deps: DepsMut,
    env: Env,
    info: MessageInfo,
    msg: ExecuteMsg,
) -> StdResult<Response> {
    match msg {
        ExecuteMsg::Mint {
            recipient,
            milestone,
            metadata,
        } => execute_mint(deps, env, info, recipient, milestone, metadata),
        ExecuteMsg::Revoke { credential_id } => {
            execute_revoke(deps, env, info, credential_id)
        }
        // 灵魂绑定：无条件拒绝所有转移调用
        ExecuteMsg::Transfer { credential_id, .. } => Err(StdError::generic_err(format!(
            "soulbound: transfer not allowed (credential_id={})",
            credential_id
        ))),
    }
}

fn execute_mint(
    deps: DepsMut,
    env: Env,
    info: MessageInfo,
    recipient: String,
    milestone: MilestoneType,
    mut metadata: CredentialMetadata,
) -> StdResult<Response> {
    // 仅管理员可铸造
    let admin = read_admin(deps.storage)?;
    if info.sender != admin {
        return Err(StdError::generic_err("Unauthorized: only admin can mint"));
    }

    // 验证接收者地址
    let recipient_addr = deps.api.addr_validate(&recipient)?;

    // 合约权威字段：强制 soulbound=true、minter=admin、issued_at=区块时间
    // （覆盖调用方传入值，确保这三个字段不可伪造）
    metadata.soulbound = true;
    metadata.minter = admin.as_str().to_string();
    metadata.issued_at = format_iso8601(env.block.time.seconds());

    // 若调用方未提供 level，按里程碑默认等级填充
    if metadata.level.trim().is_empty() {
        metadata.level = milestone.default_level().to_string();
    }

    // 生成新 ID
    let id = read_next_id(deps.storage)?;
    let credential = Credential {
        id,
        owner: recipient_addr.clone(),
        milestone: milestone.clone(),
        metadata: metadata.clone(),
        minted_at_height: env.block.height,
        minted_at_time: env.block.time.seconds(),
        revoked: false,
    };

    // 保存
    let mut creds = read_all_credentials(deps.storage)?;
    creds.push(credential.clone());
    write_all_credentials(deps.storage, &creds)?;
    write_next_id(deps.storage, id + 1)?;

    Ok(Response::new()
        .add_attribute("method", "mint")
        .add_attribute("credential_id", credential.id.to_string())
        .add_attribute("recipient", recipient_addr.as_str())
        .add_attribute("milestone", milestone.label())
        .add_attribute("level", metadata.level.as_str())
        .add_attribute("soulbound", "true"))
}

fn execute_revoke(
    deps: DepsMut,
    _env: Env,
    info: MessageInfo,
    credential_id: u64,
) -> StdResult<Response> {
    let admin = read_admin(deps.storage)?;
    if info.sender != admin {
        return Err(StdError::generic_err("Unauthorized: only admin can revoke"));
    }

    let mut creds = read_all_credentials(deps.storage)?;
    let mut found = false;
    for c in creds.iter_mut() {
        if c.id == credential_id {
            c.revoked = true;
            found = true;
            break;
        }
    }

    if !found {
        return Err(StdError::generic_err(format!(
            "Credential {} not found",
            credential_id
        )));
    }

    write_all_credentials(deps.storage, &creds)?;

    Ok(Response::new()
        .add_attribute("method", "revoke")
        .add_attribute("credential_id", credential_id.to_string()))
}

#[entry_point]
pub fn query(deps: Deps, _env: Env, msg: QueryMsg) -> StdResult<Binary> {
    match msg {
        QueryMsg::CredentialsByOwner { owner } => {
            let owner_addr = deps.api.addr_validate(&owner)?;
            let creds = read_all_credentials(deps.storage)?;
            let filtered: Vec<Credential> = creds
                .into_iter()
                .filter(|c| c.owner == owner_addr && !c.revoked)
                .collect();
            to_json_binary(&CredentialsResponse { credentials: filtered })
        }
        QueryMsg::Credential { id } => {
            let creds = read_all_credentials(deps.storage)?;
            let cred = creds
                .into_iter()
                .find(|c| c.id == id)
                .ok_or_else(|| StdError::generic_err(format!("Credential {} not found", id)))?;
            to_json_binary(&cred)
        }
        QueryMsg::ContractInfo {} => {
            let admin = read_admin(deps.storage)?.to_string();
            let creds = read_all_credentials(deps.storage)?;
            let total = creds.len() as u64;
            let active = creds.iter().filter(|c| !c.revoked).count() as u64;
            to_json_binary(&ContractInfoResponse {
                admin,
                total_minted: total,
                active_count: active,
            })
        }
    }
}

// ============================================================
// 测试模块
// ============================================================

#[cfg(test)]
mod tests {
    use super::*;
    use cosmwasm_std::testing::{
        mock_dependencies, mock_env, message_info, MockApi, MockQuerier, MockStorage,
    };
    use cosmwasm_std::{from_json, Api, BlockInfo, Empty, Env, OwnedDeps, Timestamp};

    /// 辅助：用 addr_make 生成有效地址，返回 (Addr, String)
    fn make_addr(api: &MockApi, seed: &str) -> (Addr, String) {
        let addr = api.addr_make(seed);
        let addr_str = addr.as_str().to_string();
        (addr, addr_str)
    }

    /// 辅助：构造一份示例元数据（issued_at / soulbound / minter 留空，由合约填充）
    fn sample_metadata() -> CredentialMetadata {
        CredentialMetadata {
            credential_type: "表达能力认证".to_string(),
            level: "认证表达者".to_string(),
            metrics: Metrics {
                fluency: 82,
                logic: 78,
                reception: 80,
                stall_rate: "3.2".to_string(),
            },
            improvement_curve: "卡壳率从 12 次/h 降至 3 次/h".to_string(),
            issued_at: String::new(),
            soulbound: false, // 故意传 false，验证合约强制覆盖为 true
            minter: String::new(),
        }
    }

    /// 辅助：构造一个指定区块时间的 Env（用于验证 issued_at）
    fn env_at_time(seconds: u64) -> Env {
        let mut env = mock_env();
        env.block = BlockInfo {
            height: 123_456,
            time: Timestamp::from_seconds(seconds),
            chain_id: "injective-888".to_string(),
        };
        env
    }

    /// 辅助：实例化合约，返回 (admin_addr, admin_str)
    fn setup(deps: &mut OwnedDeps<MockStorage, MockApi, MockQuerier, Empty>) -> (Addr, String) {
        let (admin, admin_str) = make_addr(&deps.api, "admin");
        let (creator, _) = make_addr(&deps.api, "creator");
        let info = message_info(&creator, &[]);
        instantiate(
            deps.as_mut(),
            mock_env(),
            info,
            InstantiateMsg { admin: admin_str.clone() },
        )
        .unwrap();
        (admin, admin_str)
    }

    #[test]
    fn test_instantiate_sets_admin() {
        let mut deps = mock_dependencies();
        let (admin, admin_str) = setup(&mut deps);

        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::ContractInfo {},
        )
        .unwrap();
        let info: ContractInfoResponse = from_json(&res).unwrap();
        assert_eq!(info.admin, admin.as_str());
        assert_eq!(admin_str, admin.as_str());
        assert_eq!(info.total_minted, 0);
        assert_eq!(info.active_count, 0);
    }

    #[test]
    fn test_mint_and_query_by_owner() {
        let mut deps = mock_dependencies();
        let (admin, _) = setup(&mut deps);
        let (_user, user_str) = make_addr(&deps.api, "user");

        // 用固定区块时间铸造，便于断言 issued_at
        let env = env_at_time(1_784_937_600); // 2026-07-25T00:00:00Z
        let res = execute(
            deps.as_mut(),
            env,
            message_info(&admin, &[]),
            ExecuteMsg::Mint {
                recipient: user_str.clone(),
                milestone: MilestoneType::Comprehensive,
                metadata: sample_metadata(),
            },
        )
        .unwrap();
        assert!(res
            .attributes
            .iter()
            .any(|a| a.key == "method" && a.value == "mint"));
        assert!(res
            .attributes
            .iter()
            .any(|a| a.key == "soulbound" && a.value == "true"));

        // 查询 owner 凭证
        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::CredentialsByOwner { owner: user_str },
        )
        .unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials.len(), 1);
        let c = &creds.credentials[0];
        assert_eq!(c.id, 1);
        assert!(!c.revoked);
        assert_eq!(c.milestone, MilestoneType::Comprehensive);
        assert_eq!(c.metadata.credential_type, "表达能力认证");
        assert_eq!(c.metadata.level, "认证表达者");
        assert_eq!(c.metadata.metrics.fluency, 82);
        assert_eq!(c.metadata.metrics.logic, 78);
        assert_eq!(c.metadata.metrics.reception, 80);
        assert_eq!(c.metadata.metrics.stall_rate, "3.2");
        assert_eq!(c.metadata.improvement_curve, "卡壳率从 12 次/h 降至 3 次/h");
        // 合约权威字段
        assert!(c.metadata.soulbound, "soulbound 必须被强制为 true");
        assert_eq!(c.metadata.minter, admin.as_str(), "minter 必须为 admin");
        assert_eq!(
            c.metadata.issued_at, "2026-07-25T00:00:00Z",
            "issued_at 必须由区块时间生成"
        );
    }

    #[test]
    fn test_unauthorized_mint() {
        let mut deps = mock_dependencies();
        let (_admin, _) = setup(&mut deps);
        let (_user, user_str) = make_addr(&deps.api, "user");
        let (attacker, _) = make_addr(&deps.api, "attacker");

        let res = execute(
            deps.as_mut(),
            mock_env(),
            message_info(&attacker, &[]),
            ExecuteMsg::Mint {
                recipient: user_str,
                milestone: MilestoneType::FluencyBreakthrough,
                metadata: sample_metadata(),
            },
        );
        assert!(res.is_err(), "非 admin 铸造必须失败");
    }

    #[test]
    fn test_soulbound_transfer_rejected() {
        let mut deps = mock_dependencies();
        let (admin, _) = setup(&mut deps);
        let (_user, user_str) = make_addr(&deps.api, "user");
        let (other, other_str) = make_addr(&deps.api, "other");

        // 先铸造一个凭证
        execute(
            deps.as_mut(),
            mock_env(),
            message_info(&admin, &[]),
            ExecuteMsg::Mint {
                recipient: user_str.clone(),
                milestone: MilestoneType::Comprehensive,
                metadata: sample_metadata(),
            },
        )
        .unwrap();

        // 任何身份（含 admin / owner / 第三方）调用 Transfer 都必须被拒绝
        for sender in [admin.clone(), other.clone()] {
            let res = execute(
                deps.as_mut(),
                mock_env(),
                message_info(&sender, &[]),
                ExecuteMsg::Transfer {
                    credential_id: 1,
                    recipient: other_str.clone(),
                },
            );
            let err = res.expect_err("Transfer 必须被拒绝（soulbound）");
            assert!(
                err.to_string().contains("soulbound: transfer not allowed"),
                "错误消息必须包含 soulbound 标识，实际: {}",
                err
            );
        }

        // 凭证依然属于原 owner，未被转移
        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::CredentialsByOwner { owner: user_str },
        )
        .unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials.len(), 1);
        assert_eq!(creds.credentials[0].owner, make_addr(&deps.api, "user").0);
    }

    #[test]
    fn test_revoke_and_contract_info() {
        let mut deps = mock_dependencies();
        let (admin, _) = setup(&mut deps);
        let (_u1, u1) = make_addr(&deps.api, "u1");
        let (_u2, u2) = make_addr(&deps.api, "u2");

        // 铸造两个凭证
        for (recip, ms) in [
            (u1.clone(), MilestoneType::FluencyBreakthrough),
            (u2.clone(), MilestoneType::StallRateDrop),
        ] {
            execute(
                deps.as_mut(),
                mock_env(),
                message_info(&admin, &[]),
                ExecuteMsg::Mint {
                    recipient: recip,
                    milestone: ms,
                    metadata: sample_metadata(),
                },
            )
            .unwrap();
        }

        // 撤销第一个
        execute(
            deps.as_mut(),
            mock_env(),
            message_info(&admin, &[]),
            ExecuteMsg::Revoke { credential_id: 1 },
        )
        .unwrap();

        // 合约信息：total=2, active=1
        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::ContractInfo {},
        )
        .unwrap();
        let info: ContractInfoResponse = from_json(&res).unwrap();
        assert_eq!(info.total_minted, 2);
        assert_eq!(info.active_count, 1);

        // 已撤销的 owner 查询应为空
        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::CredentialsByOwner { owner: u1 },
        )
        .unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials.len(), 0);

        // 单条查询仍可取回（携带 revoked=true）
        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::Credential { id: 1 },
        )
        .unwrap();
        let cred: Credential = from_json(&res).unwrap();
        assert!(cred.revoked);

        // 第二个凭证仍有效
        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::CredentialsByOwner { owner: u2 },
        )
        .unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials.len(), 1);
        assert_eq!(creds.credentials[0].milestone, MilestoneType::StallRateDrop);
    }

    #[test]
    fn test_non_admin_revoke() {
        let mut deps = mock_dependencies();
        let (admin, _) = setup(&mut deps);
        let (_user, user_str) = make_addr(&deps.api, "user");
        let (attacker, _) = make_addr(&deps.api, "attacker");

        execute(
            deps.as_mut(),
            mock_env(),
            message_info(&admin, &[]),
            ExecuteMsg::Mint {
                recipient: user_str,
                milestone: MilestoneType::Comprehensive,
                metadata: sample_metadata(),
            },
        )
        .unwrap();

        let res = execute(
            deps.as_mut(),
            mock_env(),
            message_info(&attacker, &[]),
            ExecuteMsg::Revoke { credential_id: 1 },
        );
        assert!(res.is_err(), "非 admin 撤销必须失败");
    }

    #[test]
    fn test_default_level_filled_when_empty() {
        let mut deps = mock_dependencies();
        let (admin, _) = setup(&mut deps);
        let (_user, user_str) = make_addr(&deps.api, "user");

        let mut meta = sample_metadata();
        meta.level = String::new(); // 调用方未提供 level
        execute(
            deps.as_mut(),
            mock_env(),
            message_info(&admin, &[]),
            ExecuteMsg::Mint {
                recipient: user_str.clone(),
                milestone: MilestoneType::FluencyBreakthrough,
                metadata: meta,
            },
        )
        .unwrap();

        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::CredentialsByOwner { owner: user_str },
        )
        .unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials[0].metadata.level, "流畅表达者");
        assert_eq!(
            MilestoneType::FluencyBreakthrough.default_level(),
            "流畅表达者"
        );
    }

    #[test]
    fn test_format_iso8601_anchors() {
        // Unix 纪元
        assert_eq!(format_iso8601(0), "1970-01-01T00:00:00Z");
        // 2025-01-01T00:00:00Z
        assert_eq!(format_iso8601(1_735_689_600), "2025-01-01T00:00:00Z");
        // 2026-07-25T00:00:00Z （当前项目周期）
        assert_eq!(format_iso8601(1_784_937_600), "2026-07-25T00:00:00Z");
        // 2026-07-25T03:20:15Z — 带时分秒（= 2026-07-25T00:00:00Z + 3h20m15s）
        assert_eq!(format_iso8601(1_784_949_615), "2026-07-25T03:20:15Z");
        // 闰日：2024-02-29（2024 为闰年）
        assert_eq!(format_iso8601(1_709_164_800), "2024-02-29T00:00:00Z");
    }

    #[test]
    fn test_milestone_type_metadata() {
        assert_eq!(MilestoneType::FluencyBreakthrough.label(), "流畅度突破");
        assert_eq!(MilestoneType::StallRateDrop.label(), "卡壳率下降");
        assert_eq!(MilestoneType::FillerReduction.label(), "口头禅减少");
        assert_eq!(MilestoneType::Comprehensive.label(), "综合里程碑");

        assert_eq!(
            MilestoneType::FluencyBreakthrough.default_level(),
            "流畅表达者"
        );
        assert_eq!(MilestoneType::StallRateDrop.default_level(), "进步显著");
        assert_eq!(MilestoneType::FillerReduction.default_level(), "表达精炼");
        assert_eq!(MilestoneType::Comprehensive.default_level(), "认证表达者");

        assert!(MilestoneType::FluencyBreakthrough
            .trigger()
            .contains("80"));
    }

    #[test]
    fn test_credential_id_monotonic() {
        let mut deps = mock_dependencies();
        let (admin, _) = setup(&mut deps);
        let (_user, user_str) = make_addr(&deps.api, "user");

        for _ in 0..3 {
            execute(
                deps.as_mut(),
                mock_env(),
                message_info(&admin, &[]),
                ExecuteMsg::Mint {
                    recipient: user_str.clone(),
                    milestone: MilestoneType::FillerReduction,
                    metadata: sample_metadata(),
                },
            )
            .unwrap();
        }

        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::CredentialsByOwner { owner: user_str },
        )
        .unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials.len(), 3);
        assert_eq!(creds.credentials[0].id, 1);
        assert_eq!(creds.credentials[1].id, 2);
        assert_eq!(creds.credentials[2].id, 3);
    }
}
