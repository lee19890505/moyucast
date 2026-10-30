# 更新日志

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 风格，版本号遵循语义化版本。

## [Unreleased]

### 修复

- **滚动条样式**：替换 WPF 默认浅色滚动条为深色细滚动条（圆角滑块、悬停高亮），与深色主题统一


## [v1.3.0] - 2026-10-28

### 新增

- **锁屏模式（虚拟屏投屏）**：新建一块虚拟显示，手机可锁屏/熄屏，投屏窗口照常可看、可点、可打字
  - 投屏页顶部「投屏模式」：镜像模式 / 锁屏模式
  - 锁屏模式下自动读取手机第三方 App（当前设备 59 个），支持**星标置顶**、刷新、原生图标（约 40% 命中，其余用彩色字母头像兜底）
  - App 选择改为**深色下拉框**，默认显示上次选择，选中后自动收起
  - 选好 App 点开始投屏 → 自动打开该 App → 自动锁屏（`input keyevent 26`）
- **启动前校验弹窗**：锁屏模式下手机未解锁、无线投屏不可用时，弹出**应用内深色提示层**（替代系统 MessageBox，支持 Esc / Enter 关闭）
- **保持唤醒原值恢复**：记录并还原 `stay_on_while_plugged_in`，避免污染用户设置；崩溃后下次启动自动恢复

### 变更

- **导航改为 4 个页签：设备 / 投屏 / 设置 / 更多**
  - 新增「投屏」页：投屏模式 + 启动应用
  - 「画面」与「声音」合并为「设置」页
  - 「更多」页不变：语言 / 检查更新 / 日志 / 关于
- 删除旧的「息屏投屏」（亮度归零）功能，由锁屏模式取代

### 修复

- 锁屏模式启动时窗口被误判为「鼠标离开」而自动隐藏
- 锁屏模式下自动隐藏失效（改为窗口稳定 800ms 后判定 + 启动抑制期）
- 无线投屏不可用时静默失败（改为弹窗说明原因）
- 检查更新按钮在英文下文字被截断

### English summary

**Added**
- Lock-screen mode: creates a virtual display so the phone can be locked while mirroring keeps running
  - Display mode switch (Mirror / Lock screen) on the Mirroring tab
  - Lists third-party apps with starred-first sorting, refresh, and native icons (~40% hit rate, letter avatars otherwise)
  - Dark dropdown app picker that remembers the last choice
  - Starts the selected app and locks the phone automatically
- Pre-start validation with an in-app dark dialog (phone locked / wireless unavailable)
- `stay_on_while_plugged_in` is saved and restored

**Changed**
- Navigation is now 4 tabs: Device / Mirroring / Settings / More
- Display and Audio settings merged into Settings
- Removed the legacy "dim the panel" feature in favour of lock-screen mode

**Fixed**
- Auto-hide misfired right after starting lock-screen mode
- Auto-hide stopped working in lock-screen mode
- Wireless failures were silent (now explained in a dialog)
- "Check for updates" was truncated in English

## [v1.2.0] - 2026-10-26

### 新增

- **离开即隐藏（更灵敏）模式**：无需先把鼠标移入投屏窗口，不在窗口上持续 0.8 秒就隐藏
- **主/子开关层级**：主开关关闭时收起子选项，打开后展开「触发方式」子面板（淡入动画 + 左侧蓝色竖条）
- **诊断日志开关**：把自动隐藏的判定写入 `%AppData%\MoyuCast\app.log`（超过 2MB 自动滚动）

### 修复

- 老板键隐藏后从托盘唤回主窗口时 `_bossHidden` 未复位，导致自动隐藏 / 比例锁定永久失效
- 自动隐藏的窗口稳定性判定改为 2px 容差，避免 1px 抖动导致永不触发

## [v1.1.1] - 2026-10-22

### 修复

- **鼠标移开自动隐藏误触发**：拖动投屏窗口时窗口跟随有延迟，会被误判为鼠标离开
  - 按住左键、或窗口处于移动/缩放循环时，不做离开判定
- **版本号不随 tag 更新**：`<Version>` 写死在 csproj，导致 v1.1.0 的包内部仍显示 v1.0.3
  - CI 现在从 git tag 自动注入 `-p:Version`

## [v1.1.0] - 2026-10-22

### 变更

- **无线投屏流程重构**：去掉 ①②③ 顺序编号，改为「状态徽章 + 三个独立场景」
  - 新增「读取手机 IP」按钮（只读取，不连接）
  - 「连接到该地址」明确为 adb connect 操作，并补充说明文案
  - 首次配对独立成块，内置手机端操作指引（在哪找配对码）
  - 配对地址与连接端口分离（配对端口是随机高位端口，不是 5555）
  - 配对成功后自动接力连接

### 修复

- **电脑打字无反应**：中文输入法会拦截字母键，按键到不了手机（Esc / 方向键不受影响）
  - 新增「设置 → 禁用电脑输入法」开关（默认开启），给投屏窗口解绑 IME

## [v1.0.3] - 2026-10-21

### 新增

- **中英双语界面**：默认跟随系统语言，「更多」页可一键切换，立即生效无需重启
- **托盘图标**：最小化到托盘，双击恢复；托盘菜单可直接开始 / 停止投屏
- **单实例锁**：重复启动只会唤醒已有窗口，不会开出第二个进程
- **应用内检查更新**：读取 GitHub Releases，国内网络自动走镜像
- **像素风图标**：16 ~ 256 多尺寸 ICO，应用、窗口、托盘统一使用
- **自动化冒烟测试**：`tests/MoyuCast/smoke-test.ps1`

### 变更

- 导航由 5 个页签精简为 4 个
- 「投屏窗口透明度」「老板键」移到「画面」页顶部
- 「外观」页改名「更多」
- 删除独立的「日志」页签

### 修复

- **adb 命令可能永久卡死**：改为异步同时读取 stdout/stderr，超时参数真正生效
- **窗口几何记忆不生效**：`RestoreMainGeometry()` 从未被调用
- **息屏后强杀进程导致手机永久停在 0 亮度**：新增 `DimActive` 状态与启动自动恢复
- **切换音频模式时界面冻结约 700ms**：改为后台线程重启 scrcpy
- **「scrcpy 目录」永远显示 `…`**：启动时未刷新
- **恢复亮度时 epd / 副屏模式被硬编码为 1**：改为按原值恢复
- **老板键恢复会把小窗口强行撑大**
- **刚开投屏就自动进入老板键**：窗口落位期间不再参与「鼠标离开」判定

## [v1.0.2] - 2026-10-20

### 修复

- 单文件发布时 scrcpy 目录解析失败：改用 `Environment.ProcessPath`
- `adb devices` 输出解析不严格，异常文本被当成设备
- adb 找不到时回退到 PATH 并给出明确错误

## [v1.0.1] - 2026-10-20

### 修复

- 刷新按钮缺少反馈
- 截图替换为真实界面截图，badge 改为动态

## [v1.0.0] - 2026-10-20

### 新增

- 首个版本：设备列表、无线投屏、音频三态、比例锁定、窗口置顶、透明度、老板键、日志

