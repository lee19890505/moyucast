# 更新日志
- **锁屏模式（虚拟屏投屏）**：新建虚拟显示，手机可锁屏/熄屏，投屏照常可看可操作
  - 画面页顶部新增「投屏模式」：镜像模式 / 锁屏模式
  - 锁屏模式下自动读取第三方 App 列表，支持星标置顶、刷新、原生图标（命中约 40%，其余字母头像）
  - 选择 App 后开始投屏 → 自动打开该 App → 自动锁屏
  - 修复 stay_on_while_plugged_in 不还原的问题（记录原值并恢复）

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 风格，版本号遵循语义化版本。

## [v1.2.0] - 2026-10-26

### 新增

- **离开即隐藏（更灵敏）模式**：无需先把鼠标移入投屏窗口，不在窗口上持续 0.8 秒就隐藏
- **主/子开关层级**：主开关关闭时收起子选项，打开后展开「触发方式」子面板（淡入动画 + 左侧蓝色竖条）
- **诊断日志开关**：把自动隐藏的判定写入 `%AppData%\MoyuCast\app.log`（超过 2MB 自动滚动）

### 修复

- 老板键隐藏后从托盘唤回主窗口时 `_bossHidden` 未复位，导致自动隐藏 / 比例锁定永久失效
- 自动隐藏的窗口稳定性判定改为 2px 容差，避免 1px 抖动导致永不触发


## [v1.1.1] - 2026-10-22

### 变更

- **无线投屏流程重构**：去掉 ①②③ 顺序编号，改为「状态徽章 + 三个独立场景」
  - 新增「读取手机 IP」按钮（只读取，不连接）
  - 「连接到该地址」明确为 adb connect 操作，并补充说明文案
  - 首次配对独立成块，内置手机端操作指引（在哪找配对码）
  - 配对地址与连接端口分离（配对端口是随机高位端口，不是 5555）
  - 配对成功后自动接力连接

### 修复

- **电脑打字无反应**：中文输入法会拦截字母键，按键到不了手机（Esc / 方向键不受影响）
  - 新增「画面 → 禁用电脑输入法」开关（默认开启），给投屏窗口解绑 IME
- **鼠标移开自动隐藏误触发**：拖动投屏窗口时窗口跟随有延迟，会被误判为鼠标离开
  - 按住左键、或窗口处于移动/缩放循环时，不做离开判定
- **版本号不随 tag 更新**：`<Version>` 写死在 csproj，导致 v1.1.0 的包内部仍显示 v1.0.3
  - CI 现在从 git tag 自动注入 `-p:Version`

---
## [v1.0.3] - 2026-10-21

### 新增

- **中英双语界面**：默认跟随系统语言，「更多」页可一键切换，立即生效无需重启
- **托盘图标**：最小化到托盘，双击恢复；托盘菜单可直接开始 / 停止投屏
- **单实例锁**：重复启动只会唤醒已有窗口，不会开出第二个进程
- **应用内检查更新**：读取 GitHub Releases，国内网络自动走镜像
- **像素风图标**：16 ~ 256 多尺寸 ICO，应用、窗口、托盘统一使用
- **自动化冒烟测试**：`tests/MoyuCast/smoke-test.ps1`，37 项断言覆盖启动、几何、单实例、投屏、透明度、老板键、托盘、语言

### 变更

- 导航由 5 个页签精简为 4 个：设备 / 画面 / 声音 / 更多
- 「投屏窗口透明度」「老板键」移到「画面」页顶部
- 「外观」页改名「更多」，改为：检查更新 + 运行日志（点击展开）+ 关于
- 删除独立的「日志」页签

### 修复

- **adb 命令可能永久卡死**：改为异步同时读取 stdout/stderr，超时参数真正生效
- **窗口几何记忆不生效**：`RestoreMainGeometry()` 从未被调用
- **息屏后强杀进程导致手机永久停在 0 亮度**：新增 `DimActive` 状态与启动自动恢复
- **切换音频模式时界面冻结约 700ms**：改为后台线程重启 scrcpy
- **「scrcpy 目录」永远显示 `…`**：启动时未刷新
- **恢复亮度时 epd / 副屏模式被硬编码为 1**：改为按原值恢复
- **老板键恢复会把小窗口强行撑大**
- **刚开投屏就自动进入老板键**：窗口落位期间不再参与「鼠标离开」判定（两处修复）

### English

**Added**
- Bilingual UI (Chinese / English) with instant in-app switching, no restart
- System tray icon with minimize-to-tray; tray menu can start/stop mirroring
- Single-instance lock — launching again just brings the running window forward
- In-app update check against GitHub Releases, with mirror fallback for mainland China
- Pixel-art app & tray icon (16–256 px ICO)
- Automated smoke tests (37 assertions)

**Changed**
- Navigation reduced from 5 tabs to 4: Device / Display / Audio / More
- Opacity and boss key moved to the top of the Display page
- "Appearance" renamed "More": update check + collapsible runtime log + about
- Removed the standalone Log tab

**Fixed**
- adb calls could hang forever; timeouts now actually work
- Saved window geometry was never restored
- Screen-dim state could survive a crash and leave the phone at 0 brightness
- Switching audio mode froze the UI for ~700 ms
- The scrcpy folder was never displayed
- epd / secondary brightness modes were hard-coded on restore
- Boss-key restore enlarged small windows
- Auto-hide fired immediately after starting mirroring
- 无边框窗口按钮补充无障碍名称

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

- 首个版本：设备列表、无线投屏、音频三态、息屏、比例锁定、窗口置顶、透明度、老板键、日志


