namespace CCUI.Terminal.Parsing;

/// <summary>Receives the actions of <see cref="VtParser"/>. Spans and <see cref="VtParams"/> are only valid during the call.</summary>
public interface IVtHandler
{
    void Print(int codePoint);

    void Execute(int controlCode);

    void EscDispatch(ReadOnlySpan<char> intermediates, char final);

    void CsiDispatch(VtParams parameters, char privateMarker, ReadOnlySpan<char> intermediates, char final);

    /// <param name="data">The OSC payload, e.g. <c>0;title</c>.</param>
    /// <param name="bellTerminated">True when the sequence ended with BEL rather than ST; replies should mirror it.</param>
    void OscDispatch(string data, bool bellTerminated);

    void DcsDispatch(VtParams parameters, char privateMarker, ReadOnlySpan<char> intermediates, char final, string data);
}
