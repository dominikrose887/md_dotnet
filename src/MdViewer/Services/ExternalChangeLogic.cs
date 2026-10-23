namespace MdViewer.Services;

/// <summary>
/// Waits until a disk observation stays the same long enough to be the finished
/// write, not a truncate or a burst of intermediate saves.
/// </summary>
public sealed class DiskChangeCoalescer
{
    private readonly Dictionary<Guid, Pending> _pending = new();

    private readonly record struct Pending(DiskChangeKind Kind, FileStamp Stamp, DateTime SinceUtc);

    public void Forget(Guid tab) => _pending.Remove(tab);

    public bool IsPending(Guid tab, DiskChangeKind kind) =>
        _pending.TryGetValue(tab, out var pending) && pending.Kind == kind;

    /// <summary>
    /// Returns true when <paramref name="observed"/> has been unchanged for <paramref name="delay"/>.
    /// Unstable and unreadable observations wait without resetting the clock. None cancels the wait.
    /// </summary>
    public bool TryStabilize(Guid tab, DiskChange observed, DateTime utcNow, TimeSpan delay, out DiskChange stable)
    {
        stable = default;

        if (observed.Kind == DiskChangeKind.None)
        {
            _pending.Remove(tab);
            return false;
        }

        if (observed.Kind is DiskChangeKind.Unstable or DiskChangeKind.Unavailable)
            return false;

        if (!_pending.TryGetValue(tab, out var pending)
            || pending.Kind != observed.Kind
            || pending.Stamp != observed.Stamp)
        {
            _pending[tab] = new Pending(observed.Kind, observed.Stamp, utcNow);
            return false;
        }

        if (utcNow - pending.SinceUtc < delay)
            return false;

        _pending.Remove(tab);
        stable = observed;
        return true;
    }
}

/// <summary>
/// Where the caret, selection, and scroll line should land after an external edit.
/// Offsets inside the replaced span move to the start of that span. A caret that
/// was already at the end of the file follows a pure append.
/// </summary>
public readonly record struct EditorAnchor(int Caret, int SelectionStart, int SelectionLength, int ScrollLine)
{
    public static EditorAnchor Map(string? oldText, string? newText, EditorAnchor current)
    {
        var oldN = ExternalReloadPolicy.Normalize(oldText);
        var newN = ExternalReloadPolicy.Normalize(newText);

        if (string.Equals(oldN, newN, StringComparison.Ordinal))
        {
            var start = Math.Clamp(current.SelectionStart, 0, newN.Length);
            return new EditorAnchor(
                Math.Clamp(current.Caret, 0, newN.Length),
                start,
                Math.Clamp(current.SelectionLength, 0, newN.Length - start),
                Math.Max(0, current.ScrollLine));
        }

        var prefix = CommonPrefix(oldN, newN);
        var oldEnd = oldN.Length;
        var newEnd = newN.Length;
        while (oldEnd > prefix && newEnd > prefix && oldN[oldEnd - 1] == newN[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        var delta = newN.Length - oldN.Length;

        int MapPoint(int point)
        {
            if (point < 0) point = 0;
            if (point > oldN.Length) point = oldN.Length;
            if (point <= prefix) return point;
            if (point >= oldEnd) return Math.Clamp(point + delta, 0, newN.Length);
            return prefix;
        }

        var caret = MapPoint(current.Caret);
        // A caret on an empty document is both "at the start" and "at the end".
        // Keep it at the top when the file is filled in, so a generated document
        // does not open on its last line.
        var followAppend = oldN.Length > 0
                           && current.Caret >= oldN.Length
                           && prefix >= oldN.Length
                           && current.SelectionLength == 0;
        if (followAppend)
            caret = newN.Length;

        var selStartPoint = current.SelectionStart;
        var selEndPoint = current.SelectionStart + Math.Max(0, current.SelectionLength);
        var startInside = selStartPoint > prefix && selStartPoint < oldEnd;
        var endInside = selEndPoint > prefix && selEndPoint < oldEnd;

        int selectionStart;
        int selectionLength;
        if (current.SelectionLength <= 0 || startInside || endInside)
        {
            selectionStart = caret;
            selectionLength = 0;
        }
        else
        {
            var mappedStart = MapPoint(selStartPoint);
            var mappedEnd = MapPoint(selEndPoint);
            selectionStart = Math.Min(mappedStart, mappedEnd);
            selectionLength = Math.Abs(mappedEnd - mappedStart);
        }

        int scroll;
        if (followAppend)
            scroll = LineOf(newN, caret);
        else if (LineOf(oldN, prefix) > current.ScrollLine)
            scroll = Math.Max(0, current.ScrollLine);
        else
            scroll = LineOf(newN, caret);

        return new EditorAnchor(caret, selectionStart, selectionLength, scroll);
    }

    private static int CommonPrefix(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    private static int LineOf(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 0;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n') line++;
        }

        return line;
    }
}
