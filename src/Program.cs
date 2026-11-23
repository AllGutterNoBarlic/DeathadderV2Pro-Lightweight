using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DeathAdderTool;

static class Program
{
    public const string QuitEventName = "DeathAdderTool.Quit";
    public const string ShowEventName = "DeathAdderTool.Show";
    const string MutexName = "DeathAdderTool.SingleInstance";

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    /// <summary>Sets a named event if it exists. An instance running at a different elevation can't be reached; that's not an error.</summary>
    public static bool Signal(string name)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out var ev)) return false;
            ev.Set(); ev.Dispose(); return true;
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Runs a "does it exist" probe; access denied means it exists (owned by an elevated instance).</summary>
    static bool Exists(Func<bool> probe)
    {
        try { return probe(); } catch (UnauthorizedAccessException) { return true; }
    }

    static bool Dispose(IDisposable? d) { d?.Dispose(); return true; }

    [STAThread]
    static int Main(string[] args)
    {
        bool Has(string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));

        // --selftest: offline parser + command-catalog checks (used by CI). Never sends input or touches the mouse.
        if (Has("--selftest"))
        {
            AttachConsole(-1); // WinExe has no console of its own; print to the caller's (CI) if there is one
            var errors = SelfTest.Run();
            foreach (var e in errors) Console.Error.WriteLine(e);
            Console.WriteLine(errors.Count == 0
                ? $"selftest OK ({SelfTest.CaseCount} parser cases + command catalog)"
                : $"selftest FAILED: {errors.Count} problem(s)");
            return errors.Count == 0 ? 0 : 1;
        }

        // --uninstall: ask a running instance to exit, then restore the DPI buttons (used by the uninstaller).
        // --quit:      ask a running instance to exit.
        // --restore:   put the factory DPI up/down behaviour back on the mouse.
        if (Has("--uninstall") || Has("--quit"))
        {
            bool any = false;
            foreach (var name in new[] { QuitEventName, SettingsHost.CloseEventName })
                any |= Signal(name);
            // Wait (up to 5 s) until both processes have really exited, so an uninstaller can delete the exe.
            for (int i = 0; any && i < 50; i++)
            {
                if (!Exists(() => Mutex.TryOpenExisting(MutexName, out var mx) && Dispose(mx))
                    && !Exists(() => EventWaitHandle.TryOpenExisting(SettingsHost.ActivateEventName, out var ev) && Dispose(ev)))
                    break;
                Thread.Sleep(100);
            }
            if (Has("--quit")) return 0;
        }
        if (Has("--uninstall") || Has("--restore"))
        {
            using var m = RazerHid.FindMouse();
            return m != null && m.RestoreDpiButtons() ? 0 : 1;
        }

        // --settings: the settings window, in its own process (launched by the tray app).
        if (Has("--settings")) return SettingsHost.Run();

        Mutex mutex;
        bool first;
        try { mutex = new Mutex(true, MutexName, out first); }
        catch (UnauthorizedAccessException) { return 0; } // already running elevated (we're not, or vice versa)
        using var _m = mutex;
        if (!first)
        {
            // Already running: ask that instance to show its window (not for autostart, which should stay quiet).
            if (!Has("--tray")) Signal(ShowEventName);
            return 0;
        }

        // Named events let a second launch / the uninstaller talk to us without killing the process.
        using var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var tray = new TrayApp();
        ThreadPool.RegisterWaitForSingleObject(quitEvent, (_, _) => tray.Messages.PostQuit(), null, -1, true);
        ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => tray.Messages.PostShow(), null, -1, false);

        // Started by autostart (--tray): stay in the tray. Started by the user: show the window.
        if (!Has("--tray")) tray.Messages.PostShow();

        Application.Run(); // plain Win32 message loop; WPF is only loaded when the window is first opened
        return 0;
    }
}
