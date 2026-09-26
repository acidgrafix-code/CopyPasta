using System.Globalization;
using System.Runtime.InteropServices;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Paste;
using CopyPasta.Data;
using CopyPasta.Interop;

namespace CaptureProbe;

/// <summary>
/// Runs the real Phase 1 capture pipeline and narrates it: event-driven monitoring, Win32
/// clipboard reads, filtering, and SQLite storage.
/// </summary>
/// <remarks>
/// This is the acceptance harness for Phase 1 — copy things in various applications and watch
/// what gets stored and what gets rejected. It is not the app; there is no tray icon, no menu
/// and no paste path yet.
///
///   dotnet run --project tools/CaptureProbe
///   dotnet run --project tools/CaptureProbe -- --db clips.db --exclude KeePass --text-only
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }

        using ClipDatabase database = new(options.DatabasePath);
        database.Migrate();

        SqliteClipStore store = new(database);

        if (options.Dump)
        {
            Dump(store);
            return 0;
        }

        ClipboardFormatRegistry formats = new();

        if (options.RoundTrip)
        {
            return RoundTrip(store, formats) ? 0 : 1;
        }

        if (options.PasteTest)
        {
            return PasteTest.Run(formats, options.HoldShift) ? 0 : 1;
        }

        using ClipboardMonitor monitor = new();
        Win32ClipboardSource clipboard = new(formats);

        ClipCaptureService capture = new(clipboard, store, new ForegroundApplication())
        {
            Settings = options.ToSettings(),
        };

        Console.WriteLine($"Database  : {Path.GetFullPath(options.DatabasePath)}");
        Console.WriteLine($"Existing  : {store.Count()} clips, {store.TotalAssetBytes():N0} bytes");
        Console.WriteLine($"Categories: {string.Join(", ", options.ToSettings().Filter.EnabledTypes)}");
        if (options.Excluded.Count > 0)
        {
            Console.WriteLine($"Excluding : {string.Join(", ", options.Excluded)}");
        }

        Console.WriteLine();
        Console.WriteLine("Watching the clipboard. Copy something. Ctrl+C to stop.");
        Console.WriteLine();

        monitor.ClipboardChanged += (_, _) => Report(capture, store, options);
        monitor.UnhandledMonitorException += (_, exception) =>
            Console.Error.WriteLine($"  !! handler failed: {exception.Message}");
        monitor.Start();

        // Capture whatever is already on the clipboard, so the probe is useful immediately.
        Report(capture, store, options);

        RunMessageLoop();
        return 0;
    }

    private static void Report(
        ClipCaptureService capture,
        SqliteClipStore store,
        Options options)
    {
        CaptureResult result = capture.Capture();
        string timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        if (!result.WasCaptured)
        {
            // Unchanged is the overwhelmingly common case (our own reads, duplicate
            // notifications), so it is only worth printing when explicitly asked for.
            if (result.Outcome != CaptureOutcome.Unchanged || options.Verbose)
            {
                Console.WriteLine($"[{timestamp}] {result.Outcome}");
            }

            return;
        }

        ClipSummary? summary = store
            .FetchRecent(ClipOrder.UpdatedAt, 1)
            .FirstOrDefault(clip => clip.Id == result.ClipId);

        string formats = summary is null
            ? "?"
            : string.Join(", ", summary.Formats.Select(format => format.Name));
        string title = Preview(summary?.Title);

        Console.WriteLine($"[{timestamp}] Captured  {result.ClipId?[..12]}  [{formats}]");
        if (title.Length > 0)
        {
            Console.WriteLine($"             {title}");
        }

        if (summary is { IsConcealed: true })
        {
            Console.WriteLine("             (marked sensitive — will be re-marked on paste)");
        }

        Console.WriteLine(
            $"             {store.Count()} clips, {store.TotalAssetBytes():N0} bytes");

        if (options.MaximumClips > 0)
        {
            int trimmed = store.DeleteOverflowing(ClipOrder.UpdatedAt, options.MaximumClips);
            if (trimmed > 0)
            {
                Console.WriteLine($"             trimmed {trimmed} over the {options.MaximumClips} limit");
            }
        }
    }

    /// <summary>
    /// Writes every stored clip back to the clipboard, reads it straight back, and compares
    /// content hashes.
    /// </summary>
    /// <remarks>
    /// This is the Phase 2 acceptance check, and it is stronger than eyeballing a paste: the hash
    /// covers every format's bytes, so a match proves the replay is byte-identical rather than
    /// merely looking right. It also exercises the interaction that would otherwise be easy to
    /// get wrong — Windows synthesises extra flavours from what we write (CF_TEXT from
    /// CF_UNICODETEXT, CF_DIB from CF_DIBV5), and the filter has to discard exactly those again
    /// for the round trip to close.
    /// </remarks>
    private static bool RoundTrip(SqliteClipStore store, ClipboardFormatRegistry formats)
    {
        Win32ClipboardWriter writer = new(formats);
        Win32ClipboardSource reader = new(formats);

        IReadOnlyList<ClipSummary> clips = store.FetchRecent(ClipOrder.UpdatedAt, 500);
        Console.WriteLine($"Round-tripping {clips.Count} clips through the real clipboard.");
        Console.WriteLine();

        int passed = 0;
        int failed = 0;

        foreach (ClipSummary summary in clips)
        {
            ClipContent? original = store.FetchContent(summary.Id);
            if (original is null)
            {
                continue;
            }

            string label = $"{summary.Id[..12]}  [{string.Join(", ", original.Formats.Select(f => f.Name))}]";

            if (!writer.TryWrite(new ClipboardWriteRequest(original.Assets, summary.IsConcealed)))
            {
                Console.WriteLine($"FAIL  {label}  — clipboard unavailable for writing");
                failed++;
                continue;
            }

            ClipboardSelection selection = ClipboardSelection.None;
            ClipboardRead? read = reader.TryRead(snapshot =>
            {
                selection = ClipboardFormatFilter.Select(snapshot, ClipboardFilterOptions.Default);
                return selection.Formats;
            });

            if (read is null)
            {
                Console.WriteLine($"FAIL  {label}  — clipboard unavailable for reading");
                failed++;
                continue;
            }

            if (!ClipContent.TryCreate(selection, read.Assets, out ClipContent? replayed))
            {
                Console.WriteLine($"FAIL  {label}  — nothing came back");
                failed++;
                continue;
            }

            if (replayed.Hash == original.Hash)
            {
                Console.WriteLine($"ok    {label}");
                passed++;
                continue;
            }

            Console.WriteLine($"FAIL  {label}");
            Console.WriteLine($"        wrote: {Describe(original)}");
            Console.WriteLine($"        read : {Describe(replayed)}");
            failed++;
        }

        Console.WriteLine();
        Console.WriteLine($"{passed} identical, {failed} different.");
        return failed == 0;
    }

    private static string Describe(ClipContent content) =>
        string.Join(
            ", ",
            content.Assets.Select(asset => $"{asset.Format.Name}={asset.Data.Length}B"));

    /// <summary>Prints the stored history, newest first, and exits.</summary>
    private static void Dump(SqliteClipStore store)
    {
        IReadOnlyList<ClipSummary> clips = store.FetchRecent(ClipOrder.UpdatedAt, 500);
        Console.WriteLine($"{clips.Count} clips, {store.TotalAssetBytes():N0} bytes");
        Console.WriteLine();

        foreach (ClipSummary clip in clips)
        {
            ClipContent? content = store.FetchContent(clip.Id);
            long bytes = content?.Assets.Sum(asset => (long)asset.Data.Length) ?? 0;

            Console.WriteLine(
                $"{clip.Id[..12]}  created {clip.CreatedAt}  used {clip.UpdatedAt}  " +
                $"{bytes,9:N0} B  [{string.Join(", ", clip.Formats.Select(format => format.Name))}]");

            string title = Preview(clip.Title);
            if (title.Length > 0)
            {
                Console.WriteLine($"              {title}");
            }

            if (clip.IsConcealed || clip.IsFromCloudClipboard)
            {
                List<string> flags = [];
                if (clip.IsConcealed)
                {
                    flags.Add("sensitive");
                }

                if (clip.IsFromCloudClipboard)
                {
                    flags.Add("from another device");
                }

                Console.WriteLine($"              ({string.Join(", ", flags)})");
            }
        }
    }

    private static string Preview(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        string single = title.ReplaceLineEndings(" ").Trim();
        return single.Length <= 72 ? single : single[..72] + "…";
    }

    private static void RunMessageLoop()
    {
        // WM_CLIPBOARDUPDATE is delivered to the thread that created the monitor window, so
        // that thread has to pump messages. The real app gets this for free from its UI
        // framework; here it is explicit.
        while (GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private sealed record Options
    {
        public const string Usage = """
            Usage: CaptureProbe [options]

              --db <path>        SQLite file to write (default: captureprobe.db)
              --exclude <name>   Exclude an application; repeatable
              --text-only        Capture only text
              --no-images        Capture everything except images
              --ignore-secrets   Skip clips marked as sensitive
              --max <n>          Trim to n clips after each capture
              --verbose          Also print Unchanged notifications
              --dump             Print the stored history and exit
              --roundtrip        Replay every stored clip through the clipboard and verify
                                 it comes back byte-identical, then exit
              --paste-test       Drive the full paste path into a real edit control, then exit
              --hold-shift       With --paste-test: hold Shift during the paste
              --help
            """;

        public string DatabasePath { get; private init; } = "captureprobe.db";

        public List<string> Excluded { get; } = [];

        public bool TextOnly { get; private init; }

        public bool NoImages { get; private init; }

        public bool IgnoreSecrets { get; private init; }

        public int MaximumClips { get; private init; }

        public bool Verbose { get; private init; }

        public bool ShowHelp { get; private init; }

        public bool Dump { get; private init; }

        public bool RoundTrip { get; private init; }

        public bool PasteTest { get; private init; }

        public bool HoldShift { get; private init; }

        public static Options Parse(string[] args)
        {
            Options options = new();

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index].ToLowerInvariant())
                {
                    case "--db":
                        options = options with { DatabasePath = Next(args, ref index, "--db") };
                        break;
                    case "--exclude":
                        options.Excluded.Add(Next(args, ref index, "--exclude"));
                        break;
                    case "--text-only":
                        options = options with { TextOnly = true };
                        break;
                    case "--no-images":
                        options = options with { NoImages = true };
                        break;
                    case "--ignore-secrets":
                        options = options with { IgnoreSecrets = true };
                        break;
                    case "--max":
                        options = options with
                        {
                            MaximumClips = int.Parse(
                                Next(args, ref index, "--max"),
                                CultureInfo.InvariantCulture),
                        };
                        break;
                    case "--verbose":
                        options = options with { Verbose = true };
                        break;
                    case "--dump":
                        options = options with { Dump = true };
                        break;
                    case "--roundtrip":
                        options = options with { RoundTrip = true };
                        break;
                    case "--paste-test":
                        options = options with { PasteTest = true };
                        break;
                    case "--hold-shift":
                        options = options with { HoldShift = true };
                        break;
                    case "--help":
                    case "-h":
                        options = options with { ShowHelp = true };
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {args[index]}");
                }
            }

            return options;
        }

        public CaptureSettings ToSettings()
        {
            IReadOnlySet<ClipContentType> enabled = TextOnly
                ? new HashSet<ClipContentType> { ClipContentType.Text }
                : NoImages
                    ? ClipContentTypes.All.Where(type => type != ClipContentType.Image).ToHashSet()
                    : ClipContentTypes.All.ToHashSet();

            return CaptureSettings.Default with
            {
                Filter = new ClipboardFilterOptions
                {
                    EnabledTypes = enabled,
                    IgnoresConcealedContent = IgnoreSecrets,
                },
                ExcludedApplications = Excluded.Select(name => new ExcludedApplication(name)).ToArray(),
            };
        }

        private static string Next(string[] args, ref int index, string option)
        {
            index++;
            if (index >= args.Length)
            {
                throw new ArgumentException($"{option} needs a value.");
            }

            return args[index];
        }
    }

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
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);
}
