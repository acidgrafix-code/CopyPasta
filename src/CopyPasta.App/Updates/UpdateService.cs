using CopyPasta.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace CopyPasta.App.Updates;

/// <summary>What a check found.</summary>
public enum UpdateOutcome
{
    /// <summary>Running from a plain build rather than an install, so there is nothing to update.</summary>
    NotInstalled,

    /// <summary>The feed was reachable and this is the newest release.</summary>
    UpToDate,

    /// <summary>A newer release was found and downloaded; it applies on restart.</summary>
    Ready,

    /// <summary>The feed could not be read. Says why in <see cref="UpdateCheck.Message"/>.</summary>
    Failed,
}

/// <param name="Version">The version found, when there was one.</param>
/// <param name="Message">Detail for the log and the settings pane.</param>
public sealed record UpdateCheck(UpdateOutcome Outcome, string? Version = null, string? Message = null);

/// <summary>
/// Checks the GitHub releases feed and stages an update. Port of the Sparkle integration the macOS
/// app uses.
/// </summary>
/// <remarks>
/// <para>
/// The scheduling lives in <see cref="UpdateSchedule"/>; this is only the transport. The split is
/// what makes the interesting behaviour testable, since none of it needs a network.
/// </para>
/// <para>
/// Downloading and applying are separated on purpose. An update is staged in the background and
/// then applied on the next deliberate restart, because this is a tray app the user leaves running
/// — restarting it underneath them would drop the hotkey registrations and the clipboard listener
/// with no warning.
/// </para>
/// <para>
/// Every failure is reported rather than thrown. A release host that is down, rate-limiting, or
/// unreachable behind a captive portal is an ordinary Tuesday, not a reason for a clipboard
/// manager to show an error.
/// </para>
/// </remarks>
public sealed class UpdateService
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _staged;

    /// <param name="repositoryUrl">The GitHub repository holding the releases.</param>
    /// <param name="allowPrerelease">Whether to offer releases marked as pre-release.</param>
    public UpdateService(string repositoryUrl, bool allowPrerelease = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);

        try
        {
            _manager = new UpdateManager(new GithubSource(repositoryUrl, null, allowPrerelease));
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            // A malformed URL is a packaging mistake, not something to crash a running app over.
            // Leaving the manager null makes every call report NotInstalled.
            _manager = null;
        }
    }

    /// <summary>Whether this copy was installed, and so can update itself.</summary>
    /// <remarks>
    /// False for a plain <c>dotnet run</c> build and for the portable zip, both of which have no
    /// Velopack metadata to update against.
    /// </remarks>
    public bool IsInstalled => _manager?.IsInstalled ?? false;

    /// <summary>The installed version, or null when running uninstalled.</summary>
    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    /// <summary>A staged update waiting for a restart, if there is one.</summary>
    public string? PendingVersion => _staged?.TargetFullRelease.Version.ToString();

    /// <summary>Looks for a newer release and downloads it if there is one.</summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || !_manager.IsInstalled)
        {
            return new UpdateCheck(UpdateOutcome.NotInstalled, CurrentVersion);
        }

        try
        {
            UpdateInfo? update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return new UpdateCheck(UpdateOutcome.UpToDate, CurrentVersion);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _manager.DownloadUpdatesAsync(update, progress: null, cancelToken: cancellationToken)
                .ConfigureAwait(false);

            _staged = update;
            string version = update.TargetFullRelease.Version.ToString();

            return new UpdateCheck(UpdateOutcome.Ready, version, $"{version} is ready; restart to apply");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Velopack surfaces network, HTTP-status and feed-parsing problems as a range of
            // exception types, and a new one appearing should not take the app down.
            return new UpdateCheck(UpdateOutcome.Failed, CurrentVersion, exception.Message);
        }
    }

    /// <summary>
    /// Applies a staged update and restarts. Does not return when it succeeds.
    /// </summary>
    /// <remarks>
    /// The caller must have shut down first: this exits the process immediately, so anything not
    /// already flushed is lost.
    /// </remarks>
    public bool ApplyAndRestart()
    {
        if (_manager is null || _staged is null)
        {
            return false;
        }

        try
        {
            _manager.ApplyUpdatesAndRestart(_staged.TargetFullRelease);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
