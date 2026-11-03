using System.Globalization;
using System.Text.RegularExpressions;
using MoyuCast.Models;

namespace MoyuCast.Services;

/// <summary>
/// 读取一台设备的基础信息。所有字段合并到一次 adb shell 调用里，
/// 用 @@MC_xxx 标记分段，避免为十几个字段启动十几次 adb。
/// </summary>
public class DeviceInfoService
{
    private readonly AdbService _adb;

    public DeviceInfoService(AdbService adb) => _adb = adb;

    public DeviceInfo Get(string serial, string state)
    {
        var info = new DeviceInfo { Serial = serial, State = state, IsWireless = serial.Contains(':') };
        if (!string.Equals(state, "device", StringComparison.OrdinalIgnoreCase))
        {
            info.Error = state;
            return info;
        }

        const string shell =
            "echo @@MC_NAME; settings get global device_name; " +
            "echo @@MC_PROPS; getprop ro.product.manufacturer; getprop ro.product.model; " +
            "getprop ro.product.marketname; getprop ro.build.version.release; " +
            "getprop ro.build.version.sdk; getprop ro.product.cpu.abi; " +
            "getprop ro.soc.model; getprop ro.board.platform; " +
            "echo @@MC_WM; wm size; wm density; " +
            "echo @@MC_MEM; grep -E 'MemTotal|MemAvailable' /proc/meminfo; " +
            "echo @@MC_DF; df -k /data; " +
            "echo @@MC_BAT; dumpsys battery; " +
            "echo @@MC_IP; ip -f inet addr show wlan0; " +
            "echo @@MC_UP; cat /proc/uptime";

        var output = _adb.Run($"-s {serial} shell \"{shell}\"", 20000);
        if (output.StartsWith("ERR", StringComparison.OrdinalIgnoreCase) || output.Length == 0)
        {
            info.Error = string.IsNullOrWhiteSpace(_adb.LastError) ? output : _adb.LastError;
            return info;
        }

        var sec = SplitSections(output);

        var name = Clean(Get(sec, "NAME"));
        var manufacturer = Line(Get(sec, "PROPS"), 0);
        var model = Line(Get(sec, "PROPS"), 1);
        var market = Line(Get(sec, "PROPS"), 2);
        info.Manufacturer = manufacturer;
        info.Model = model;
        info.Name = !string.IsNullOrWhiteSpace(name) && name != "null"
            ? name
            : (!string.IsNullOrWhiteSpace(market) && market != "null" ? market : model);
        info.AndroidVersion = Line(Get(sec, "PROPS"), 3);
        info.Sdk = Line(Get(sec, "PROPS"), 4);
        info.Abi = Line(Get(sec, "PROPS"), 5);
        info.Soc = FirstNonEmpty(Line(Get(sec, "PROPS"), 6), Line(Get(sec, "PROPS"), 7));

        ParseScreen(Get(sec, "WM"), info);
        ParseMemory(Get(sec, "MEM"), info);
        ParseStorage(Get(sec, "DF"), info);
        ParseBattery(Get(sec, "BAT"), info);
        info.WifiIp = FirstMatch(Get(sec, "IP"), @"inet\s+(\d+\.\d+\.\d+\.\d+)");
        var up = FirstMatch(Get(sec, "UP"), @"^\s*([\d.]+)");
        if (double.TryParse(up, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            info.UptimeSeconds = seconds;

        return info;
    }

    private static Dictionary<string, string> SplitSections(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(text, @"^@@MC_([A-Z]+)\s*$", RegexOptions.Multiline);
        for (int i = 0; i < matches.Count; i++)
        {
            var name = matches[i].Groups[1].Value;
            int start = matches[i].Index + matches[i].Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            result[name] = text.Substring(start, end - start).Replace("\r", "").Trim('\n', ' ');
        }
        return result;
    }

    private static string Get(Dictionary<string, string> sec, string key)
        => sec.TryGetValue(key, out var v) ? v : "";

    private static string Clean(string s) => (s ?? "").Trim();

    private static string Line(string section, int index)
    {
        var lines = (section ?? "").Split('\n');
        return index >= 0 && index < lines.Length ? lines[index].Trim() : "";
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v != "null") ?? "";

    private static string FirstMatch(string text, string pattern)
    {
        var m = Regex.Match(text ?? "", pattern, RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static void ParseScreen(string section, DeviceInfo info)
    {
        var physical = Regex.Match(section, @"Physical size:\s*(\d+)\s*x\s*(\d+)", RegexOptions.IgnoreCase);
        var any = physical.Success ? physical : Regex.Match(section, @"(\d+)\s*x\s*(\d+)");
        if (any.Success)
        {
            info.ScreenWidth = int.Parse(any.Groups[1].Value);
            info.ScreenHeight = int.Parse(any.Groups[2].Value);
        }
        var d = Regex.Match(section, @"(?:Physical density|density):\s*(\d+)", RegexOptions.IgnoreCase);
        if (!d.Success) d = Regex.Match(section, @"(\d+)");
        if (d.Success) info.Density = int.Parse(d.Groups[1].Value);
    }

    private static void ParseMemory(string section, DeviceInfo info)
    {
        var total = Regex.Match(section, @"MemTotal:\s*(\d+)\s*kB", RegexOptions.IgnoreCase);
        var avail = Regex.Match(section, @"MemAvailable:\s*(\d+)\s*kB", RegexOptions.IgnoreCase);
        if (total.Success) info.RamTotalKb = long.Parse(total.Groups[1].Value);
        if (avail.Success) info.RamAvailableKb = long.Parse(avail.Groups[1].Value);
    }

    private static void ParseStorage(string section, DeviceInfo info)
    {
        foreach (var raw in (section ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.Contains("/data", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 &&
                long.TryParse(parts[1], out var total) &&
                long.TryParse(parts[2], out var used))
            {
                info.StorageTotalKb = total;
                info.StorageUsedKb = used;
                return;
            }
        }
    }

    private static void ParseBattery(string section, DeviceInfo info)
    {
        var level = Regex.Match(section, @"level:\s*(\d+)", RegexOptions.IgnoreCase);
        if (level.Success) info.BatteryLevel = int.Parse(level.Groups[1].Value);
        var temp = Regex.Match(section, @"temperature:\s*(-?\d+)", RegexOptions.IgnoreCase);
        if (temp.Success) info.BatteryTemperatureTenths = int.Parse(temp.Groups[1].Value);
        var status = Regex.Match(section, @"status:\s*(\d+)", RegexOptions.IgnoreCase);
        if (status.Success) info.BatteryStatus = status.Groups[1].Value;
    }
}

