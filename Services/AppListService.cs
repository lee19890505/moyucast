using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MoyuCast.Models;

namespace MoyuCast.Services;

/// <summary>
/// 读取设备上「可启动的第三方 App」列表，并尽力从 APK 里提取原生图标。
/// 说明：Android 没有暴露「App 显示名 + 图标」的 adb 接口，图标只能按文件名猜（约 44% 命中），
/// 拿不到的由界面降级成字母头像。
/// </summary>
public static class AppListService
{
    private static readonly Dictionary<string, string> BuiltinNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["com.tencent.mm"] = "微信",
        ["com.tencent.wetype"] = "微信输入法",
        ["com.alibaba.android.rimet"] = "钉钉",
        ["cn.wps.moffice_eng"] = "WPS Office",
        ["com.dragon.read"] = "番茄免费小说",
        ["com.luna.music"] = "汽水音乐",
        ["com.xiaomi.router"] = "小米路由器",
        ["com.eg.android.AlipayGphone"] = "支付宝",
        ["com.taobao.taobao"] = "淘宝",
        ["com.jingdong.app.mall"] = "京东",
        ["com.ss.android.ugc.aweme"] = "抖音",
        ["com.xingin.xhs"] = "小红书",
        ["com.netease.cloudmusic"] = "网易云音乐",
        ["com.tencent.mobileqq"] = "QQ",
        ["com.tencent.qqlive"] = "腾讯视频",
        ["com.qiyi.video"] = "爱奇艺",
        ["tv.danmaku.bili"] = "哔哩哔哩",
        ["com.zhihu.android"] = "知乎",
        ["com.sina.weibo"] = "微博",
        ["com.android.chrome"] = "Chrome",
    };

    private static readonly Regex ComponentRegex = new(@"^\s*([A-Za-z0-9_.]+)/([A-Za-z0-9_.$]+)\s*$", RegexOptions.Compiled);

    public static string IconCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoyuCast", "icons");

    public static Task<List<AppItem>> LoadAsync(AdbService adb, string serial, AppSettings settings, CancellationToken ct)
        => Task.Run(() => Load(adb, serial, settings, ct), ct);

    public static List<AppItem> Load(AdbService adb, string serial, AppSettings settings, CancellationToken ct)
    {
        var result = new List<AppItem>();

        var thirdParty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pkgOut = adb.Run($"-s {serial} shell pm list packages -3", 15000);
        foreach (var line in pkgOut.Split('\n'))
        {
            var t = line.Trim();
            if (!t.StartsWith("package:")) continue;
            thirdParty.Add(t.Substring("package:".Length).Trim());
        }
        if (thirdParty.Count == 0) return result;

        var outText = adb.Run(
            $"-s {serial} shell cmd package query-activities --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER",
            20000);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in outText.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            var m = ComponentRegex.Match(line);
            if (!m.Success) continue;

            var pkg = m.Groups[1].Value;
            var act = m.Groups[2].Value;
            if (!thirdParty.Contains(pkg)) continue;
            if (!seen.Add(pkg)) continue;

            result.Add(new AppItem
            {
                Package = pkg,
                Activity = act,
                DisplayName = ResolveName(pkg, settings, out var known) ? known : ShortName(pkg),
                IsStarred = settings.StarredApps != null && settings.StarredApps.Contains(pkg)
            });
        }

        return Sort(result, settings);
    }

    public static List<AppItem> Sort(List<AppItem> items, AppSettings settings)
    {
        var starred = new HashSet<string>(settings.StarredApps ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        return items
            .OrderByDescending(x => starred.Contains(x.Package))
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool ResolveName(string pkg, AppSettings settings, out string name)
    {
        if (settings.AppAliases != null && settings.AppAliases.TryGetValue(pkg, out var alias) && !string.IsNullOrWhiteSpace(alias))
        {
            name = alias;
            return true;
        }
        if (BuiltinNames.TryGetValue(pkg, out var built))
        {
            name = built;
            return true;
        }
        name = null;
        return false;
    }

    private static string ShortName(string pkg)
    {
        var idx = pkg.LastIndexOf('.');
        var s = idx >= 0 && idx < pkg.Length - 1 ? pkg.Substring(idx + 1) : pkg;
        return s.Replace('_', ' ');
    }

    /// <summary>尝试从 APK 里提取图标；失败返回 null（界面用字母头像兜底）</summary>
    public static string TryGetIcon(AdbService adb, string serial, string pkg, CancellationToken ct)
    {
        try
        {
            var cache = Path.Combine(IconCacheDir, pkg + ".png");
            if (File.Exists(cache) && new FileInfo(cache).Length > 64) return cache;

            var pathOut = adb.Run($"-s {serial} shell pm path {pkg}", 8000);
            var apk = pathOut.Replace("package:", "").Replace("\r", "").Trim();
            if (string.IsNullOrEmpty(apk) || !apk.EndsWith(".apk")) return null;

            var entry = FindIconEntry(adb, serial, apk);
            if (string.IsNullOrEmpty(entry)) return null;

            ct.ThrowIfCancellationRequested();
            var bytes = adb.RunBinary($"-s {serial} exec-out \"unzip -p '{apk}' '{entry}'\"", 25000);
            if (bytes == null || bytes.Length < 128) return null;
            if (bytes[0] != 0x89 || bytes[1] != 0x50) return null;   // PNG magic

            Directory.CreateDirectory(IconCacheDir);
            File.WriteAllBytes(cache, bytes);
            return cache;
        }
        catch
        {
            return null;
        }
    }

    private static string FindIconEntry(AdbService adb, string serial, string apk)
    {
        var cmd = $"unzip -l '{apk}' 2>/dev/null | grep -iE 'ic_launcher[^ ]*\\.png$' | sort -k1 -n -r | head -1";
        var line = adb.Run($"-s {serial} shell \"{cmd}\"", 15000);
        var entry = LastToken(line);
        if (!string.IsNullOrEmpty(entry)) return entry;

        cmd = $"unzip -l '{apk}' 2>/dev/null | grep -iE 'mipmap[^ ]*\\.png$' | sort -k1 -n -r | head -1";
        line = adb.Run($"-s {serial} shell \"{cmd}\"", 15000);
        return LastToken(line);
    }

    private static string LastToken(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var t = line.Trim();
        var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var last = parts[parts.Length - 1];
        return last.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? last : null;
    }

    /// <summary>
    /// 批量提取图标：把 (包名|APK路径) 推到设备，用一个 shell 脚本把图标写到 /sdcard/MoyuCast/icons，
    /// 再一次性 adb pull 回来。比逐个 exec-out 快一个数量级。
    /// </summary>
    public static int SyncIcons(AdbService adb, string serial, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "moyucast-icons-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(tmp, "out");
        try
        {
            Directory.CreateDirectory(tmp);

            var pkgOut = adb.Run($"-s {serial} shell pm list packages -f -3", 20000);
            var pairs = new StringBuilder();
            foreach (var line in pkgOut.Split('\n'))
            {
                var t = line.Trim();
                if (!t.StartsWith("package:")) continue;
                var body = t.Substring("package:".Length);
                var eq = body.LastIndexOf('=');
                if (eq <= 0) continue;
                var apk = body.Substring(0, eq).Trim();
                var pkg = body.Substring(eq + 1).Trim();
                if (apk.Length == 0 || pkg.Length == 0) continue;
                pairs.Append(pkg).Append('|').Append(apk).Append('\n');
            }
            if (pairs.Length == 0) return 0;

            var pairsFile = Path.Combine(tmp, "pairs.txt");
            File.WriteAllText(pairsFile, pairs.ToString(), new UTF8Encoding(false));

            var scriptFile = Path.Combine(tmp, "icons.sh");
            var script =
                "#!/system/bin/sh\n" +
                "rm -rf /sdcard/MoyuCast/icons\n" +
                "mkdir -p /sdcard/MoyuCast/icons\n" +
                "while IFS='|' read -r p a; do\n" +
                "  [ -z \"$p\" ] && continue\n" +
                "  e=$(unzip -l \"$a\" 2>/dev/null | grep -iE 'ic_launcher[^ ]*\\.png$' | sort -k1 -n -r | head -1 | awk '{print $NF}')\n" +
                "  [ -z \"$e\" ] && continue\n" +
                "  unzip -p \"$a\" \"$e\" > \"/sdcard/MoyuCast/icons/$p.png\" 2>/dev/null\n" +
                "done < /data/local/tmp/moyucast_pairs.txt\n";
            File.WriteAllText(scriptFile, script, new UTF8Encoding(false));

            adb.Run($"-s {serial} push \"{pairsFile}\" /data/local/tmp/moyucast_pairs.txt", 20000);
            adb.Run($"-s {serial} push \"{scriptFile}\" /data/local/tmp/moyucast_icons.sh", 20000);
            adb.Run($"-s {serial} shell \"sh /data/local/tmp/moyucast_icons.sh\"", 180000);

            ct.ThrowIfCancellationRequested();
            adb.Run($"-s {serial} pull /sdcard/MoyuCast/icons \"{outDir}\"", 180000);
            adb.Run($"-s {serial} shell \"rm -rf /sdcard/MoyuCast\"", 20000);
            adb.Run($"-s {serial} shell \"rm -f /data/local/tmp/moyucast_pairs.txt /data/local/tmp/moyucast_icons.sh\"", 20000);

            var srcDir = Directory.Exists(outDir) ? outDir : Path.Combine(outDir, "icons");
            if (!Directory.Exists(srcDir)) return 0;

            Directory.CreateDirectory(IconCacheDir);
            var count = 0;
            foreach (var file in Directory.GetFiles(srcDir, "*.png"))
            {
                var name = Path.GetFileName(file);
                if (new FileInfo(file).Length < 128) continue;
                File.Copy(file, Path.Combine(IconCacheDir, name), true);
                count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }
}


