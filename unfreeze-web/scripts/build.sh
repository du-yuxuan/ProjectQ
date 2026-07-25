#!/bin/bash
# Unfreeze 构建脚本 — 前后端 + 合约
set -e

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$PROJECT_ROOT"

echo "🔨 构建 Unfreeze 验证版..."
echo ""

# --- 后端 ---
echo "📦 [1/3] 构建后端..."
cd "$PROJECT_ROOT/backend"
pnpm install --silent 2>/dev/null || npm install --silent
npx prisma generate
npx prisma db push
npx tsc --noEmit
echo "✅ 后端类型检查通过"
echo ""

# --- 前端 ---
echo "📦 [2/3] 构建前端..."
cd "$PROJECT_ROOT/frontend"
pnpm install --silent 2>/dev/null || npm install --silent
npx tsc --noEmit
npx vite build
echo "✅ 前端构建完成"
echo ""

# --- Injective 合约（可选，需要 Rust） ---
if command -v cargo &> /dev/null; then
    echo "📦 [3/3] 构建 CosmWasm 合约..."
    cd "$PROJECT_ROOT/injective"
    cargo build --release 2>/dev/null && echo "✅ 合约编译完成" || echo "⚠️  合约编译跳过（可能需要 cosmwasm-optimizer）"
else
    echo "⚠️  [3/3] 跳过合约构建 — 未安装 Rust/Cargo"
fi

echo ""
echo "🎉 构建完成！"
echo ""
echo "启动开发服务器:"
echo "  后端: cd backend && pnpm dev   (或 npm run dev)"
echo "  前端: cd frontend && pnpm dev (或 npm run dev)"
echo "  访问: http://localhost:5173"
