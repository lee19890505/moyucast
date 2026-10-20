using System.Diagnostics;
using System.IO;
using System.Text;
using ScrcpyDesk.Models;

namespace ScrcpyDesk.Services;

public class ScrcpyRunner
{
    private Process _proc;

    public event Action<string> Log;
    public event Action<int> Exited;

    public bool IsRunning => _proc != null && !_proc.HasExited;

    public string WindowTitle { get; private set; } = "ScrcpyDesk-Mirror";

    public string BuildArguments(AppSettings s, string serial, int screenW, int screenH)
    {
        var a = new List<string>();
        a.Add("--window-title");
        a.Add("\"" + WindowTitle + "\"");

        if (!string.IsNullOrWhiteSpace(serial))
        {
            a.Add("--serial");
            a.Add(serial);
        }

        if (s.StayAwake) a.Add("--stay-awake");
        if (s.AlwaysOnTop) a.Add("--always-on-top");
        if (s.TurnScreenOff) a.Add("--turn-screen-off");

        // 音频
        switch (s.AudioMode)
        {
            case 0: // 手机响，电脑不继承
                a.Add("--no-audio");
                break;
            case 1: // 两边同时响
                a.Add("--audio-source=playback");
                a.Add("--audio-dup");
                break;
            case 2: // 仅电脑响（手机会静音）
                a.Add("--audio-source=output");
                break;
        }

        // 键盘模式
        a.Add(s.KeyboardUhid ? "--keyboard=uhid" : "--keyboard=sdk");

        // 窗口宽度（按手机比例，高度由 scrcpy 自动算）
        if (s.ScrcpyWidth > 0)
        {
            a.Add("--window-width");
            a.Add(s.ScrcpyWidth.ToString());
        }

        return string.Join(" ", a);
    }

    public void Start(AppSettings s, string serial, int screenW, int screenH)
    {
        Stop();

        string exe = Path.Combine(s.ScrcpyDir, "scrcpy.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("找不到 scrcpy.exe", exe);

        string args = BuildArguments(s, serial, screenW, screenH);
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
            WorkingDirectory = s.ScrcpyDir
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
        p.Exited += (_, e) => { int c = -1; try { c = p.ExitCode; } catch { } Exited?.Invoke(c); };

        Log?.Invoke("> scrcpy " + args);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _proc = p;
    }

    public void Stop()
    {
        if (_proc == null) return;
        try
        {
            if (!_proc.HasExited)
            {
                _proc.Kill(true);
                _proc.WaitForExit(3000);
            }
        }
        catch { }
        finally
        {
            try { _proc.Dispose(); } catch { }
            _proc = null;
        }
    }
}
