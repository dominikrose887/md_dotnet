using MdViewer.Services;
using Xunit;

namespace MdViewer.Tests;

public class ExternalReloadPolicyTests
{
    [Fact]
    public void Clean_buffer_reloads_when_disk_differs()
    {
        var decision = ExternalReloadPolicy.Decide(isDirty: false, "hello", "hello!");
        Assert.Equal(ExternalReloadPolicy.Decision.Reload, decision);
    }

    [Fact]
    public void Dirty_buffer_asks_instead_of_reloading()
    {
        var decision = ExternalReloadPolicy.Decide(isDirty: true, "local edit", "from disk");
        Assert.Equal(ExternalReloadPolicy.Decision.Prompt, decision);
    }

    [Fact]
    public void Line_ending_difference_is_not_a_change()
    {
        var decision = ExternalReloadPolicy.Decide(isDirty: false, "a\nb\n", "a\r\nb\r\n");
        Assert.Equal(ExternalReloadPolicy.Decision.None, decision);
    }

    [Fact]
    public void Dirty_buffer_that_matches_disk_is_marked_clean()
    {
        var decision = ExternalReloadPolicy.Decide(isDirty: true, "same\n", "same\r\n");
        Assert.Equal(ExternalReloadPolicy.Decision.MarkClean, decision);
    }

    [Fact]
    public void Binary_rewrite_is_never_a_silent_reload()
    {
        var decision = ExternalReloadPolicy.Decide(isDirty: false, "# Title", "\0\0\0");
        Assert.Equal(ExternalReloadPolicy.Decision.Prompt, decision);
    }

    [Fact]
    public void Empty_disk_against_clean_text_reloads()
    {
        var decision = ExternalReloadPolicy.Decide(isDirty: false, "notes", "");
        Assert.Equal(ExternalReloadPolicy.Decision.Reload, decision);
    }
}

public class DiskChangeCoalescerTests
{
    private static readonly FileStamp First = new(DateTime.UnixEpoch, 10);
    private static readonly FileStamp Second = new(DateTime.UnixEpoch.AddSeconds(2), 12);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Single_write_waits_out_the_settle_delay()
    {
        var coalescer = new DiskChangeCoalescer();
        var id = Guid.NewGuid();
        var change = new DiskChange(DiskChangeKind.Modified, "v1", First);
        var delay = TimeSpan.FromMilliseconds(400);

        Assert.False(coalescer.TryStabilize(id, change, T0, delay, out _));
        Assert.False(coalescer.TryStabilize(id, change, T0.AddMilliseconds(399), delay, out _));
        Assert.True(coalescer.TryStabilize(id, change, T0.AddMilliseconds(400), delay, out var stable));
        Assert.Equal("v1", stable.Content);
    }

    [Fact]
    public void Burst_restarts_the_wait_on_each_new_stamp()
    {
        var coalescer = new DiskChangeCoalescer();
        var id = Guid.NewGuid();
        var first = new DiskChange(DiskChangeKind.Modified, "partial", First);
        var second = new DiskChange(DiskChangeKind.Modified, "done", Second);
        var delay = TimeSpan.FromMilliseconds(400);

        Assert.False(coalescer.TryStabilize(id, first, T0, delay, out _));
        Assert.False(coalescer.TryStabilize(id, second, T0.AddMilliseconds(350), delay, out _));
        Assert.False(coalescer.TryStabilize(id, second, T0.AddMilliseconds(700), delay, out _));
        Assert.True(coalescer.TryStabilize(id, second, T0.AddMilliseconds(750), delay, out var stable));
        Assert.Equal("done", stable.Content);
    }

    [Fact]
    public void Locked_file_does_not_cancel_a_settled_write()
    {
        var coalescer = new DiskChangeCoalescer();
        var id = Guid.NewGuid();
        var change = new DiskChange(DiskChangeKind.Modified, "v1", First);
        var locked = new DiskChange(DiskChangeKind.Unstable, null, First);
        var delay = TimeSpan.FromMilliseconds(400);

        Assert.False(coalescer.TryStabilize(id, change, T0, delay, out _));
        Assert.False(coalescer.TryStabilize(id, locked, T0.AddMilliseconds(200), delay, out _));
        Assert.True(coalescer.TryStabilize(id, change, T0.AddMilliseconds(400), delay, out _));
    }

    [Fact]
    public void Matching_baseline_cancels_the_wait()
    {
        var coalescer = new DiskChangeCoalescer();
        var id = Guid.NewGuid();
        var change = new DiskChange(DiskChangeKind.Modified, "v1", First);
        var none = new DiskChange(DiskChangeKind.None, null, First);
        var delay = TimeSpan.FromMilliseconds(400);

        Assert.False(coalescer.TryStabilize(id, change, T0, delay, out _));
        Assert.False(coalescer.TryStabilize(id, none, T0.AddMilliseconds(100), delay, out _));
        Assert.False(coalescer.TryStabilize(id, change, T0.AddMilliseconds(500), delay, out _));
        Assert.True(coalescer.TryStabilize(id, change, T0.AddMilliseconds(900), delay, out _));
    }

    [Fact]
    public void Delete_uses_the_longer_delay_it_is_given()
    {
        var coalescer = new DiskChangeCoalescer();
        var id = Guid.NewGuid();
        var deleted = new DiskChange(DiskChangeKind.Deleted, null, FileStamp.Missing);
        var delay = TimeSpan.FromMilliseconds(1200);

        Assert.False(coalescer.TryStabilize(id, deleted, T0, delay, out _));
        Assert.False(coalescer.TryStabilize(id, deleted, T0.AddMilliseconds(400), delay, out _));
        Assert.True(coalescer.TryStabilize(id, deleted, T0.AddMilliseconds(1200), delay, out var stable));
        Assert.Equal(DiskChangeKind.Deleted, stable.Kind);
    }

    [Fact]
    public void Pending_delete_stays_visible_until_it_is_resolved()
    {
        var coalescer = new DiskChangeCoalescer();
        var id = Guid.NewGuid();
        var deleted = new DiskChange(DiskChangeKind.Deleted, null, FileStamp.Missing);

        Assert.False(coalescer.TryStabilize(id, deleted, T0, TimeSpan.FromMilliseconds(1200), out _));
        Assert.True(coalescer.IsPending(id, DiskChangeKind.Deleted));

        coalescer.Forget(id);
        Assert.False(coalescer.IsPending(id, DiskChangeKind.Deleted));
    }
}

public class EditorAnchorTests
{
    [Fact]
    public void Append_below_the_viewport_keeps_the_reading_position()
    {
        var anchor = EditorAnchor.Map(
            "hello\n",
            "hello\nworld\n",
            new EditorAnchor(Caret: 0, SelectionStart: 0, SelectionLength: 0, ScrollLine: 0));

        Assert.Equal(0, anchor.Caret);
        Assert.Equal(0, anchor.ScrollLine);
    }

    [Fact]
    public void Caret_at_end_follows_an_append()
    {
        var anchor = EditorAnchor.Map(
            "hello\n",
            "hello\nworld\n",
            new EditorAnchor(Caret: 6, SelectionStart: 6, SelectionLength: 0, ScrollLine: 1));

        Assert.Equal("hello\nworld\n".Length, anchor.Caret);
        Assert.Equal(2, anchor.ScrollLine);
    }

    [Fact]
    public void Filling_an_empty_file_stays_at_the_top()
    {
        var anchor = EditorAnchor.Map(
            "",
            "# Generated\n\nbody\n",
            new EditorAnchor(Caret: 0, SelectionStart: 0, SelectionLength: 0, ScrollLine: 0));

        Assert.Equal(0, anchor.Caret);
        Assert.Equal(0, anchor.ScrollLine);
    }

    [Fact]
    public void Insertion_before_the_caret_shifts_it()
    {
        var anchor = EditorAnchor.Map(
            "hello",
            "Xhello",
            new EditorAnchor(Caret: 5, SelectionStart: 5, SelectionLength: 0, ScrollLine: 0));

        Assert.Equal(6, anchor.Caret);
    }

    [Fact]
    public void Selection_after_the_change_moves_with_the_text()
    {
        var anchor = EditorAnchor.Map(
            "hello world",
            "Xhello world",
            new EditorAnchor(Caret: 11, SelectionStart: 6, SelectionLength: 5, ScrollLine: 0));

        Assert.Equal(7, anchor.SelectionStart);
        Assert.Equal(5, anchor.SelectionLength);
    }

    [Fact]
    public void Selection_inside_the_edit_collapses()
    {
        var anchor = EditorAnchor.Map(
            "abcdef",
            "abXYef",
            new EditorAnchor(Caret: 3, SelectionStart: 3, SelectionLength: 1, ScrollLine: 0));

        Assert.Equal(0, anchor.SelectionLength);
        Assert.Equal(2, anchor.Caret);
    }

    [Fact]
    public void Same_text_with_different_newlines_keeps_the_caret()
    {
        var anchor = EditorAnchor.Map(
            "a\nb\n",
            "a\r\nb\r\n",
            new EditorAnchor(Caret: 2, SelectionStart: 2, SelectionLength: 0, ScrollLine: 1));

        Assert.Equal(2, anchor.Caret);
        Assert.Equal(1, anchor.ScrollLine);
    }
}
