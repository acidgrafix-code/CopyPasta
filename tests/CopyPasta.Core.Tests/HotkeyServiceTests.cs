using CopyPasta.Core.Hotkeys;

namespace CopyPasta.Core.Tests;

/// <summary>A registrar that records what was asked of it and can be told to refuse.</summary>
public sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    private readonly Dictionary<int, KeyCombination> _registered = [];

    /// <summary>Combinations to report as already owned by another application.</summary>
    public HashSet<KeyCombination> Conflicting { get; } = [];

    /// <summary>Combinations to fail with some other error.</summary>
    public HashSet<KeyCombination> Failing { get; } = [];

    public List<int> Unregistered { get; } = [];

    public IReadOnlyDictionary<int, KeyCombination> Registered => _registered;

    public event EventHandler<int>? Pressed;

    public HotkeyStatus TryRegister(int id, KeyCombination combination, out string? detail)
    {
        detail = null;

        if (!combination.IsValid)
        {
            return HotkeyStatus.Invalid;
        }

        if (Conflicting.Contains(combination))
        {
            return HotkeyStatus.Conflict;
        }

        if (Failing.Contains(combination))
        {
            detail = "the device is not ready";
            return HotkeyStatus.Failed;
        }

        _registered[id] = combination;
        return HotkeyStatus.Registered;
    }

    public void Unregister(int id)
    {
        Unregistered.Add(id);
        _registered.Remove(id);
    }

    /// <summary>Simulates the user pressing a registered combination.</summary>
    public void Press(KeyCombination combination)
    {
        foreach ((int id, KeyCombination registered) in _registered)
        {
            if (registered == combination)
            {
                Pressed?.Invoke(this, id);
                return;
            }
        }
    }

    public void PressId(int id) => Pressed?.Invoke(this, id);
}

public class HotkeyServiceTests
{
    private static readonly KeyCombination CtrlAltV =
        new(VirtualKeys.V, HotkeyModifiers.Control | HotkeyModifiers.Alt);

    private static readonly KeyCombination CtrlAltH =
        new(VirtualKeys.H, HotkeyModifiers.Control | HotkeyModifiers.Alt);

    private readonly FakeHotkeyRegistrar _registrar = new();

    // ---- Binding ------------------------------------------------------------------------

    [Fact]
    public void A_valid_binding_is_registered()
    {
        using HotkeyService service = new(_registrar);

        HotkeyResult result = service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);

        Assert.Equal(HotkeyStatus.Registered, result.Status);
        Assert.True(result.IsLive);
        Assert.Equal(CtrlAltV, Assert.Single(_registrar.Registered).Value);
    }

    [Fact]
    public void A_null_binding_leaves_the_action_unbound()
    {
        using HotkeyService service = new(_registrar);

        HotkeyResult result = service.Bind(HotkeyService.Actions.ClearHistory, null);

        Assert.Equal(HotkeyStatus.Unbound, result.Status);
        Assert.Empty(_registrar.Registered);
    }

    [Fact]
    public void A_combination_without_a_modifier_is_rejected_before_it_reaches_the_system()
    {
        using HotkeyService service = new(_registrar);

        HotkeyResult result = service.Bind(
            HotkeyService.Actions.ShowMainMenu,
            new KeyCombination(VirtualKeys.V, HotkeyModifiers.None));

        Assert.Equal(HotkeyStatus.Invalid, result.Status);
        Assert.Empty(_registrar.Registered);
    }

    [Fact]
    public void Re_binding_an_action_releases_its_previous_hotkey()
    {
        using HotkeyService service = new(_registrar);
        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);

        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltH);

        Assert.Single(_registrar.Unregistered);
        Assert.Equal(CtrlAltH, Assert.Single(_registrar.Registered).Value);
    }

    // ---- Conflicts ------------------------------------------------------------------------

    [Fact]
    public void A_combination_another_application_owns_is_reported_not_swallowed()
    {
        // The Phase 4 requirement: macOS discards the registration result entirely, so a taken
        // combination silently does nothing and the user has no way to find out why.
        _registrar.Conflicting.Add(CtrlAltV);
        using HotkeyService service = new(_registrar);

        HotkeyResult result = service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);

        Assert.Equal(HotkeyStatus.Conflict, result.Status);
        Assert.False(result.IsLive);
        Assert.Contains("already in use", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_conflicted_action_is_not_treated_as_live()
    {
        _registrar.Conflicting.Add(CtrlAltV);
        using HotkeyService service = new(_registrar);

        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);

        Assert.Empty(service.LiveActions);
    }

    [Fact]
    public void Other_failures_carry_the_system_explanation()
    {
        _registrar.Failing.Add(CtrlAltV);
        using HotkeyService service = new(_registrar);

        HotkeyResult result = service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);

        Assert.Equal(HotkeyStatus.Failed, result.Status);
        Assert.Equal("the device is not ready", result.Detail);
    }

    [Fact]
    public void One_failed_binding_does_not_stop_the_others()
    {
        _registrar.Conflicting.Add(CtrlAltV);
        using HotkeyService service = new(_registrar);

        IReadOnlyList<HotkeyResult> results = service.Apply(new Dictionary<string, KeyCombination?>
        {
            [HotkeyService.Actions.ShowMainMenu] = CtrlAltV,
            [HotkeyService.Actions.ShowHistoryMenu] = CtrlAltH,
        });

        Assert.Equal(2, results.Count);
        Assert.Equal(
            HotkeyService.Actions.ShowHistoryMenu,
            Assert.Single(results, result => result.IsLive).ActionId);
    }

    // ---- Dispatch ---------------------------------------------------------------------------

    [Fact]
    public void Pressing_a_hotkey_raises_its_action()
    {
        using HotkeyService service = new(_registrar);
        service.Bind(HotkeyService.Actions.ShowHistoryMenu, CtrlAltH);

        List<string> fired = [];
        service.Triggered += (_, actionId) => fired.Add(actionId);

        _registrar.Press(CtrlAltH);

        Assert.Equal([HotkeyService.Actions.ShowHistoryMenu], fired);
    }

    [Fact]
    public void Each_hotkey_raises_only_its_own_action()
    {
        using HotkeyService service = new(_registrar);
        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);
        service.Bind(HotkeyService.Actions.ShowHistoryMenu, CtrlAltH);

        List<string> fired = [];
        service.Triggered += (_, actionId) => fired.Add(actionId);

        _registrar.Press(CtrlAltV);
        _registrar.Press(CtrlAltH);

        Assert.Equal(
            [HotkeyService.Actions.ShowMainMenu, HotkeyService.Actions.ShowHistoryMenu],
            fired);
    }

    [Fact]
    public void An_unknown_id_is_ignored()
    {
        using HotkeyService service = new(_registrar);
        service.Triggered += (_, _) => Assert.Fail("nothing should have fired");

        _registrar.PressId(9999);
    }

    [Fact]
    public void An_unbound_action_no_longer_fires()
    {
        using HotkeyService service = new(_registrar);
        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);
        KeyCombination registered = _registrar.Registered.Values.First();

        service.Unbind(HotkeyService.Actions.ShowMainMenu);
        service.Triggered += (_, _) => Assert.Fail("nothing should have fired");

        _registrar.Press(registered);
    }

    [Fact]
    public void A_throwing_handler_is_reported_and_hotkeys_keep_working()
    {
        // A handler pops a menu; letting it unwind into the window procedure would take the
        // process down.
        using HotkeyService service = new(_registrar);
        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);

        List<Exception> failures = [];
        service.Triggered += (_, _) => throw new InvalidOperationException("boom");
        service.HandlerFailed += (_, exception) => failures.Add(exception);

        _registrar.Press(CtrlAltV);
        _registrar.Press(CtrlAltV);

        Assert.Equal(2, failures.Count);
    }

    // ---- Snippet folders ---------------------------------------------------------------------

    [Fact]
    public void Arbitrary_identifiers_work_alongside_the_built_in_actions()
    {
        // Snippet folders are keyed by their own id, exactly as macOS keys them by folder UUID.
        using HotkeyService service = new(_registrar);
        string folderId = Guid.NewGuid().ToString("n");

        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);
        HotkeyResult result = service.Bind(folderId, CtrlAltH);

        Assert.True(result.IsLive);
        Assert.Contains(folderId, service.LiveActions);
        Assert.Equal(2, service.LiveActions.Count);
    }

    // ---- Lifetime -------------------------------------------------------------------------------

    [Fact]
    public void Disposing_releases_every_hotkey()
    {
        HotkeyService service = new(_registrar);
        service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV);
        service.Bind(HotkeyService.Actions.ShowHistoryMenu, CtrlAltH);

        service.Dispose();

        Assert.Empty(_registrar.Registered);
        Assert.Equal(2, _registrar.Unregistered.Count);
    }

    [Fact]
    public void Using_a_disposed_service_is_an_error()
    {
        HotkeyService service = new(_registrar);
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            service.Bind(HotkeyService.Actions.ShowMainMenu, CtrlAltV));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new HotkeyService(null!));

        using HotkeyService service = new(_registrar);
        Assert.Throws<ArgumentNullException>(() => service.Apply(null!));
        Assert.Throws<ArgumentException>(() => service.Bind("  ", CtrlAltV));
    }
}
