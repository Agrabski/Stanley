using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.Media;
using Avalonia.Platform;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// The fonts lettering can use, on the Avalonia side: installs the ones Stanley ships with
/// (<see cref="Install"/> - into <see cref="Lettering"/>, which draws the page, and into
/// Avalonia's font manager, so the inline text editor and the font box show them too) and
/// maps a family name to the Avalonia <see cref="FontFamily"/> that looks like what the page
/// draws. Stanley ships <see cref="Inter"/> (the Avalonia.Fonts.Inter package: MIT, the
/// font itself under the SIL Open Font License 1.1) as the default lettering font, so a
/// comic looks the same on every computer; every font installed on the computer is offered
/// beside it.
/// </summary>
public static class LetteringFonts
{
    /// <summary>The bundled default lettering font.</summary>
    public const string Inter = "Inter";

    private static readonly Uri InterAssets = new("avares://Avalonia.Fonts.Inter/Assets");
    private static readonly FontFamily InterFamily = new("fonts:Inter#Inter");
    private static readonly Lock Gate = new();
    private static bool _installed;
    private static IReadOnlyList<FontChoice>? _choices;

    /// <summary>Registers the bundled fonts with <see cref="Lettering"/> (once; later calls do nothing) - Inter's every weight, as the default.</summary>
    public static void Install()
    {
        lock (Gate)
        {
            if (_installed)
                return;
            _installed = true;
            _choices = null;
        }

        var loader = new StandardAssetLoader(typeof(InterFontCollection).Assembly);
        foreach (var face in loader.GetAssets(InterAssets, null).Where(u => u.AbsolutePath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = loader.Open(face);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            Lettering.AddBundledFace(Inter, bytes.ToArray(), isDefault: true);
        }
    }

    /// <summary>Installs the bundled fonts and makes them available to Avalonia's text too (the inline editor, the font box).</summary>
    public static AppBuilder WithLetteringFonts(this AppBuilder builder)
    {
        Install();
        return builder.WithInterFont();
    }

    /// <summary>What <paramref name="family"/> is drawn in on the page (itself, or the default when this computer lacks it), as an Avalonia font family.</summary>
    public static FontFamily AvaloniaFamily(string? family)
    {
        var drawn = Lettering.Resolve(family);
        return string.Equals(drawn, Inter, StringComparison.OrdinalIgnoreCase) && Lettering.IsBundled(Inter) ? InterFamily : new FontFamily(drawn);
    }

    /// <summary>Every font the font box offers: the bundled ones first, then the computer's own, by name.</summary>
    public static IReadOnlyList<FontChoice> Choices
    {
        get
        {
            lock (Gate)
                return _choices ??= Lettering.BundledFamilies.Select(f => new FontChoice(f, isBundled: true))
                    .Concat(Lettering.SystemFamilies.Select(f => new FontChoice(f, isBundled: false)))
                    .ToList();
        }
    }

    /// <summary>The font box's entry for <paramref name="family"/> (null: the default font), or null when this computer doesn't have it.</summary>
    public static FontChoice? Find(string? family)
    {
        var name = string.IsNullOrWhiteSpace(family) ? Lettering.DefaultFamily : family.Trim();
        return Choices.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The name to store for a picked font: null for the default lettering font, so a comic keeps following the default.</summary>
    public static string? Stored(string? family) =>
        string.IsNullOrWhiteSpace(family) || string.Equals(family.Trim(), Lettering.DefaultFamily, StringComparison.OrdinalIgnoreCase) ? null : family.Trim();
}

/// <summary>One entry in the font box: a family name, shown in its own face.</summary>
public sealed class FontChoice(string name, bool isBundled)
{

    public string Name { get; } = name;

    /// <summary>Shipped with Stanley, so it looks the same on every computer.</summary>
    public bool IsBundled { get; } = isBundled;

    public bool IsDefault => string.Equals(Name, Lettering.DefaultFamily, StringComparison.OrdinalIgnoreCase);

    /// <summary>The small note beside the name: the default, or built in.</summary>
    public string Note => IsDefault ? "default" : IsBundled ? "built in" : "";

    public FontFamily Preview => field ??= LetteringFonts.AvaloniaFamily(Name);

    public override string ToString() => Name;
}
