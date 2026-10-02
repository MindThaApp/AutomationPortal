namespace Flowmate.Triggers;

internal enum KeyKind { Text, Backspace, Reset, Ignore }

internal readonly record struct KeyInput(KeyKind Kind, string Text = "");

/// <summary>What to do to undo an expansion: caret right, delete the expansion, type the abbreviation back.</summary>
internal sealed record UndoInfo(string Typed, int ExpandedLength, int CursorBack, IntPtr Window, DateTime At);

internal sealed record TypingResult(PhraseMatch? Match = null, UndoInfo? Undo = null);

/// <summary>
/// The pure decision logic behind typed phrases and text expansion: feed it key events, get back matches and undo requests.
/// Not thread-safe; the owner locks.
/// </summary>
internal sealed class TypingEngine
{
    public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(5);
    private readonly PhraseBuffer _buffer = new();
    private readonly Func<DateTime> _now;
    private IReadOnlyList<PhraseDef> _defs = Array.Empty<PhraseDef>();
    private IntPtr _window;
    private UndoInfo? _undo;

    public TypingEngine(Func<DateTime>? now = null) { _now = now ?? (() => DateTime.UtcNow); }

    public long Generation { get; private set; }
    public string BufferText => _buffer.Text;
    public void SetPhrases(IReadOnlyList<PhraseDef> defs) => _defs = defs;
    public void ClearBuffer() => _buffer.Clear();

    public TypingResult Process(KeyInput key, IntPtr window)
    {
        if (window != _window)
        {
            _window = window;
            _buffer.Clear();
            _undo = null;
        }
        if (_undo is not null && _now() - _undo.At > UndoWindow) _undo = null;

        switch (key.Kind)
        {
            case KeyKind.Ignore:
                return new TypingResult();

            case KeyKind.Backspace:
                Generation++;
                if (_undo is { } u)
                {
                    _undo = null;
                    _buffer.Clear();
                    return new TypingResult(Undo: u);
                }
                _buffer.Backspace();
                return new TypingResult();

            case KeyKind.Reset:
                Generation++;
                _undo = null;
                _buffer.Clear();
                return new TypingResult();

            default:
                Generation++;
                _undo = null;
                _buffer.Append(key.Text);
                if (_buffer.FindMatch(_defs) is { } hit)
                {
                    _buffer.Clear();
                    return new TypingResult(Match: new PhraseMatch(hit.Typed, hit.Def.Phrase, Generation, window));
                }
                return new TypingResult();
        }
    }

    /// <summary>Remembers an expansion so an immediate Backspace can undo it.</summary>
    public void NoteExpansion(string typed, int expandedLength, int cursorBack, IntPtr window) =>
        _undo = new UndoInfo(typed, expandedLength, cursorBack, window, _now());

    public void ClearUndo() => _undo = null;
}
