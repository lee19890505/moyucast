using System.IO;
using System.Text.Json;

namespace MoyuCast.Models;

public class AppSettings
{
    public string ScrcpyDir { get; set; } = "";
    /// <summary>界面语言：zh-CN / en-US，空字符串表示跟随系统</summary>
    public string Language { get; set; } = "";
    public string LastSerial { get; set; } = "";
    public string LastWirelessIp { get; set; } = "";
    public int AudioMode { get; set; } = 0;
    public bool TurnScreenOff { get; set; } = false;
    public string DisplayMode { get; set; } = "mirror";
    public string LastLockApp { get; set; } = "";
    public List<string> StarredApps { get; set; } = new();
    public Dictionary<string, string> AppAliases { get; set; } = new();
    public bool LockAspect { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    /// <summary>锁屏投屏启动后是否自动发送电源键关闭物理屏</summary>
    public bool AutoLockOnStart { get; set; } = true;
    public bool StayAwake { get; set; } = true;
    public bool Wireless { get; set; } = false;
    public double Opacity { get; set; } = 1.0;
    public string BossKey { get; set; } = "Ctrl+Alt+H";
    public int ScrcpyWidth { get; set; } = 0;
    public bool KeyboardUhid { get; set; } = true;
    public bool DisablePcIme { get; set; } = true;
    public bool AutoHideOnLeave { get; set; } = false;
    /// <summary>true = 只要鼠标不在投屏窗口上就隐藏（无需先移入）；false = 必须先移入再移出</summary>
    public bool AutoHideAggressive { get; set; } = false;
    /// <summary>是否把自动隐藏的判定写入 app.log</summary>
    public bool Diagnostics { get; set; } = false;

    // ---- 息屏状态（防止进程被强杀后手机停在 0 亮度）----
    /// <summary>锁屏模式是否改过 stay_on_while_plugged_in（用于崩溃后还原）</summary>
    public bool StayOnActive { get; set; } = false;
    public int StayOnSaved { get; set; } = 0;
    public bool DimActive { get; set; } = false;
    public int DimSavedBrightness { get; set; } = 29;
    public int DimSavedBrightnessMode { get; set; } = 1;
    public int DimSavedScreenTimeout { get; set; } = 30000;
    public int DimSavedEpdMode { get; set; } = 1;
    public int DimSavedSecondaryMode { get; set; } = 1;

    // ---- 窗口几何记忆 ----
    public double MainLeft { get; set; } = double.NaN;
    public double MainTop { get; set; } = double.NaN;
    public double MainWidth { get; set; } = 430;
    public double MainHeight { get; set; } = 760;
    public bool MainMaximized { get; set; } = false;

    // ---- 窗口位置（按模式 / App 存，为多窗口做准备）----
    public Dictionary<string, WindowRect> WindowRects { get; set; } = new();

    // 旧字段仅用于迁移，新版本以 WindowRects 为准
    public bool MirrorHasRect { get; set; } = false;
    public int MirrorLeft { get; set; }
    public int MirrorTop { get; set; }
    public int MirrorWidth { get; set; }
    public int MirrorHeight { get; set; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoyuCast");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    s.MigrateLegacyGeometry();
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    /// <summary>旧版本只有一组 Mirror* 几何字段，迁移到按模式区分的字典。</summary>
    private void MigrateLegacyGeometry()
    {
        if (WindowRects == null) WindowRects = new();
        if (WindowRects.Count > 0) return;
        if (!MirrorHasRect || MirrorWidth <= 0 || MirrorHeight <= 0) return;

        var key = DisplayMode == "lock" && !string.IsNullOrWhiteSpace(LastLockApp)
            ? "lock:" + LastLockApp
            : "mirror";
        WindowRects[key] = new WindowRect
        {
            Left = MirrorLeft,
            Top = MirrorTop,
            Width = MirrorWidth,
            Height = MirrorHeight
        };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

