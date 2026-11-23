using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

namespace DeathAdderTool;

/// <summary>
/// Entry point of the settings-window process (DeathAdderTool.exe --settings).
/// The window lives in its own process so the tray process never loads WPF, and closing the window
/// returns every byte of the UI's memory to Windows. Each WPF reference sits behind this NoInlining
/// method, because the JIT loads an assembly the first time a method that uses it is compiled.
/// </summary>
static class SettingsHost
{
    public const string ActivateEventName = "DeathAdderTool.ActivateSettings";
    public const string CloseEventName = "DeathAdderTool.CloseSettings"; // set by --quit / --uninstall

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run()
    {
        // One settings window at a time: a second launch just brings the first one forward.
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName, out bool created);
        if (!created) { activate.Set(); return 0; }

        using var svc = new MouseService();
        Macro.DpiHandler = arg => svc.DpiCommand(arg); // for the Test button

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new SettingsWindow(svc);
        ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => app.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            // Bring to front without any cross-process foreground permission: briefly topmost, then normal.
            window.Topmost = true;
            window.Activate();
            window.Topmost = false;
        })), null, -1, false);
        using var close = new EventWaitHandle(false, EventResetMode.AutoReset, CloseEventName);
        ThreadPool.RegisterWaitForSingleObject(close, (_, _) => app.Dispatcher.BeginInvoke(new Action(() =>
        {
            window.ForceClose = true; // quit/uninstall: no "unsaved changes?" prompt
            app.Shutdown();
        })), null, -1, true);
        int code = app.Run(window);
        window.TestTask?.Wait(TimeSpan.FromSeconds(65)); // let a running Test macro finish (and release its keys)
        return code;
    }
}
