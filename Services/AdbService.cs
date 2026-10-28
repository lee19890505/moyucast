using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MoyuCast.Services;

public class AdbService
{
    public string ScrcpyDir { get; set; }
    public string LastError { get; private set; } = "";

    public AdbService(string scrcpyDir) => ScrcpyDir = scrcpyDir;

    /// <summary>优先用 scrcpy 目录里的 adb，找不到就退回系统 PATH</summary>
    public string AdbExe
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ScrcpyDir))
            {
                var p = Path.Combine(ScrcpyDir, "adb.exe");
                if (File.Exists(p)) return p;
            }
            return "adb";
        }
    }

    /// <summary>
    /// 执行 adb 命令。
    /// stdout/stderr 必须用异步方式同时读，否则一端写满 4KB 管道缓冲就会死锁，
    /// 超时参数也永远不会生效（历史 BUG-01）。
    /// </summary>
    public string Run(string args, int timeoutMs = 15000)
    {
        LastError = "";
        var exe = AdbExe;
        if (exe != "adb" && !File.Exists(exe))
        {
            LastError = "找不到 adb.exe：" + exe;
            return "ERR: " + LastError;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = string.IsNullOrWhiteSpace(ScrcpyDir) ? Environment.CurrentDirectory : ScrcpyDir
            };

            using var p = new Process { StartInfo = psi };
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            var outDone = new ManualResetEventSlim(false);
            var errDone = new ManualResetEventSlim(false);

            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) { outDone.Set(); return; }
                lock (sbOut) sbOut.AppendLine(e.Data);
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) { errDone.Set(); return; }
                lock (sbErr) sbErr.AppendLine(e.Data);
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            bool exited = p.WaitForExit(timeoutMs);
            if (!exited)
            {
                try { p.Kill(true); } catch { }
                try { p.WaitForExit(2000); } catch { }
                LastError = "adb 命令超时（" + timeoutMs + "ms）";
            }

            // 等待异步读取线程把剩余数据排空（最多 2 秒，防止极端情况下再次卡死）
            outDone.Wait(2000);
            errDone.Wait(2000);

            string stdout, stderr;
            lock (sbOut) stdout = sbOut.ToString();
            lock (sbErr) stderr = sbErr.ToString();

            if (exited && p.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
                LastError = stderr.Trim();

            return (stdout + stderr).Trim();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return "ERR: " + ex.Message;
        }
    }

    private static readonly HashSet<string> ValidStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "device", "offline", "unauthorized", "recovery", "sideload", "bootloader", "host"
    };

    public List<(string serial, string state, string model)> ListDevices()
    {
        var list = new List<(string, string, string)>();
        var output = Run("devices -l");

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("*")) continue;
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains(" daemon ", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("adb:", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            string serial = parts[0];
            string state = parts[1];

            bool okState = ValidStates.Contains(state) ||
                           state.StartsWith("no permissions", StringComparison.OrdinalIgnoreCase);
            if (!okState) continue;

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

    public int GetScreenDpi(string serial)
    {
        var o = Run($"-s {serial} shell wm density", 10000);
        var m = Regex.Match(o, @"(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>执行 adb 命令并把 stdout 以二进制返回（取 APK 图标等二进制数据用）</summary>
    public byte[] RunBinary(string args, int timeoutMs = 20000)
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
                WorkingDirectory = string.IsNullOrWhiteSpace(ScrcpyDir) ? Environment.CurrentDirectory : ScrcpyDir
            };
            using var p = new Process { StartInfo = psi };
            p.Start();
            using var ms = new MemoryStream();
            var readTask = p.StandardOutput.BaseStream.CopyToAsync(ms);
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return Array.Empty<byte>();
            }
            readTask.Wait(3000);
            errTask.Wait(1000);
            return ms.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    public bool IsWirelessSerial(string serial) => serial.Contains(":");
}
