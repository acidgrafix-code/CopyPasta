using System.IO;
using CopyPasta.Interop;

namespace CopyPasta.App.Tests;

/// <summary>
/// The screenshots folder watcher. Port of the macOS <c>ScreenShotObserver</c>.
/// </summary>
public sealed class ScreenshotWatcherTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"copypasta-shots-{Guid.NewGuid():n}");

    public ScreenshotWatcherTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>Writes a file the way a screenshot tool would: create, fill, close.</summary>
    private string WriteImage(string name, int bytes = 2048)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static async Task<string?> WaitForCaptureAsync(
        ScreenshotWatcher watcher,
        Func<Task> act,
        TimeSpan? timeout = null)
    {
        TaskCompletionSource<string> captured = new();
        watcher.ScreenshotCaptured += (_, path) => captured.TrySetResult(path);

        await act();

        Task completed = await Task.WhenAny(
            captured.Task,
            Task.Delay(timeout ?? TimeSpan.FromSeconds(10)));

        return completed == captured.Task ? await captured.Task : null;
    }

    // ---- Watching ----------------------------------------------------------------------

    [Fact]
    public async Task A_new_screenshot_is_reported()
    {
        using ScreenshotWatcher watcher = new(_directory);
        Assert.True(watcher.Start());

        string? captured = await WaitForCaptureAsync(
            watcher,
            () => Task.Run(() => WriteImage("shot.png")));

        Assert.NotNull(captured);
        Assert.Equal("shot.png", Path.GetFileName(captured));
    }

    [Theory]
    [InlineData("shot.jpg")]
    [InlineData("shot.jpeg")]
    [InlineData("shot.bmp")]
    [InlineData("SHOT.PNG")]
    public async Task Every_image_extension_a_screenshot_tool_uses_is_watched(string name)
    {
        using ScreenshotWatcher watcher = new(_directory);
        watcher.Start();

        string? captured = await WaitForCaptureAsync(watcher, () => Task.Run(() => WriteImage(name)));

        Assert.NotNull(captured);
    }

    [Fact]
    public async Task Files_that_are_not_images_are_ignored()
    {
        using ScreenshotWatcher watcher = new(_directory);
        watcher.Start();

        string? captured = await WaitForCaptureAsync(
            watcher,
            () => Task.Run(() => WriteImage("notes.txt")),
            TimeSpan.FromSeconds(2));

        Assert.Null(captured);
    }

    [Fact]
    public async Task A_file_renamed_into_place_is_reported()
    {
        // Several tools write to a temporary name and rename, which arrives as a rename rather
        // than a creation.
        using ScreenshotWatcher watcher = new(_directory);
        watcher.Start();

        string temporary = WriteImage("pending.tmp");

        string? captured = await WaitForCaptureAsync(
            watcher,
            () => Task.Run(() => File.Move(temporary, Path.Combine(_directory, "renamed.png"))));

        Assert.NotNull(captured);
        Assert.Equal("renamed.png", Path.GetFileName(captured));
    }

    [Fact]
    public async Task A_file_still_being_written_is_not_reported_until_it_is_closed()
    {
        // The whole reason the watcher waits: reading on the notification yields a truncated file.
        using ScreenshotWatcher watcher = new(_directory);
        watcher.Start();

        string path = Path.Combine(_directory, "slow.png");
        FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);

        TaskCompletionSource<string> captured = new();
        watcher.ScreenshotCaptured += (_, captured_path) => captured.TrySetResult(captured_path);

        await stream.WriteAsync(new byte[1024]);
        await stream.FlushAsync();

        // Held open: nothing should be reported yet.
        Task early = await Task.WhenAny(captured.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(captured.Task, early);

        await stream.DisposeAsync();

        Task completed = await Task.WhenAny(captured.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(captured.Task, completed);
    }

    [Fact]
    public async Task A_placeholder_that_never_gets_content_is_not_reported()
    {
        // Some tools create the file first and write it a moment later. An empty placeholder that
        // is removed before anything is written must not be reported.
        //
        // Deliberately not written-then-deleted: a file that is briefly complete on disk may
        // legitimately be seen and reported before the delete lands, and asserting otherwise is a
        // race rather than a contract. The capture path already tolerates the file vanishing
        // afterwards.
        using ScreenshotWatcher watcher = new(_directory);
        watcher.Start();

        string? captured = await WaitForCaptureAsync(
            watcher,
            () => Task.Run(() =>
            {
                string path = Path.Combine(_directory, "fleeting.png");
                File.WriteAllBytes(path, []);
                File.Delete(path);
            }),
            TimeSpan.FromSeconds(2));

        Assert.Null(captured);
    }

    [Fact]
    public async Task An_empty_file_is_not_reported_while_it_stays_empty()
    {
        // Zero length means the writer has not filled it in yet.
        using ScreenshotWatcher watcher = new(_directory);
        watcher.Start();

        string? captured = await WaitForCaptureAsync(
            watcher,
            () => Task.Run(() => File.WriteAllBytes(Path.Combine(_directory, "empty.png"), [])),
            TimeSpan.FromSeconds(2));

        Assert.Null(captured);
    }

    // ---- Lifecycle -----------------------------------------------------------------------

    [Fact]
    public void Watching_a_folder_that_does_not_exist_reports_failure_rather_than_throwing()
    {
        // Normal on a machine where no screenshot has ever been saved.
        using ScreenshotWatcher watcher = new(Path.Combine(_directory, "nope"));

        Assert.False(watcher.Start());
        Assert.False(watcher.IsWatching);
    }

    [Fact]
    public void Starting_twice_is_harmless()
    {
        using ScreenshotWatcher watcher = new(_directory);

        Assert.True(watcher.Start());
        Assert.True(watcher.Start());
        Assert.True(watcher.IsWatching);
    }

    [Fact]
    public async Task Stopping_ends_the_reports()
    {
        using ScreenshotWatcher watcher = new(_directory);
        watcher.Start();
        watcher.Stop();

        string? captured = await WaitForCaptureAsync(
            watcher,
            () => Task.Run(() => WriteImage("after-stop.png")),
            TimeSpan.FromSeconds(2));

        Assert.Null(captured);
        Assert.False(watcher.IsWatching);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        ScreenshotWatcher watcher = new(_directory);
        watcher.Start();
        watcher.Dispose();
        watcher.Dispose();
    }

    [Fact]
    public void The_default_folder_is_the_one_Windows_saves_screenshots_to()
    {
        Assert.EndsWith(
            Path.Combine("Pictures", "Screenshots"),
            ScreenshotWatcher.DefaultDirectory,
            StringComparison.OrdinalIgnoreCase);
    }
}
