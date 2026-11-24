# AdventureX 26 智能挂牌赛后使用指南

> 📌 **第三方指南声明**
>
> 本文档由 AdventureX 26 参赛选手编写，为非官方第三方赛后使用指南，旨在帮助参赛者更好地在赛后使用智能挂牌设备。内容基于 [Quote/0 官方文档](https://dot.mindreset.tech/) 与 [AdventureX 官方文档](https://adventurex.feishu.cn/docx/XAjDddUNto4QlrxCQw2cWyKYnYb) 整理，如有出入请以官方文档为准。
>
> 编写日期：2026-07-28 ｜ 适用于 Quote/0 智能墨水屏设备（AdventureX 26 限定挂牌版本）
>
> 📎 飞书文档：[查看在线版本](https://my.feishu.cn/docx/PsbTdtY4MohhZTxClTLclmHtnLd)

---

## 📌 写在前面

AdventureX 26 赛事落幕了，但你的智能挂牌旅程才刚刚开始。

你手里那块沉甸甸的挂牌，本质上是一台完整的 **Quote/0 智能墨水屏设备**--2.66 英寸 E-Ink 屏幕、296×152 分辨率、内置 800mAh 电池、续航最长一年。它的内部电路和固件与标准版 Quote/0 **完全一致**，所有官方文档、API 和开发者工具通用。

> ⚠️ **硬件差异提醒**
>
> AdventureX 限定挂牌的**外壳和外观**与标准版 Quote/0 不同。社区中为标准版 Quote/0 设计的桌面支架、显示器挂架等 3D 打印配件**可能无法直接适配**。目前推荐使用磁吸或粘贴等通用方案；如需专用支架，可等待社区大佬完善开发，或需要更新文档请联系 @杜宇轩。

本指南不是手把手教程，而是一张「赛后使用地图」--帮你快速找到关键资源，理解核心操作，并探索把挂牌变成桌面挂屏的多种玩法。

> 📖 **本指南包含以下内容：**
>
> - **一、基础操作指引** - 核心链接收藏、碰一碰 NFC、充电、重置网络（换 Wi-Fi）
> - **日常使用注意事项** - 防水、清洁、贴纸维护、磁铁干扰、维修
> - **二、挂屏与美化方案** - 继续使用腕带、磁吸方案（含胶水选择与圆形磁铁推荐）、粘贴方案
> - **三、开发玩法** - 文本/图片 API、画板 API（Canvas）、AI 辅助生成（Dot Skill）、社区项目推荐
> - **四、内容灵感** - 赛后可显示的内容类型、循环/固定内容模式、分享给家人
> - **延伸资源** - 社区软硬件项目与官方资源汇总

---

## 一、基础操作指引

### 📎 1.1 相关链接指引

以下是赛后持续使用挂牌所需的核心资源，建议收藏：

| 资源 | 链接 | 用途 |
|------|------|------|
| **Dot. 官网** | [dot.mindreset.tech](https://dot.mindreset.tech/) | 产品主页与文档入口 |
| **Dot. App 下载** | [App 下载页](https://dot.mindreset.tech/app/download) | iOS / Android 客户端 |
| **配对指南** | [如何配对 Quote/0](https://dot.mindreset.tech/docs/quote_0/start/how_to_pair) | 首次激活与绑定设备 |
| **碰一碰指南** | [How to Tap](https://dot.mindreset.tech/docs/quote_0/start/action) | NFC 触碰唤起 App 说明 |
| **充电说明** | [How to Charge](https://dot.mindreset.tech/docs/quote_0/start/how_to_charge) | Type-C 充电与状态灯 |
| **重置网络** | [Reset Network](https://dot.mindreset.tech/docs/quote_0/firmware/reset_network) | 换 Wi-Fi / 网络故障时使用 |
| **重置设备** | [Reset Device](https://dot.mindreset.tech/docs/quote_0/firmware/reset_device) | 恢复出厂设置 |
| **更新固件** | [Update Device](https://dot.mindreset.tech/docs/quote_0/firmware/update_firmware) | 网页工具在线更新 |
| **FAQ** | [常见问题](https://dot.mindreset.tech/device/quote/faq) | 配对问题、网络切换等排障 |
| **诊断工具** | [Diagnostic Tool](https://dot.mindreset.tech/docs/dot/diagnostic_tool) | 检查网络/服务/设备状态 |
| **开发者平台** | [开发者平台](https://dot.mindreset.tech/developers) | API 密钥管理与开发文档 |
| **开放 API** | [Open API 文档](https://dot.mindreset.tech/docs/service/open) | 文本/图片/画板 API 参考 |
| **社区共创** | [Co Create](https://dot.mindreset.tech/docs/service/co_create) | 社区软硬件项目合集 |
| **分享设备** | [与亲友一同分享](https://dot.mindreset.tech/docs/quote_0/start/share_device) | 多人共管设备 |

> 💡 **App 兼容性**：iOS 17+ / Android 12L+ / HarmonyOS 4.0 及以下可直接安装；HarmonyOS NEXT 以上需使用[卓易通](https://www.droitong.com/)安装。

### 📱 1.2 碰一碰（NFC）

用支持 NFC 的手机轻触挂牌右侧空白区域（Bonjour! 贴纸处），即可唤起 Dot. App 查看或操作当前内容。碰一碰时设备无需唤醒或接电。

> 💡 部分带磁铁或金属材质的手机壳可能干扰 NFC 信号，使用时建议取下手机壳。

### ⚡ 1.3 充电

Type-C 接口充电，约 1.5 小时充满。充电时白色状态灯闪烁，充满后常亮。首次使用建议充满。

### 🔄 1.4 如何重置网络

#### 为什么需要重置网络？

Quote/0 在设计之初被定位为「智能家居」设备，使用场景是**固定在一个稳定的 Wi-Fi 网络中**。因此它**不支持多网络切换**--不像手机那样可以随意切换 Wi-Fi。

比赛期间你连接的是场内 Wi-Fi（`ADVX-players`），赛后回到家需要换成家庭网络，就必须通过**重置网络**来完成切换。

> 🔧 **一句话总结**：换 Wi-Fi = 重置网络 + 重新配对。

#### 重置网络步骤

重置网络需通过电脑连接设备，使用**网页工具**（Web Serial）完成。需准备：支持数据传输的 Type-C 数据线 + Chromium 内核浏览器（Chrome / Edge / Arc 等）。完整操作步骤请查看 [官方文档](https://dot.mindreset.tech/docs/quote_0/firmware/reset_network)。

> 🔧 **简要流程：** 连接数据线 → 打开网页工具 → 点击「连接设备」→ 选择设备 → 点击「重置网络」→ 用 Dot. App 重新配对新 Wi-Fi。
>
> ⚠️ 如果屏幕无变化：断开数据线，刷新网页，重新连接后再试。

#### 内容保留规则

重新配对时，如果使用的是**与之前相同的 Dot. 账号**，设备上之前设定的内容**不会丢失**；如果换成了不同账号（比如朋友的），之前的内容将被清除。

---

## ⚠️ 日常使用注意事项

> - **防水**：❌ 设备不防水，进水请立即拆卸干燥，避免电路腐蚀
> - **清洁**：软布干擦即可，❌ 勿用酒精、有机溶剂或腐蚀性清洁剂
> - **贴纸**：表面为不干胶贴纸，勿触及液体；可联系官方更换 UV 水晶贴
> - **磁铁干扰**：粘贴磁铁时避开右侧 NFC 区域（Bonjour! 贴纸处）
> - **维修**：如遇贴纸破损、电路损坏等，可联系官方维修（[服务与维修](https://dot.mindreset.tech/docs/quote_0/repair_pricing)）
>
> 更多注意事项请查看 [官方 FAQ](https://dot.mindreset.tech/device/quote/faq)。

---

## 二、挂屏与美化方案

赛场上，挂牌是你的身份标识；赛后，它可以变成你桌面、显示器边框上的一块精致墨水屏。以下是几种让挂牌「更好看、更好用」的方案。

### 📿 2.1 继续使用腕带 / 挂绳

最简单的方案--**什么都不用改，继续用**。

AdventureX 挂牌自带的腕带 / 挂绳本身就是一种便携方案：

- **桌面摆放**：将腕带绕在桌灯支架、显示器底座或书架隔板上，挂牌自然垂挂，墨水屏朝外即可当作信息面板。
- **墙面悬挂**：在墙上钉一颗小钉子或挂钩，用腕带挂环挂上，秒变墙面电子相框。
- **随身佩戴**：继续当作手环 / 挂牌使用，通勤、出差时携带，碰一碰即可查看内容。

> 💡 **优势**：零改造、零成本、可随时取下。适合不想动手 DIY 的同学。

### 🧲 2.2 磁吸方案（推荐）

#### 方案思路

在挂牌背面粘贴磁铁或引磁贴，使其可以吸附在显示器边框、金属桌面、冰箱等铁磁表面上。由于 AdventureX 挂牌外壳与标准版 Quote/0 不同，**磁吸是最通用、最不受外壳形状限制的方案**。

#### 方案 A：引磁贴 + 磁吸底座

这是 Quote/0 官方桌面支架采用的思路：

1. 在挂牌背面粘贴一片**引磁贴**（薄铁片，带 3M 背胶，常见于手机磁吸车载支架配件）。
2. 在桌面 / 显示器上固定一个**磁吸底座**（含钕磁铁）。
3. 设备靠近底座时自动吸附，可随时取下。

> 📎 **官方参考**：Quote/0 标准版桌面支架（[MakerWorld 模型](https://makerworld.com.cn/zh/models/1653938-quote-0-zhuo-mian-zhi-jia)）需配合引磁贴使用。注意：该 3D 打印支架是针对标准版 Quote/0 外壳设计的，AdventureX 挂牌可能无法直接卡入，但引磁贴 + 独立磁吸底座的思路通用。

#### 方案 B：直接在背面粘磁铁

在挂牌背面直接粘上小磁铁，然后吸附到显示器边框等铁磁表面：

1. 在挂牌背面（避开 NFC 区域）粘 2-4 颗小磁铁。
2. 将挂牌直接吸附在显示器金属边框、金属置物架或冰箱门上。

> 💡 适合显示器边框较薄、金属材质的场景。如果你的显示器边框不是金属的，可以在边框上贴一片引磁贴作为配合面。

#### 粘磁铁用什么胶水？

针对「在挂牌背面粘磁铁」这一需求，以下是几种胶水的对比：

| 胶水类型 | 粘接强度 | 固化时间 | 可拆卸性 | 推荐度 | 说明 |
|----------|---------|---------|---------|--------|------|
| **3M VHB 双面胶** | ★★★★ | 即粘即用 | 较好（可撕下） | ⭐⭐⭐⭐⭐ | 社区方案首选。强力、干净、可移除。选薄款（0.5-1mm）。推荐直接买带背胶的成品磁铁。 |
| **AB 环氧树脂胶** | ★★★★★ | 12-24h | 不可逆 | ⭐⭐⭐⭐ | 最强永久粘接。选「金属粘塑料」型 AB 胶。 |
| **UV 无影胶** | ★★★★ | 数秒（需 UV 灯） | 不可逆 | ⭐⭐⭐ | 固化后透明无痕，需配合 UV 灯。 |
| **502 / 瞬间胶** | ★★★ | 数秒 | 不可逆 | ⭐⭐ | ⚠️ 不推荐。脆性大，易脱落；可能腐蚀磁铁镀镍层。 |
| **热熔胶** | ★★ | 数十秒 | 可加热拆卸 | ⭐⭐ | 粘接力弱。仅适合临时固定。 |
| **玻璃胶 / 硅酮胶** | ★★ | 24-48h | 可切割拆除 | ⭐⭐ | ⚠️ 非结构胶，粘接力有限。不推荐用于粘磁铁。 |

> ✅ **最佳推荐**
> 1. **首选**：带 3M 背胶的钕磁铁成品（一步到位，免涂胶，粘接力强，可清洁撕下）
> 2. **次选**：3M VHB 双面胶 + 普通钕磁铁（灵活度高，可自选磁铁规格）
> 3. **永久固定**：AB 环氧胶（确定不需要取下磁铁时，粘接最牢固）

#### 圆形小磁铁更好吗？

**是的，圆形小磁铁是更好的选择。** 原因如下：

| 对比维度 | 圆形小磁铁 | 方形 / 条形磁铁 |
|---------|-----------|---------------|
| **磁力分布** | 均匀，吸附平稳 | 边角磁力集中，可能翘角 |
| **厚度** | 可做到 1-2mm 超薄 | 通常较厚，设备凸起明显 |
| **贴合度** | 圆形背面贴合更自然 | 方形棱角可能影响贴合 |
| **NFC 干扰** | 体积小，磁场范围可控 | 面积大，更易干扰 NFC |
| **多颗布局** | 可均匀分布 2-4 颗，磁力均衡 | 不易灵活布局 |
| **美观度** | 小巧隐蔽 | 较显眼 |

**推荐规格：**
- **材质**：钕铁硼强磁（N35 / N42 / N52 均可，N35 足够）
- **镀层**：镍铜镍三层镀（防锈防腐）
- **尺寸推荐**：
  - **D5×2mm**：超薄隐蔽，建议 4 颗分布在四角
  - **D8×2mm（⭐ 推荐）**：厚度与磁力的最佳平衡，2-3 颗即可
  - **D10×3mm**：磁力强劲，适合大屏场景，1-2 颗足够
- **背胶**：优先选购**带 3M 背胶**的成品款

> 💡 **采购建议**：淘宝 / 京东搜索「带背胶钕磁铁 圆形」，选择直径 8mm、厚度 2mm、带 3M 胶的款式，买 4-6 颗备用。单价通常 0.5-2 元/颗。

#### ⚠️ 磁吸方案注意事项

- **避开 NFC 区域**：挂牌的 NFC 感应区在右侧空白区域（Bonjour! 贴纸处）。磁铁可能干扰 NFC 信号，粘贴磁铁时请**避开设备右侧**，尽量贴在背面的中上部或左侧。
- **避免遮挡充电口**：左侧有 Type-C 充电口，磁铁位置不要影响充电线插拔。
- **磁铁厚度**：选择薄磁铁（1-3mm），避免设备吸附后过度凸起。
- **磁力适中**：磁力太强可能导致取下困难；建议用多颗小磁铁分散分布，而非单颗大磁铁。

### 🩹 2.3 粘贴方案

如果不需要磁吸拆卸，直接将挂牌固定在桌面或墙面上也是一种简单有效的方案。

#### 方案 A：3M 双面胶 / VHB 胶带（推荐）

- **3M VHB 双面胶**：高强度结构胶带，粘接力强，撕下时不易留残胶。
- **3M 无痕胶贴**：粘接力稍弱但更易清理，适合需要经常取下的场景。

> 💡 **使用技巧**：先清洁粘贴表面（干布擦拭，勿用酒精），贴好后按压 30 秒，静置 24 小时达到最大粘力。

#### 方案 B：玻璃胶 / 硅酮密封胶

> ⚠️ **注意**：玻璃胶的主要用途是密封防水，并非结构粘接。粘接强度有限，固化时间长（24-48 小时），固化后呈弹性而非刚性。如果设备受到震动或碰撞，可能脱落。
>
> 如果一定要用玻璃胶，建议选择**中性硅酮胶**（非酸性，避免腐蚀设备表面），并在设备背面和桌面都涂胶，增加接触面积。但整体而言，3M VHB 双面胶或 AB 环氧胶是更可靠的选择。

#### 方案 C：纳米胶带 / 魔力胶

- **纳米双面胶**：可水洗重复使用，粘接力适中，适合临时固定。
- **无痕魔力扣**：3M 出品的可拆卸固定方案，适合光滑表面。

> 📎 **社区硬件参考**：标准版 Quote/0 社区已有多个 3D 打印显示器支架方案（[官方社区共创 - 硬件](https://dot.mindreset.tech/docs/service/co_create/hardware)），但需注意这些方案均针对标准版外壳设计。AdventureX 挂牌可参考其思路，但可能需要自行调整尺寸或等待适配版本。

---

## 三、开发玩法

Quote/0 不只是一块墨水屏，它还有完整的开放 API 和开发者工具。无论你是想用代码动态推送内容，还是用 AI 自动生成画板，都能实现。以下是三种控制方式，更多内容可查看 [Dot. 开发者平台](https://dot.mindreset.tech/developers)。

### 🔌 3.1 文本 / 图片 API

最直接的接入方式。通过 HTTP 请求向设备推送文字或图片，适合快速验证想法或与现有系统集成。

**文字 API：**
```bash
curl --request POST \
  --url https://dot.mindreset.tech/api/authV2/open/device/{{DEVICEID}}/text \
  --header 'Authorization: Bearer {{API_KEY}}' \
  --header 'Content-Type: application/json' \
  --data '{"refreshNow":true}'
```

**图片 API：**
```bash
curl --request POST \
  --url https://dot.mindreset.tech/api/authV2/open/device/{{DEVICEID}}/image \
  --header 'Authorization: Bearer {{API_KEY}}' \
  --header 'Content-Type: application/json' \
  --data '{"refreshNow":true,"image":"{{IMAGE}}"}'
```

> 💡 API 密钥在 Dot. App 中生成和管理。App 不会保存你的密钥，请自行备份。如怀疑泄露，可在 App 中删除并重新生成。

### 🎨 3.2 画板 API（Canvas）

如果你需要自定义布局，画板 API 提供了更细粒度的控制。你可以组合文本、图片、列表、进度条等元素，构建自己的信息面板。

画板使用 JSON 描述结构和内容，支持条件渲染（`$for`、`$ifAny`）和动态数据填充。适合做仪表盘、状态监控、数据可视化等场景。

```bash
curl --request POST \
  --url https://dot.mindreset.tech/api/authV2/open/device/{{DEVICEID}}/canvas \
  --header 'Authorization: Bearer {{API_KEY}}' \
  --header 'Content-Type: application/json' \
  --data '{"refreshNow":true,"data":{"badge":"DOT OPENAPI","title":"画板 API","message":"用 JSON 组合屏幕内容","status":"LIVE"},"windowData":{"default":[{"type":"div","props":{"tw":"flex flex-col w-full h-full","children":[{"type":"span","props":{"tw":"font-bold","children":"{{get inputData \"title\"}}"}}]}}]},"border":0}'
```

### 🤖 3.3 Dot Skill（AI 辅助生成）

如果你是一位真正的 Vibe Coder，或者只是单纯不想写代码，可以尝试用自然语言描述你想要的效果。只需要使用 Dot Skill 生成对应的画板 JSON 或直接推送内容，便可以马上看到原型！

```bash
npx skills add https://github.com/MindReset/dot_skill.git
```

安装后，剩下的交给 AI 吧！

### 🌐 3.4 社区项目

社区已有大量优秀的软硬件集成项目，可以直接使用或参考：

- **Home Assistant 集成** - 将 Quote/0 接入智能家居
- **天气日历** - 自动推送天气与日历信息
- **AI 使用量仪表盘** - 展示 Claude / Codex / DeepSeek 等的 API 用量
- **桌面状态** - 显示电脑系统状态
- **Bad Apple** - 在墨水屏上播放视频（实验性）
- **Quote/0 + 快捷指令** - iOS 快捷指令集成
- **MCP 工具** - 多个社区版本的 MCP 集成

> 🔗 更多社区项目：[官方社区共创 - 软件](https://dot.mindreset.tech/docs/service/co_create/software)

---

## 四、内容灵感：赛后可以显示什么？

设备配对到家庭网络后，通过 Dot. App 的「内容工坊」可以推送多种内容：

| 内容类型 | 说明 |
|---------|------|
| **天气信息** | 实时天气与预报 |
| **待办清单** | 滴答清单同步 |
| **日程安排** | 日历联动 |
| **RSS 订阅** | 新闻 / 博客聚合 |
| **热榜** | 豆瓣 / 微博等榜单 |
| **自定义文本 / 图片** | 个人座右铭、电子相框 |
| **API 推送** | 用代码动态推送内容 |

### 内容模式

> 🔄 **循环内容**：类似播放列表，多内容按设定间隔轮播（间隔可设 1 分钟 ~ 12 小时）。设备按照列表顺序依次显示，可在 App 中长按拖拽调整顺序。使用电池时，较长间隔可延长续航。

> 📅 **固定内容**：类似课程表，在指定时间段显示特定内容。例如设置每周一至周五早晨 7:00-9:00 显示天气，周六周日晚间 20:00-22:00 显示电影榜单。在固定内容设定的时间段之外，设备自动切换至循环内容模式。

### 分享给家人

通过 Dot. App 将设备分享给家人好友，双方都能查看和控制。详见 [分享设备指南](https://dot.mindreset.tech/docs/quote_0/start/share_device)。

---

## 📚 延伸资源

- **社区软件项目**：[官方共创 - 软件](https://dot.mindreset.tech/docs/service/co_create/software) - Home Assistant 集成、天气日历、AI 用量仪表盘、Bad Apple 视频播放等
- **社区硬件项目**：[官方共创 - 硬件](https://dot.mindreset.tech/docs/service/co_create/hardware) - 3D 打印支架、桌面充电底座、显示器挂架等
- **GitHub 文档源**：[MindReset/dot_web_docs](https://github.com/MindReset/dot_web_docs) - 官方文档开源仓库
- **Dot Skill**：`npx skills add https://github.com/MindReset/dot_skill.git` - AI 辅助生成画板布局

---

> 🎉 **祝你和你的 Quote/0 赛后生活愉快！** 这块小屏幕陪你走过了 AdventureX 2026 的赛场，接下来，它可以在你的桌面上继续陪伴每一天。
>
> 有问题？先查 [FAQ](https://dot.mindreset.tech/device/quote/faq)，再试 [诊断工具](https://dot.mindreset.tech/docs/dot/diagnostic_tool)，最后联系官方社区。
