namespace CCUI.Terminal.Buffer;

/// <summary>A bounded ring of lines that scrolled off the top of the screen. Index 0 is the oldest line.</summary>
public sealed class Scrollback
{
    private TerminalLine[] _lines;
    private int _start;

    public Scrollback(int capacity)
    {
        Capacity = Math.Max(0, capacity);
        _lines = new TerminalLine[Math.Min(Capacity, 256)];
    }

    public int Capacity { get; }

    public int Count { get; private set; }

    public TerminalLine this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _lines[(_start + index) % _lines.Length];
        }
    }

    public void Add(TerminalLine line)
    {
        if (Capacity == 0)
        {
            return;
        }

        if (Count < _lines.Length)
        {
            _lines[(_start + Count) % _lines.Length] = line;
            Count++;
            return;
        }

        if (_lines.Length < Capacity)
        {
            Grow();
            _lines[Count++] = line;
            return;
        }

        // Full: overwrite the oldest line.
        _lines[_start] = line;
        _start = (_start + 1) % _lines.Length;
    }

    public void Clear()
    {
        Array.Clear(_lines);
        _start = 0;
        Count = 0;
    }

    private void Grow()
    {
        var bigger = new TerminalLine[Math.Min(Capacity, _lines.Length * 2)];
        for (var i = 0; i < Count; i++)
        {
            bigger[i] = _lines[(_start + i) % _lines.Length];
        }

        _lines = bigger;
        _start = 0;
    }
}
