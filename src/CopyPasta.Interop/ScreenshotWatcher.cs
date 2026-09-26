namespace CopyPasta.Interop;

/// <summary>
/// Watches the screenshots folder and reports new images.
/// </summary>
/// <remarks>
/// <para>
/// Port of the macOS <c>ScreenShotObserver</c>, which uses an <c>NSMetadataQuery</c> over Spotlight.
/// Windows has no equivalent index to query, but it does have a conventional destination:
/// <c>%USERPROFILE%\Pictures\Screenshots</c>, where Win+PrtScn and the Snipping Tool both save. A
/// <c>FileSystemWatcher</c> on that folder is the direct analogue.
/// </para>
/// <para>
/// The awkward part is that a file appears before it is finished being written, so reading it on
/// the notification often yields a truncated or locked file. Each candidate is therefore retried
/// until it can be opened for exclusive reading, which is what "the writer has let go" means.
/// </para>
/// </remarks>
public sealed class ScreenshotWatcher : IDisposable
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private readonly string _directory;
    private readonly TimeSpan _settleTimeout;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public ScreenshotWatcher(string? directory = null, TimeSpan? settleTimeout = null)
    {
        _directory = directory ?? DefaultDirectory;
        _settleTimeout = settleTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Where Windows puts screenshots taken with Win+PrtScn.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Screenshots");

    /// <summary>Raised on a thread-pool thread with the path of a newly saved screenshot.</summary>
    public event EventHandler<string>? ScreenshotCaptured;

    /// <summary>Raised when a handler throws. Watching continues.</summary>
    public event EventHandler<Exception>? HandlerFailed;

    public bool IsWatching => _watcher is not null;

    /// <summary>The folder being watched, for diagnostics.</summary>
    public string Directory => _directory;

    /// <summary>
    /// Starts watching. Returns false when the folder does not exist — which is normal on a machine
    /// where no screenshot has ever been saved, and not worth an error.
    /// </summary>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_watcher is not null)
        {
            return true;
        }

        if (!System.IO.Directory.Exists(_directory))
        {
            return false;
        }

        _watcher = new FileSystemWatcher(_directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };

        _watcher.Created += OnFileEvent;

        // Some tools write to a temporary name and rename into place, which arrives as a rename
        // rather than a creation.
        _watcher.Renamed += OnFileEvent;

        _watcher.EnableRaisingEvents = true;
        return true;
    }

    public void Stop()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileEvent;
        _watcher.Renamed -= OnFileEvent;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!ImageExtensions.Contains(Path.GetExtension(e.FullPath), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_inFlight)
        {
            // A single save can raise several events; only the first starts a wait.
            if (!_inFlight.Add(e.FullPath))
            {
                return;
            }
        }

        _ = Task.Run(() => WaitAndReportAsync(e.FullPath));
    }

    private async Task WaitAndReportAsync(string path)
    {
        try
        {
            if (await WaitUntilReadableAsync(path).ConfigureAwait(false))
            {
                ScreenshotCaptured?.Invoke(this, path);
            }
        }
        catch (Exception exception)
        {
            HandlerFailed?.Invoke(this, exception);
        }
        finally
        {
            lock (_inFlight)
            {
                _inFlight.Remove(path);
            }
        }
    }

    /// <summary>
    /// Waits until the file can be opened exclusively, which is how "the writer has finished" is
    /// detectable without guessing at a delay.
    /// </summary>
    private async Task<bool> WaitUntilReadableAsync(string path)
    {
        DateTime deadline = DateTime.UtcNow + _settleTimeout;
        int delay = 50;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);

                // A zero-length file is a placeholder the writer has not filled in yet.
                if (stream.Length > 0)
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                // Created and removed again; nothing to capture.
                return false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still being written.
            }

            await Task.Delay(delay).ConfigureAwait(false);
            delay = Math.Min(delay * 2, 500);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
