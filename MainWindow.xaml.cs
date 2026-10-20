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
    private DateTime _outsideSince = DateTime.MinValue;

    private IntPtr _scrcpyHwnd = IntPtr.Zero;
    private double _videoAspect = 0;
    private bool _syncing = true;
    private string _activeSerial = "";

    private bool _screenDimmed;
    private int _savedBrightness = 29;
    private int _savedBrightnessMode = 1;
    private int _savedScreenTimeout = 30000;

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

        _settings = AppSettings.Load();
        _adb = new AdbService(_settings.ScrcpyDir);

        _runner.Log += AppendLog;
        _runner.Exited += OnScrcpyExited;

        LoadSettingsToUi();
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
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
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
        int idx = btn == Nav0 ? 0 : btn == Nav1 ? 1 : btn == Nav2 ? 2 : btn == Nav3 ? 3 : 4;
        ShowPage(idx);
    }

    private void ShowPage(int idx)
    {
        var pages = new UIElement[] { PageDevice, PageDisplay, PageAudio, PageAppearance, PageLog };
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;

        var navs = new[] { Nav0, Nav1, Nav2, Nav3, Nav4 };
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

    private static string ResolveScrcpyDir(string configured)
    {
        bool HasScrcpy(string dir) =>
            !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "scrcpy.exe"));

        if (HasScrcpy(configured)) return configured;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "scrcpy"),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..", "scrcpy"),
            @"D:\工具\scrcpy-win64-v3.3.4",
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
            ? "未找到 scrcpy，请点右侧选择"
            : _settings.ScrcpyDir;
    }

    private void BtnPickScrcpy_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 scrcpy 所在文件夹（里面有 scrcpy.exe）"
        };
        if (dlg.ShowDialog() == true)
        {
            var dir = dlg.FolderName;
            if (!File.Exists(Path.Combine(dir, "scrcpy.exe")))
            {
                LblStatus.Text = "该目录下没有 scrcpy.exe";
                return;
            }
            _settings.ScrcpyDir = dir;
            _settings.Save();
            _adb = new AdbService(dir);
            RefreshScrcpyPathUi();
            AppendLog("scrcpy 目录已设为: " + dir);
            _ = RefreshDevicesAsync();
        }
    }
    // ---------------- 设备 ----------------

    private async Task RefreshDevicesAsync()
    {
        BtnRefresh.IsEnabled = false;
        LblStatus.Text = "正在扫描设备…";
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

            if (items.Count == 0)
            {
                LblStatus.Text = "未发现设备";
            }
            else
            {
                var pick = items.FirstOrDefault(x => x.Serial == prev)
                           ?? items.FirstOrDefault(x => x.Serial == _settings.LastSerial)
                           ?? items[0];
                CmbDevice.SelectedItem = pick;
                LblStatus.Text = items.Count == 1 ? "已连接 1 台设备" : $"已连接 {items.Count} 台设备";
            }
        }
        catch (Exception ex)
        {
            LblStatus.Text = "扫描失败";
            AppendLog("刷新设备失败: " + ex.Message);
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

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
            LblStatus.Text = "请先用 USB 连接手机";
            AppendLog("一键无线：需要先用 USB 连接一次");
            return;
        }

        ShowLoading("正在开启无线投屏…", "读取手机 WiFi 地址");
        try
        {
            var ipOut = await Task.Run(() => _adb.Run($"-s {usb.Serial} shell ip -f inet addr show wlan0"));
            var m = Regex.Match(ipOut, @"inet\s+(\d+\.\d+\.\d+\.\d+)");
            if (!m.Success)
            {
                AppendLog("读取 WiFi IP 失败：" + ipOut.Replace("\n", " "));
                LblStatus.Text = "读取 IP 失败，请手动填写";
                return;
            }
            var ip = m.Groups[1].Value;
            TxtIp.Text = ip;
            TxtLoadingSub.Text = $"已获取 IP {ip}，正在切换 TCP 模式…";

            await Task.Run(() => _adb.Run($"-s {usb.Serial} tcpip 5555"));
            await Task.Delay(2500);

            TxtLoadingSub.Text = $"正在连接 {ip}:5555 …";
            var r = await Task.Run(() => _adb.Connect($"{ip}:5555"));
            AppendLog($"connect {ip}:5555 -> {r}");

            ChkWireless.IsChecked = true;
            await RefreshDevicesAsync();
            LblStatus.Text = r.Contains("connected") ? $"无线已连接 {ip}" : "无线连接失败";
        }
        catch (Exception ex) { AppendLog(ex.Message); }
        finally { HideLoading(); }
    }

    private void BtnPair_Click(object sender, RoutedEventArgs e)
    {
        var host = TxtIp.Text.Trim();
        var port = TxtPort.Text.Trim();
        var code = TxtPairCode.Text.Trim();
        if (host.Length == 0 || code.Length == 0) { LblStatus.Text = "请填写 IP 和配对码"; return; }

        var addr = host.Contains(':') ? host : $"{host}:{port}";
        var r = _adb.Pair(addr, code);
        AppendLog("pair " + addr + " -> " + r);
        LblStatus.Text = r.Contains("Successfully") ? "配对成功" : "配对失败，见日志";
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        var host = TxtIp.Text.Trim();
        var port = TxtPort.Text.Trim();
        if (host.Length == 0) { LblStatus.Text = "请填写 IP"; return; }

        var addr = host.Contains(':') ? host : $"{host}:{port}";
        BtnConnect.IsEnabled = false;
        ShowLoading("正在连接…", addr);
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
        if (_runner.IsRunning) { StopMirroring("已停止"); return; }

        string serial = "";
        if (ChkWireless.IsChecked == true)
        {
            var host = TxtIp.Text.Trim();
            if (host.Length == 0) { LblStatus.Text = "请填写无线 IP"; return; }
            serial = host.Contains(':') ? host : $"{host}:{TxtPort.Text.Trim()}";
        }
        else if (CmbDevice.SelectedItem is DeviceItem d)
        {
            serial = d.Serial;
        }

        if (string.IsNullOrEmpty(serial)) { LblStatus.Text = "请先选择设备"; return; }

        SaveUiToSettings();
        _settings.Save();
        _activeSerial = serial;

        BtnStart.IsEnabled = false;
        ShowLoading("正在连接设备…", "推送服务端并建立画面通道");

        var sw = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                var size = _adb.GetScreenSize(serial);
                _runner.Start(_settings, serial, size.w, size.h);
            });

            BtnStart.Content = "停止投屏";
            LblStatus.Text = "投屏中";

            bool ok = await WaitForMirrorWindowAsync(8000);
            AppendLog(ok ? $"投屏就绪（{sw.ElapsedMilliseconds} ms）" : "投屏窗口未出现，请查看日志");

            if (ChkScreenOff.IsChecked == true) ApplyScreenDim(true);
        }
        catch (Exception ex)
        {
            LblStatus.Text = "启动失败";
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
        BtnStart.Content = "开始投屏";
        LblStatus.Text = status;
    }

    private void OnScrcpyExited(int code)
    {
        Dispatcher.Invoke(() =>
        {
            _scrcpyHwnd = IntPtr.Zero;
            _videoAspect = 0;
            BtnStart.Content = "开始投屏";
            LblStatus.Text = $"投屏已退出（{code}）";
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
        if (string.IsNullOrEmpty(_activeSerial)) { LblStatus.Text = "请先开始投屏"; return; }
        ApplyScreenDim(!_screenDimmed);
        ChkScreenOff.IsChecked = _screenDimmed;
    }

    private int ParseInt(string s, int fallback)
        => int.TryParse(s?.Trim(), out var v) ? v : fallback;

    private void ApplyScreenDim(bool dim)
    {
        if (string.IsNullOrEmpty(_activeSerial)) return;
        try
        {
            if (dim && !_screenDimmed)
            {
                _savedBrightness = ParseInt(_adb.Run($"-s {_activeSerial} shell settings get system screen_brightness"), 29);
                _savedBrightnessMode = ParseInt(_adb.Run($"-s {_activeSerial} shell settings get system screen_brightness_mode"), 1);
                _savedScreenTimeout = ParseInt(_adb.Run($"-s {_activeSerial} shell settings get system screen_off_timeout"), 30000);

                _adb.Run($"-s {_activeSerial} shell svc power stayon true");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_off_timeout 2147483647");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness_mode 0");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness_mode_epd 0");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness_mode_secondary 0");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness 0");
                _adb.Run($"-s {_activeSerial} shell cmd display set-brightness 0");
                _screenDimmed = true;
                AppendLog("已息屏：亮度=0，设备保持唤醒");
            }
            else if (!dim && _screenDimmed)
            {
                _adb.Run($"-s {_activeSerial} shell cmd display reset-brightness-configuration");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness {_savedBrightness}");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness_mode {_savedBrightnessMode}");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness_mode_epd 1");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_brightness_mode_secondary 1");
                _adb.Run($"-s {_activeSerial} shell settings put system screen_off_timeout {_savedScreenTimeout}");
                _adb.Run($"-s {_activeSerial} shell svc power stayon false");
                _screenDimmed = false;
                AppendLog("已恢复亮度");
            }
        }
        catch (Exception ex) { AppendLog("息屏操作失败: " + ex.Message); }
    }

    // ---------------- 音频 ----------------

    private void Audio_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        int mode = RadAudio1.IsChecked == true ? 1 : (RadAudio2.IsChecked == true ? 2 : 0);
        _settings.AudioMode = mode;
        _settings.Save();

        switch (mode)
        {
            case 0: LblAudioHint.Text = "手机出声，电脑不继承。"; break;
            case 1: LblAudioHint.Text = "手机与电脑同时出声（需 Android 13+）。"; break;
            case 2: LblAudioHint.Text = "仅电脑出声，手机会静音。"; break;
        }

        if (_runner.IsRunning && !string.IsNullOrEmpty(_activeSerial))
        {
            AppendLog("音频模式切换，正在重启投屏…");
            try
            {
                _runner.Stop();
                Thread.Sleep(400);
                var size = _adb.GetScreenSize(_activeSerial);
                _runner.Start(_settings, _activeSerial, size.w, size.h);
                _scrcpyHwnd = IntPtr.Zero;
                _videoAspect = 0;
                LblStatus.Text = "已按新音频模式重启";
            }
            catch (Exception ex) { AppendLog(ex.Message); }
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
        AppendLog(_settings.AutoHideOnLeave ? "已开启：鼠标移开自动隐藏" : "已关闭：鼠标移开自动隐藏");
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
            AppendLog("鼠标移开投屏窗口，自动隐藏");
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
            TxtBossKey.Text = "请按下组合键…";
            BtnCaptureKey.Content = "取消";
            Focus();
        }
        else
        {
            TxtBossKey.Text = _settings.BossKey;
            BtnCaptureKey.Content = "录制";
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;

        if (e.Key == Key.Escape)
        {
            _capturingHotkey = false;
            TxtBossKey.Text = _settings.BossKey;
            BtnCaptureKey.Content = "录制";
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
        BtnCaptureKey.Content = "录制";

        UnregisterBossKey();
        RegisterBossKey();
        AppendLog("老板键已改为 " + desc);
        e.Handled = true;
    }

    private void RegisterBossKey()
    {
        var helper = new WindowInteropHelper(this);
        var src = HwndSource.FromHwnd(helper.Handle);
        src?.AddHook(WndProc);

        if (!HotkeyUtil.TryParse(_settings.BossKey, out var mods, out var vk))
        {
            AppendLog("老板键格式无效：" + _settings.BossKey);
            return;
        }

        _hotkeyRegistered = NativeWin32.RegisterHotKey(helper.Handle, HOTKEY_ID, mods, vk);
        AppendLog(_hotkeyRegistered ? "老板键已注册：" + _settings.BossKey
                                   : "老板键注册失败（可能被占用）：" + _settings.BossKey);
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
            AppendLog("老板键：已隐藏");
        }
        else
        {
            // ---- 恢复主窗口 ----
            Show();
            WindowState = WindowState.Normal;
            Left = _bossMainLeft;
            Top = _bossMainTop;
            Width = Math.Clamp(_bossMainW, 360, 4000);
            Height = Math.Clamp(_bossMainH, 520, 3000);
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
            AppendLog("老板键：已恢复");
        }
    }

    // ---------------- 窗口几何记忆 ----------------

    private void RestoreMainGeometry()
    {
        try
        {
            Width = Math.Clamp(_settings.MainWidth, 360, 4000);
            Height = Math.Clamp(_settings.MainHeight, 520, 3000);

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
        _scrcpyHwnd = NativeWin32.FindWindowByTitleContains(_runner.WindowTitle);
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
                    AppendLog("已恢复上次的投屏窗口位置/大小");
                }

                NativeWin32.SetTopmost(hwnd, ChkTop.IsChecked == true);
                NativeWin32.SetAlpha(hwnd, SldOpacity.Value / 100.0);
                AppendLog($"已锁定投屏窗口（比例 {_videoAspect:0.000}）");
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

    // ---------------- 日志 ----------------

    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => AppendLog(line)); return; }
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        TxtLog.ScrollToEnd();
    }
}