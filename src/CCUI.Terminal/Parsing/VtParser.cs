using System.Buffers;
using System.Text;

namespace CCUI.Terminal.Parsing;

/// <summary>
/// A streaming VT/ANSI parser after Paul Williams' DEC state machine (https://vt100.net/emu/dec_ansi_parser),
/// extended with UTF-8 input, colon sub-parameters and BEL-terminated OSC strings.
/// </summary>
public sealed class VtParser
{
    private const int MaxStringLength = 1 << 20;
    private const int Esc = 0x1B;
    private const int Bel = 0x07;

    private readonly IVtHandler _handler;
    private readonly VtParams _params = new();
    private readonly char[] _intermediates = new char[2];
    private readonly StringBuilder _string = new();
    private readonly byte[] _utf8Carry = new byte[4];
    private int _utf8CarryCount;
    private int _intermediateCount;
    private bool _intermediateOverflow;
    private char _privateMarker;
    private char _dcsFinal;
    private bool _stringOverflow;
    private State _state = State.Ground;

    public VtParser(IVtHandler handler) => _handler = handler;

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,
        DcsEntry,
        DcsParam,
        DcsIntermediate,
        DcsPassthrough,
        DcsIgnore,
        IgnoredString,
    }

    /// <summary>Feeds raw bytes. UTF-8 sequences split across calls are carried over.</summary>
    public void Advance(ReadOnlySpan<byte> data)
    {
        if (_utf8CarryCount == 0)
        {
            AdvanceUtf8(data);
            return;
        }

        var length = _utf8CarryCount + data.Length;
        var combined = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            _utf8Carry.AsSpan(0, _utf8CarryCount).CopyTo(combined);
            data.CopyTo(combined.AsSpan(_utf8CarryCount));
            _utf8CarryCount = 0;
            AdvanceUtf8(combined.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(combined);
        }
    }

    public void Advance(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            Advance(rune.Value);
        }
    }

    public void Advance(int c)
    {
        // Transitions that apply in every state.
        if (c == 0x18 || c == 0x1A)
        {
            // CAN and SUB abort the current sequence.
            _handler.Execute(c);
            _state = State.Ground;
            return;
        }

        if (c == Esc)
        {
            switch (_state)
            {
                case State.OscString:
                    // ESC starts the string terminator (ESC \); the backslash is swallowed by the Escape state.
                    DispatchOsc(bellTerminated: false);
                    break;
                case State.DcsPassthrough:
                    DispatchDcs();
                    break;
            }

            ClearSequence();
            _state = State.Escape;
            return;
        }

        switch (_state)
        {
            case State.Ground:
                if (c < 0x20)
                {
                    _handler.Execute(c);
                }
                else if (c != 0x7F && (c < 0x80 || c >= 0xA0))
                {
                    _handler.Print(c);
                }

                break;

            case State.Escape:
                if (c < 0x20)
                {
                    _handler.Execute(c);
                }
                else if (c <= 0x2F)
                {
                    Collect(c);
                    _state = State.EscapeIntermediate;
                }
                else if (c == '[')
                {
                    _state = State.CsiEntry;
                }
                else if (c == ']')
                {
                    _string.Clear();
                    _stringOverflow = false;
                    _state = State.OscString;
                }
                else if (c == 'P')
                {
                    _state = State.DcsEntry;
                }
                else if (c is 'X' or '^' or '_')
                {
                    _state = State.IgnoredString;
                }
                else if (c <= 0x7E)
                {
                    EscDispatch(c);
                }

                break;

            case State.EscapeIntermediate:
                if (c < 0x20)
                {
                    _handler.Execute(c);
                }
                else if (c <= 0x2F)
                {
                    Collect(c);
                }
                else if (c <= 0x7E)
                {
                    EscDispatch(c);
                }

                break;

            case State.CsiEntry:
            case State.DcsEntry:
                var isCsi = _state == State.CsiEntry;
                if (c < 0x20)
                {
                    if (isCsi)
                    {
                        _handler.Execute(c);
                    }
                }
                else if (c <= 0x2F)
                {
                    Collect(c);
                    _state = isCsi ? State.CsiIntermediate : State.DcsIntermediate;
                }
                else if (c <= 0x3B)
                {
                    Param(c);
                    _state = isCsi ? State.CsiParam : State.DcsParam;
                }
                else if (c <= 0x3F)
                {
                    _privateMarker = (char)c;
                    _state = isCsi ? State.CsiParam : State.DcsParam;
                }
                else if (c <= 0x7E)
                {
                    Final(c, isCsi);
                }

                break;

            case State.CsiParam:
            case State.DcsParam:
                isCsi = _state == State.CsiParam;
                if (c < 0x20)
                {
                    if (isCsi)
                    {
                        _handler.Execute(c);
                    }
                }
                else if (c <= 0x2F)
                {
                    Collect(c);
                    _state = isCsi ? State.CsiIntermediate : State.DcsIntermediate;
                }
                else if (c <= 0x3B)
                {
                    Param(c);
                }
                else if (c <= 0x3F)
                {
                    _state = isCsi ? State.CsiIgnore : State.DcsIgnore;
                }
                else if (c <= 0x7E)
                {
                    Final(c, isCsi);
                }

                break;

            case State.CsiIntermediate:
            case State.DcsIntermediate:
                isCsi = _state == State.CsiIntermediate;
                if (c < 0x20)
                {
                    if (isCsi)
                    {
                        _handler.Execute(c);
                    }
                }
                else if (c <= 0x2F)
                {
                    Collect(c);
                }
                else if (c <= 0x3F)
                {
                    _state = isCsi ? State.CsiIgnore : State.DcsIgnore;
                }
                else if (c <= 0x7E)
                {
                    Final(c, isCsi);
                }

                break;

            case State.CsiIgnore:
                if (c < 0x20)
                {
                    _handler.Execute(c);
                }
                else if (c is >= 0x40 and <= 0x7E)
                {
                    _state = State.Ground;
                }

                break;

            case State.OscString:
                if (c == Bel)
                {
                    DispatchOsc(bellTerminated: true);
                    _state = State.Ground;
                }
                else if (c >= 0x20)
                {
                    AppendString(c);
                }

                break;

            case State.DcsPassthrough:
                if (c == Bel)
                {
                    // Not standard, but some programs end DCS with BEL.
                    DispatchDcs();
                    _state = State.Ground;
                }
                else
                {
                    AppendString(c);
                }

                break;

            case State.DcsIgnore:
            case State.IgnoredString:
                if (c == Bel)
                {
                    _state = State.Ground;
                }

                break;
        }
    }

    private void AdvanceUtf8(ReadOnlySpan<byte> data)
    {
        var i = 0;
        while (i < data.Length)
        {
            var b = data[i];
            if (b < 0x80)
            {
                Advance(b);
                i++;
                continue;
            }

            var status = Rune.DecodeFromUtf8(data[i..], out var rune, out var consumed);
            if (status == OperationStatus.NeedMoreData)
            {
                data[i..].CopyTo(_utf8Carry);
                _utf8CarryCount = data.Length - i;
                return;
            }

            // Invalid data decodes to U+FFFD with the offending bytes consumed.
            Advance(rune.Value);
            i += consumed;
        }
    }

    private void ClearSequence()
    {
        _params.Clear();
        _intermediateCount = 0;
        _intermediateOverflow = false;
        _privateMarker = '\0';
    }

    private void Collect(int c)
    {
        if (_intermediateCount < _intermediates.Length)
        {
            _intermediates[_intermediateCount++] = (char)c;
        }
        else
        {
            _intermediateOverflow = true;
        }
    }

    private void Param(int c)
    {
        if (c is >= '0' and <= '9')
        {
            _params.AddDigit(c - '0');
        }
        else
        {
            _params.Separator(colon: c == ':');
        }
    }

    private void EscDispatch(int final)
    {
        _state = State.Ground;
        if (!_intermediateOverflow)
        {
            _handler.EscDispatch(_intermediates.AsSpan(0, _intermediateCount), (char)final);
        }
    }

    private void Final(int final, bool isCsi)
    {
        _params.Finish();
        if (!isCsi)
        {
            _dcsFinal = (char)final;
            _string.Clear();
            _stringOverflow = false;
            _state = State.DcsPassthrough;
            return;
        }

        _state = State.Ground;
        if (!_intermediateOverflow)
        {
            _handler.CsiDispatch(_params, _privateMarker, _intermediates.AsSpan(0, _intermediateCount), (char)final);
        }
    }

    private void AppendString(int c)
    {
        if (_string.Length >= MaxStringLength)
        {
            _stringOverflow = true;
            return;
        }

        if (c < 0x10000)
        {
            _string.Append((char)c);
        }
        else
        {
            _string.Append(char.ConvertFromUtf32(c));
        }
    }

    private void DispatchOsc(bool bellTerminated)
    {
        if (!_stringOverflow)
        {
            _handler.OscDispatch(_string.ToString(), bellTerminated);
        }

        _string.Clear();
    }

    private void DispatchDcs()
    {
        if (!_stringOverflow && !_intermediateOverflow)
        {
            _handler.DcsDispatch(_params, _privateMarker, _intermediates.AsSpan(0, _intermediateCount), _dcsFinal, _string.ToString());
        }

        _string.Clear();
    }
}
