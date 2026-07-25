#!/bin/bash
# 安装 APK + 启动后端 + 启动应用

echo "=== 1. 安装 APK ==="
adb install -r /Volumes/Workbench/UnF/Q/Build/Q-Cue.apk 2>&1

echo ""
echo "=== 2. 启动后端 ==="
lsof -ti:3001 | xargs kill -9 2>/dev/null
sleep 1
cd /Volumes/Workbench/UnF/Q/backend
npx tsx src/app.ts &
BACKEND_PID=$!
sleep 3
echo "后端状态: $(curl -s http://localhost:3001/api/health 2>/dev/null | head -c 80)"

echo ""
echo "=== 3. 启动 Q-Cue ==="
adb shell am start -n com.AdventureX.QCue/com.unity3d.player.UnityPlayerActivity 2>&1

echo ""
echo "等待 8 秒..."
sleep 8

echo "=== 4. Logcat ==="
adb logcat -d -t 300 2>&1 | grep -iE "QWS|websocket|connect|session_started|score|hook|error|exception|10.0.2.2|Unity" | grep -v "SPR-OpenXR|Choreographer|gralloc|BufferQueue|SurfaceView|InputTransport|ViewRootImpl|ActivityThread|InputDispatcher|WindowManager|libunity|alwayson|PxrUnityNative|Display" | head -20

echo ""
echo "=== 5. 模拟器状态 ==="
adb shell "dumpsys activity activities 2>/dev/null" | grep -i "topResumed\|adventure\|qcue" | head -3

echo ""
echo "后端 PID: $BACKEND_PID"
echo "完成后端会在后台运行"
