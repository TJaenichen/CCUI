namespace CCUI.Terminal.Parsing;

/// <summary>
/// Numeric parameters of a CSI or DCS sequence. An omitted parameter reads as -1. Colon-separated
/// sub-parameters (e.g. <c>38:2::255:0:0</c>) are kept in order and flagged via <see cref="IsSubParameter"/>.
/// </summary>
public sealed class VtParams
{
    public const int MaxCount = 32;
    private const int MaxValue = 65535;

    private readonly int[] _values = new int[MaxCount];
    private readonly bool[] _isSub = new bool[MaxCount];
    private int _current = -1;
    private bool _currentIsSub;
    private bool _pending;

    public int Count { get; private set; }

    public int this[int index] => index < Count ? _values[index] : -1;

    public bool IsSubParameter(int index) => index < Count && _isSub[index];

    /// <summary>The value at <paramref name="index"/>, or <paramref name="defaultValue"/> when it is omitted.</summary>
    public int Get(int index, int defaultValue) => this[index] is >= 0 and var v ? v : defaultValue;

    /// <summary>Like <see cref="Get"/>, but a zero also means "use the default" (as for cursor movement counts).</summary>
    public int GetNonZero(int index, int defaultValue) => this[index] is > 0 and var v ? v : defaultValue;

    public override string ToString()
    {
        var parts = new string[Count];
        for (var i = 0; i < Count; i++)
        {
            parts[i] = (i > 0 ? (_isSub[i] ? ":" : ";") : string.Empty) + (_values[i] < 0 ? string.Empty : _values[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return string.Concat(parts);
    }

    internal void Clear()
    {
        Count = 0;
        _current = -1;
        _currentIsSub = false;
        _pending = false;
    }

    internal void AddDigit(int digit)
    {
        _pending = true;
        _current = _current < 0 ? digit : Math.Min(MaxValue, (_current * 10) + digit);
    }

    internal void Separator(bool colon)
    {
        Push();
        _pending = true;
        _currentIsSub = colon;
    }

    internal void Finish()
    {
        if (_pending)
        {
            Push();
        }
    }

    private void Push()
    {
        if (Count < MaxCount)
        {
            _values[Count] = _current;
            _isSub[Count] = _currentIsSub;
            Count++;
        }

        _current = -1;
        _currentIsSub = false;
        _pending = false;
    }
}
