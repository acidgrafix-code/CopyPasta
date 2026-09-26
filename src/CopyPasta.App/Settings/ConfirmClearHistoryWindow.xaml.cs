using System.Windows;
using CopyPasta.Core.Localization;

namespace CopyPasta.App.Settings;

/// <param name="Confirmed">The user chose to clear the history.</param>
/// <param name="SuppressFutureAlerts">The user ticked "Don't ask again".</param>
public readonly record struct ClearHistoryDecision(bool Confirmed, bool SuppressFutureAlerts);

/// <summary>
/// The confirmation shown before wiping the history.
/// </summary>
/// <remarks>
/// A custom window rather than <c>MessageBox</c> because of the suppression checkbox: macOS uses
/// <c>NSAlert.showsSuppressionButton</c>, and ticking it is the only way the
/// <c>showsClearHistoryAlert</c> setting ever gets turned off in normal use. A plain message box
/// cannot offer that.
/// </remarks>
public partial class ConfirmClearHistoryWindow : Window
{
    private bool _confirmed;

    public ConfirmClearHistoryWindow(Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        InitializeComponent();

        Title = localizer["Clear History"];
        MessageText.Text = localizer["Clear History"];
        DetailText.Text = localizer["Are you sure you want to clear your clipboard history?"];
        ConfirmButton.Content = localizer["Clear History"];
        CancelButton.Content = localizer["Cancel"];
    }

    /// <summary>Shows the dialog and reports what the user chose.</summary>
    public static ClearHistoryDecision Ask(Localizer localizer)
    {
        ConfirmClearHistoryWindow window = new(localizer);
        window.ShowDialog();

        return new ClearHistoryDecision(
            window._confirmed,
            window.DoNotAskAgain.IsChecked == true);
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}
