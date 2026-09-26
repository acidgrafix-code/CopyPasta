using CopyPasta.Core.Capture;

namespace CopyPasta.Core.Tests;

/// <summary>
/// The two-pronged exclusion check ported from macOS: focused process, plus the private-format
/// marker that catches apps which write to the clipboard without ever taking focus.
/// </summary>
public class ExcludedApplicationMatcherTests
{
    private static bool IsExcluded(
        ForegroundApplicationInfo? foreground,
        IReadOnlyList<ExcludedApplication> excluded,
        params ClipboardFormat[] formats)
    {
        ExcludedApplicationMatcher matcher = new(new FakeForegroundApplication { Current = foreground });
        return matcher.IsExcluded(excluded, formats);
    }

    private static ExcludedApplication[] Excluding(params string[] identifiers) =>
        identifiers.Select(identifier => new ExcludedApplication(identifier)).ToArray();

    // ---- Nothing excluded --------------------------------------------------------------

    [Fact]
    public void An_empty_exclusion_list_excludes_nothing()
    {
        Assert.False(IsExcluded(
            new ForegroundApplicationInfo("KeePass", @"C:\Apps\KeePass.exe"),
            [],
            ClipboardFormat.UnicodeText));
    }

    [Fact]
    public void A_blank_identifier_is_ignored_rather_than_matching_everything()
    {
        Assert.False(IsExcluded(
            new ForegroundApplicationInfo("notepad", null),
            Excluding("", "   "),
            ClipboardFormat.UnicodeText));
    }

    // ---- Foreground process -------------------------------------------------------------

    [Fact]
    public void A_process_name_matches()
    {
        Assert.True(IsExcluded(
            new ForegroundApplicationInfo("KeePass", null),
            Excluding("KeePass")));
    }

    [Fact]
    public void A_process_name_match_is_case_insensitive()
    {
        Assert.True(IsExcluded(
            new ForegroundApplicationInfo("KeePass", null),
            Excluding("keepass")));
    }

    [Fact]
    public void A_full_executable_path_matches()
    {
        Assert.True(IsExcluded(
            new ForegroundApplicationInfo("KeePass", @"C:\Apps\KeePass.exe"),
            Excluding(@"C:\Apps\KeePass.exe")));
    }

    [Fact]
    public void A_stored_path_still_matches_after_the_app_moves()
    {
        // Settings may have stored a path, but the app has since been reinstalled elsewhere.
        // Falling back to the file name keeps the exclusion working.
        Assert.True(IsExcluded(
            new ForegroundApplicationInfo("KeePass", @"D:\Portable\KeePass.exe"),
            Excluding(@"C:\Apps\KeePass.exe")));
    }

    [Fact]
    public void A_different_application_does_not_match()
    {
        Assert.False(IsExcluded(
            new ForegroundApplicationInfo("notepad", @"C:\Windows\notepad.exe"),
            Excluding("KeePass")));
    }

    [Fact]
    public void An_unknown_foreground_application_does_not_match_on_process_identity()
    {
        Assert.False(IsExcluded(null, Excluding("KeePass"), ClipboardFormat.UnicodeText));
    }

    // ---- Private-format marker -----------------------------------------------------------

    [Fact]
    public void A_private_format_marker_matches_even_when_the_app_never_took_focus()
    {
        // The case the macOS comment calls out: a background helper or browser extension puts
        // the clip there, so the foreground process is someone else entirely. Its private
        // format name is the only evidence of who owns the clip.
        Assert.True(IsExcluded(
            new ForegroundApplicationInfo("explorer", null),
            Excluding("KeePass.Internal.Marker"),
            ClipboardFormat.UnicodeText,
            ClipboardFormat.FromName("KeePass.Internal")));
    }

    [Fact]
    public void The_marker_must_be_a_prefix_of_the_identifier_not_the_reverse()
    {
        // Mirrors the macOS direction: identifier.hasPrefix(typeName).
        Assert.False(IsExcluded(
            new ForegroundApplicationInfo("explorer", null),
            Excluding("KeePass"),
            ClipboardFormat.FromName("KeePass.Internal.Marker")));
    }

    [Fact]
    public void A_standard_format_is_never_treated_as_an_ownership_marker()
    {
        // Without this guard an exclusion for "PNGGleam" would be matched by the standard PNG
        // format and silently block every image copy in the system.
        Assert.False(IsExcluded(
            new ForegroundApplicationInfo("mspaint", null),
            Excluding("PNGGleam.exe"),
            ClipboardFormat.Png));
    }

    [Fact]
    public void A_very_short_private_format_is_not_treated_as_a_marker()
    {
        Assert.False(IsExcluded(
            new ForegroundApplicationInfo("explorer", null),
            Excluding("Acme.Suite"),
            ClipboardFormat.FromName("Acme")));
    }

    [Fact]
    public void A_long_private_format_prefix_does_match()
    {
        Assert.True(IsExcluded(
            new ForegroundApplicationInfo("explorer", null),
            Excluding("Acme.Suite.Password"),
            ClipboardFormat.FromName("Acme.Suite")));
    }

    [Fact]
    public void Unnamed_formats_are_ignored()
    {
        Assert.False(IsExcluded(
            new ForegroundApplicationInfo("explorer", null),
            Excluding("anything"),
            default(ClipboardFormat)));
    }

    // ---- Argument validation -------------------------------------------------------------

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ExcludedApplicationMatcher(null!));

        ExcludedApplicationMatcher matcher = new(new FakeForegroundApplication());
        Assert.Throws<ArgumentNullException>(() => matcher.IsExcluded(null!, []));
        Assert.Throws<ArgumentNullException>(() => matcher.IsExcluded([], null!));
    }
}
