using System.Text;

namespace CCUI.Core.Claude;

/// <summary>
/// Reads a transcript incrementally: each call returns the complete lines appended since the last call. A trailing
/// partial line is held back until its newline arrives. If the file shrinks it is read again from the start.
/// </summary>
public sealed class TranscriptReader
{
    private readonly StringBuilder _partial = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public TranscriptReader(string path) => Path = path;

    public string Path { get; }

    public long Offset { get; private set; }

    public IReadOnlyList<string> ReadNewLines()
    {
        var lines = new List<string>();
        FileStream stream;
        try
        {
            stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return lines;
        }

        using (stream)
        {
            if (stream.Length < Offset)
            {
                Offset = 0;
                _partial.Clear();
                _decoder.Reset();
            }

            stream.Seek(Offset, SeekOrigin.Begin);
            var bytes = new byte[1 << 16];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            int read;
            while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                Offset += read;
                var count = _decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                for (var i = 0; i < count; i++)
                {
                    if (chars[i] == '\n')
                    {
                        var line = _partial.ToString().TrimEnd('\r');
                        _partial.Clear();
                        if (line.Length > 0)
                        {
                            lines.Add(line);
                        }
                    }
                    else
                    {
                        _partial.Append(chars[i]);
                    }
                }
            }
        }

        return lines;
    }
}
