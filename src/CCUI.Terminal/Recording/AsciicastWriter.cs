using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CCUI.Terminal.Recording;

/// <summary>
/// Records terminal output as an asciicast v2 file (https://docs.asciinema.org/manual/asciicast/v2/), which the demo
/// mode and tests can replay and asciinema can play.
/// </summary>
public sealed class AsciicastWriter : IDisposable
{
    private readonly TextWriter _writer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly Lock _gate = new();

    public AsciicastWriter(TextWriter writer, int columns, int rows, string? title = null)
    {
        _writer = writer;
        var header = new Dictionary<string, object>
        {
            ["version"] = 2,
            ["width"] = columns,
            ["height"] = rows,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        if (title is not null)
        {
            header["title"] = title;
        }

        _writer.WriteLine(JsonSerializer.Serialize(header));
    }

    public void WriteOutput(ReadOnlySpan<byte> data) => WriteEvent("o", data);

    public void WriteInput(ReadOnlySpan<byte> data) => WriteEvent("i", data);

    public void WriteResize(int columns, int rows)
    {
        lock (_gate)
        {
            WriteLine("r", string.Create(CultureInfo.InvariantCulture, $"{columns}x{rows}"));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    private void WriteEvent(string code, ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            var chars = new char[_decoder.GetCharCount(data, flush: false)];
            _decoder.GetChars(data, chars, flush: false);
            if (chars.Length > 0)
            {
                WriteLine(code, new string(chars));
            }
        }
    }

    private void WriteLine(string code, string payload)
    {
        var time = _clock.Elapsed.TotalSeconds.ToString("0.000000", CultureInfo.InvariantCulture);
        _writer.WriteLine($"[{time}, {JsonSerializer.Serialize(code)}, {JsonSerializer.Serialize(payload)}]");
        _writer.Flush();
    }
}
