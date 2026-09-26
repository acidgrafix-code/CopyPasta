namespace CopyPasta.Core.Updates;

/// <summary>How often the app should look for a new release.</summary>
/// <param name="Automatic">Whether to check without being asked. A manual check ignores this.</param>
/// <param name="Interval">Time between automatic checks.</param>
public sealed record UpdatePolicy(bool Automatic, TimeSpan Interval)
{
    /// <summary>Daily, matching the macOS default.</summary>
    public static UpdatePolicy Default { get; } = new(Automatic: true, TimeSpan.FromDays(1));
}

/// <summary>
/// Decides when an automatic update check is due. Port of the scheduling Sparkle does for the
/// macOS app.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the code that actually talks to the release feed, because this is the part with
/// the interesting cases — a clock that moved, a settings file that was hand-edited, a first run
/// with nothing recorded — and none of them should need a network to test.
/// </para>
/// <para>
/// Sparkle stores its schedule in its own defaults; here it lives in <c>settings.json</c> with
/// everything else, which means a user can edit it into a nonsensical state. Every value is
/// therefore clamped rather than trusted.
/// </para>
/// </remarks>
public static class UpdateSchedule
{
    /// <summary>
    /// The shortest interval honoured, however the setting is written.
    /// </summary>
    /// <remarks>
    /// A zero or negative interval would mean checking on every timer tick, which is a way to get
    /// rate-limited by the release host rather than a way to stay current.
    /// </remarks>
    public static TimeSpan MinimumInterval { get; } = TimeSpan.FromHours(1);

    /// <summary>How often to wake up and ask whether a check is due.</summary>
    /// <remarks>
    /// Deliberately much shorter than the interval: a machine that was asleep at the moment a
    /// check came due should check shortly after waking, not wait a whole further interval.
    /// </remarks>
    public static TimeSpan PollInterval { get; } = TimeSpan.FromMinutes(15);

    /// <summary>The interval a policy actually uses, after clamping.</summary>
    public static TimeSpan EffectiveInterval(UpdatePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return policy.Interval < MinimumInterval ? MinimumInterval : policy.Interval;
    }

    /// <summary>Whether an automatic check should run now.</summary>
    /// <param name="lastCheck">When a check last completed, or null if none ever has.</param>
    public static bool IsDue(UpdatePolicy policy, DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.Automatic)
        {
            return false;
        }

        if (lastCheck is null)
        {
            return true;
        }

        // A timestamp in the future means the clock moved backwards, or the file was edited.
        // Waiting for the future to arrive could suppress checks indefinitely, so treat it as due
        // and let the successful check rewrite the stamp.
        if (lastCheck > now)
        {
            return true;
        }

        return now - lastCheck >= EffectiveInterval(policy);
    }
}
