using System.Globalization;

namespace Passenger.Xaml.Features;

/// <summary>A colour as XAML writes one: alpha, red, green and blue.</summary>
public readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    /// <summary>As <c>#RRGGBB</c>, or <c>#AARRGGBB</c> when it is not opaque.</summary>
    public string Hex => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>The colours XAML names (Avalonia's and WPF's <c>Colors</c>, the web's), and reading a colour as XAML writes
/// one: a name, or <c>#RGB</c>, <c>#ARGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c>.</summary>
public static class KnownColours
{
    /// <summary>Each name with its <c>#AARRGGBB</c>, in order.</summary>
    public static readonly IReadOnlyList<(string Name, string Value)> Names =
    [
        ("AliceBlue", "#FFF0F8FF"), ("AntiqueWhite", "#FFFAEBD7"), ("Aqua", "#FF00FFFF"), ("Aquamarine", "#FF7FFFD4"), ("Azure", "#FFF0FFFF"),
        ("Beige", "#FFF5F5DC"), ("Bisque", "#FFFFE4C4"), ("Black", "#FF000000"), ("BlanchedAlmond", "#FFFFEBCD"), ("Blue", "#FF0000FF"),
        ("BlueViolet", "#FF8A2BE2"), ("Brown", "#FFA52A2A"), ("BurlyWood", "#FFDEB887"), ("CadetBlue", "#FF5F9EA0"), ("Chartreuse", "#FF7FFF00"),
        ("Chocolate", "#FFD2691E"), ("Coral", "#FFFF7F50"), ("CornflowerBlue", "#FF6495ED"), ("Cornsilk", "#FFFFF8DC"), ("Crimson", "#FFDC143C"),
        ("Cyan", "#FF00FFFF"), ("DarkBlue", "#FF00008B"), ("DarkCyan", "#FF008B8B"), ("DarkGoldenrod", "#FFB8860B"), ("DarkGray", "#FFA9A9A9"),
        ("DarkGreen", "#FF006400"), ("DarkKhaki", "#FFBDB76B"), ("DarkMagenta", "#FF8B008B"), ("DarkOliveGreen", "#FF556B2F"), ("DarkOrange", "#FFFF8C00"),
        ("DarkOrchid", "#FF9932CC"), ("DarkRed", "#FF8B0000"), ("DarkSalmon", "#FFE9967A"), ("DarkSeaGreen", "#FF8FBC8F"), ("DarkSlateBlue", "#FF483D8B"),
        ("DarkSlateGray", "#FF2F4F4F"), ("DarkTurquoise", "#FF00CED1"), ("DarkViolet", "#FF9400D3"), ("DeepPink", "#FFFF1493"), ("DeepSkyBlue", "#FF00BFFF"),
        ("DimGray", "#FF696969"), ("DodgerBlue", "#FF1E90FF"), ("Firebrick", "#FFB22222"), ("FloralWhite", "#FFFFFAF0"), ("ForestGreen", "#FF228B22"),
        ("Fuchsia", "#FFFF00FF"), ("Gainsboro", "#FFDCDCDC"), ("GhostWhite", "#FFF8F8FF"), ("Gold", "#FFFFD700"), ("Goldenrod", "#FFDAA520"),
        ("Gray", "#FF808080"), ("Green", "#FF008000"), ("GreenYellow", "#FFADFF2F"), ("Honeydew", "#FFF0FFF0"), ("HotPink", "#FFFF69B4"),
        ("IndianRed", "#FFCD5C5C"), ("Indigo", "#FF4B0082"), ("Ivory", "#FFFFFFF0"), ("Khaki", "#FFF0E68C"), ("Lavender", "#FFE6E6FA"),
        ("LavenderBlush", "#FFFFF0F5"), ("LawnGreen", "#FF7CFC00"), ("LemonChiffon", "#FFFFFACD"), ("LightBlue", "#FFADD8E6"), ("LightCoral", "#FFF08080"),
        ("LightCyan", "#FFE0FFFF"), ("LightGoldenrodYellow", "#FFFAFAD2"), ("LightGray", "#FFD3D3D3"), ("LightGreen", "#FF90EE90"), ("LightPink", "#FFFFB6C1"),
        ("LightSalmon", "#FFFFA07A"), ("LightSeaGreen", "#FF20B2AA"), ("LightSkyBlue", "#FF87CEFA"), ("LightSlateGray", "#FF778899"), ("LightSteelBlue", "#FFB0C4DE"),
        ("LightYellow", "#FFFFFFE0"), ("Lime", "#FF00FF00"), ("LimeGreen", "#FF32CD32"), ("Linen", "#FFFAF0E6"), ("Magenta", "#FFFF00FF"),
        ("Maroon", "#FF800000"), ("MediumAquamarine", "#FF66CDAA"), ("MediumBlue", "#FF0000CD"), ("MediumOrchid", "#FFBA55D3"), ("MediumPurple", "#FF9370DB"),
        ("MediumSeaGreen", "#FF3CB371"), ("MediumSlateBlue", "#FF7B68EE"), ("MediumSpringGreen", "#FF00FA9A"), ("MediumTurquoise", "#FF48D1CC"),
        ("MediumVioletRed", "#FFC71585"), ("MidnightBlue", "#FF191970"), ("MintCream", "#FFF5FFFA"), ("MistyRose", "#FFFFE4E1"), ("Moccasin", "#FFFFE4B5"),
        ("NavajoWhite", "#FFFFDEAD"), ("Navy", "#FF000080"), ("OldLace", "#FFFDF5E6"), ("Olive", "#FF808000"), ("OliveDrab", "#FF6B8E23"),
        ("Orange", "#FFFFA500"), ("OrangeRed", "#FFFF4500"), ("Orchid", "#FFDA70D6"), ("PaleGoldenrod", "#FFEEE8AA"), ("PaleGreen", "#FF98FB98"),
        ("PaleTurquoise", "#FFAFEEEE"), ("PaleVioletRed", "#FFDB7093"), ("PapayaWhip", "#FFFFEFD5"), ("PeachPuff", "#FFFFDAB9"), ("Peru", "#FFCD853F"),
        ("Pink", "#FFFFC0CB"), ("Plum", "#FFDDA0DD"), ("PowderBlue", "#FFB0E0E6"), ("Purple", "#FF800080"), ("Red", "#FFFF0000"),
        ("RosyBrown", "#FFBC8F8F"), ("RoyalBlue", "#FF4169E1"), ("SaddleBrown", "#FF8B4513"), ("Salmon", "#FFFA8072"), ("SandyBrown", "#FFF4A460"),
        ("SeaGreen", "#FF2E8B57"), ("SeaShell", "#FFFFF5EE"), ("Sienna", "#FFA0522D"), ("Silver", "#FFC0C0C0"), ("SkyBlue", "#FF87CEEB"),
        ("SlateBlue", "#FF6A5ACD"), ("SlateGray", "#FF708090"), ("Snow", "#FFFFFAFA"), ("SpringGreen", "#FF00FF7F"), ("SteelBlue", "#FF4682B4"),
        ("Tan", "#FFD2B48C"), ("Teal", "#FF008080"), ("Thistle", "#FFD8BFD8"), ("Tomato", "#FFFF6347"), ("Transparent", "#00FFFFFF"),
        ("Turquoise", "#FF40E0D0"), ("Violet", "#FFEE82EE"), ("Wheat", "#FFF5DEB3"), ("White", "#FFFFFFFF"), ("WhiteSmoke", "#FFF5F5F5"),
        ("Yellow", "#FFFFFF00"), ("YellowGreen", "#FF9ACD32"),
    ];

    private static readonly Dictionary<string, string> ByName = Names.ToDictionary(n => n.Name, n => n.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>A colour as XAML reads one, or null for text that is none.</summary>
    public static Argb? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }
        string value = text.Trim();
        if (ByName.TryGetValue(value, out string? named))
        {
            value = named;
        }
        if (value.Length < 2 || value[0] != '#' || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint bits))
        {
            return null;
        }
        return (value.Length - 1) switch
        {
            3 => new Argb(255, Twice((bits >> 8) & 0xF), Twice((bits >> 4) & 0xF), Twice(bits & 0xF)),
            4 => new Argb(Twice((bits >> 12) & 0xF), Twice((bits >> 8) & 0xF), Twice((bits >> 4) & 0xF), Twice(bits & 0xF)),
            6 => new Argb(255, (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits),
            8 => new Argb((byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits),
            _ => null,
        };

        static byte Twice(uint nibble) => (byte)(nibble * 17);
    }

    /// <summary>Whether a name is one of the named colours.</summary>
    public static bool IsName(string text) => ByName.ContainsKey(text.Trim());
}
