namespace MoyuCast.Models;

/// <summary>设备页展示的一台 Android 设备的基础信息（一次 adb shell 批量读取）。</summary>
public class DeviceInfo
{
    public string Serial { get; set; } = "";
    public string State { get; set; } = "";
    public bool IsWireless { get; set; }

    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string AndroidVersion { get; set; } = "";
    public string Sdk { get; set; } = "";
    public string Abi { get; set; } = "";
    public string Soc { get; set; } = "";
    public string WifiIp { get; set; } = "";

    public long RamTotalKb { get; set; }
    public long RamAvailableKb { get; set; }
    public long StorageTotalKb { get; set; }
    public long StorageUsedKb { get; set; }

    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int Density { get; set; }

    public int BatteryLevel { get; set; } = -1;
    public int BatteryTemperatureTenths { get; set; } = int.MinValue;
    public string BatteryStatus { get; set; } = "";

    public double UptimeSeconds { get; set; }
    public string Error { get; set; } = "";

    /// <summary>按像素和对角线 dpi 估算的屏幕英寸数。</summary>
    public double ScreenInches
    {
        get
        {
            if (ScreenWidth <= 0 || ScreenHeight <= 0 || Density <= 0) return 0;
            return Math.Sqrt((double)ScreenWidth * ScreenWidth + (double)ScreenHeight * ScreenHeight) / Density;
        }
    }

    public long RamUsedKb => RamTotalKb > 0 && RamAvailableKb >= 0
        ? Math.Max(0, RamTotalKb - RamAvailableKb) : 0;
}
