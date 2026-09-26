namespace CopyPasta.Core.Snippets;

/// <summary>A named group of snippets. Port of the macOS <c>SnippetFolder</c>.</summary>
/// <param name="Id">Stable identifier; also the key for the folder's own global hotkey.</param>
/// <param name="Index">Position among folders, zero-based.</param>
/// <param name="IsEnabled">Disabled folders stay stored but vanish from the menu.</param>
public sealed record SnippetFolder(Guid Id, string Title, int Index, bool IsEnabled = true)
{
    public const string DefaultTitle = "untitled folder";
}

/// <summary>A reusable piece of text. Port of the macOS <c>Snippet</c>.</summary>
/// <param name="Index">Position within its folder, zero-based.</param>
public sealed record Snippet(
    Guid Id,
    Guid FolderId,
    string Title,
    string Content,
    int Index,
    bool IsEnabled = true)
{
    public const string DefaultTitle = "untitled snippet";
}

/// <summary>A folder with its snippets. Port of the macOS <c>SnippetFolderDetail</c>.</summary>
public sealed record SnippetFolderDetail(SnippetFolder Folder, IReadOnlyList<Snippet> Snippets)
{
    /// <summary>Only the snippets that should appear in a menu.</summary>
    public IEnumerable<Snippet> EnabledSnippets => Snippets.Where(snippet => snippet.IsEnabled);
}

/// <summary>A folder as read from an exported file, before it is given identifiers.</summary>
public sealed record ImportedFolder(string Title, IReadOnlyList<ImportedSnippet> Snippets);

/// <summary>A snippet as read from an exported file.</summary>
public sealed record ImportedSnippet(string Title, string Content);
