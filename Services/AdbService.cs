using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MoyuCast.Services;

public class AdbService
{
    public string ScrcpyDir { get; set; }

    public AdbService(string scrcpyDir) => ScrcpyDir = scrcpyDir;

    private string AdbExe => Path.Combine(ScrcpyDir, "adb.exe");

    public string Run(string args, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = AdbExe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = ScrcpyDir
            };
            using var p = Process.Start(psi);
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return stdout + stderr;
            }
            return (stdout + stderr).Trim();
        }
        catch (Exception ex)
        {
            return "ERR: " + ex.Message;
        }
    }

    public List<(string serial, string state, string model)> ListDevices()
    {
        var list = new List<(string, string, string)>();
        var output = Run("devices -l");
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("List of devices")) continue;
            if (line.StartsWith("*")) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            string serial = parts[0];
            string state = parts[1];
            string model = "";
            foreach (var p in parts)
                if (p.StartsWith("model:")) model = p.Substring(6);
            list.Add((serial, state, model));
        }
        return list;
    }

    public string Pair(string hostPort, string code)
        => Run($"pair {hostPort} {code}", 30000);

    public string Connect(string hostPort)
        => Run($"connect {hostPort}", 20000);

    public string Disconnect(string hostPort)
        => Run($"disconnect {hostPort}", 10000);

    public string RestartServer()
    {
        Run("kill-server", 8000);
        return Run("start-server", 15000);
    }

    public (int w, int h) GetScreenSize(string serial)
    {
        var o = Run($"-s {serial} shell wm size", 10000);
        var m = Regex.Match(o, @"(\d+)\s*x\s*(\d+)");
        if (m.Success)
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        return (0, 0);
    }

    public bool IsWirelessSerial(string serial) => serial.Contains(":");
}
