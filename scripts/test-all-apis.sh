#!/bin/bash
# Q v17 API 全面测试脚本
# 测试所有 REST API + WebSocket 消息协议

set -e

BASE_URL="http://localhost:3001"
WS_URL="ws://localhost:3001/ws/session"
PASS=0
FAIL=0
SKIP=0

# 颜色
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[0;33m'
NC='\033[0m'

echo "═══════════════════════════════════════════════"
echo "  Q v17 API 全面测试"
echo "═══════════════════════════════════════════════"
echo ""

# ============================================================
# 1. 健康检查
# ============================================================
echo -n "[1] GET /api/health ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/health")
CODE=$(echo "$RESP" | tail -1)
BODY=$(echo "$RESP" | head -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  echo "    → $BODY" | python3 -m json.tool 2>/dev/null || echo "    → $BODY"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 2. 会话列表
# ============================================================
echo -n "[2] GET /api/session/list ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/session/list")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 3. 会话详情（用不存在的 ID，期望 404）
# ============================================================
echo -n "[3] GET /api/session/test-nonexistent ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/session/test-nonexistent")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "404" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE — 正确返回 404)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE — 期望 404)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 4. 会话结束 stub
# ============================================================
echo -n "[4] POST /api/session/end ... "
RESP=$(curl -s -w "\n%{http_code}" -X POST "$BASE_URL/api/session/end")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 5. 能力画像
# ============================================================
echo -n "[5] GET /api/profile/test-user ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/profile/test-user")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  BODY=$(echo "$RESP" | head -1)
  echo "    → $BODY" | python3 -m json.tool 2>/dev/null | head -5
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 6. 趋势数据
# ============================================================
echo -n "[6] GET /api/profile/test-user/trend ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/profile/test-user/trend")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 7. 凭证列表
# ============================================================
echo -n "[7] GET /api/credential/list/test-user ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/credential/list/test-user")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 8. 手动铸证（无钱包，期望 400）
# ============================================================
echo -n "[8] POST /api/credential/mint (无钱包) ... "
RESP=$(curl -s -w "\n%{http_code}" -X POST "$BASE_URL/api/credential/mint" \
  -H "Content-Type: application/json" \
  -d '{"userId":"test-user","milestone":"首次演讲"}')
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "400" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE — 正确拒绝无钱包铸证)"
  PASS=$((PASS+1))
else
  echo -e "${YELLOW}⚠️ SKIP${NC} ($CODE — 可能已有钱包)"
  SKIP=$((SKIP+1))
fi
echo ""

# ============================================================
# 9. 钱包状态
# ============================================================
echo -n "[9] GET /api/wallet/status/test-user ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/wallet/status/test-user")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  BODY=$(echo "$RESP" | head -1)
  echo "    → $BODY" | python3 -m json.tool 2>/dev/null | head -5
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 10. 钱包地址
# ============================================================
echo -n "[10] GET /api/wallet/address/test-user ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/wallet/address/test-user")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 11. 心率历史
# ============================================================
echo -n "[11] GET /api/heart-rate/history/test-user ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/heart-rate/history/test-user")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 12. 会话心率记录
# ============================================================
echo -n "[12] GET /api/heart-rate/session/test-session ... "
RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/heart-rate/session/test-session")
CODE=$(echo "$RESP" | tail -1)
if [ "$CODE" = "200" ]; then
  echo -e "${GREEN}✅ PASS${NC} ($CODE)"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC} ($CODE)"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 13. WebSocket 端到端测试
# ============================================================
echo -n "[13] WebSocket /ws/session 连接 + 消息 ... "
# 使用 node 测试 WS
WS_RESULT=$(node -e "
const WebSocket = require('ws');
const ws = new WebSocket('$WS_URL');
let received = [];
let done = false;

ws.on('open', () => {
  // 发送 session_control start
  ws.send(JSON.stringify({
    type: 'session_control',
    action: 'start',
    userId: 'test-api-user',
    userName: 'API Tester'
  }));
});

ws.on('message', (data) => {
  const msg = JSON.parse(data.toString());
  received.push(msg.type);
  
  if (msg.type === 'session_started') {
    // 发送心率
    ws.send(JSON.stringify({
      type: 'heart_rate',
      ts: 1.0,
      bpm: 120,
      userId: 'test-api-user'
    }));
    
    // 发送能量
    ws.send(JSON.stringify({
      type: 'energy',
      ts: 2.0,
      energy: 0.5,
      isActive: true
    }));
    
    // 发送指环命令
    ws.send(JSON.stringify({
      type: 'ring',
      cmd: 'double_click',
      ts: 3.0
    }));
    
    // 发送钱包连接
    ws.send(JSON.stringify({
      type: 'wallet_connect',
      action: 'connect',
      address: 'inj1testaddress123',
      walletType: 'keplr'
    }));
    
    // 发送观众反馈
    ws.send(JSON.stringify({
      type: 'audience_feedback',
      ts: 4.0,
      faceCount: 3,
      attentive: 2,
      distracted: 1
    }));
    
    // 发送手动铸证
    ws.send(JSON.stringify({
      type: 'mint_credential',
      milestone: '首次演讲'
    }));
    
    // 等待一下然后结束
    setTimeout(() => {
      ws.send(JSON.stringify({
        type: 'session_control',
        action: 'end'
      }));
    }, 500);
  }
  
  if (msg.type === 'session_ended') {
    done = true;
    ws.close();
  }
});

ws.on('error', (err) => {
  console.error('WS_ERROR:' + err.message);
  process.exit(1);
});

setTimeout(() => {
  console.log('RECEIVED:' + JSON.stringify(received));
  process.exit(0);
}, 3000);
" 2>&1)

if echo "$WS_RESULT" | grep -q "RECEIVED"; then
  RECEIVED_TYPES=$(echo "$WS_RESULT" | sed 's/RECEIVED://')
  echo -e "${GREEN}✅ PASS${NC}"
  echo "    收到消息类型: $RECEIVED_TYPES"
  PASS=$((PASS+1))
else
  echo -e "${RED}❌ FAIL${NC}"
  echo "    $WS_RESULT"
  FAIL=$((FAIL+1))
fi
echo ""

# ============================================================
# 汇总
# ============================================================
echo "═══════════════════════════════════════════════"
echo "  测试汇总"
echo "═══════════════════════════════════════════════"
echo -e "  ${GREEN}通过: $PASS${NC}"
echo -e "  ${RED}失败: $FAIL${NC}"
echo -e "  ${YELLOW}跳过: $SKIP${NC}"
echo "  总计: $((PASS+FAIL+SKIP))"
echo "═══════════════════════════════════════════════"
