namespace CCUI.Terminal.Wpf;

/// <summary>How bold ("intense") text is shown; same values as Windows Terminal's intenseTextStyle.</summary>
public enum IntenseTextStyle
{
    /// <summary>Bold font and the bright variant of palette colours 0-7.</summary>
    All,

    /// <summary>Bold font only.</summary>
    Bold,

    /// <summary>Bright colours only.</summary>
    Bright,

    /// <summary>No distinction.</summary>
    None,
}
