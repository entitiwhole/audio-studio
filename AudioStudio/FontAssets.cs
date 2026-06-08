using System.IO;
using System.Windows;
using System.Windows.Media;

namespace AudioStudio;

public static class FontAssets
{
    public const string ResourceKey = "BnoteFont";

    private static readonly string FontFileName = "ofont.ru_Uncage.ttf";
    private static readonly string[] FamilyNames = ["UNCAGE", "Uncage"];

    public static void Register(Application app)
    {
        app.Resources[ResourceKey] = ResolveFontFamily();
    }

    private static FontFamily ResolveFontFamily()
    {
        if (TryPackResource(out var family)) return family;
        if (TryDiskFile(out family)) return family;
        if (TrySystemFont(out family)) return family;
        return new FontFamily("Segoe UI");
    }

    private static bool TryPackResource(out FontFamily family)
    {
        family = default!;
        try
        {
            var packRoot = new Uri("pack://application:,,,/", UriKind.Absolute);
            var fontUri = new Uri(packRoot, $"Fonts/{FontFileName}");
            if (Application.GetResourceStream(fontUri) == null) return false;

            foreach (var name in FamilyNames)
            {
                var candidate = new FontFamily(packRoot, $"./Fonts/#{name}");
                if (IsAvailable(candidate))
                {
                    family = candidate;
                    return true;
                }
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    private static bool TryDiskFile(out FontFamily family)
    {
        family = default!;
        foreach (var path in CandidateFontPaths())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var uri = new Uri(path, UriKind.Absolute);
                foreach (var name in FamilyNames)
                {
                    var candidate = new FontFamily(uri, $"./#{name}");
                    if (IsAvailable(candidate))
                    {
                        family = candidate;
                        return true;
                    }
                }
            }
            catch
            {
                // try next path
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateFontPaths()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        yield return Path.Combine(baseDir, "Fonts", FontFileName);
        yield return Path.Combine(baseDir, FontFileName);

        var devFonts = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fonts", FontFileName));
        yield return devFonts;
    }

    private static bool TrySystemFont(out FontFamily family)
    {
        family = default!;
        foreach (var name in FamilyNames)
        {
            var candidate = new FontFamily(name);
            if (IsAvailable(candidate))
            {
                family = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsAvailable(FontFamily family)
    {
        try
        {
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return typeface.TryGetGlyphTypeface(out _);
        }
        catch
        {
            return false;
        }
    }
}
