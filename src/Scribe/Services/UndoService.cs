namespace Scribe.Services;

/// <summary>A single reversible edit.</summary>
public sealed class UndoStep
{
    public required string Label { get; init; }
    public required Action Undo { get; init; }
    public required Action Redo { get; init; }
}

/// <summary>
/// Per-page undo history.
///
/// Steps are recorded as paired actions rather than page snapshots: a page of
/// dense handwriting is megabytes of stroke data, and snapshotting every pen
/// lift would cost far more memory than the edits themselves.
/// </summary>
public sealed class UndoService
{
    private readonly Stack<UndoStep> _undo = new();
    private readonly Stack<UndoStep> _redo = new();

    private const int MaxDepth = 200;

    /// <summary>
    /// Set while an undo or redo is being applied. Recording listeners check
    /// this so replayed edits are not captured as brand new steps.
    /// </summary>
    public bool IsApplying { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? Changed;

    public void Record(UndoStep step)
    {
        if (IsApplying) return;

        _undo.Push(step);

        // A new edit invalidates any forward history.
        _redo.Clear();

        if (_undo.Count > MaxDepth) TrimOldest();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Record(string label, Action undo, Action redo) =>
        Record(new UndoStep { Label = label, Undo = undo, Redo = redo });

    public void Undo()
    {
        if (_undo.Count == 0) return;

        var step = _undo.Pop();
        Apply(step.Undo);
        _redo.Push(step);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;

        var step = _redo.Pop();
        Apply(step.Redo);
        _undo.Push(step);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(Action action)
    {
        IsApplying = true;
        try
        {
            action();
        }
        finally
        {
            IsApplying = false;
        }
    }

    private void TrimOldest()
    {
        // Stack has no bottom-removal, so rebuild without the oldest entry.
        var kept = _undo.ToArray();
        _undo.Clear();
        for (int i = kept.Length - 2; i >= 0; i--) _undo.Push(kept[i]);
    }
}
