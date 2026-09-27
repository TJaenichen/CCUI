using System.Windows.Input;

namespace CCUI.Terminal.Wpf;

/// <summary>A key chord that sends fixed text to the process, e.g. Shift+Enter → ESC CR for a newline in Claude Code.</summary>
public sealed record TerminalKeyBinding(Key Key, ModifierKeys Modifiers, string Text)
{
    public bool Matches(Key key, ModifierKeys modifiers) => Key == key && Modifiers == modifiers;
}
