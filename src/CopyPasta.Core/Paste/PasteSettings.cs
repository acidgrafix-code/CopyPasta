namespace CopyPasta.Core.Paste;

/// <summary>An optional modifier-triggered action.</summary>
/// <param name="IsEnabled">Whether the action is available at all.</param>
/// <param name="Modifier">The modifier that triggers it.</param>
public sealed record ModifierAction(bool IsEnabled, ModifierKey Modifier)
{
    public static ModifierAction Disabled(ModifierKey modifier) => new(false, modifier);

    public static ModifierAction Enabled(ModifierKey modifier) => new(true, modifier);
}

/// <summary>
/// Settings for the paste path. Port of the <c>@Shared</c> values read by the macOS
/// <c>PasteService</c>.
/// </summary>
public sealed record PasteSettings
{
    public static PasteSettings Default { get; } = new();

    /// <summary>
    /// Synthesise the paste shortcut after copying. When false the clip is only placed on the
    /// clipboard and the user pastes it themselves. Port of <c>pastesAutomatically</c>.
    /// </summary>
    public bool PastesAutomatically { get; init; } = true;

    /// <summary>
    /// Hold to paste the clip's text without formatting. Port of
    /// <c>pastesPlainTextWithModifier</c> / <c>plainTextPasteModifier</c>.
    /// </summary>
    /// <remarks>
    /// macOS defaults this modifier to Command. Shift is the Windows default here because
    /// Ctrl+Shift+V is already the platform's own idiom for "paste without formatting", so Shift
    /// is what a Windows user will reach for.
    /// </remarks>
    public ModifierAction PastePlainText { get; init; } = ModifierAction.Enabled(ModifierKey.Shift);

    /// <summary>
    /// Hold to delete the clip instead of pasting it. Port of
    /// <c>deletesHistoryWithModifier</c> / <c>historyDeletionModifier</c>.
    /// </summary>
    public ModifierAction DeleteWithoutPasting { get; init; } =
        ModifierAction.Disabled(ModifierKey.Control);

    /// <summary>
    /// Hold to paste the clip and then remove it from the history. Port of
    /// <c>pastesAndDeletesHistoryWithModifier</c> / <c>pasteAndDeleteHistoryModifier</c>.
    /// </summary>
    public ModifierAction PasteAndDelete { get; init; } = ModifierAction.Disabled(ModifierKey.Alt);
}
