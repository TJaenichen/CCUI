using CCUI.Core.Claude;

namespace CCUI.Core.Tests;

public sealed class TranscriptReaderTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void HoldsBackAPartialLastLine()
    {
        File.WriteAllText(_path, "one\ntw");
        var reader = new TranscriptReader(_path);

        Assert.Equal(["one"], reader.ReadNewLines());

        File.AppendAllText(_path, "o\nthree\n");
        Assert.Equal(["two", "three"], reader.ReadNewLines());
        Assert.Empty(reader.ReadNewLines());
    }

    [Fact]
    public void KeepsMultiByteCharactersSplitAcrossReads()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("ä😀\n");
        File.WriteAllBytes(_path, bytes[..3]);
        var reader = new TranscriptReader(_path);
        Assert.Empty(reader.ReadNewLines());

        using (var stream = new FileStream(_path, FileMode.Append))
        {
            stream.Write(bytes, 3, bytes.Length - 3);
        }

        Assert.Equal(["ä😀"], reader.ReadNewLines());
    }

    [Fact]
    public void StartsOverWhenTheFileShrinks()
    {
        File.WriteAllText(_path, "a long first line\n");
        var reader = new TranscriptReader(_path);
        reader.ReadNewLines();

        File.WriteAllText(_path, "new\n");

        Assert.Equal(["new"], reader.ReadNewLines());
    }

    [Fact]
    public void ReturnsNothingForAMissingFile()
    {
        Assert.Empty(new TranscriptReader(_path + ".missing").ReadNewLines());
    }
}
