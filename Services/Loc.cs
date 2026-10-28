using System.ComponentModel;
using System.Globalization;

namespace MoyuCast.Services;

/// <summary>
/// 轻量本地化：以中文原文作为 key，英文查表。
/// 切换语言时触发 PropertyChanged("Item[]")，XAML 里所有
/// {Binding [中文原文], Source={x:Static loc:Loc.Instance}} 会立即刷新。
/// 查不到的 key 原样返回，保证不会出现空白界面。
/// </summary>
public class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new Loc();

    public event PropertyChangedEventHandler PropertyChanged;

    private static readonly Dictionary<string, string> En = new()
    {
        // ---------- 导航 / 页面标题 ----------
        ["设备"] = "Device",
        ["画面"] = "Display",
        ["声音"] = "Audio",
        ["更多"] = "More",
        ["设置"] = "Settings",
        ["关于"] = "About",
        ["投屏"] = "Mirroring",

        // ---------- 设备页 ----------
        ["选择设备"] = "Select device",
        ["刷新"] = "Refresh",
        ["scrcpy 目录"] = "scrcpy folder",
        ["选择"] = "Select",
        ["未找到 scrcpy，请点右侧选择"] = "scrcpy not found — click Select",
        ["无线投屏"] = "Wireless mirroring",
        ["需先用 USB 连接手机一次，会自动读取手机 WiFi IP 并切换为无线。"] =
            "Connect the phone over USB once. The app reads the Wi-Fi IP and switches to wireless automatically.",
        ["或手动连接"] = "or connect manually",
        ["① 一键开启无线投屏"] = "① One-click wireless",
        ["② 连接"] = "② Connect",
        ["③ 首次配对（需要手机显示配对码）"] = "③ First-time pairing (code shown on phone)",
        ["配对"] = "Pair",
        ["未连接"] = "Not connected",
        ["开始投屏"] = "Start mirroring",
        ["停止投屏"] = "Stop mirroring",
        ["投屏中"] = "Mirroring",
        ["已停止"] = "Stopped",
        ["启动失败"] = "Failed to start",
        ["扫描失败"] = "Scan failed",
        ["未发现设备"] = "No device found",
        ["正在扫描设备…"] = "Scanning devices…",
        ["已连接 1 台设备"] = "1 device connected",
        ["已连接 {0} 台设备"] = "{0} devices connected",
        ["已刷新 · 未发现设备 · {0:HH:mm:ss}"] = "Refreshed · no device · {0:HH:mm:ss}",
        ["已刷新 · {0} 台设备 · {1:HH:mm:ss}"] = "Refreshed · {0} device(s) · {1:HH:mm:ss}",
        ["手动刷新设备：{0} 台"] = "Manual refresh: {0} device(s)",
        ["adb 错误：请检查 scrcpy 目录"] = "adb error: check the scrcpy folder",
        ["adb 错误: "] = "adb error: ",
        ["刷新设备失败: "] = "Device refresh failed: ",
        ["请先用 USB 连接手机"] = "Connect the phone over USB first",
        ["一键无线：需要先用 USB 连接一次"] = "One-click wireless: connect over USB first",
        ["正在开启无线投屏…"] = "Enabling wireless mirroring…",
        ["读取手机 WiFi 地址"] = "Reading phone Wi-Fi address",
        ["读取 WiFi IP 失败："] = "Failed to read Wi-Fi IP: ",
        ["读取 IP 失败，请手动填写"] = "Could not read IP, please enter it manually",
        ["已获取 IP {0}，正在切换 TCP 模式…"] = "Got IP {0}, switching to TCP mode…",
        ["正在连接 {0}:5555 …"] = "Connecting to {0}:5555 …",
        ["无线已连接 {0}"] = "Wireless connected: {0}",
        ["无线连接失败"] = "Wireless connection failed",
        ["请填写 IP 和配对码"] = "Please enter IP and pairing code",
        ["配对成功"] = "Paired",
        ["配对失败，见日志"] = "Pairing failed, see log",
        ["请填写 IP"] = "Please enter an IP",

        // ---------- 无线投屏 v2 ----------
        ["未检测到设备"] = "No device detected",
        ["一键转为无线投屏"] = "Switch to wireless",
        ["手机已通过 USB 连接时可用：自动读取 WiFi 地址并切换到无线，之后可以拔掉数据线。"] =
            "Available when the phone is connected over USB: reads its Wi-Fi address and switches to wireless. You can unplug afterwards.",
        ["手动连接"] = "Manual connection",
        ["读取手机 IP"] = "Read phone IP",
        ["连接到该地址"] = "Connect to this address",
        ["通过 adb connect 建立无线连接，不需要数据线。首次使用需先完成下方配对。"] =
            "Opens a wireless link via adb connect — no cable needed. First use requires pairing below.",
        ["首次配对（仅 Android 11+ 需要）"] = "First-time pairing (Android 11+ only)",
        ["在手机上操作："] = "On your phone:",
        ["设置 → 开发者选项 → 无线调试 → 使用配对码配对设备"] =
            "Settings → Developer options → Wireless debugging → Pair device with pairing code",
        ["屏幕会显示 6 位配对码和一个 IP:端口"] = "It shows a 6-digit code and an IP:port",
        ["配对地址"] = "Pairing address",
        ["配对码"] = "Pairing code",
        ["配对端口是手机随机分配的高位端口，不是 5555；配对只需一次。"] =
            "The pairing port is a random high port, not 5555. Pairing is only needed once.",
        ["已无线连接 {0}"] = "Wireless connected: {0}",
        ["已通过 USB 连接 {0}，可一键转为无线"] = "USB connected: {0} — ready to switch to wireless",
        ["未检测到设备：请先用 USB 连接，或在手机上开启无线调试"] =
            "No device: connect over USB first, or enable wireless debugging on the phone",
        ["读取 IP 需要先通过 USB 连接手机"] = "Reading the IP requires a USB connection",
        ["已读取手机 IP：{0}"] = "Phone IP: {0}",
        ["可以拔掉数据线了"] = "You can unplug the cable now",
        ["请填写配对地址（手机显示的 IP:端口）"] = "Enter the pairing address (IP:port shown on the phone)",
        ["配对码应为 6 位数字"] = "The pairing code must be 6 digits",
        ["正在配对…"] = "Pairing…",
        ["配对成功，正在连接…"] = "Paired, connecting…",
        ["配对成功，但连接失败，请点「连接到该地址」重试"] =
            "Paired but connection failed — try \"Connect to this address\"",
        ["连接失败：请确认手机已开无线调试并完成配对"] =
            "Connection failed: make sure wireless debugging is on and pairing is done",

        // ---------- 输入法 ----------
        ["禁用电脑输入法（解决打字无反应）"] = "Disable PC input method (fixes typing)",
        ["中文输入法会拦截字母键，导致电脑打字没反应。开启后投屏窗口绕过电脑输入法。"] =
            "A Chinese IME swallows letter keys, so typing does nothing. Enable this to bypass the IME for the mirror window.",
        ["正在连接…"] = "Connecting…",
        ["请填写无线 IP"] = "Please enter the wireless IP",
        ["请先选择设备"] = "Select a device first",
        ["正在连接设备…"] = "Connecting to device…",
        ["推送服务端并建立画面通道"] = "Pushing server and opening video stream",
        ["投屏就绪（{0} ms）"] = "Mirroring ready ({0} ms)",
        ["投屏窗口未出现，请查看日志"] = "Mirror window did not appear, check the log",
        ["投屏已退出（{0}）"] = "Mirroring exited ({0})",
        ["请先开始投屏"] = "Start mirroring first",
        ["选择 scrcpy 所在文件夹（里面有 scrcpy.exe）"] = "Choose the scrcpy folder (contains scrcpy.exe)",
        ["该目录下没有 scrcpy.exe"] = "scrcpy.exe not found in that folder",
        ["scrcpy 目录已设为: "] = "scrcpy folder set to: ",
        ["scrcpy 目录: "] = "scrcpy folder: ",
        ["⚠ 未找到 scrcpy 目录，请在「设备」页手动选择"] =
            "⚠ scrcpy folder not found. Pick it on the Device page.",
        ["首次启动需要推送服务端，约 2-5 秒"] = "First launch pushes the server, about 2-5 s",
        ["正在连接设备…"] = "Connecting to device…",

        // ---------- 画面页 ----------
        ["投屏窗口透明度"] = "Mirror window opacity",
        ["实时生效，只作用于投屏窗口。"] = "Applies live to the mirror window only.",
        ["老板键（隐藏 / 显示投屏窗口）"] = "Boss key (hide / show mirror window)",
        ["录制"] = "Record",
        ["取消"] = "Cancel",
        ["请按下组合键…"] = "Press a shortcut…",
        ["点「录制」后按下你想要的组合键，例如 Alt+` 或 Ctrl+Shift+Q。"] =
            "Click Record, then press a shortcut (e.g. Alt+` or Ctrl+Shift+Q).",
        ["息屏投屏"] = "Dim phone screen",
        ["把屏幕亮度压到 0 并保持设备唤醒，电脑端画面和操作不受影响。"] =
            "Dims the phone to 0 brightness and keeps it awake. Mirroring and input keep working.",
        ["立即应用 / 恢复"] = "Apply / restore now",
        ["锁定窗口比例（无黑边）"] = "Lock aspect ratio (no black bars)",
        ["窗口宽度"] = "Window width",
        ["px（0 为自动）"] = "px (0 = auto)",
        ["窗口置顶"] = "Always on top",
        ["模拟物理键盘（打字更可靠）"] = "Physical keyboard (HID, more reliable)",
        ["使用 HID 键盘模式，几乎所有 App 都能收到按键。关闭则用默认 SDK 模式。"] =
            "Uses HID keyboard mode so almost every app receives keys. Turn off to use SDK mode.",
        ["鼠标移开自动隐藏"] = "Auto-hide on mouse leave",
        ["鼠标离开投屏窗口超过 0.5 秒，自动进入老板键隐藏状态；按老板键即可恢复。"] =
            "After the cursor leaves the mirror window for 0.5 s, everything hides. Press the boss key to restore.",
        ["已息屏：亮度=0，设备保持唤醒"] = "Screen dimmed: brightness 0, device kept awake",
        ["已恢复亮度"] = "Brightness restored",
        ["息屏操作失败: "] = "Screen-dim failed: ",
        ["检测到上次息屏未恢复，已自动还原屏幕亮度"] =
            "Previous screen-dim was not restored; brightness restored automatically.",
        ["鼠标移开投屏窗口，自动隐藏"] = "Cursor left the mirror window, auto-hidden",
        ["已开启：鼠标移开自动隐藏"] = "On: auto-hide on mouse leave",
        ["已关闭：鼠标移开自动隐藏"] = "Off: auto-hide on mouse leave",
        ["老板键已改为 "] = "Boss key changed to ",
        ["老板键格式无效："] = "Invalid boss key: ",
        ["老板键已注册："] = "Boss key registered: ",
        ["老板键注册失败（可能被占用）："] = "Boss key registration failed (maybe in use): ",
        ["老板键：已隐藏"] = "Boss key: hidden",
        ["老板键：已恢复"] = "Boss key: restored",
        ["已恢复上次的投屏窗口位置/大小"] = "Restored last mirror window position/size",
        ["已锁定投屏窗口（比例 {0:0.000}）"] = "Mirror window locked (aspect {0:0.000})",

        // ---------- 声音页 ----------
        ["手机响"] = "Phone",
        ["两边响"] = "Both",
        ["仅电脑"] = "PC only",
        ["默认手机出声，电脑不继承。切换会自动重启投屏。"] =
            "Phone plays audio; PC stays silent. Changing this restarts mirroring.",
        ["手机出声，电脑不继承。"] = "Phone plays audio; PC does not.",
        ["手机与电脑同时出声（需 Android 13+）。"] = "Both phone and PC play audio (Android 13+).",
        ["仅电脑出声，手机会静音。"] = "Sound on PC only; phone muted.",
        ["音频模式切换，正在重启投屏…"] = "Switching audio mode, restarting mirroring…",
        ["已按新音频模式重启"] = "Restarted with the new audio mode",
        ["重启后未等到投屏窗口，请查看日志"] = "No mirror window after restart, check the log",
        ["音频模式已切换"] = "Audio mode switched",
        ["音频模式切换后未检测到投屏窗口"] = "No mirror window after audio mode switch",
        ["重启投屏失败: "] = "Failed to restart mirroring: ",

        // ---------- 更多页 ----------
        ["检查更新"] = "Check for updates",
        ["当前版本"] = "Current version",
        ["当前版本 v{0}"] = "Current version v{0}",
        ["从 GitHub Releases 获取最新版本，国内网络自动走镜像。"] =
            "Checks GitHub Releases. Falls back to a mirror in mainland China.",
        ["正在检查更新…"] = "Checking for updates…",
        ["未获取到版本信息，请稍后再试。"] = "No version info returned, try again later.",
        ["发现新版本 v{0}（当前 v{1}）"] = "New version v{0} available (current v{1})",
        ["检查更新：发现新版本 v{0}"] = "Update check: new version v{0}",
        ["已是最新版本 v{0}"] = "Up to date (v{0})",
        ["检查更新：已是最新版本 v"] = "Update check: up to date (v",
        ["检查失败："] = "Check failed: ",
        ["检查更新失败: "] = "Update check failed: ",
        ["发现新版本 v{0}\n当前版本 v{1}\n\n是否打开发布页下载？"] =
            "New version v{0} found.\nCurrent version v{1}.\n\nOpen the release page?",
        ["MoyuCast 检查更新"] = "MoyuCast Update",
        ["运行日志"] = "Runtime log",
        ["展开日志 ▾"] = "Show log ▾",
        ["收起日志 ▴"] = "Hide log ▴",
        ["打开项目主页"] = "Open project page",
        ["打开项目主页失败: "] = "Failed to open project page: ",
        ["MoyuCast 摸鱼投屏 v{0}\n基于 scrcpy 3.3.4 的 Windows 图形化投屏控制台\n开源协议：MIT（本项目自身代码）"] =
            "MoyuCast v{0}\nWindows GUI for scrcpy 3.3.4\nLicense: MIT (this project only)",

        // ---------- 托盘 ----------
        ["MoyuCast 摸鱼投屏"] = "MoyuCast",
        ["MoyuCast · 投屏中"] = "MoyuCast · Mirroring",
        ["显示主窗口"] = "Show main window",
        ["退出 MoyuCast"] = "Exit MoyuCast",
        ["MoyuCast 仍在运行"] = "MoyuCast is still running",
        ["已最小化到托盘，双击托盘图标可恢复窗口。"] =
            "Minimized to tray. Double-click the tray icon to restore.",
        ["托盘图标初始化失败: "] = "Tray icon init failed: ",

        ["鼠标离开投屏窗口后自动进入老板键隐藏。"] = "Hides everything when the cursor leaves the mirror window.",
        ["触发方式"] = "How it triggers",
        ["打开：无需先移入窗口，不在窗口上持续 0.8 秒就隐藏。"] = "On: no need to enter first; hides after 0.8 s outside the window.",
        ["关闭：需要先移入投屏窗口，再移出才隐藏（约 0.5 秒）。"] = "Off: the cursor must enter the mirror window first, then leave (about 0.5 s).",
        // ---------- 校验弹窗 ----------
        ["请选择应用"] = "Select an app",
        ["提示"] = "Notice",
        ["确定"] = "OK",
        ["请填写无线投屏的 IP 地址"] = "Enter the wireless mirroring IP address",
        ["无线投屏不可用"] = "Wireless mirroring unavailable",
        ["无线投屏不可用：无法连接到 {0}\n\n请检查：\n• 手机与电脑是否在同一 Wi-Fi\n• 手机是否开启「无线调试」\n• 是否需要重新配对\n\n设备状态：{1}"] = "Wireless mirroring unavailable: cannot connect to {0}\n\nCheck:\n- phone and PC are on the same Wi-Fi\n- wireless debugging is enabled on the phone\n- pairing may be required again\n\nDevice state: {1}",
        ["手机当前处于锁屏状态。\n锁屏模式下需要先解锁手机，才能启动 App 并建立虚拟屏。\n\n请解锁后重试。"] = "The phone is locked.\nLock-screen mode needs the phone unlocked to launch the app and create a virtual display.\n\nPlease unlock and try again.",

        // ---------- 投屏模式 / 锁屏模式 ----------
        ["投屏模式"] = "Display mode",
        ["镜像模式"] = "Mirror",
        ["锁屏模式"] = "Lock screen",
        ["启动应用"] = "Launch app",
        ["新建一块虚拟屏幕，手机会自动锁屏，投屏照常运行。"] = "Creates a virtual display; the phone locks itself while mirroring keeps running.",
        ["投屏内容与手机屏幕完全一致。手动锁屏后投屏会显示锁屏界面或黑屏；手机屏幕内容与投屏一致，不适合藏起来看。"] = "Mirrors the phone screen exactly. If you lock the phone manually, the mirror shows the lock screen or goes black; the phone and the mirror are always in sync.",
        ["使用说明：选一个 App，点开始投屏后会自动打开并锁屏；锁屏期间不能换 App，要换请先解锁手机；请保持 USB 连接。"] = "Pick an app; it opens automatically and the phone locks. You cannot switch apps while locked - unlock once to change. Keep the USB cable connected.",
        ["请先选择设备再加载应用列表"] = "Select a device before loading apps",
        ["请先在锁屏模式里选择一个应用"] = "Pick an app for lock mode first",
        ["手机当前已锁屏，请先解锁再开始投屏"] = "Phone is locked - unlock it before starting",
        ["锁屏模式下需要先解锁手机，才能启动 App"] = "Lock mode needs the phone unlocked to launch the app",
        ["投屏模式已切换，请重新开始投屏"] = "Display mode changed, please start mirroring again",
        ["已发送锁屏命令，物理屏已关闭，投屏保持运行"] = "Lock command sent; physical screen off, mirroring still running",
        ["已锁屏（投屏中）"] = "Locked (mirroring)",
        ["已开启保持唤醒（防止锁屏后休眠）"] = "Stay-awake enabled (prevents sleep)",
        ["已恢复保持唤醒设置"] = "Stay-awake setting restored",
        ["已加载 {0} 个可启动应用"] = "Loaded {0} launchable apps",
        ["已提取 {0} 个应用图标"] = "Extracted {0} app icons",

        // ---------- 自动隐藏 / 诊断 ----------
        ["离开即隐藏（更灵敏）"] = "Hide as soon as you leave (more sensitive)",
        ["打开后无需先把鼠标移入投屏窗口：只要不在窗口上，持续 0.8 秒就隐藏。"] = "No need to move into the mirror window first: hides after the cursor has been outside it for 0.8 s.",
        ["当前已关闭。打开后：鼠标先移入投屏窗口、再移出才会隐藏。"] = "Currently off. Once enabled: the cursor must enter the mirror window first, then leave.",
        ["当前：只要鼠标不在投屏窗口上，持续 0.8 秒就隐藏（无需先移入）。更灵敏，但切到别的窗口时投屏也会消失。"] = "Current: hides after the cursor has been outside the mirror window for 0.8 s (no need to enter first). More sensitive - the mirror may vanish when you switch windows.",
        ["当前：需要先移入投屏窗口，再移出才会隐藏（约 0.5 秒）。适合边工作边看。"] = "Current: the cursor must enter the mirror window first, then leave (about 0.5 s). Good when working alongside.",
        ["已开启：离开即隐藏（无需先移入投屏窗口）"] = "On: hide as soon as you leave (no need to enter first)",
        ["已关闭：离开即隐藏（需先移入投屏窗口）"] = "Off: hide as soon as you leave (requires entering first)",
        ["记录诊断日志"] = "Write diagnostic log",
        ["开启后会把自动隐藏的判定写入 app.log，排查问题用。"] = "Writes auto-hide decisions to app.log for troubleshooting.",
        ["已开启：诊断日志"] = "On: diagnostic log",
        ["已关闭：诊断日志"] = "Off: diagnostic log",

        // ---------- 语言卡片 ----------
        ["界面语言"] = "Language",
        ["中文"] = "中文",
        ["English"] = "English",
    };

    public string Current { get; private set; } = "zh-CN";
    public bool IsEnglish => Current.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>XAML 绑定入口：{Binding [中文原文], Source={x:Static loc:Loc.Instance}}</summary>
    public string this[string key] => Translate(key);

    public static string T(string zh) => Instance.Translate(zh);

    public static string F(string zh, params object[] args)
    {
        var template = Instance.Translate(zh);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    public string Translate(string zh)
    {
        if (string.IsNullOrEmpty(zh)) return zh ?? "";
        if (!IsEnglish) return zh;
        return En.TryGetValue(zh, out var en) ? en : zh;
    }

    public void SetLanguage(string lang)
    {
        var next = string.IsNullOrWhiteSpace(lang) ? "zh-CN" : lang;
        if (next.Equals(Current, StringComparison.OrdinalIgnoreCase)) return;
        Current = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
    }

    /// <summary>没有保存过语言时：中文系统用中文，其它用英文</summary>
    public static string DetectDefault()
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN" : "en-US";
}


