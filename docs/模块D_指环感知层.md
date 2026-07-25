# 模块D：指环感知层（RingDebugApp 复用）

> 项目：Q（Cue）
> 版本：v17
> 路径：`资料/RingDebugApp/`（验证版，v17 直接复用）
> 状态：✅ 已验证（Rokid 真机已跑通，PICO 可直接复用）

---

## 一、概述

RingDebugApp 是一个原生 Android App（Kotlin + AndroidX），连接 Zilo Whisper 指环调试全部功能。原在 Rokid 眼镜上验证，v17 架构下**可直接在 PICO 头显上安装复用运行**（PICO OS 基于 Android，BLE Central 可用）。

### v17 定位

| 角色 | 说明 |
|------|------|
| 协议层 | RingProtocol / RingBleManager 全部可跑，CRC16/编解码/包重组/命令常量/解析 |
| 调试面板 | App UI（480×640 小屏、大字号）在 PICO 上作为调试面板运行 |
| 正式 HUD | 由 PICO Unity 空间渲染接管（效果分/递钩/倒计时/凭证卡片），不使用本 App UI |

---

## 二、技术架构

四层架构，自底向上：

```
┌─────────────────────────────────────────────┐
│ UI 层   MainActivity (ViewBinding, 眼镜友好布局)  │  按钮/日志/IMU实时显示
├─────────────────────────────────────────────┤
│ 命令层  RingCommander                           │  高层命令封装(系统/日志/校时/录音/IMU)
├─────────────────────────────────────────────┤
│ BLE 层  RingBleManager                           │  扫描NUS/连接/启用TX通知/写RX分片/包回调
├─────────────────────────────────────────────┤
│ 协议层  RingProtocol                             │  CRC16/包编解码/PacketStream重组/命令常量/解析
└─────────────────────────────────────────────┘
```

**数据流**：
```
指环 BLE 通知(TX 6E400003) → RingBleManager.onCharacteristicChanged
  → PacketStream.feed 重组 → RingProtocol.decode → onPacket 回调
  → MainActivity.handlePacket 按命令分发解析 → UI 显示

下行: UI 按钮 → RingCommander → RingProtocol.encode 编包
  → RingBleManager.send 分片写 RX(6E400002)
```

---

## 三、已验证功能（全部指环功能）

| 功能 | 命令 | 实现 | v17 复用 |
|------|------|------|----------|
| 系统信息 | 0x0101 | getSystemInfo → 解析固件/时间/存储 | ✅ |
| 日志存储 | 0x0301 | getLogStorage | ✅ |
| 日志数据 | 0x0303 | readLogChunk(offset,length) | ✅ |
| 校时 | 0x0401/0x0402 | 收请求解析T1，应答T1T2T3 | ✅ |
| 录音数量 | 0x0501 | getAudioCount | ✅ |
| 快速提取 | 0x0509 | startQuickExtract(index) | ✅ |
| 录音帧 | 0x0505 | 实时显示 idx/off/size/end | ✅ |
| 清空录音 | 0x050B | clearAudio (破坏性) | ✅ |
| IMU开启 | 0x0601 | startSensor → 解析采样率/量程 | ✅ |
| IMU停止 | 0x0603 | stopSensor | ✅ |
| IMU数据 | 0x0605 | 解析16字节样本(ts+6轴) 实时显示 | ✅ |
| 双击事件 | 0x0701 | 实时日志 | ✅ |
| 手势事件 | 0x0702 | 解析gesture_id | ✅ |
| 按键双击 | 0x0703 | 实时日志 | ✅ |
| 按键单击 | 0x0704 | 切模式事件 | ✅ |

---

## 四、固件约束与 v17 处理

| 能力 | 固件支持 | v17 处理 |
|------|---------|---------|
| 双麦录音（Speex 16kHz） | ✅ 支持（0x0505） | 保留；PICO 麦克风为主 STT 源，指环双麦为降噪辅源 |
| IMU 6 轴微颤 | ✅ 支持（0x0601/0x0605） | 保留 |
| 按钮单击/双击/HMM 手势 | ✅ 支持（0x0701-0x0704） | 保留 |
| **震动提醒** | ❌ 固件不支持 | 演示期口头禅超频提醒改由 PICO 空间角标变红承担；震动列为〔Future〕 |
| **心率** | ❌ 固件不支持 | 演示用手动输入面板（PICO 空间数字键盘）模拟；真实心率列为〔Future〕 |

---

## 五、BLE 协议（NUS v4）

### 5.1 BLE 连接

- **扫描**：按 NUS 服务 UUID（6E400001）过滤，降级按设备名"ring"匹配
- **连接**：connectGatt(TRANSPORT_LE) → discoverServices → 取 NUS 服务/特性 → 启用 TX 通知
- **读写**：TX（6E400003）notify 收数据；RX（6E400002）写指令（分片，MTU≈20）

### 5.2 v4 包格式

```
包头 11 字节（大端序）:
  magic: 0x3F (1 byte)
  version: u16 (4)
  command: u16
  body_length: u32
  body_crc: u16

CRC16: 与 ring_sound.py crc16_compute 完全一致（初始 0xFFFF）

PacketStream: 累积 BLE 通知片段 → 按 magic 定位 → 按 body_length 取整包
  → CRC 校验 → 产出完整 Packet
```

---

## 六、PICO 复用路径

### 6.1 直接安装运行

PICO OS 基于 Android，RingDebugApp 作为原生 Android App 可直接安装：

```bash
# 构建 APK（需 Android SDK + Gradle）
cd RingDebugApp
./gradlew assembleDebug

# 安装到 PICO 头显
adb install app/build/outputs/apk/debug/app-debug.apk
```

### 6.2 Unity 集成路径

正式 HUD 由 PICO Unity 空间渲染，指环数据通过以下路径传入 Unity：

```
方案 1（推荐）: Android BLE → RingDebugApp 后台服务 → Android Native Plugin → Unity
  → RingInputBridge.cs 接收指环命令 → 映射到钩子操作（确认/消除/切换）

方案 2（降级）: 指环 → BLE → Android → WebSocket → 后端 → WS → Unity
  → 指环命令通过后端中转，增加延迟但架构统一
```

---

## 七、代码结构

```
RingDebugApp/
├── settings.gradle / build.gradle / gradle.properties / gradle/wrapper/
├── app/
│   ├── build.gradle                      # AGP8.5.2 Kotlin1.9.24 compileSdk34 minSdk26 target31
│   └── src/main/
│       ├── AndroidManifest.xml           # BLE/Bluetooth权限 + uses-feature bluetooth_le
│       ├── java/com/unfreeze/ringdebug/
│       │   ├── MainActivity.kt            # UI + 包调度分发
│       │   ├── RingBleManager.kt          # BLE 扫描/连接/读写/通知回调
│       │   ├── RingProtocol.kt            # CRC16/包编解码/PacketStream/命令常量
│       │   └── RingCommander.kt           # 高层命令封装
│       └── res/                           # 眼镜友好布局
```

---

## 八、验证结论

| 验证项 | 状态 | 说明 |
|-------|------|------|
| BLE NUS v4 协议 | ✅ 跑通 | CRC16/编解码/包重组/通知片段 |
| 系统信息/校时 | ✅ 跑通 | 0x0101/0x0401/0x0402 |
| 双麦录音提取 | ✅ 跑通 | 0x0505/0x0509 |
| IMU 6 轴批量数据 | ✅ 跑通 | 0x0601/0x0603/0x0605 |
| 按钮事件（单击/双击/手势） | ✅ 跑通 | 0x0701-0x0704 |
| PICO 复用 | ✅ 可直接安装 | PICO OS = Android，BLE Central 可用 |
| 震动反馈 | ❌ 固件不支持 | v17 列为〔Future〕 |
| 心率检测 | ❌ 固件不支持 | v17 用手动输入面板演示 |
