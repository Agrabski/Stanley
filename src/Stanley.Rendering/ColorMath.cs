using SkiaSharp;

namespace Stanley.Rendering;

/// <summary>
/// Recolouring drawn art (docs/sticker-system.md §6.1): an element tagged with a colour
/// slot was painted in some shade of the sticker's default for that slot. Given the slot's
/// new colour, the element keeps its offset from the default - lightness added, chroma
/// scaled, hue turned - in OKLab, so an artist's highlights and shadows survive any
/// recolour. A grey (ink, a white highlight) on a colourful default isn't a shade of it
/// and stays as drawn, so an outline on a tagged shape stays black.
/// </summary>
public static class ColorMath
{
    public static SKColor Recolor(SKColor element, SKColor defaultColor, SKColor newColor)
    {
        if (defaultColor.Red == newColor.Red && defaultColor.Green == newColor.Green && defaultColor.Blue == newColor.Blue)
            return element;
        var (el, ea, eb) = ToOklab(element);
        var (dl, da, db) = ToOklab(defaultColor);
        var (nl, na, nb) = ToOklab(newColor);
        var (ec, eh) = Polar(ea, eb);
        var (dc, dh) = Polar(da, db);
        var (nc, nh) = Polar(na, nb);
        if (ec < Grey && dc >= 2 * Grey)
            return element;

        var l = Math.Clamp(nl + (el - dl), 0, 1);
        // Chroma as a ratio of the default's (a greyer shade stays greyer); a grey default has no ratio to keep.
        var c = dc > Grey ? nc * (ec / dc) : nc + (ec - dc);
        var h = nh + (dc > Grey && ec > Grey ? Wrap(eh - dh) : 0);
        var (r, g, b) = FromOklab(l, c * Math.Cos(h), c * Math.Sin(h));
        return new SKColor(r, g, b, element.Alpha);
    }

    /// <summary>OKLab chroma below which a colour reads as grey.</summary>
    private const double Grey = 0.02;

    private static (double L, double A, double B) ToOklab(SKColor c)
    {
        double Lin(byte v)
        {
            var x = v / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        var (r, g, b) = (Lin(c.Red), Lin(c.Green), Lin(c.Blue));
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static (byte R, byte G, byte B) FromOklab(double L, double a, double b)
    {
        var l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
        byte Gamma(double x)
        {
            x = Math.Clamp(x, 0, 1);
            var v = x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055;
            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
        return (Gamma(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            Gamma(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            Gamma(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    private static (double C, double H) Polar(double a, double b) => (Math.Sqrt(a * a + b * b), Math.Atan2(b, a));

    private static double Wrap(double radians) => Math.IEEERemainder(radians, 2 * Math.PI);
}
