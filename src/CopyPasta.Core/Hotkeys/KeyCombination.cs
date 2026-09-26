using System.Diagnostics.CodeAnalysis;

namespace CopyPasta.Core.Hotkeys;

/// <summary>Modifier keys held as part of a hotkey.</summary>
/// <remarks>
/// The values match the Win32 <c>MOD_*</c> constants that <c>RegisterHotKey</c> expects, so no
/// translation is needed at the boundary. macOS carries Carbon modifier masks for the same purpose.
/// </remarks>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

/// <summary>
/// A global hotkey: one key plus at least one modifier. Port of the macOS <c>KeyCombo</c>.
/// </summary>
/// <remarks>
/// At least one modifier is required. Win32 will happily register a bare key globally, which would
/// swallow that key for every application on the machine — a footgun with no legitimate use here.
/// </remarks>
public readonly record struct KeyCombination(uint VirtualKey, HotkeyModifiers Modifiers)
{
    public bool IsValid => VirtualKey != 0 && Modifiers != HotkeyModifiers.None;

    /// <summary>A human-readable form such as <c>Ctrl+Alt+V</c>.</summary>
    public override string ToString()
    {
        if (VirtualKey == 0)
        {
            return string.Empty;
        }

        List<string> parts = [];

        // Fixed order so the same combination always reads the same way, whatever order the user
        // pressed the keys in.
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(VirtualKeys.NameOf(VirtualKey));
        return string.Join("+", parts);
    }

    /// <summary>Parses the form produced by <see cref="ToString"/>.</summary>
    public static bool TryParse(string? text, out KeyCombination combination)
    {
        combination = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;

        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();

            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "alt" or "option":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "win" or "windows" or "cmd" or "command":
                    modifiers |= HotkeyModifiers.Windows;
                    continue;
            }

            // Anything that is not a modifier must be the key, and there can be only one.
            if (virtualKey != 0 || !VirtualKeys.TryParse(part, out virtualKey))
            {
                return false;
            }
        }

        if (virtualKey == 0 || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        combination = new KeyCombination(virtualKey, modifiers);
        return true;
    }
}

/// <summary>Virtual-key codes and their display names.</summary>
public static class VirtualKeys
{
    public const uint A = 0x41;
    public const uint B = 0x42;
    public const uint H = 0x48;
    public const uint V = 0x56;

    private static readonly Dictionary<uint, string> NamesByCode = BuildNames();
    private static readonly Dictionary<string, uint> CodesByName =
        NamesByCode.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The display name for a code, or a <c>0x..</c> fallback for one with no name here.</summary>
    public static string NameOf(uint virtualKey) =>
        NamesByCode.TryGetValue(virtualKey, out string? name)
            ? name
            : $"0x{virtualKey:X2}";

    public static bool TryParse(string name, [NotNullWhen(true)] out uint virtualKey)
    {
        if (CodesByName.TryGetValue(name, out virtualKey))
        {
            return true;
        }

        // Accept the raw hex fallback that NameOf produces.
        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(name.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out virtualKey))
        {
            return virtualKey != 0;
        }

        virtualKey = 0;
        return false;
    }

    private static Dictionary<uint, string> BuildNames()
    {
        Dictionary<uint, string> names = [];

        for (uint code = 0x41; code <= 0x5A; code++)
        {
            names[code] = ((char)code).ToString();
        }

        for (uint code = 0x30; code <= 0x39; code++)
        {
            names[code] = ((char)code).ToString();
        }

        for (uint index = 1; index <= 24; index++)
        {
            names[0x70 + index - 1] = $"F{index}";
        }

        names[0x08] = "Backspace";
        names[0x09] = "Tab";
        names[0x0D] = "Enter";
        names[0x13] = "Pause";
        names[0x1B] = "Escape";
        names[0x20] = "Space";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "Left";
        names[0x26] = "Up";
        names[0x27] = "Right";
        names[0x28] = "Down";
        names[0x2D] = "Insert";
        names[0x2E] = "Delete";
        names[0xBA] = "Semicolon";
        names[0xBB] = "Plus";
        names[0xBC] = "Comma";
        names[0xBD] = "Minus";
        names[0xBE] = "Period";
        names[0xBF] = "Slash";
        names[0xC0] = "Backtick";
        names[0xDB] = "LeftBracket";
        names[0xDC] = "Backslash";
        names[0xDD] = "RightBracket";
        names[0xDE] = "Quote";

        return names;
    }
}
