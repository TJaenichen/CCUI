using CCUI.Terminal.Input;

namespace CCUI.Terminal.Tests;

public sealed class KeyEncoderTests
{
    [Theory]
    [InlineData(TerminalKey.Up, KeyModifiers.None, false, "\e[A")]
    [InlineData(TerminalKey.Up, KeyModifiers.None, true, "\eOA")]
    [InlineData(TerminalKey.Left, KeyModifiers.Control, false, "\e[1;5D")]
    [InlineData(TerminalKey.Right, KeyModifiers.Shift | KeyModifiers.Alt, true, "\e[1;4C")]
    [InlineData(TerminalKey.Home, KeyModifiers.None, false, "\e[H")]
    [InlineData(TerminalKey.Delete, KeyModifiers.None, false, "\e[3~")]
    [InlineData(TerminalKey.PageUp, KeyModifiers.Control, false, "\e[5;5~")]
    [InlineData(TerminalKey.F1, KeyModifiers.None, false, "\eOP")]
    [InlineData(TerminalKey.F5, KeyModifiers.None, false, "\e[15~")]
    [InlineData(TerminalKey.F12, KeyModifiers.Shift, false, "\e[24;2~")]
    [InlineData(TerminalKey.Enter, KeyModifiers.None, false, "\r")]
    [InlineData(TerminalKey.Enter, KeyModifiers.Alt, false, "\e\r")]
    [InlineData(TerminalKey.Tab, KeyModifiers.Shift, false, "\e[Z")]
    [InlineData(TerminalKey.Backspace, KeyModifiers.None, false, "\x7f")]
    [InlineData(TerminalKey.Backspace, KeyModifiers.Control, false, "\b")]
    [InlineData(TerminalKey.Escape, KeyModifiers.None, false, "\e")]
    public void EncodesKeys(TerminalKey key, KeyModifiers modifiers, bool applicationCursor, string expected)
    {
        Assert.Equal(expected, KeyEncoder.Encode(key, modifiers, applicationCursor));
    }

    [Theory]
    [InlineData('c', false, "\x03")]
    [InlineData('A', false, "\x01")]
    [InlineData(' ', false, "\0")]
    [InlineData('[', false, "\e")]
    [InlineData('r', true, "\e\x12")]
    public void EncodesControlCharacters(char c, bool alt, string expected)
    {
        Assert.Equal(expected, KeyEncoder.EncodeControl(c, alt));
    }

    [Fact]
    public void HasNoControlCharacterForDigitsOutsideTheXtermSet()
    {
        Assert.Null(KeyEncoder.EncodeControl('1', alt: false));
    }

    [Fact]
    public void PasteNormalisesNewlinesAndBrackets()
    {
        Assert.Equal("a\rb\rc", KeyEncoder.EncodePaste("a\r\nb\nc", bracketed: false));
        Assert.Equal("\e[200~ab\e[201~", KeyEncoder.EncodePaste("a\eb", bracketed: true));
    }

    [Fact]
    public void AltPrefixesTextWithEscape()
    {
        Assert.Equal("\ex", KeyEncoder.EncodeText("x", alt: true));
    }
}
