using CopyPasta.Core.Hotkeys;

namespace CopyPasta.Core.Tests;

public class KeyCombinationTests
{
    // ---- Validity ----------------------------------------------------------------------

    [Fact]
    public void A_key_with_a_modifier_is_valid()
    {
        Assert.True(new KeyCombination(VirtualKeys.V, HotkeyModifiers.Control).IsValid);
    }

    [Fact]
    public void A_bare_key_is_rejected()
    {
        // Win32 would register it happily, and it would then swallow that key for every
        // application on the machine.
        Assert.False(new KeyCombination(VirtualKeys.V, HotkeyModifiers.None).IsValid);
    }

    [Fact]
    public void A_modifier_with_no_key_is_rejected()
    {
        Assert.False(new KeyCombination(0, HotkeyModifiers.Control).IsValid);
        Assert.False(default(KeyCombination).IsValid);
    }

    // ---- Display -----------------------------------------------------------------------

    [Fact]
    public void Modifiers_are_shown_in_a_fixed_order()
    {
        // The order the user happened to press them in must not change how it reads.
        KeyCombination combination = new(
            VirtualKeys.V,
            HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Alt);

        Assert.Equal("Ctrl+Alt+Shift+V", combination.ToString());
    }

    [Theory]
    [InlineData(0x56, HotkeyModifiers.Control, "Ctrl+V")]
    [InlineData(0x48, HotkeyModifiers.Alt, "Alt+H")]
    [InlineData(0x31, HotkeyModifiers.Windows, "Win+1")]
    [InlineData(0x70, HotkeyModifiers.Shift, "Shift+F1")]
    [InlineData(0x20, HotkeyModifiers.Control, "Ctrl+Space")]
    [InlineData(0xC0, HotkeyModifiers.Control, "Ctrl+Backtick")]
    public void Combinations_read_the_way_a_user_would_write_them(
        uint key,
        HotkeyModifiers modifiers,
        string expected)
    {
        Assert.Equal(expected, new KeyCombination(key, modifiers).ToString());
    }

    [Fact]
    public void An_empty_combination_prints_as_nothing()
    {
        Assert.Equal(string.Empty, default(KeyCombination).ToString());
    }

    [Fact]
    public void An_unnamed_key_falls_back_to_its_code()
    {
        Assert.Equal("Ctrl+0xFF", new KeyCombination(0xFF, HotkeyModifiers.Control).ToString());
    }

    // ---- Parsing -------------------------------------------------------------------------

    [Fact]
    public void A_combination_survives_a_round_trip()
    {
        KeyCombination original = new(
            VirtualKeys.V,
            HotkeyModifiers.Control | HotkeyModifiers.Alt);

        Assert.True(KeyCombination.TryParse(original.ToString(), out KeyCombination parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void An_unnamed_key_survives_a_round_trip_too()
    {
        // The hex fallback has to parse back, or a key this build has no name for would be lost
        // from the settings file the first time it was rewritten.
        KeyCombination original = new(0xFF, HotkeyModifiers.Control);

        Assert.True(KeyCombination.TryParse(original.ToString(), out KeyCombination parsed));
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("ctrl+alt+v")]
    [InlineData("CTRL+ALT+V")]
    [InlineData(" Ctrl + Alt + V ")]
    public void Parsing_is_forgiving_about_case_and_spacing(string text)
    {
        Assert.True(KeyCombination.TryParse(text, out KeyCombination parsed));
        Assert.Equal(
            new KeyCombination(VirtualKeys.V, HotkeyModifiers.Control | HotkeyModifiers.Alt),
            parsed);
    }

    [Theory]
    [InlineData("control+v", HotkeyModifiers.Control)]
    [InlineData("option+v", HotkeyModifiers.Alt)]
    [InlineData("cmd+v", HotkeyModifiers.Windows)]
    [InlineData("command+v", HotkeyModifiers.Windows)]
    public void Alternative_modifier_spellings_are_accepted(string text, HotkeyModifiers expected)
    {
        // "Command" and "Option" appear in settings carried over from the macOS vocabulary.
        Assert.True(KeyCombination.TryParse(text, out KeyCombination parsed));
        Assert.Equal(expected, parsed.Modifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("V")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Ctrl+V+B")]
    public void Nonsense_does_not_parse(string? text)
    {
        Assert.False(KeyCombination.TryParse(text, out _));
    }

    // ---- Key names -------------------------------------------------------------------------

    [Fact]
    public void Letters_digits_and_function_keys_are_all_named()
    {
        Assert.Equal("A", VirtualKeys.NameOf(0x41));
        Assert.Equal("Z", VirtualKeys.NameOf(0x5A));
        Assert.Equal("0", VirtualKeys.NameOf(0x30));
        Assert.Equal("F1", VirtualKeys.NameOf(0x70));
        Assert.Equal("F24", VirtualKeys.NameOf(0x87));
    }

    [Fact]
    public void Every_named_key_parses_back_to_its_own_code()
    {
        foreach (uint code in Enumerable.Range(0x41, 26).Select(value => (uint)value))
        {
            Assert.True(VirtualKeys.TryParse(VirtualKeys.NameOf(code), out uint parsed));
            Assert.Equal(code, parsed);
        }
    }

    [Fact]
    public void An_unknown_key_name_does_not_parse()
    {
        Assert.False(VirtualKeys.TryParse("Sparkle", out _));
    }
}
