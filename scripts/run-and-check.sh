#!/bin/bash
lsof -ti:3001 | xargs kill -9 2>/dev/null
sleep 1
cd /Volumes/Workbench/UnF/Q/backend
npx tsx src/app.ts &
SERVER_PID=$!
sleep 3
echo "后端状态: $(curl -s http://localhost:3001/api/health 2>/dev/null | head -c 100)"
echo ""
echo "模拟器访问后端 (10.0.2.2):"
adb shell curl -s http://10.0.2.2:3001/api/health 2>&1 | head -3
echo ""
echo "=== 应用 Logcat ==="
adb logcat -d -t 500 2>&1 | grep -i "QWebSocket\|websocket\|connect\|adventure\|Q-Cue\|unity\|error\|exception" | grep -v "SPR-OpenXR\|Choreographer\|gralloc\|BufferQueue\|SurfaceView\|InputTransport\|ViewRootImpl\|ActivityThread\|InputDispatcher\|WindowManager" | head -15
kill $SERVER_PID 2>/dev/null
