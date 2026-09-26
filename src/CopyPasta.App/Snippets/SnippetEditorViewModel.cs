using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CopyPasta.Core.Snippets;

namespace CopyPasta.App.Snippets;

/// <summary>Shared behaviour for the two node kinds in the editor tree.</summary>
public abstract class SnippetTreeNode : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isEnabled = true;

    public Guid Id { get; init; }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>
    /// Disabled items stay in the editor but vanish from the menu. Port of the macOS enable toggle.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>A folder in the editor tree.</summary>
public sealed class FolderNode : SnippetTreeNode
{
    public ObservableCollection<SnippetNode> Snippets { get; } = [];

    /// <summary>The folder's own global hotkey, shown in the editor. Empty when unbound.</summary>
    public string Hotkey { get; set; } = string.Empty;
}

/// <summary>A snippet in the editor tree.</summary>
public sealed class SnippetNode : SnippetTreeNode
{
    private string _content = string.Empty;

    public Guid FolderId { get; set; }

    public string Content
    {
        get => _content;
        set => Set(ref _content, value);
    }
}

/// <summary>
/// The snippet editor's state and operations, kept free of WPF so it can be reasoned about — and
/// later tested — without a window.
/// </summary>
/// <remarks>
/// Port of the macOS <c>CPYSnippetsEditorWindowController</c>, minus its view plumbing. Every
/// mutation writes through to the store immediately, exactly as the original does: there is no
/// save button, and closing the window cannot lose work.
/// </remarks>
public sealed class SnippetEditorViewModel
{
    private readonly ISnippetStore _store;

    public SnippetEditorViewModel(ISnippetStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Reload();
    }

    public ObservableCollection<FolderNode> Folders { get; } = [];

    /// <summary>Raised when something changed that the menu or hotkeys should pick up.</summary>
    public event EventHandler? Changed;

    public void Reload()
    {
        Folders.Clear();

        foreach (SnippetFolderDetail detail in _store.FetchFolderDetails())
        {
            FolderNode folder = new()
            {
                Id = detail.Folder.Id,
                Title = detail.Folder.Title,
                IsEnabled = detail.Folder.IsEnabled,
            };

            foreach (Snippet snippet in detail.Snippets)
            {
                folder.Snippets.Add(new SnippetNode
                {
                    Id = snippet.Id,
                    FolderId = snippet.FolderId,
                    Title = snippet.Title,
                    Content = snippet.Content,
                    IsEnabled = snippet.IsEnabled,
                });
            }

            Folders.Add(folder);
        }
    }

    // ---- Create ---------------------------------------------------------------------------

    public FolderNode AddFolder()
    {
        SnippetFolder folder = _store.InsertFolder();

        FolderNode node = new()
        {
            Id = folder.Id,
            Title = folder.Title,
            IsEnabled = folder.IsEnabled,
        };

        Folders.Add(node);
        NotifyChanged();
        return node;
    }

    /// <summary>Adds a snippet to a folder, or to the folder a snippet belongs to.</summary>
    public SnippetNode? AddSnippet(SnippetTreeNode? selection)
    {
        FolderNode? folder = FolderFor(selection) ?? Folders.LastOrDefault();
        if (folder is null)
        {
            return null;
        }

        Snippet? snippet = _store.InsertSnippet(folder.Id);
        if (snippet is null)
        {
            return null;
        }

        SnippetNode node = new()
        {
            Id = snippet.Id,
            FolderId = folder.Id,
            Title = snippet.Title,
            Content = snippet.Content,
            IsEnabled = snippet.IsEnabled,
        };

        folder.Snippets.Add(node);
        NotifyChanged();
        return node;
    }

    // ---- Update ----------------------------------------------------------------------------

    public void Rename(SnippetTreeNode node, string title)
    {
        ArgumentNullException.ThrowIfNull(node);

        // An empty title would render as a blank menu row, so fall back to the default rather
        // than letting the user create an invisible entry.
        string effective = string.IsNullOrWhiteSpace(title)
            ? node is FolderNode ? SnippetFolder.DefaultTitle : Snippet.DefaultTitle
            : title;

        node.Title = effective;

        if (node is FolderNode folder)
        {
            _store.UpdateFolderTitle(folder.Id, effective);
        }
        else
        {
            _store.UpdateSnippetTitle(node.Id, effective);
        }

        NotifyChanged();
    }

    public void UpdateContent(SnippetNode snippet, string content)
    {
        ArgumentNullException.ThrowIfNull(snippet);

        snippet.Content = content;
        _store.UpdateSnippetContent(snippet.Id, content);
        NotifyChanged();
    }

    public void ToggleEnabled(SnippetTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        node.IsEnabled = !node.IsEnabled;

        if (node is FolderNode folder)
        {
            _store.UpdateFolderIsEnabled(folder.Id, node.IsEnabled);
        }
        else
        {
            _store.UpdateSnippetIsEnabled(node.Id, node.IsEnabled);
        }

        NotifyChanged();
    }

    // ---- Delete ------------------------------------------------------------------------------

    public void Delete(SnippetTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node is FolderNode folder)
        {
            _store.DeleteFolder(folder.Id);
            Folders.Remove(folder);
        }
        else if (node is SnippetNode snippet)
        {
            _store.DeleteSnippet(snippet.Id);
            FolderById(snippet.FolderId)?.Snippets.Remove(snippet);
        }

        NotifyChanged();
    }

    // ---- Reorder and move ----------------------------------------------------------------------

    public void MoveFolder(FolderNode folder, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(folder);

        int current = Folders.IndexOf(folder);
        if (current < 0)
        {
            return;
        }

        int destination = Math.Clamp(targetIndex, 0, Folders.Count - 1);
        if (current == destination)
        {
            return;
        }

        Folders.Move(current, destination);
        _store.UpdateFolderOrder(Folders.Select(node => node.Id).ToArray());
        NotifyChanged();
    }

    /// <summary>
    /// Moves a snippet within its folder or into another one.
    /// </summary>
    /// <param name="targetIndex">
    /// Where it should land in the destination, or -1 to append.
    /// </param>
    public void MoveSnippet(SnippetNode snippet, FolderNode destination, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentNullException.ThrowIfNull(destination);

        FolderNode? source = FolderById(snippet.FolderId);
        if (source is null)
        {
            return;
        }

        source.Snippets.Remove(snippet);

        int index = targetIndex < 0 || targetIndex > destination.Snippets.Count
            ? destination.Snippets.Count
            : targetIndex;

        destination.Snippets.Insert(index, snippet);

        if (source.Id == destination.Id)
        {
            _store.UpdateSnippetOrder(destination.Snippets.Select(node => node.Id).ToArray());
        }
        else
        {
            snippet.FolderId = destination.Id;

            // One call, so the reparent and the destination's new order commit together.
            _store.MoveSnippet(
                snippet.Id,
                destination.Id,
                destination.Snippets.Select(node => node.Id).ToArray());

            // The source folder's remaining snippets close the gap the move left.
            _store.UpdateSnippetOrder(source.Snippets.Select(node => node.Id).ToArray());
        }

        NotifyChanged();
    }

    // ---- Import and export ----------------------------------------------------------------------

    /// <summary>Appends the folders in an exported file. Returns how many arrived.</summary>
    public int Import(string xml)
    {
        if (!SnippetXml.TryImport(xml, out IReadOnlyList<ImportedFolder> folders, out _))
        {
            return -1;
        }

        _store.InsertFolders(folders);
        Reload();
        NotifyChanged();
        return folders.Count;
    }

    public string Export() => SnippetXml.Export(_store.FetchFolderDetails());

    // ---- Helpers --------------------------------------------------------------------------------

    public FolderNode? FolderFor(SnippetTreeNode? node) => node switch
    {
        FolderNode folder => folder,
        SnippetNode snippet => FolderById(snippet.FolderId),
        _ => null,
    };

    private FolderNode? FolderById(Guid id) => Folders.FirstOrDefault(folder => folder.Id == id);

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
