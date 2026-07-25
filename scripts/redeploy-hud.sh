#!/bin/bash
set -e
adb devices
if ! curl -s http://localhost:3001/api/health >/dev/null 2>&1; then
  cd /Volumes/Workbench/UnF/Q/backend
  npx tsx src/app.ts &
  sleep 3
fi
echo "后端: $(curl -s http://localhost:3001/api/health | head -c 80)"
echo "安装 APK..."
adb install -r /Volumes/Workbench/UnF/Q/Build/Q-Cue.apk
echo "启动..."
adb shell am force-stop com.AdventureX.QCue || true
sleep 1
adb shell am start -n com.AdventureX.QCue/com.unity3d.player.UnityPlayerActivity
sleep 8
echo "=== Logcat ==="
adb logcat -d -t 200 | grep -iE "HUD|QWS|SpatialHUD|QScene|已创建|已连接|session|WebSocket|error|exception" | grep -v "Secure MR\|PxrUnity\|Display\|SPR-\|Choreographer\|gralloc\|BufferQueue" | head -25
echo "=== 前台 ==="
adb shell dumpsys activity activities | grep -i "topResumed\|adventure" | head -3
adb shell screencap -p /sdcard/hud.png
adb pull /sdcard/hud.png /tmp/pico-hud.png
ls -lh /tmp/pico-hud.png
