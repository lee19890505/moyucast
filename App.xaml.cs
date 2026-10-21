using System.Threading;
using System.Windows;

namespace MoyuCast;

/// <summary>
/// 应用入口。除启动主窗口外，还负责「单实例」互斥：
/// 第二次运行时不会开新窗口，而是通知已运行的实例把窗口显示出来。
/// </summary>
public partial class App : Application
{
    private const string MutexName = "MoyuCast.SingleInstance.4E1F7C2A";
    private const string ShowEventName = "MoyuCast.ShowWindow.4E1F7C2A";

    private Mutex _mutex;
    private EventWaitHandle _showEvent;
    private Thread _listener;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            // 已有实例在运行：唤醒它，然后本进程退出
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _listener = new Thread(ListenForShowRequest)
        {
            IsBackground = true,
            Name = "MoyuCast-SingleInstance"
        };
        _listener.Start();

        base.OnStartup(e);
    }

    private void ListenForShowRequest()
    {
        while (true)
        {
            try
            {
                if (!_showEvent.WaitOne()) continue;

                Dispatcher.Invoke(() =>
                {
                    var win = MainWindow;
                    if (win == null) return;

                    if (!win.IsVisible) win.Show();
                    if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;

                    win.Activate();
                    win.Topmost = true;
                    win.Topmost = false;   // 置顶一下再取消，确保窗口跑到最前
                    win.Focus();
                });
            }
            catch
            {
                // 单个唤醒请求失败不应影响后续请求
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _showEvent?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
