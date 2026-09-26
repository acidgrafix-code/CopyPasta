using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using CopyPasta.Interop;
using Velopack;

namespace CopyPasta.App;

/// <summary>
/// Entry point. Runs a single instance, pumps messages, and logs to a file.
/// </summary>
/// <remarks>
/// Tray-only, like the macOS app's <c>LSUIElement</c>: there is no main window at all, so the
/// message loop is explicit rather than supplied by a UI framework.
/// </remarks>
internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\CopyPasta.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        // Must be first, before anything else touches the filesystem or the tray. Velopack uses
        // this to run install, update and uninstall hooks: on those runs it does its work and
        // exits the process, so nothing below should assume it will be reached.
        VelopackApp.Build()
            // Runs as part of installation, while the installer is still working. This is where
            // the shell gets nudged, not OnFirstRun: Explorer caches shortcut icons by path and
            // does not reliably notice that a newly-installed target supplies one, so the desktop
            // shortcut can draw with the generic executable icon. A silent install never launches
            // the app, so a first-run hook would fire long after the user had already seen it.
            .OnAfterInstallFastCallback(_ =>
            {
                Log(DefaultLogPath, "installed");
                ShellNotification.NotifyAssociationsChanged();
            })
            .OnAfterUpdateFastCallback(version =>
            {
                Log(DefaultLogPath, $"updated to {version}");

                // An update replaces the binary the shortcut points at, so the cached icon can go
                // stale again even though the shortcut never changed.
                ShellNotification.NotifyAssociationsChanged();
            })
            .OnFirstRun(_ => Log(DefaultLogPath, "first run"))
            .Run();

        InstallCrashHandlers();

        // A second copy would fight the first over the clipboard and the tray icon.
        using Mutex instanceLock = new(initiallyOwned: false, SingleInstanceMutexName, out _);
        if (!instanceLock.WaitOne(TimeSpan.Zero))
        {
            return 1;
        }

        try
        {
            AppSettings settings = AppSettings.Load();
            string databasePath = DatabasePathFrom(args);
            string logPath = DefaultLogPath;

            if (args.Contains("--dump-menu", StringComparer.OrdinalIgnoreCase))
            {
                return DumpMenu(settings, databasePath);
            }

            if (RunSnippetDiagnostic(args, settings, databasePath) is { } snippetExitCode)
            {
                return snippetExitCode;
            }

            if (RunOcrDiagnostic(args, settings, databasePath) is { } ocrExitCode)
            {
                return ocrExitCode;
            }

            using TrayApplication app = new(settings, databasePath, PostQuit);
            app.Trace += (_, message) => Log(logPath, message);

            app.Start();

            // An escape hatch: with the tray icon hidden and no hotkeys bound there would
            // otherwise be no way back into the settings.
            if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            {
                app.ShowSettings();
            }

            if (ValueFor(args, "--render-settings") is { } renderDirectory)
            {
                app.RenderSettingsPanes(renderDirectory);
                return 0;
            }

            try
            {
                RunMessageLoop();
            }
            finally
            {
                app.Shutdown();
            }

            return 0;
        }
        catch (Exception exception)
        {
            // Nothing is on screen to show an error in, so record it and fail visibly in the log.
            Log(DefaultLogPath, $"fatal: {exception}");
            return 2;
        }
        finally
        {
            instanceLock.ReleaseMutex();
        }
    }

    /// <summary>
    /// Prints the menu and exits, without showing anything.
    /// </summary>
    /// <remarks>
    /// A WinExe has no console of its own, so the output goes to a file next to the settings and the
    /// path is written to the log.
    /// </remarks>
    private static int DumpMenu(AppSettings settings, string databasePath)
    {
        using TrayApplication app = new(settings, databasePath, static () => { });

        string outputPath = Path.Combine(AppSettings.DefaultDirectory, "menu-dump.txt");
        Directory.CreateDirectory(AppSettings.DefaultDirectory);

        using StreamWriter output = new(outputPath, append: false);
        MenuDiagnostics.Dump(app.BuildMenuForInspection(), output);

        return 0;
    }

    /// <summary>
    /// The snippet import/export diagnostics, or null when none was asked for.
    /// </summary>
    /// <remarks>
    /// The Phase 5 acceptance path. The editor's own import and export go through the same view
    /// model, so exercising them here tests the real storage route rather than a parallel one, and
    /// does it without needing anyone to click through a dialog.
    /// </remarks>
    private static int? RunSnippetDiagnostic(string[] args, AppSettings settings, string databasePath)
    {
        string? importPath = ValueFor(args, "--import-snippets");
        string? exportPath = ValueFor(args, "--export-snippets");
        bool dump = args.Contains("--dump-snippets", StringComparer.OrdinalIgnoreCase);

        if (importPath is null && exportPath is null && !dump)
        {
            return null;
        }

        using TrayApplication app = new(settings, databasePath, static () => { });
        string reportPath = Path.Combine(AppSettings.DefaultDirectory, "snippets-report.txt");
        Directory.CreateDirectory(AppSettings.DefaultDirectory);

        using StreamWriter report = new(reportPath, append: false);

        try
        {
            if (importPath is not null)
            {
                int imported = app.ImportSnippets(File.ReadAllText(importPath));
                report.WriteLine($"imported {imported} folders from {importPath}");
            }

            if (exportPath is not null)
            {
                File.WriteAllText(exportPath, app.ExportSnippets());
                report.WriteLine($"exported to {exportPath}");
            }

            if (dump)
            {
                app.DumpSnippets(report);
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or InvalidOperationException)
        {
            report.WriteLine($"failed: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The OCR, search and storage diagnostics, or null when none was asked for.
    /// </summary>
    /// <remarks>
    /// A WinExe has no console, so output goes to a file next to the settings.
    /// </remarks>
    private static int? RunOcrDiagnostic(string[] args, AppSettings settings, string databasePath)
    {
        string? ocrImage = ValueFor(args, "--ocr-test");
        string? query = ValueFor(args, "--search");
        bool storage = args.Contains("--storage", StringComparer.OrdinalIgnoreCase);

        if (ocrImage is null && query is null && !storage)
        {
            return null;
        }

        using TrayApplication app = new(settings, databasePath, static () => { });

        string reportPath = Path.Combine(AppSettings.DefaultDirectory, "ocr-report.txt");
        Directory.CreateDirectory(AppSettings.DefaultDirectory);

        using StreamWriter report = new(reportPath, append: false);
        int exitCode = 0;

        try
        {
            if (ocrImage is not null)
            {
                exitCode = app.RunOcrCheckAsync(ocrImage, report).GetAwaiter().GetResult();
            }

            if (query is not null)
            {
                app.Search(query, report);
            }

            if (storage)
            {
                app.ReportStorage(report);
            }
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or InvalidOperationException)
        {
            report.WriteLine($"failed: {exception.Message}");
            exitCode = 1;
        }

        return exitCode;
    }

    private static string? ValueFor(string[] args, string option)
    {
        int index = Array.FindIndex(
            args,
            argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string DatabasePathFrom(string[] args)
    {
        int index = Array.FindIndex(
            args,
            argument => string.Equals(argument, "--db", StringComparison.OrdinalIgnoreCase));

        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }

        return Path.Combine(AppSettings.DefaultDirectory, "clips.db");
    }

    private static string DefaultLogPath =>
        Path.Combine(AppSettings.DefaultDirectory, "copypasta.log");

    /// <summary>
    /// Records unhandled exceptions rather than letting them vanish.
    /// </summary>
    /// <remarks>
    /// A tray-only app has no window to show an error in, so an unhandled exception would
    /// otherwise be an app that simply disappears with no trace. Both handlers are needed: WPF
    /// swallows exceptions on its own dispatcher thread, and everything else surfaces through the
    /// AppDomain. Neither marks the exception handled — the process should still fail, just
    /// legibly.
    /// </remarks>
    private static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log(DefaultLogPath, $"FATAL (domain): {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(DefaultLogPath, $"unobserved task exception: {e.Exception}");

            // Not fatal on its own; the background queues are allowed to fail an item.
            e.SetObserved();
        };
    }

    private static void Log(string path, string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.AppendAllText(path, $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the reason the app stops working.
        }
    }

    private static void RunMessageLoop()
    {
        while (GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private static void PostQuit() => PostQuitMessage(0);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);
}
