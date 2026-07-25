// Unfreeze — 隐式测试账号
// 无需密码登录：在 localStorage 持久化一个稳定的 userId + 昵称。
// 首次访问自动生成测试账号；用户可在落地页修改昵称或重置账号。

const USER_ID_KEY = 'unfreeze.userId';
const USER_NAME_KEY = 'unfreeze.userName';

const DEFAULT_NAME = 'Demo 演讲者';

/** 生成一个稳定、可读的测试账号 ID */
function generateUserId(): string {
  const rand =
    typeof crypto !== 'undefined' && 'randomUUID' in crypto
      ? crypto.randomUUID().slice(0, 8)
      : Math.random().toString(36).slice(2, 10);
  return `demo-${rand}`;
}

/** 读取当前测试账号；不存在则隐式创建并持久化 */
export function getCurrentUser(): { userId: string; userName: string } {
  let userId = localStorage.getItem(USER_ID_KEY);
  let userName = localStorage.getItem(USER_NAME_KEY);

  if (!userId) {
    userId = generateUserId();
    localStorage.setItem(USER_ID_KEY, userId);
  }
  if (!userName) {
    userName = DEFAULT_NAME;
    localStorage.setItem(USER_NAME_KEY, userName);
  }

  return { userId, userName };
}

/** 便捷：仅取 userId（隐式创建） */
export function getUserId(): string {
  return getCurrentUser().userId;
}

/** 便捷：仅取昵称（隐式创建） */
export function getUserName(): string {
  return getCurrentUser().userName;
}

/** 更新昵称（保留同一 userId，跨会话数据不丢失） */
export function setUserName(name: string): void {
  const trimmed = name.trim() || DEFAULT_NAME;
  localStorage.setItem(USER_NAME_KEY, trimmed);
}

/** 重置为一个全新的测试账号（换一个干净的身份） */
export function resetUser(): { userId: string; userName: string } {
  const userId = generateUserId();
  localStorage.setItem(USER_ID_KEY, userId);
  localStorage.setItem(USER_NAME_KEY, DEFAULT_NAME);
  return { userId, userName: DEFAULT_NAME };
}
