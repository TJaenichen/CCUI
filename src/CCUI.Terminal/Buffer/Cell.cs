namespace CCUI.Terminal.Buffer;

public enum CellWidth : byte
{
    /// <summary>A single-column cell. <c>default(Cell)</c> is a blank normal cell.</summary>
    Normal = 0,

    /// <summary>The left half of a two-column character; it carries the text.</summary>
    WideLead = 1,

    /// <summary>The right half of a two-column character; it carries no text.</summary>
    WideTrail = 2,
}

/// <summary>One screen cell: a grapheme cluster (null for blank) with its style.</summary>
public readonly record struct Cell(string? Text, CellStyle Style, CellWidth Width = CellWidth.Normal)
{
    public static Cell Blank(CellStyle style) => new(null, style);

    public bool IsBlank => Text is null || Text == " ";

    public string DisplayText => Text ?? " ";
}
