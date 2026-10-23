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
    public bool LockAspect { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
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
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
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

