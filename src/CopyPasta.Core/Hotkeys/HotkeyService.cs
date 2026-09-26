namespace CopyPasta.Core.Hotkeys;

/// <summary>Why a hotkey did or did not end up registered.</summary>
public enum HotkeyStatus
{
    /// <summary>Registered and live.</summary>
    Registered,

    /// <summary>
    /// Another application already owns this combination system-wide, so it was not registered.
    /// </summary>
    Conflict,

    /// <summary>The combination is missing a key or a modifier.</summary>
    Invalid,

    /// <summary>No combination is bound to this action.</summary>
    Unbound,

    /// <summary>Registration failed for some other reason.</summary>
    Failed,
}

/// <param name="ActionId">Which action the binding is for.</param>
/// <param name="Combination">What was attempted, if anything.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Detail">A human-readable explanation, for conflicts and failures.</param>
public sealed record HotkeyResult(
    string ActionId,
    KeyCombination? Combination,
    HotkeyStatus Status,
    string? Detail = null)
{
    public bool IsLive => Status == HotkeyStatus.Registered;

    /// <summary>A line suitable for a log or a settings pane.</summary>
    public override string ToString() => Status switch
    {
        HotkeyStatus.Registered => $"{ActionId}: {Combination} registered",
        HotkeyStatus.Conflict =>
            $"{ActionId}: {Combination} is already in use by another application" +
            (Detail is null ? string.Empty : $" ({Detail})"),
        HotkeyStatus.Invalid => $"{ActionId}: {Combination} needs a key and at least one modifier",
        HotkeyStatus.Unbound => $"{ActionId}: not bound",
        _ => $"{ActionId}: {Combination} could not be registered" +
             (Detail is null ? string.Empty : $" ({Detail})"),
    };
}

/// <summary>Registers system-wide hotkeys. Implemented over Win32 in <c>CopyPasta.Interop</c>.</summary>
public interface IHotkeyRegistrar
{
    /// <summary>Attempts to register one combination under a numeric id.</summary>
    HotkeyStatus TryRegister(int id, KeyCombination combination, out string? detail);

    /// <summary>Releases a previously registered id. Safe to call for an unregistered id.</summary>
    void Unregister(int id);

    /// <summary>Raised when a registered hotkey is pressed, with its numeric id.</summary>
    event EventHandler<int>? Pressed;
}

/// <summary>
/// Owns the app's global hotkeys. Port of the macOS <c>HotKeyService</c>.
/// </summary>
/// <remarks>
/// <para>
/// Action ids are strings so that per-folder snippet hotkeys — which macOS keys by folder UUID —
/// slot in alongside the built-in actions without a second mechanism. The numeric ids Win32 needs
/// are an implementation detail assigned here.
/// </para>
/// <para>
/// A conflict is reported rather than swallowed. macOS's <c>HotKey.register()</c> returns a
/// discardable result and the app ignores it, so a combination another program already owns simply
/// does nothing, with no way for the user to find out why.
/// </para>
/// </remarks>
public sealed class HotkeyService : IDisposable
{
    /// <summary>Built-in action ids. Snippet folders use their own identifier.</summary>
    public static class Actions
    {
        public const string ShowMainMenu = "ShowMainMenu";
        public const string ShowHistoryMenu = "ShowHistoryMenu";
        public const string ShowSnippetMenu = "ShowSnippetMenu";
        public const string EditSnippets = "EditSnippets";
        public const string ClearHistory = "ClearHistory";
    }

    private readonly IHotkeyRegistrar _registrar;
    private readonly Dictionary<string, int> _idsByAction = [];
    private readonly Dictionary<int, string> _actionsById = [];
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService(IHotkeyRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);

        _registrar = registrar;
        _registrar.Pressed += OnPressed;
    }

    /// <summary>Raised on the registrar's thread when a hotkey fires, with the action id.</summary>
    public event EventHandler<string>? Triggered;

    /// <summary>Raised when a <see cref="Triggered"/> handler throws. Hotkeys keep working.</summary>
    public event EventHandler<Exception>? HandlerFailed;

    /// <summary>Action ids currently registered.</summary>
    public IReadOnlyCollection<string> LiveActions => _idsByAction.Keys;

    /// <summary>
    /// Makes the given bindings live, replacing any previous ones.
    /// </summary>
    /// <param name="bindings">
    /// Action id to combination. A null combination unbinds the action.
    /// </param>
    /// <returns>One result per requested action, including the ones that failed.</returns>
    public IReadOnlyList<HotkeyResult> Apply(IReadOnlyDictionary<string, KeyCombination?> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<HotkeyResult> results = [];

        foreach ((string actionId, KeyCombination? combination) in bindings)
        {
            results.Add(Bind(actionId, combination));
        }

        return results;
    }

    /// <summary>Binds a single action, replacing whatever it had.</summary>
    public HotkeyResult Bind(string actionId, KeyCombination? combination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Unbind(actionId);

        if (combination is not { } combo)
        {
            return new HotkeyResult(actionId, null, HotkeyStatus.Unbound);
        }

        if (!combo.IsValid)
        {
            return new HotkeyResult(actionId, combo, HotkeyStatus.Invalid);
        }

        int id = _nextId++;
        HotkeyStatus status = _registrar.TryRegister(id, combo, out string? detail);

        if (status != HotkeyStatus.Registered)
        {
            return new HotkeyResult(actionId, combo, status, detail);
        }

        _idsByAction[actionId] = id;
        _actionsById[id] = actionId;
        return new HotkeyResult(actionId, combo, HotkeyStatus.Registered);
    }

    /// <summary>Releases an action's hotkey. Safe to call for an unbound action.</summary>
    public void Unbind(string actionId)
    {
        if (!_idsByAction.Remove(actionId, out int id))
        {
            return;
        }

        _actionsById.Remove(id);
        _registrar.Unregister(id);
    }

    /// <summary>Releases every hotkey.</summary>
    public void UnbindAll()
    {
        foreach (int id in _idsByAction.Values)
        {
            _registrar.Unregister(id);
        }

        _idsByAction.Clear();
        _actionsById.Clear();
    }

    private void OnPressed(object? sender, int id)
    {
        if (!_actionsById.TryGetValue(id, out string? actionId))
        {
            return;
        }

        // A handler pops a menu, which blocks on the message loop; never let it unwind into the
        // window procedure that delivered the message.
        try
        {
            Triggered?.Invoke(this, actionId);
        }
        catch (Exception exception)
        {
            HandlerFailed?.Invoke(this, exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _registrar.Pressed -= OnPressed;
        UnbindAll();
    }
}
