using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace CopyPasta.App.Snippets;

/// <summary>
/// The snippet editor. Port of the macOS <c>CPYSnippetsEditorWindowController</c>.
/// </summary>
/// <remarks>
/// Every edit writes through immediately, as the original does — there is no save button and
/// closing the window cannot lose work. The window itself is disposable state; the view model owns
/// everything that matters.
/// </remarks>
public partial class SnippetEditorWindow : Window
{
    private readonly SnippetEditorViewModel _model;
    private Point _dragOrigin;
    private SnippetTreeNode? _dragging;

    public SnippetEditorWindow(SnippetEditorViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        InitializeComponent();
        DataContext = _model;
    }

    private SnippetTreeNode? Selected => Tree.SelectedItem as SnippetTreeNode;

    // ---- Toolbar -----------------------------------------------------------------------

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        FolderNode folder = _model.AddFolder();
        Status($"Added '{folder.Title}'.");
        SelectAndFocusTitle(folder);
    }

    private void OnAddSnippet(object sender, RoutedEventArgs e)
    {
        SnippetNode? snippet = _model.AddSnippet(Selected);

        if (snippet is null)
        {
            Status("Add a folder first.");
            return;
        }

        Status($"Added '{snippet.Title}'.");
        SelectAndFocusTitle(snippet);
    }

    private void OnToggleEnabled(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } node)
        {
            return;
        }

        _model.ToggleEnabled(node);
        Status($"'{node.Title}' is now {(node.IsEnabled ? "enabled" : "disabled")}.");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } node)
        {
            return;
        }

        // A folder takes its snippets with it, which is worth confirming; a single snippet is not.
        if (node is FolderNode folder && folder.Snippets.Count > 0)
        {
            MessageBoxResult answer = MessageBox.Show(
                this,
                $"Delete '{folder.Title}' and its {folder.Snippets.Count} snippets?",
                "Delete folder",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        _model.Delete(node);
        ClearEditor();
        Status($"Deleted '{node.Title}'.");
    }

    // ---- Import and export ---------------------------------------------------------------

    private void OnImport(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Import snippets",
            Filter = "Snippet files (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            int imported = _model.Import(File.ReadAllText(dialog.FileName));

            if (imported < 0)
            {
                Status("That file could not be read as a snippet export.");
                return;
            }

            Status($"Imported {imported} folder{(imported == 1 ? string.Empty : "s")}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status($"Could not read the file: {exception.Message}");
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Export snippets",
            Filter = "Snippet files (*.xml)|*.xml",
            FileName = "snippets.xml",
            DefaultExt = ".xml",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _model.Export());
            Status($"Exported to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status($"Could not write the file: {exception.Message}");
        }
    }

    // ---- Editing ----------------------------------------------------------------------------

    private void OnSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (Selected)
        {
            case SnippetNode snippet:
                TitleBox.Text = snippet.Title;
                ContentBox.Text = snippet.Content;
                ContentBox.IsEnabled = true;
                break;

            case FolderNode folder:
                TitleBox.Text = folder.Title;
                ContentBox.Text = string.Empty;

                // A folder has no body, so the box is present but inert rather than misleading.
                ContentBox.IsEnabled = false;
                break;

            default:
                ClearEditor();
                break;
        }
    }

    private void OnTitleCommitted(object sender, RoutedEventArgs e)
    {
        if (Selected is { } node && TitleBox.Text != node.Title)
        {
            _model.Rename(node, TitleBox.Text);
            TitleBox.Text = node.Title;
        }
    }

    private void OnTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnTitleCommitted(sender, e);
            e.Handled = true;
        }
    }

    private void OnContentCommitted(object sender, RoutedEventArgs e)
    {
        if (Selected is SnippetNode snippet && ContentBox.Text != snippet.Content)
        {
            _model.UpdateContent(snippet, ContentBox.Text);
        }
    }

    private void ClearEditor()
    {
        TitleBox.Text = string.Empty;
        ContentBox.Text = string.Empty;
        ContentBox.IsEnabled = false;
    }

    private void SelectAndFocusTitle(SnippetTreeNode node)
    {
        // Selecting through the container tree is fiddly; setting the editor fields directly and
        // focusing the title box gets the user straight to naming the thing they just made.
        TitleBox.Text = node.Title;
        ContentBox.IsEnabled = node is SnippetNode;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void Status(string message) => StatusText.Text = message;

    // ---- Drag and drop -------------------------------------------------------------------------

    private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(null);
        _dragging = null;
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Vector moved = _dragOrigin - e.GetPosition(null);

        // The system drag threshold, so a sloppy click does not start a drag.
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (NodeUnder(e.OriginalSource as DependencyObject) is not { } node)
        {
            return;
        }

        _dragging = node;
        DragDrop.DoDragDrop(Tree, node, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsValidDrop(NodeUnder(e.OriginalSource as DependencyObject))
            ? DragDropEffects.Move
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        SnippetTreeNode? target = NodeUnder(e.OriginalSource as DependencyObject);

        if (_dragging is null || !IsValidDrop(target))
        {
            return;
        }

        switch (_dragging)
        {
            case FolderNode folder when target is FolderNode destination:
                _model.MoveFolder(folder, _model.Folders.IndexOf(destination));
                Status($"Moved '{folder.Title}'.");
                break;

            case SnippetNode snippet:
                MoveSnippetOnto(snippet, target!);
                break;
        }

        _dragging = null;
        e.Handled = true;
    }

    private void MoveSnippetOnto(SnippetNode snippet, SnippetTreeNode target)
    {
        switch (target)
        {
            case FolderNode folder:
                // Dropped on a folder: append to it.
                _model.MoveSnippet(snippet, folder, -1);
                Status($"Moved '{snippet.Title}' into '{folder.Title}'.");
                break;

            case SnippetNode sibling when _model.FolderFor(sibling) is { } folder:
                _model.MoveSnippet(snippet, folder, folder.Snippets.IndexOf(sibling));
                Status($"Moved '{snippet.Title}'.");
                break;
        }
    }

    /// <summary>A folder can only be reordered among folders; a snippet can go anywhere.</summary>
    private bool IsValidDrop(SnippetTreeNode? target)
    {
        if (_dragging is null || target is null || ReferenceEquals(_dragging, target))
        {
            return false;
        }

        return _dragging is not FolderNode || target is FolderNode;
    }

    private static SnippetTreeNode? NodeUnder(DependencyObject? source)
    {
        while (source is not null and not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return (source as TreeViewItem)?.DataContext as SnippetTreeNode;
    }
}
