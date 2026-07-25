#!/bin/bash
# 安装 + 启动后端 + 启动应用
echo "=== 安装 APK ==="
adb install -r /Volumes/Workbench/UnF/Q/Build/Q-Cue.apk 2>&1

echo ""
echo "=== 启动后端 ==="
lsof -ti:3001 | xargs kill -9 2>/dev/null
sleep 1
cd /Volumes/Workbench/UnF/Q/backend
npx tsx src/app.ts &
sleep 3
echo "后端: $(curl -s http://localhost:3001/api/health 2>/dev/null | head -c 80)"

echo ""
echo "=== 启动 Q-Cue ==="
adb shell am start -n com.AdventureX.QCue/com.unity3d.player.UnityPlayerActivity 2>&1

echo ""
echo "等待 5 秒..."
sleep 5

echo "=== Logcat ==="
adb logcat -d -t 200 2>&1 | grep -iE "QWS|websocket|connect|adventure|error|Unity|exception|10.0.2.2|session_started|score|hook" | grep -v "SPR-OpenXR|Choreographer|gralloc|BufferQueue|SurfaceView|InputTransport|ViewRootImpl|ActivityThread|InputDispatcher|WindowManager|libunity|alwayson" | head -20

echo ""
echo "=== 模拟器状态 ==="
adb shell "dumpsys activity activities 2>/dev/null" | grep -i "topResumed\|adventure\|qcue" | head -3

# 截屏
adb shell screencap -p /sdcard/screen2.png 2>&1
adb pull /sdcard/screen2.png /tmp/pico-screen2.png 2>&1
echo ""
echo "截屏已保存: /tmp/pico-screen2.png"
