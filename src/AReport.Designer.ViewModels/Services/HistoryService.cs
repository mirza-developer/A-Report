namespace AReport.Designer.Services;

/// <summary>Snapshot-based undo/redo. Snapshots are the serialized design, so history never holds live objects.</summary>
public sealed class HistoryService(int limit = 100)
{
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records the state before a change.</summary>
    public void Push(string snapshot)
    {
        _undo.Add(snapshot);
        if (_undo.Count > limit) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Drops the most recent snapshot (an edit turned out to change nothing).</summary>
    public void DiscardLast()
    {
        if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
    }

    public string? Undo(string current)
    {
        if (!CanUndo) return null;
        _redo.Add(current);
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return previous;
    }

    public string? Redo(string current)
    {
        if (!CanRedo) return null;
        _undo.Add(current);
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return next;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
