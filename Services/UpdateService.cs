using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MoyuCast.Services;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>读取 GitHub Releases 最新版本。国内直连 api.github.com 不通时会自动走镜像。</summary>
public static class UpdateService
{
    private const string Owner = "lee19890505";
    private const string Repo = "moyucast";

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var api = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        var urls = new[]
        {
            api,
            "https://gh-proxy.com/" + api,
            "https://ghproxy.net/" + api,
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MoyuCast/" + CurrentVersion);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        Exception last = null;
        foreach (var url in urls)
        {
            try
            {
                var json = await http.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new UpdateInfo
                {
                    Version = (root.TryGetProperty("tag_name", out var t) ? t.GetString() : "")?.TrimStart('v', 'V') ?? "",
                    Url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "",
                    Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : ""
                };
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException("检查更新失败：" + (last?.Message ?? "网络不可用"));
    }

    public static bool IsNewer(string remote, string local)
    {
        static Version Parse(string s) =>
            Version.TryParse((s ?? "").Trim().TrimStart('v', 'V'), out var v) ? v : new Version(0, 0, 0);

        return Parse(remote) > Parse(local);
    }
}
