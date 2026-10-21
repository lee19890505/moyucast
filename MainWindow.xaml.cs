using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MoyuCast.Models;
using MoyuCast.Services;

namespace MoyuCast;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private AdbService _adb;
    private readonly ScrcpyRunner _runner = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _spin;
    private readonly DispatcherTimer _hoverTimer;
    private bool _autoHideArmed;
    private NativeWin32.RECT _lastHoverRect;
    private bool _hasLastHoverRect;
    private DateTime _autoHideSuppressUntil = DateTime.MinValue;
    private DateTime _outsideSince = DateTime.MinValue;

    private IntPtr _scrcpyHwnd = IntPtr.Zero;
    private double _videoAspect = 0;
    private bool _syncing = true;
    private string _activeSerial = "";

    private bool _screenDimmed;
    private int _savedBrightness = 29;
    private int _savedBrightnessMode = 1;
    private int _savedScreenTimeout = 30000;
    private int _savedEpdMode = 1;
    private int _savedSecondaryMode = 1;

    private bool _capturingHotkey;
    private bool _hotkeyRegistered;
    private const int HOTKEY_ID = 0x5A01;

    private class DeviceItem
    {
        public string Serial { get; set; }
        public string Display { get; set; }
        public override string ToString() => Display;
    }

    public MainWindow()
    {
        InitializeComponent();
        InitTray();

        _settings = AppSettings.Load();
        _settings.ScrcpyDir = ResolveScrcpyDir(_settings.ScrcpyDir);
        _adb = new AdbService(_settings.ScrcpyDir);
        if (string.IsNullOrEmpty(_settings.ScrcpyDir))
        {
            AppendLog(Loc.T("⚠ 未找到 scrcpy 目录，请在「设备」页手动选择"));
            try
            {
                File.AppendAllText(Path.Combine(GetAppDir(), "startup.log"),
                    string.Format("{0:yyyy-MM-dd HH:mm:ss} processPath={1} appDir={2} baseDir={3}\n",
                        DateTime.Now, Environment.ProcessPath, GetAppDir(), AppContext.BaseDirectory));
            }
            catch { }
        }
        else
        {
            AppendLog(Loc.T("scrcpy 目录: ") + _settings.ScrcpyDir);
            _settings.Save();
        }

        _runner.Log += AppendLog;
        _runner.Exited += OnScrcpyExited;

        LoadSettingsToUi();
        RestoreMainGeometry();
        ShowPage(0);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _hoverTimer.Tick += HoverTimer_Tick;
        _hoverTimer.Start();

        _spin = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _spin.Tick += (_, _) => ArcRotate.Angle = (ArcRotate.Angle + 14) % 360;

        Loaded += OnLoaded;
        Closing += OnClosing;
        _syncing = false;
    }

    // ---------------- 生命周期 ----------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settings.MainMaximized) WindowState = WindowState.Maximized;
        RegisterBossKey();
        RestoreLeftoverDim();
        await RefreshDevicesAsync();
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveUiToSettings();
        SaveGeometry();
        _settings.Save();
        ApplyScreenDim(false);
        _runner.Stop();
        UnregisterBossKey();
        DisposeTray();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => HideToTray();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------- 加载遮罩 ----------------

    private void ShowLoading(string main, string sub)
    {
        TxtLoading.Text = main;
        TxtLoadingSub.Text = sub;
        PnlLoading.Visibility = Visibility.Visible;
        _spin.Start();
    }

    private void HideLoading()
    {
        _spin.Stop();
        PnlLoading.Visibility = Visibility.Collapsed;
    }

    // ---------------- 导航 ----------------

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        if (btn == null) return;
        int idx = btn == Nav0 ? 0 : btn == Nav1 ? 1 : btn == Nav2 ? 2 : 3;
        ShowPage(idx);
    }

    private void ShowPage(int idx)
    {
        var pages = new UIElement[] { PageDevice, PageDisplay, PageAudio, PageMore };
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;

        var navs = new[] { Nav0, Nav1, Nav2, Nav3 };
        for (int i = 0; i < navs.Length; i++)
            navs[i].Tag = i == idx ? "active" : null;
    }

    // ---------------- 设置读写 ----------------

    private void LoadSettingsToUi()
    {
        _syncing = true;
        ChkWireless.IsChecked = _settings.Wireless;
        PnlWireless.Visibility = _settings.Wireless ? Visibility.Visible : Visibility.Collapsed;
        TxtIp.Text = _settings.LastWirelessIp;
        ChkScreenOff.IsChecked = _settings.TurnScreenOff;
        ChkLockAspect.IsChecked = _settings.LockAspect;
        ChkTop.IsChecked = _settings.AlwaysOnTop;
        ChkUhid.IsChecked = _settings.KeyboardUhid;
        ChkAutoHide.IsChecked = _settings.AutoHideOnLeave;
        TxtWidth.Text = _settings.ScrcpyWidth.ToString();
        SldOpacity.Value = Math.Round(_settings.Opacity * 100);
        LblOpacity.Text = $"{SldOpacity.Value:0}%";
        TxtBossKey.Text = _settings.BossKey;

        RadAudio0.IsChecked = _settings.AudioMode == 0;
        RadAudio1.IsChecked = _settings.AudioMode == 1;
        RadAudio2.IsChecked = _settings.AudioMode == 2;

        LblVersion.Text = Loc.F("当前版本 v{0}", UpdateService.CurrentVersion);
        LblAbout.Text = Loc.F("MoyuCast 摸鱼投屏 v{0}\n基于 scrcpy 3.3.4 的 Windows 图形化投屏控制台\n开源协议：MIT（本项目自身代码）", UpdateService.CurrentVersion);
        RefreshScrcpyPathUi();
        UpdateLangButtons();
        _syncing = false;
    }

    private void SaveUiToSettings()
    {
        _settings.Wireless = ChkWireless.IsChecked == true;
        _settings.LastWirelessIp = TxtIp.Text.Trim();
        _settings.TurnScreenOff = ChkScreenOff.IsChecked == true;
        _settings.LockAspect = ChkLockAspect.IsChecked == true;
        _settings.AlwaysOnTop = ChkTop.IsChecked == true;
        _settings.KeyboardUhid = ChkUhid.IsChecked == true;
        _settings.AutoHideOnLeave = ChkAutoHide.IsChecked == true;
        _settings.Opacity = SldOpacity.Value / 100.0;
        _settings.BossKey = TxtBossKey.Text.Trim();
        _settings.AudioMode = RadAudio1.IsChecked == true ? 1 : (RadAudio2.IsChecked == true ? 2 : 0);

        if (int.TryParse(TxtWidth.Text.Trim(), out var w)) _settings.ScrcpyWidth = w;
        if (CmbDevice.SelectedItem is DeviceItem d) _settings.LastSerial = d.Serial;
    }


    // ---------------- scrcpy 目录自动探测 ----------------

    /// <summary>单文件发布时 AppContext.BaseDirectory 不可靠，优先用 ProcessPath 的目录</summary>
    private static string GetAppDir()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p))
            {
                var d = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(d)) return d;
            }
        }
        catch { }
        try { return AppContext.BaseDirectory; } catch { }
        return Environment.CurrentDirectory;
    }

    private static string ResolveScrcpyDir(string configured)
    {
        bool HasScrcpy(string dir) =>
            !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "scrcpy.exe"));

        if (HasScrcpy(configured)) return configured;

        var appDir = GetAppDir();
        var candidates = new[]
        {
            Path.Combine(appDir, "scrcpy"),
            appDir,
            Path.Combine(appDir, "..", "scrcpy"),
            Path.Combine(appDir, "..", "..", "scrcpy"),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "scrcpy"),
            @"C:\scrcpy",
        };
        foreach (var c in candidates)
        {
            try { if (HasScrcpy(c)) return Path.GetFullPath(c); } catch { }
        }
        return configured ?? "";
    }

    private void RefreshScrcpyPathUi()
    {
        if (TxtScrcpyDir == null) return;
        TxtScrcpyDir.Text = string.IsNullOrEmpty(_settings.ScrcpyDir)
            ? Loc.T("未找到 scrcpy，请点右侧选择")
            : _settings.ScrcpyDir;
    }

    private void BtnPickScrcpy_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("选择 scrcpy 所在文件夹（里面有 scrcpy.exe）")
        };
        if (dlg.ShowDialog() == true)
        {
            var dir = dlg.FolderName;
            if (!File.Exists(Path.Combine(dir, "scrcpy.exe")))
            {
                LblStatus.Text = Loc.T("该目录下没有 scrcpy.exe");
                return;
            }
            _settings.ScrcpyDir = dir;
            _settings.Save();
            _adb = new AdbService(dir);
            RefreshScrcpyPathUi();
            AppendLog(Loc.T("scrcpy 目录已设为: ") + dir);
            _ = RefreshDevicesAsync();
        }
    }
    // ---------------- 设备 ----------------

    private async Task<int> RefreshDevicesAsync()
    {
        int count = 0;
        BtnRefresh.IsEnabled = false;
        LblStatus.Text = Loc.T("正在扫描设备…");
        try
        {
            var devices = await Task.Run(() => _adb.ListDevices());
            var items = devices.Select(d => new DeviceItem
            {
                Serial = d.serial,
                Display = $"{d.serial}  ({(string.IsNullOrEmpty(d.model) ? d.state : d.model)})"
            }).ToList();

            var prev = (CmbDevice.SelectedItem as DeviceItem)?.Serial;
            CmbDevice.ItemsSource = items;
            count = items.Count;

            if (items.Count == 0)
            {
                if (!string.IsNullOrEmpty(_adb.LastError))
                {
                    LblStatus.Text = Loc.T("adb 错误：请检查 scrcpy 目录");
                    AppendLog(Loc.T("adb 错误: ") + _adb.LastError);
                }
                else
                {
                    LblStatus.Text = Loc.T("未发现设备");
                }
            }
            else
            {
                var pick = items.FirstOrDefault(x => x.Serial == prev)
                           ?? items.FirstOrDefault(x => x.Serial == _settings.LastSerial)
                           ?? items[0];
                CmbDevice.SelectedItem = pick;
                LblStatus.Text = items.Count == 1 ? Loc.T("已连接 1 台设备") : Loc.F("已连接 {0} 台设备", items.Count);
            }
        }
        catch (Exception ex)
        {
            LblStatus.Text = Loc.T("扫描失败");
            AppendLog(Loc.T("刷新设备失败: ") + ex.Message);
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
        return count;
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        var old = BtnRefresh.Content;
        BtnRefresh.Content = "…";
        try
        {
            var n = await RefreshDevicesAsync();
            LblStatus.Text = n == 0
                ? Loc.F("已刷新 · 未发现设备 · {0:HH:mm:ss}", DateTime.Now)
                : Loc.F("已刷新 · {0} 台设备 · {1:HH:mm:ss}", n, DateTime.Now);
            AppendLog(Loc.F("手动刷新设备：{0} 台", n));
        }
        finally { BtnRefresh.Content = old; }
    }

    private void ChkWireless_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlWireless == null) return;
        PnlWireless.Visibility = ChkWireless.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnShowPair_Click(object sender, RoutedEventArgs e)
    {
        PnlPair.Visibility = PnlPair.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void BtnAutoWireless_Click(object sender, RoutedEventArgs e)
    {
        var usb = (CmbDevice.ItemsSource as List<DeviceItem>)?.FirstOrDefault(x => !x.Serial.Contains(":"));
        if (usb == null)
        {
            LblStatus.Text = Loc.T("请先用 USB 连接手机");
            AppendLog(Loc.T("一键无线：需要先用 USB 连接一次"));
            return;
        }

        ShowLoading(Loc.T("正在开启无线投屏…"), Loc.T("读取手机 WiFi 地址"));
        try
        {
            var ipOut = await Task.Run(() => _adb.Run($"-s {usb.Serial} shell ip -f inet addr show wlan0"));
            var m = Regex.Match(ipOut, @"inet\s+(\d+\.\d+\.\d+\.\d+)");
            if (!m.Success)
            {
                AppendLog(Loc.T("读取 WiFi IP 失败：") + ipOut.Replace("\n", " "));
                LblStatus.Text = Loc.T("读取 IP 失败，请手动填写");
                return;
            }
            var ip = m.Groups[1].Value;
            TxtIp.Text = ip;
            TxtLoadingSub.Text = Loc.F("已获取 IP {0}，正在切换 TCP 模式…", ip);

            await Task.Run(() => _adb.Run($"-s {usb.Serial} tcpip 5555"));
            await Task.Delay(2500);

            TxtLoadingSub.Text = Loc.F("正在连接 {0}:5555 …", ip);
            var r = await Task.Run(() => _adb.Connect($"{ip}:5555"));
            AppendLog($"connect {ip}:5555 -> {r}");

            ChkWireless.IsChecked = true;
            await RefreshDevicesAsync();
            LblStatus.Text = r.Contains("connected") ? Loc.F("无线已连接 {0}", ip) : Loc.T("无线连接失败");
        }
        catch (Exception ex) { AppendLog(ex.Message); }
        finally { HideLoading(); }
    }

    private void BtnPair_Click(object sender, RoutedEventArgs e)
    {
        var host = TxtIp.Text.Trim();
        var port = TxtPort.Text.Trim();
        var code = TxtPairCode.Text.Trim();
        if (host.Length == 0 || code.Length == 0) { LblStatus.Text = Loc.T("请填写 IP 和配对码"); return; }

        var addr = host.Contains(':') ? host : $"{host}:{port}";
        var r = _adb.Pair(addr, code);
        AppendLog("pair " + addr + " -> " + r);
        LblStatus.Text = r.Contains("Successfully") ? Loc.T("配对成功") : Loc.T("配对失败，见日志");
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        var host = TxtIp.Text.Trim();
        var port = TxtPort.Text.Trim();
        if (host.Length == 0) { LblStatus.Text = Loc.T("请填写 IP"); return; }

        var addr = host.Contains(':') ? host : $"{host}:{port}";
        BtnConnect.IsEnabled = false;
        ShowLoading(Loc.T("正在连接…"), addr);
        try
        {
            var r = await Task.Run(() => _adb.Connect(addr));
            AppendLog("connect " + addr + " -> " + r);
            await RefreshDevicesAsync();
        }
        finally { BtnConnect.IsEnabled = true; HideLoading(); }
    }

    // ---------------- 投屏控制 ----------------

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_runner.IsRunning) { StopMirroring(Loc.T("已停止")); return; }

        string serial = "";
        if (ChkWireless.IsChecked == true)
        {
            var host = TxtIp.Text.Trim();
            if (host.Length == 0) { LblStatus.Text = Loc.T("请填写无线 IP"); return; }
            serial = host.Contains(':') ? host : $"{host}:{TxtPort.Text.Trim()}";
        }
        else if (CmbDevice.SelectedItem is DeviceItem d)
        {
            serial = d.Serial;
        }

        if (string.IsNullOrEmpty(serial)) { LblStatus.Text = Loc.T("请先选择设备"); return; }

        SaveUiToSettings();
        _settings.Save();
        _activeSerial = serial;

        BtnStart.IsEnabled = false;
        ShowLoading(Loc.T("正在连接设备…"), Loc.T("推送服务端并建立画面通道"));

        var sw = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                var size = _adb.GetScreenSize(serial);
                _runner.Start(_settings, serial, size.w, size.h);
            });

            BtnStart.Content = Loc.T("停止投屏");
            UpdateTrayText();
            LblStatus.Text = Loc.T("投屏中");

            bool ok = await WaitForMirrorWindowAsync(8000);
            AppendLog(ok ? Loc.F("投屏就绪（{0} ms）", sw.ElapsedMilliseconds) : Loc.T("投屏窗口未出现，请查看日志"));

            if (ChkScreenOff.IsChecked == true) ApplyScreenDim(true);
        }
        catch (Exception ex)
        {
            LblStatus.Text = Loc.T("启动失败");
            AppendLog(ex.Message);
        }
        finally
        {
            HideLoading();
            BtnStart.IsEnabled = true;
        }
    }

    private async Task<bool> WaitForMirrorWindowAsync(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (EnsureScrcpyWindow() != IntPtr.Zero) return true;
            await Task.Delay(120);
        }
        return false;
    }

    private void StopMirroring(string status)
    {
        ApplyScreenDim(false);
        _runner.Stop();
        _scrcpyHwnd = IntPtr.Zero;
        _videoAspect = 0;
        _mirrorRectApplied = false;
        BtnStart.Content = Loc.T("开始投屏");
        UpdateTrayText();
        LblStatus.Text = status;
    }

    private void OnScrcpyExited(int code)
    {
        Dispatcher.Invoke(() =>
        {
            if (_restartingMirror) return;   // 主动重启（如音频切换）时忽略旧进程退出
            _scrcpyHwnd = IntPtr.Zero;
            _videoAspect = 0;
            BtnStart.Content = Loc.T("开始投屏");
            UpdateTrayText();
            LblStatus.Text = Loc.F("投屏已退出（{0}）", code);
            ApplyScreenDim(false);
        });
    }

    // ---------------- 息屏（亮度归零 + 保持唤醒）----------------

    private void ChkScreenOff_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (_runner.IsRunning) ApplyScreenDim(ChkScreenOff.IsChecked == true);
    }

    private void BtnApplyDim_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeSerial)) { LblStatus.Text = Loc.T("请先开始投屏"); return; }
        ApplyScreenDim(!_screenDimmed);
        ChkScreenOff.IsChecked = _screenDimmed;
    }

    private int ParseInt(string s, int fallback)
        => int.TryParse(s?.Trim(), out var v) ? v : fallback;

    private void ApplyScreenDim(bool dim, string serial = null)
    {
        var target = string.IsNullOrEmpty(serial) ? _activeSerial : serial;
        if (string.IsNullOrEmpty(target)) return;
        try
        {
            if (dim && !_screenDimmed)
            {
                _savedBrightness = ParseInt(_adb.Run($"-s {target} shell settings get system screen_brightness"), 29);
                _savedBrightnessMode = ParseInt(_adb.Run($"-s {target} shell settings get system screen_brightness_mode"), 1);
                _savedScreenTimeout = ParseInt(_adb.Run($"-s {target} shell settings get system screen_off_timeout"), 30000);
                _savedEpdMode = ParseInt(_adb.Run($"-s {target} shell settings get system screen_brightness_mode_epd"), 1);
                _savedSecondaryMode = ParseInt(_adb.Run($"-s {target} shell settings get system screen_brightness_mode_secondary"), 1);

                // 先把原始值落盘：即使进程随后被强杀，下次启动也能还原
                _settings.DimActive = true;
                _settings.DimSavedBrightness = _savedBrightness;
                _settings.DimSavedBrightnessMode = _savedBrightnessMode;
                _settings.DimSavedScreenTimeout = _savedScreenTimeout;
                _settings.DimSavedEpdMode = _savedEpdMode;
                _settings.DimSavedSecondaryMode = _savedSecondaryMode;
                _settings.Save();

                _adb.Run($"-s {target} shell svc power stayon true");
                _adb.Run($"-s {target} shell settings put system screen_off_timeout 2147483647");
                _adb.Run($"-s {target} shell settings put system screen_brightness_mode 0");
                _adb.Run($"-s {target} shell settings put system screen_brightness_mode_epd 0");
                _adb.Run($"-s {target} shell settings put system screen_brightness_mode_secondary 0");
                _adb.Run($"-s {target} shell settings put system screen_brightness 0");
                _adb.Run($"-s {target} shell cmd display set-brightness 0");
                _screenDimmed = true;
                AppendLog(Loc.T("已息屏：亮度=0，设备保持唤醒"));
            }
            else if (!dim && _screenDimmed)
            {
                _adb.Run($"-s {target} shell cmd display reset-brightness-configuration");
                _adb.Run($"-s {target} shell settings put system screen_brightness {_savedBrightness}");
                _adb.Run($"-s {target} shell settings put system screen_brightness_mode {_savedBrightnessMode}");
                _adb.Run($"-s {target} shell settings put system screen_brightness_mode_epd {_savedEpdMode}");
                _adb.Run($"-s {target} shell settings put system screen_brightness_mode_secondary {_savedSecondaryMode}");
                _adb.Run($"-s {target} shell settings put system screen_off_timeout {_savedScreenTimeout}");
                _adb.Run($"-s {target} shell svc power stayon false");
                _screenDimmed = false;
                _settings.DimActive = false;
                _settings.Save();
                AppendLog(Loc.T("已恢复亮度"));
            }
        }
        catch (Exception ex) { AppendLog(Loc.T("息屏操作失败: ") + ex.Message); }
    }

    /// <summary>启动时如发现上次息屏没来得及恢复，自动补一次</summary>
    private void RestoreLeftoverDim()
    {
        if (!_settings.DimActive) return;

        var serial = !string.IsNullOrEmpty(_settings.LastSerial) ? _settings.LastSerial : _activeSerial;
        if (string.IsNullOrEmpty(serial))
        {
            _settings.DimActive = false;
            _settings.Save();
            return;
        }

        _savedBrightness = _settings.DimSavedBrightness;
        _savedBrightnessMode = _settings.DimSavedBrightnessMode;
        _savedScreenTimeout = _settings.DimSavedScreenTimeout;
        _savedEpdMode = _settings.DimSavedEpdMode;
        _savedSecondaryMode = _settings.DimSavedSecondaryMode;
        _screenDimmed = true;   // 让 ApplyScreenDim(false) 走恢复分支
        ApplyScreenDim(false, serial);
        AppendLog(Loc.T("检测到上次息屏未恢复，已自动还原屏幕亮度"));
    }

    // ---------------- 音频 ----------------

    private async void Audio_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        int mode = RadAudio1.IsChecked == true ? 1 : (RadAudio2.IsChecked == true ? 2 : 0);
        _settings.AudioMode = mode;
        _settings.Save();

        ApplyAudioHint(mode);

        if (!_runner.IsRunning || string.IsNullOrEmpty(_activeSerial)) return;

        var serial = _activeSerial;
        AppendLog(Loc.T("音频模式切换，正在重启投屏…"));
        BtnStart.IsEnabled = false;
        _restartingMirror = true;
        try
        {
            // 全部放到后台线程，避免 UI 卡顿（历史 BUG-04）
            await Task.Run(() => _runner.Stop());
            await Task.Delay(400);
            var size = await Task.Run(() => _adb.GetScreenSize(serial));
            await Task.Run(() => _runner.Start(_settings, serial, size.w, size.h));

            _scrcpyHwnd = IntPtr.Zero;
            _videoAspect = 0;

            bool ok = await WaitForMirrorWindowAsync(8000);
            LblStatus.Text = ok ? Loc.T("已按新音频模式重启") : Loc.T("重启后未等到投屏窗口，请查看日志");
            AppendLog(ok ? Loc.T("音频模式已切换") : Loc.T("音频模式切换后未检测到投屏窗口"));
        }
        catch (Exception ex) { AppendLog(Loc.T("重启投屏失败: ") + ex.Message); }
        finally
        {
            _restartingMirror = false;
            BtnStart.IsEnabled = true;
        }
    }

    // ---------------- 外观 ----------------

    private void ChkAutoHide_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _settings.AutoHideOnLeave = ChkAutoHide.IsChecked == true;
        _settings.Save();
        _autoHideArmed = false;
        _outsideSince = DateTime.MinValue;
        AppendLog(_settings.AutoHideOnLeave ? Loc.T("已开启：鼠标移开自动隐藏") : Loc.T("已关闭：鼠标移开自动隐藏"));
    }

    private void HoverTimer_Tick(object sender, EventArgs e)
    {
        if (!_settings.AutoHideOnLeave) { _autoHideArmed = false; return; }
        if (_bossHidden) return;

        var hwnd = EnsureScrcpyWindow();
        if (hwnd == IntPtr.Zero || !NativeWin32.IsWindowVisible(hwnd))
        {
            _autoHideArmed = false;
            _outsideSince = DateTime.MinValue;
            return;
        }

        if (!NativeWin32.GetWindowRect(hwnd, out var r)) return;

        // 窗口还在移动（刚启动时 scrcpy 会先出现在默认位置，再被移到记忆位置），
        // 这期间不参与「鼠标是否离开」的判定，否则会把窗口自身移动误判成鼠标离开。
        if (_hasLastHoverRect &&
            (r.Left != _lastHoverRect.Left || r.Top != _lastHoverRect.Top ||
             r.Right != _lastHoverRect.Right || r.Bottom != _lastHoverRect.Bottom))
        {
            _lastHoverRect = r;
            _autoHideArmed = false;
            _outsideSince = DateTime.MinValue;
            return;
        }
        _lastHoverRect = r;
        _hasLastHoverRect = true;

        // 刚开投屏的短暂抑制期，避免窗口还没落位就触发
        if (DateTime.UtcNow < _autoHideSuppressUntil) return;

        NativeWin32.GetCursorPos(out var pt);
        bool inside = pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom;

        if (inside)
        {
            _autoHideArmed = true;
            _outsideSince = DateTime.MinValue;
            return;
        }

        if (!_autoHideArmed) return;

        if (_outsideSince == DateTime.MinValue)
        {
            _outsideSince = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _outsideSince).TotalMilliseconds >= 500)
        {
            _autoHideArmed = false;
            _outsideSince = DateTime.MinValue;
            AppendLog(Loc.T("鼠标移开投屏窗口，自动隐藏"));
            ToggleBossMode();
        }
    }

    private void ChkTop_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var hwnd = EnsureScrcpyWindow();
        if (hwnd != IntPtr.Zero)
            NativeWin32.SetTopmost(hwnd, ChkTop.IsChecked == true);
    }

    private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblOpacity == null) return;
        LblOpacity.Text = $"{e.NewValue:0}%";
        var hwnd = _scrcpyHwnd;
        if (hwnd != IntPtr.Zero && NativeWin32.IsWindow(hwnd))
            NativeWin32.SetAlpha(hwnd, e.NewValue / 100.0);
    }

    // ---------------- 老板键（可自定义）----------------

    private void BtnCaptureKey_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = !_capturingHotkey;
        if (_capturingHotkey)
        {
            TxtBossKey.Text = Loc.T("请按下组合键…");
            BtnCaptureKey.Content = Loc.T("取消");
            Focus();
        }
        else
        {
            TxtBossKey.Text = _settings.BossKey;
            BtnCaptureKey.Content = Loc.T("录制");
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;

        if (e.Key == Key.Escape)
        {
            _capturingHotkey = false;
            TxtBossKey.Text = _settings.BossKey;
            BtnCaptureKey.Content = Loc.T("录制");
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return;

        var desc = HotkeyUtil.Describe(mods, key, out _, out _);
        TxtBossKey.Text = desc;
        _settings.BossKey = desc;
        _settings.Save();

        _capturingHotkey = false;
        BtnCaptureKey.Content = Loc.T("录制");

        UnregisterBossKey();
        RegisterBossKey();
        AppendLog(Loc.T("老板键已改为 ") + desc);
        e.Handled = true;
    }

    private void RegisterBossKey()
    {
        var helper = new WindowInteropHelper(this);
        var src = HwndSource.FromHwnd(helper.Handle);
        src?.AddHook(WndProc);

        if (!HotkeyUtil.TryParse(_settings.BossKey, out var mods, out var vk))
        {
            AppendLog(Loc.T("老板键格式无效：") + _settings.BossKey);
            return;
        }

        _hotkeyRegistered = NativeWin32.RegisterHotKey(helper.Handle, HOTKEY_ID, mods, vk);
        AppendLog(_hotkeyRegistered ? Loc.T("老板键已注册：") + _settings.BossKey
                                   : Loc.T("老板键注册失败（可能被占用）：") + _settings.BossKey);
    }

    private void UnregisterBossKey()
    {
        try
        {
            if (_hotkeyRegistered)
                NativeWin32.UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_ID);
        }
        catch { }
        _hotkeyRegistered = false;
    }

    private bool _restartingMirror;
    private System.Windows.Forms.NotifyIcon _tray;
    private System.Windows.Forms.ContextMenuStrip _trayMenu;
    private bool _trayHintShown;
    private bool _bossHidden;
    private NativeWin32.RECT _bossMirrorRect;
    private bool _bossMirrorMinimized;
    private double _bossMainLeft, _bossMainTop, _bossMainW, _bossMainH;
    private WindowState _bossMainState;
    private double _bossOpacity = 1.0;
    private bool _mirrorRectApplied;
    private DateTime _suppressAspectUntil = DateTime.MinValue;

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
        HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            long lp = lParam.ToInt64();
            int sx = (short)(lp & 0xFFFF);
            int sy = (short)((lp >> 16) & 0xFFFF);
            var p = PointFromScreen(new Point(sx, sy));

            const double b = 7;
            bool left = p.X <= b, right = p.X >= ActualWidth - b;
            bool top = p.Y <= b, bottom = p.Y >= ActualHeight - b;

            int hit = 0;
            if (top && left) hit = HTTOPLEFT;
            else if (top && right) hit = HTTOPRIGHT;
            else if (bottom && left) hit = HTBOTTOMLEFT;
            else if (bottom && right) hit = HTBOTTOMRIGHT;
            else if (left) hit = HTLEFT;
            else if (right) hit = HTRIGHT;
            else if (top) hit = HTTOP;
            else if (bottom) hit = HTBOTTOM;

            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
            return new IntPtr(HTCLIENT);
        }

        if (msg == NativeWin32.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleBossMode();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>老板键：隐藏/恢复「投屏窗口」和「设置窗口」，并完整还原几何与透明度</summary>
    private void ToggleBossMode()
    {
        var mirror = EnsureScrcpyWindow();

        if (!_bossHidden)
        {
            // ---- 保存 ----
            _bossOpacity = SldOpacity.Value / 100.0;
            _bossMirrorMinimized = false;
            if (mirror != IntPtr.Zero)
            {
                NativeWin32.GetWindowRect(mirror, out _bossMirrorRect);
                _bossMirrorMinimized = NativeWin32.IsIconic(mirror);
            }

            _bossMainState = WindowState;
            // 最小化/最大化时 Left/Top/Width/Height 不可靠，必须用 RestoreBounds
            var rb = RestoreBounds;
            _bossMainLeft = rb.IsEmpty ? Left : rb.Left;
            _bossMainTop = rb.IsEmpty ? Top : rb.Top;
            _bossMainW = rb.IsEmpty ? Width : rb.Width;
            _bossMainH = rb.IsEmpty ? Height : rb.Height;

            // ---- 隐藏 ----
            if (mirror != IntPtr.Zero) NativeWin32.ShowWindow(mirror, NativeWin32.SW_HIDE);
            Hide();
            _bossHidden = true;
            AppendLog(Loc.T("老板键：已隐藏"));
        }
        else
        {
            // ---- 恢复主窗口 ----
            Show();
            WindowState = WindowState.Normal;
            Left = _bossMainLeft;
            Top = _bossMainTop;
            Width = SanitizeSize(_bossMainW, Width, 200, 4000);
            Height = SanitizeSize(_bossMainH, Height, 300, 3000);
            if (_bossMainState == WindowState.Maximized) WindowState = WindowState.Maximized;
            else if (_bossMainState == WindowState.Minimized) WindowState = WindowState.Minimized;
            Activate();

            // ---- 恢复投屏窗口 ----
            if (mirror != IntPtr.Zero)
            {
                NativeWin32.ShowWindow(mirror, NativeWin32.SW_SHOW);
                NativeWin32.MoveResize(mirror,
                    _bossMirrorRect.Left, _bossMirrorRect.Top,
                    _bossMirrorRect.Right - _bossMirrorRect.Left,
                    _bossMirrorRect.Bottom - _bossMirrorRect.Top);
                NativeWin32.SetAlpha(mirror, _bossOpacity);
                if (_bossMirrorMinimized) NativeWin32.ShowWindow(mirror, 6 /* SW_MINIMIZE */);
                else NativeWin32.SetForegroundWindow(mirror);
            }

            _suppressAspectUntil = DateTime.UtcNow.AddMilliseconds(900);
            _autoHideArmed = false;
            _outsideSince = DateTime.MinValue;
            _bossHidden = false;
            AppendLog(Loc.T("老板键：已恢复"));
        }
    }

    // ---------------- 窗口几何记忆 ----------------

    /// <summary>校验保存下来的窗口尺寸：NaN / 过小 / 异常值一律回退到当前值</summary>
    private static double SanitizeSize(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min) return fallback;
        return Math.Min(value, max);
    }

    private void RestoreMainGeometry()
    {
        try
        {
            Width = SanitizeSize(_settings.MainWidth, Width, 200, 4000);
            Height = SanitizeSize(_settings.MainHeight, Height, 300, 3000);

            if (!double.IsNaN(_settings.MainLeft) && !double.IsNaN(_settings.MainTop))
            {
                double vl = SystemParameters.VirtualScreenLeft;
                double vt = SystemParameters.VirtualScreenTop;
                double vw = SystemParameters.VirtualScreenWidth;
                double vh = SystemParameters.VirtualScreenHeight;
                if (_settings.MainLeft >= vl - 50 && _settings.MainLeft < vl + vw &&
                    _settings.MainTop >= vt - 50 && _settings.MainTop < vt + vh)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = _settings.MainLeft;
                    Top = _settings.MainTop;
                }
            }
        }
        catch { }
    }

    private void SaveGeometry()
    {
        try
        {
            var rb = RestoreBounds;
            if (!rb.IsEmpty)
            {
                _settings.MainLeft = rb.Left;
                _settings.MainTop = rb.Top;
                _settings.MainWidth = rb.Width;
                _settings.MainHeight = rb.Height;
            }
            _settings.MainMaximized = WindowState == WindowState.Maximized;

            if (_scrcpyHwnd != IntPtr.Zero && NativeWin32.IsWindow(_scrcpyHwnd) &&
                NativeWin32.GetWindowRect(_scrcpyHwnd, out var r))
            {
                _settings.MirrorHasRect = true;
                _settings.MirrorLeft = r.Left;
                _settings.MirrorTop = r.Top;
                _settings.MirrorWidth = r.Right - r.Left;
                _settings.MirrorHeight = r.Bottom - r.Top;
            }
            _settings.Opacity = SldOpacity.Value / 100.0;
        }
        catch { }
    }

    // ---------------- 窗口跟踪 / 比例锁定 ----------------

    private IntPtr EnsureScrcpyWindow()
    {
        if (_scrcpyHwnd != IntPtr.Zero && NativeWin32.IsWindow(_scrcpyHwnd))
            return _scrcpyHwnd;

        var h = NativeWin32.FindWindowByTitleContains(_runner.WindowTitle);

        // 刚发现新投屏窗口时，先解除「鼠标移开自动隐藏」的待命状态。
        // scrcpy 初始窗口可能恰好盖住鼠标、随后被移到记忆位置，
        // 会被误判成「鼠标移开」导致刚开投屏就自动隐藏（实测 1 秒即触发）。
        if (h != IntPtr.Zero && h != _scrcpyHwnd)
        {
            _autoHideArmed = false;
            _outsideSince = DateTime.MinValue;
        }

        // 新窗口出现：重置鼠标判定状态，并给自己 1.5 秒落位时间
        if (h != IntPtr.Zero && h != _scrcpyHwnd)
        {
            _hasLastHoverRect = false;
            _autoHideSuppressUntil = DateTime.UtcNow.AddMilliseconds(1500);
        }

        _scrcpyHwnd = h;
        return _scrcpyHwnd;
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
        if (!_runner.IsRunning) return;

        var hwnd = EnsureScrcpyWindow();
        if (hwnd == IntPtr.Zero) return;

        if (_videoAspect <= 0)
        {
            if (NativeWin32.GetClientRect(hwnd, out var cr) && cr.Right > 0 && cr.Bottom > 0)
            {
                _videoAspect = (double)cr.Right / cr.Bottom;

                if (!_mirrorRectApplied && _settings.MirrorHasRect &&
                    _settings.MirrorWidth > 0 && _settings.MirrorHeight > 0)
                {
                    NativeWin32.MoveResize(hwnd, _settings.MirrorLeft, _settings.MirrorTop,
                        _settings.MirrorWidth, _settings.MirrorHeight);
                    _mirrorRectApplied = true;
                    AppendLog(Loc.T("已恢复上次的投屏窗口位置/大小"));
                }

                NativeWin32.SetTopmost(hwnd, ChkTop.IsChecked == true);
                NativeWin32.SetAlpha(hwnd, SldOpacity.Value / 100.0);
                AppendLog(Loc.F("已锁定投屏窗口（比例 {0:0.000}）", _videoAspect));
            }
            return;
        }

        if (_bossHidden) return;
        if (DateTime.UtcNow < _suppressAspectUntil) return;

        if (ChkLockAspect.IsChecked == true)
            EnforceAspect(hwnd);
    }

    private int _lastW, _lastH;
    private DateTime _lastResizeUtc = DateTime.MinValue;

    private void EnforceAspect(IntPtr hwnd)
    {
        if (!NativeWin32.GetClientRect(hwnd, out var cr)) return;
        if (cr.Right <= 0 || cr.Bottom <= 0) return;

        // 尺寸还在变化（用户正在拖）→ 只记录，不干预
        if (cr.Right != _lastW || cr.Bottom != _lastH)
        {
            _lastW = cr.Right;
            _lastH = cr.Bottom;
            _lastResizeUtc = DateTime.UtcNow;
            return;
        }

        // 松手后稳定 180ms 再纠正
        if ((DateTime.UtcNow - _lastResizeUtc).TotalMilliseconds < 180) return;

        double cur = (double)cr.Right / cr.Bottom;
        if (Math.Abs(cur - _videoAspect) < 0.02) return;

        int newH = (int)Math.Round(cr.Right / _videoAspect);
        if (newH <= 0) return;

        if (NativeWin32.GetWindowRect(hwnd, out var wr))
        {
            int frameW = (wr.Right - wr.Left) - cr.Right;
            int frameH = (wr.Bottom - wr.Top) - cr.Bottom;

            // 关键：SWP_NOMOVE —— 保持窗口左上角不动，只改尺寸
            NativeWin32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0,
                cr.Right + frameW, newH + frameH,
                NativeWin32.SWP_NOMOVE | NativeWin32.SWP_NOZORDER | NativeWin32.SWP_NOACTIVATE);
        }
    }

    // ---------------- 语言 ----------------

    private void BtnLangZh_Click(object sender, RoutedEventArgs e) => SwitchLanguage("zh-CN");
    private void BtnLangEn_Click(object sender, RoutedEventArgs e) => SwitchLanguage("en-US");

    private void SwitchLanguage(string lang)
    {
        if (Loc.Instance.Current.Equals(lang, StringComparison.OrdinalIgnoreCase)) return;

        Loc.Instance.SetLanguage(lang);
        _settings.Language = lang;
        _settings.Save();

        ApplyLanguage();
        AppendLog($"Language / 语言: {(Loc.Instance.IsEnglish ? "English" : "中文")}");
    }

    /// <summary>语言切换后，刷新那些不是通过 Binding 生成的文本</summary>
    private void ApplyLanguage()
    {
        LblVersion.Text = Loc.F("当前版本 v{0}", UpdateService.CurrentVersion);
        LblAbout.Text = Loc.F("MoyuCast 摸鱼投屏 v{0}\n基于 scrcpy 3.3.4 的 Windows 图形化投屏控制台\n开源协议：MIT（本项目自身代码）",
            UpdateService.CurrentVersion);
        LblUpdate.Text = Loc.T("从 GitHub Releases 获取最新版本，国内网络自动走镜像。");
        BtnStart.Content = _runner.IsRunning ? Loc.T("停止投屏") : Loc.T("开始投屏");
        BtnCaptureKey.Content = _capturingHotkey ? Loc.T("取消") : Loc.T("录制");
        BtnToggleLog.Content = PnlLog.Visibility == Visibility.Visible ? Loc.T("收起日志 ▴") : Loc.T("展开日志 ▾");
        ApplyAudioHint(_settings.AudioMode);
        UpdateLangButtons();
        UpdateTrayText();
    }

    private void UpdateLangButtons()
    {
        bool en = Loc.Instance.IsEnglish;
        if (BtnLangZh == null || BtnLangEn == null) return;
        BtnLangZh.IsEnabled = en;    // 当前语言按钮置灰
        BtnLangEn.IsEnabled = !en;
    }

    private void ApplyAudioHint(int mode)
    {
        switch (mode)
        {
            case 1: LblAudioHint.Text = Loc.T("手机与电脑同时出声（需 Android 13+）。"); break;
            case 2: LblAudioHint.Text = Loc.T("仅电脑出声，手机会静音。"); break;
            default: LblAudioHint.Text = Loc.T("手机出声，电脑不继承。"); break;
        }
    }

    // ---------------- 日志折叠 ----------------

    private void BtnToggleLog_Click(object sender, RoutedEventArgs e)
    {
        bool show = PnlLog.Visibility != Visibility.Visible;
        PnlLog.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BtnToggleLog.Content = show ? Loc.T("收起日志 ▴") : Loc.T("展开日志 ▾");
        if (show) TxtLog.ScrollToEnd();
    }

    // ---------------- 关于 ----------------

    private void BtnOpenRepo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/lee19890505/moyucast") { UseShellExecute = true });
        }
        catch (Exception ex) { AppendLog(Loc.T("打开项目主页失败: ") + ex.Message); }
    }

    // ---------------- 检查更新 ----------------

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        LblUpdate.Text = Loc.T("正在检查更新…");
        try
        {
            var info = await UpdateService.CheckAsync();
            if (string.IsNullOrEmpty(info.Version))
            {
                LblUpdate.Text = Loc.T("未获取到版本信息，请稍后再试。");
                return;
            }

            if (UpdateService.IsNewer(info.Version, UpdateService.CurrentVersion))
            {
                LblUpdate.Text = Loc.F("发现新版本 v{0}（当前 v{1}）", info.Version, UpdateService.CurrentVersion);
                AppendLog(Loc.F("检查更新：发现新版本 v{0}", info.Version));
                var r = MessageBox.Show(
                    Loc.F("发现新版本 v{0}\n当前版本 v{1}\n\n是否打开发布页下载？", info.Version, UpdateService.CurrentVersion),
                    Loc.T("MoyuCast 检查更新"), MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes && !string.IsNullOrEmpty(info.Url))
                    Process.Start(new ProcessStartInfo(info.Url) { UseShellExecute = true });
            }
            else
            {
                LblUpdate.Text = Loc.F("已是最新版本 v{0}", UpdateService.CurrentVersion);
                AppendLog(Loc.T("检查更新：已是最新版本 v") + UpdateService.CurrentVersion);
            }
        }
        catch (Exception ex)
        {
            LblUpdate.Text = Loc.T("检查失败：") + ex.Message;
            AppendLog(Loc.T("检查更新失败: ") + ex.Message);
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    // ---------------- 托盘图标 ----------------

    private void InitTray()
    {
        try
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            _trayMenu = new System.Windows.Forms.ContextMenuStrip();
            _trayMenu.Opening += (_, __) => RefreshTrayMenu();

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = Loc.T("MoyuCast 摸鱼投屏"),
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _tray.DoubleClick += (_, __) => ShowFromTray();
            RefreshTrayMenu();
        }
        catch (Exception ex)
        {
            AppendLog(Loc.T("托盘图标初始化失败: ") + ex.Message);
        }
    }

    private void RefreshTrayMenu()
    {
        if (_trayMenu == null) return;
        _trayMenu.Items.Clear();

        var show = new System.Windows.Forms.ToolStripMenuItem(Loc.T("显示主窗口"));
        show.Click += (_, __) => ShowFromTray();
        _trayMenu.Items.Add(show);

        var toggle = new System.Windows.Forms.ToolStripMenuItem(_runner.IsRunning ? Loc.T("停止投屏") : Loc.T("开始投屏"));
        toggle.Click += (_, __) =>
        {
            ShowFromTray();
            if (_runner.IsRunning) StopMirroring(Loc.T("已停止"));
            else BtnStart_Click(BtnStart, new RoutedEventArgs());
        };
        _trayMenu.Items.Add(toggle);

        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exit = new System.Windows.Forms.ToolStripMenuItem(Loc.T("退出 MoyuCast"));
        exit.Click += (_, __) =>
        {
            Close();
        };
        _trayMenu.Items.Add(exit);
    }

    private void UpdateTrayText()
    {
        if (_tray == null) return;
        try
        {
            _tray.Text = _runner.IsRunning ? Loc.T("MoyuCast · 投屏中") : Loc.T("MoyuCast 摸鱼投屏");
        }
        catch { }
    }

    private void HideToTray()
    {
        Hide();
        if (!_trayHintShown && _tray != null)
        {
            _trayHintShown = true;
            try
            {
                _tray.ShowBalloonTip(2500, Loc.T("MoyuCast 仍在运行"),
                    Loc.T("已最小化到托盘，双击托盘图标可恢复窗口。"), System.Windows.Forms.ToolTipIcon.Info);
            }
            catch { }
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void DisposeTray()
    {
        try
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
        catch { }
    }

    // ---------------- 日志 ----------------

    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => AppendLog(line)); return; }
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        TxtLog.ScrollToEnd();
    }
}


