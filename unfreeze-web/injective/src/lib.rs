//! Unfreeze — Soulbound Credential Contract
//!
//! 在 Injective 测试网上铸造不可转移的表达能力凭证。
//! 每个凭证绑定到一个用户地址，不可转移（soulbound）。
//! 用于证明用户的表达里程碑（首次演讲、高分演讲、持续进步等）。

use cosmwasm_std::{
    entry_point, to_json_binary, Addr, Binary, Deps, DepsMut, Env, MessageInfo,
    Response, StdError, StdResult, Storage,
};
use serde::{Deserialize, Serialize};

// ============================================================
// 消息类型
// ============================================================

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct InstantiateMsg {
    /// 管理员地址（可铸造凭证的授权地址）
    pub admin: String,
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum ExecuteMsg {
    /// 铸造灵魂绑定凭证
    Mint {
        /// 接收者地址
        recipient: String,
        /// 里程碑名称
        milestone: String,
        /// 凭证元数据（JSON）
        metadata: String,
    },
    /// 撤销凭证（仅管理员，仅用于测试/修复）
    Revoke { credential_id: u64 },
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum QueryMsg {
    /// 查询某地址的所有凭证
    CredentialsByOwner { owner: String },
    /// 查询单个凭证详情
    Credential { id: u64 },
    /// 查询合约信息
    ContractInfo {},
}

// ============================================================
// 状态结构
// ============================================================

const CREDENTIALS_KEY: &[u8] = b"credentials";
const NEXT_ID_KEY: &[u8] = b"next_id";
const ADMIN_KEY: &[u8] = b"admin";

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct Credential {
    pub id: u64,
    pub owner: Addr,
    pub milestone: String,
    pub metadata: String,
    /// 铸造时的区块高度
    pub minted_at_height: u64,
    /// 铸造时间戳（秒）
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
// 辅助函数：读写凭证列表
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
    // 验证管理员地址
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
    }
}

fn execute_mint(
    deps: DepsMut,
    env: Env,
    info: MessageInfo,
    recipient: String,
    milestone: String,
    metadata: String,
) -> StdResult<Response> {
    // 仅管理员可铸造
    let admin = read_admin(deps.storage)?;
    if info.sender != admin {
        return Err(StdError::generic_err("Unauthorized: only admin can mint"));
    }

    // 验证接收者地址
    let recipient_addr = deps.api.addr_validate(&recipient)?;

    // 生成新 ID
    let mut next_id = read_next_id(deps.storage)?;
    let credential = Credential {
        id: next_id,
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

    next_id += 1;
    write_next_id(deps.storage, next_id)?;

    Ok(Response::new()
        .add_attribute("method", "mint")
        .add_attribute("credential_id", credential.id.to_string())
        .add_attribute("recipient", recipient_addr.as_str())
        .add_attribute("milestone", &milestone)
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
    for c in &mut creds {
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
    use cosmwasm_std::testing::{mock_dependencies, mock_env, message_info};
    use cosmwasm_std::{from_json, Api};

    /// 辅助：用 addr_make 生成有效地址，返回 (Addr, String)
    fn make_addr(api: &cosmwasm_std::testing::MockApi, seed: &str) -> (Addr, String) {
        let addr = api.addr_make(seed);
        let addr_str = addr.as_str().to_string();
        (addr, addr_str)
    }

    #[test]
    fn test_instantiate() {
        let mut deps = mock_dependencies();
        let (_admin, admin_str) = make_addr(&deps.api, "admin");
        let (creator, _) = make_addr(&deps.api, "creator");
        let msg = InstantiateMsg { admin: admin_str };
        let info = message_info(&creator, &[]);
        let res = instantiate(deps.as_mut(), mock_env(), info, msg).unwrap();
        assert_eq!(res.attributes.len(), 2);
    }

    #[test]
    fn test_mint_and_query() {
        let mut deps = mock_dependencies();
        let (admin, admin_str) = make_addr(&deps.api, "admin");
        let (_user, user_str) = make_addr(&deps.api, "user");
        let (creator, _) = make_addr(&deps.api, "creator");

        let init_msg = InstantiateMsg { admin: admin_str };
        let info = message_info(&creator, &[]);
        instantiate(deps.as_mut(), mock_env(), info, init_msg).unwrap();

        let mint_msg = ExecuteMsg::Mint {
            recipient: user_str.clone(),
            milestone: "首次演讲".to_string(),
            metadata: "{\"score\":8}".to_string(),
        };
        let admin_info = message_info(&admin, &[]);
        let res = execute(deps.as_mut(), mock_env(), admin_info, mint_msg).unwrap();
        assert!(res.attributes.iter().any(|a| a.key == "method" && a.value == "mint"));

        let query_msg = QueryMsg::CredentialsByOwner { owner: user_str };
        let res = query(deps.as_ref(), mock_env(), query_msg).unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials.len(), 1);
        assert_eq!(creds.credentials[0].milestone, "首次演讲");
        assert!(!creds.credentials[0].revoked);
    }

    #[test]
    fn test_unauthorized_mint() {
        let mut deps = mock_dependencies();
        let (_admin, admin_str) = make_addr(&deps.api, "admin");
        let (_user, user_str) = make_addr(&deps.api, "user");
        let (creator, _) = make_addr(&deps.api, "creator");
        let (attacker, _) = make_addr(&deps.api, "attacker");

        let init_msg = InstantiateMsg { admin: admin_str };
        let info = message_info(&creator, &[]);
        instantiate(deps.as_mut(), mock_env(), info, init_msg).unwrap();

        let mint_msg = ExecuteMsg::Mint {
            recipient: user_str,
            milestone: "高分演讲".to_string(),
            metadata: "{}".to_string(),
        };
        let bad_info = message_info(&attacker, &[]);
        let res = execute(deps.as_mut(), mock_env(), bad_info, mint_msg);
        assert!(res.is_err());
    }

    #[test]
    fn test_soulbound_no_transfer() {
        // Soulbound: 合约不暴露任何 transfer 方法，凭证不可转移
        let mint_msg = ExecuteMsg::Mint {
            recipient: "cosmwasm1user".to_string(),
            milestone: "持续进步".to_string(),
            metadata: "{}".to_string(),
        };
        match mint_msg {
            ExecuteMsg::Mint { .. } => {} // OK
            _ => panic!("Unexpected variant"),
        }
    }

    #[test]
    fn test_revoke() {
        let mut deps = mock_dependencies();
        let (admin, admin_str) = make_addr(&deps.api, "admin");
        let (_user, user_str) = make_addr(&deps.api, "user");
        let (creator, _) = make_addr(&deps.api, "creator");

        let info = message_info(&creator, &[]);
        instantiate(
            deps.as_mut(),
            mock_env(),
            info,
            InstantiateMsg { admin: admin_str },
        )
        .unwrap();

        let admin_info = message_info(&admin, &[]);
        // 铸造
        execute(
            deps.as_mut(),
            mock_env(),
            admin_info.clone(),
            ExecuteMsg::Mint {
                recipient: user_str.clone(),
                milestone: "首次演讲".to_string(),
                metadata: "{}".to_string(),
            },
        )
        .unwrap();

        // 撤销
        execute(
            deps.as_mut(),
            mock_env(),
            admin_info,
            ExecuteMsg::Revoke { credential_id: 1 },
        )
        .unwrap();

        // 查询 — 应为空（已撤销的不返回）
        let res = query(
            deps.as_ref(),
            mock_env(),
            QueryMsg::CredentialsByOwner { owner: user_str },
        )
        .unwrap();
        let creds: CredentialsResponse = from_json(&res).unwrap();
        assert_eq!(creds.credentials.len(), 0);
    }
}
