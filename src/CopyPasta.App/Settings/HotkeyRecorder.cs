using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CopyPasta.Core.Hotkeys;

namespace CopyPasta.App.Settings;

/// <summary>
/// A button that records a key combination when clicked.
/// </summary>
/// <remarks>
/// <para>
/// Port of the macOS <c>KeyHolder.RecordView</c>. While recording it swallows every key so the
/// combination being captured does not also trigger whatever it is normally bound to — pressing
/// Alt+F4 to record it should not close the window.
/// </para>
/// <para>
/// Modifiers alone are never accepted: a combination needs a real key, and without this the
/// recorder would "finish" the moment the user pressed Ctrl.
/// </para>
/// </remarks>
public sealed class HotkeyRecorder : Button
{
    public static readonly DependencyProperty CombinationProperty =
        DependencyProperty.Register(
            nameof(Combination),
            typeof(KeyCombination?),
            typeof(HotkeyRecorder),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnCombinationChanged));

    private bool _recording;

    public HotkeyRecorder()
    {
        Focusable = true;
        MinWidth = 150;
        UpdateCaption();
    }

    /// <summary>Raised whenever the recorded combination changes, including when cleared.</summary>
    public event EventHandler<KeyCombination?>? CombinationChanged;

    public KeyCombination? Combination
    {
        get => (KeyCombination?)GetValue(CombinationProperty);
        set => SetValue(CombinationProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        BeginRecording();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        EndRecording();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        // Nothing reaches the rest of the app while recording.
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                EndRecording();
                return;

            case Key.Back or Key.Delete:
                Combination = null;
                EndRecording();
                return;
        }

        if (IsModifier(key))
        {
            // Show the modifiers as they are held, so the user can see the combination forming.
            UpdateCaption($"{Describe(Keyboard.Modifiers)}…");
            return;
        }

        HotkeyModifiers modifiers = Translate(Keyboard.Modifiers);
        if (modifiers == HotkeyModifiers.None)
        {
            // A bare key would be registered globally and swallow that key everywhere.
            UpdateCaption("Add a modifier…");
            return;
        }

        Combination = new KeyCombination((uint)KeyInterop.VirtualKeyFromKey(key), modifiers);
        EndRecording();
    }

    private void BeginRecording()
    {
        _recording = true;
        Keyboard.Focus(this);
        UpdateCaption("Press a combination…");
    }

    private void EndRecording()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        UpdateCaption();
    }

    private void UpdateCaption(string? recordingText = null)
    {
        if (recordingText is not null)
        {
            Content = recordingText;
            return;
        }

        Content = Combination is { } combination && combination.IsValid
            ? combination.ToString()
            : "Not set";
    }

    private static void OnCombinationChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (sender is not HotkeyRecorder recorder)
        {
            return;
        }

        recorder.UpdateCaption();
        recorder.CombinationChanged?.Invoke(recorder, recorder.Combination);
    }

    private static bool IsModifier(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin
        or Key.System;

    private static HotkeyModifiers Translate(ModifierKeys modifiers)
    {
        HotkeyModifiers result = HotkeyModifiers.None;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotkeyModifiers.Windows;
        }

        return result;
    }

    private static string Describe(ModifierKeys modifiers)
    {
        List<string> parts = [];

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        return parts.Count == 0 ? string.Empty : string.Join("+", parts) + "+";
    }
}
