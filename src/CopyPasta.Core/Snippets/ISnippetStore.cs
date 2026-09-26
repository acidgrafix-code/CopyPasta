namespace CopyPasta.Core.Snippets;

/// <summary>
/// Persistence for snippets. Port of the macOS <c>SnippetRepositoryProtocol</c>.
/// </summary>
/// <remarks>
/// Ordering is expressed as "here is the new order of these ids" rather than "move this one to
/// position N", matching macOS. A drag-and-drop tree already knows the resulting order, and
/// rewriting it wholesale avoids the shuffling bugs that per-item moves invite.
/// </remarks>
public interface ISnippetStore
{
    /// <summary>Every folder with its snippets, both in their stored order.</summary>
    IReadOnlyList<SnippetFolderDetail> FetchFolderDetails();

    /// <summary>One folder with its snippets, or null if the id is unknown.</summary>
    SnippetFolderDetail? FetchFolderDetail(Guid folderId);

    /// <summary>One snippet, or null if the id is unknown.</summary>
    Snippet? FetchSnippet(Guid snippetId);

    /// <summary>Appends a new folder with the default title.</summary>
    SnippetFolder InsertFolder();

    /// <summary>
    /// Appends folders read from an exported file, keeping their order and their snippets.
    /// </summary>
    IReadOnlyList<SnippetFolderDetail> InsertFolders(IReadOnlyList<ImportedFolder> folders);

    void UpdateFolderTitle(Guid folderId, string title);

    void UpdateFolderIsEnabled(Guid folderId, bool isEnabled);

    /// <summary>Reorders folders to match the given sequence.</summary>
    void UpdateFolderOrder(IReadOnlyList<Guid> folderIds);

    /// <summary>Deletes a folder and everything in it.</summary>
    bool DeleteFolder(Guid folderId);

    /// <summary>Appends a new snippet to a folder.</summary>
    Snippet? InsertSnippet(Guid folderId);

    void UpdateSnippetTitle(Guid snippetId, string title);

    void UpdateSnippetContent(Guid snippetId, string content);

    void UpdateSnippetIsEnabled(Guid snippetId, bool isEnabled);

    /// <summary>Reorders snippets to match the given sequence.</summary>
    void UpdateSnippetOrder(IReadOnlyList<Guid> snippetIds);

    /// <summary>
    /// Moves a snippet into another folder and applies the destination's new order.
    /// </summary>
    /// <param name="snippetIdsInDestination">
    /// The destination folder's snippet ids in their resulting order, including the moved one.
    /// </param>
    void MoveSnippet(Guid snippetId, Guid folderId, IReadOnlyList<Guid> snippetIdsInDestination);

    bool DeleteSnippet(Guid snippetId);
}
