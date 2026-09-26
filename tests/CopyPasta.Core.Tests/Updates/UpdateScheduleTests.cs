using CopyPasta.Core.Updates;

namespace CopyPasta.Core.Tests.Updates;

/// <summary>
/// When an automatic update check is due. The part of updating that does not need a network.
/// </summary>
public class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static UpdatePolicy Daily => new(Automatic: true, TimeSpan.FromDays(1));

    [Fact]
    public void A_copy_that_has_never_checked_is_due()
    {
        Assert.True(UpdateSchedule.IsDue(Daily, lastCheck: null, Now));
    }

    [Fact]
    public void Nothing_is_due_when_automatic_checking_is_off()
    {
        // Including the never-checked case, which is the one most likely to slip through.
        UpdatePolicy manual = Daily with { Automatic = false };

        Assert.False(UpdateSchedule.IsDue(manual, lastCheck: null, Now));
        Assert.False(UpdateSchedule.IsDue(manual, Now - TimeSpan.FromDays(30), Now));
    }

    [Fact]
    public void A_check_is_not_due_again_until_the_interval_has_passed()
    {
        Assert.False(UpdateSchedule.IsDue(Daily, Now - TimeSpan.FromHours(23), Now));
    }

    [Fact]
    public void A_check_is_due_once_the_interval_has_passed()
    {
        Assert.True(UpdateSchedule.IsDue(Daily, Now - TimeSpan.FromHours(25), Now));
    }

    [Fact]
    public void The_interval_boundary_itself_counts_as_due()
    {
        // Otherwise a poll landing exactly on the boundary defers a whole further interval.
        Assert.True(UpdateSchedule.IsDue(Daily, Now - TimeSpan.FromDays(1), Now));
    }

    [Fact]
    public void A_timestamp_in_the_future_is_treated_as_due()
    {
        // The clock moved back, or settings.json was edited. Waiting for the future to arrive
        // would suppress checks until then, which for a hand-typed year could be forever.
        Assert.True(UpdateSchedule.IsDue(Daily, Now + TimeSpan.FromDays(365), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(60)]
    public void An_interval_below_the_floor_is_raised_to_it(int seconds)
    {
        // A zero or negative interval would check on every poll, which is how you get
        // rate-limited rather than how you stay current.
        UpdatePolicy policy = new(Automatic: true, TimeSpan.FromSeconds(seconds));

        Assert.Equal(UpdateSchedule.MinimumInterval, UpdateSchedule.EffectiveInterval(policy));
        Assert.False(UpdateSchedule.IsDue(policy, Now - TimeSpan.FromMinutes(30), Now));
        Assert.True(UpdateSchedule.IsDue(policy, Now - TimeSpan.FromMinutes(61), Now));
    }

    [Fact]
    public void An_interval_above_the_floor_is_left_alone()
    {
        Assert.Equal(TimeSpan.FromDays(1), UpdateSchedule.EffectiveInterval(Daily));
    }

    [Fact]
    public void The_poll_runs_more_often_than_the_shortest_interval()
    {
        // The point of polling: a machine asleep when a check came due should check soon after
        // waking, not a whole interval later.
        Assert.True(UpdateSchedule.PollInterval < UpdateSchedule.MinimumInterval);
    }

    [Fact]
    public void The_first_check_happens_soon_after_starting_rather_than_a_poll_later()
    {
        // Otherwise someone who uses the app in short bursts never reaches a first tick, and so
        // never checks at all however long the app has been installed.
        Assert.True(UpdateSchedule.StartupDelay < UpdateSchedule.PollInterval);
        Assert.True(UpdateSchedule.StartupDelay > TimeSpan.Zero);
    }

    [Fact]
    public void The_default_policy_matches_the_macOS_one()
    {
        Assert.True(UpdatePolicy.Default.Automatic);
        Assert.Equal(TimeSpan.FromDays(1), UpdatePolicy.Default.Interval);
    }
}
