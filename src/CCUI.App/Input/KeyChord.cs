using System.Windows.Input;

namespace CCUI.App.Input;

/// <summary>A key with modifiers, parsed from text such as "Ctrl+Shift+D" or "Alt+Left".</summary>
public readonly record struct KeyChord(Key Key, ModifierKeys Modifiers)
{
    private static readonly KeyConverter Keys = new();

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => (ModifierKeys?)null,
            };

            if (modifier is null)
            {
                return false;
            }

            modifiers |= modifier.Value;
        }

        try
        {
            if (Keys.ConvertFromInvariantString(parts[^1]) is Key key)
            {
                chord = new KeyChord(key, modifiers);
                return true;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException)
        {
        }

        return false;
    }

    /// <summary>True when the key event is this chord (Alt combinations arrive as Key.System).</summary>
    public bool Matches(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == Key && Keyboard.Modifiers == Modifiers;
    }
}
