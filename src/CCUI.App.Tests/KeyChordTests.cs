using System.Windows.Input;
using CCUI.App.Input;

namespace CCUI.App.Tests;

public sealed class KeyChordTests
{
    [Theory]
    [InlineData("Alt+Left", Key.Left, ModifierKeys.Alt)]
    [InlineData("Ctrl+Shift+D", Key.D, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("shift+enter", Key.Enter, ModifierKeys.Shift)]
    [InlineData("F5", Key.F5, ModifierKeys.None)]
    public void ParsesChords(string text, Key key, ModifierKeys modifiers)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(key, modifiers), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hyper+X")]
    [InlineData("Ctrl+NoSuchKey")]
    public void RejectsInvalidChords(string text) => Assert.False(KeyChord.TryParse(text, out _));
}
