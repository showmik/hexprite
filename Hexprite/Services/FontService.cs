using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>
    /// Discovers and loads fonts for the Text Tool.
    /// Sources: bundled fonts (Assets/Fonts/) and user custom fonts (%AppData%/Hexprite/Fonts/).
    /// </summary>
    public static class FontService
    {
        private static readonly string CustomFontsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite", "Fonts");

        /// <summary>
        /// Loads all available fonts: bundled first, then custom.
        /// </summary>
        public static List<FontEntry> LoadAllFonts()
        {
            var fonts = new List<FontEntry>();

            // 1. Load bundled fonts from embedded resources
            LoadBundledFonts(fonts);

            // 2. Load custom fonts from user directory
            LoadCustomFonts(fonts);

            return fonts;
        }

        /// <summary>
        /// Loads fonts embedded as resources in Assets/Fonts/.
        /// </summary>
        private static void LoadBundledFonts(List<FontEntry> fonts)
        {
            try
            {
                var bundledUri = new Uri("pack://application:,,,/Assets/Fonts/");
                var families = Fonts.GetFontFamilies(bundledUri);

                foreach (var family in families)
                {
                    string name = GetFontDisplayName(family);
                    fonts.Add(new FontEntry(name, family, family.Source, isBuiltIn: true));
                }
            }
            catch
            {
                // No bundled fonts or invalid resource path — that's fine
            }
        }

        /// <summary>
        /// Loads fonts from the user's custom fonts directory.
        /// </summary>
        private static void LoadCustomFonts(List<FontEntry> fonts)
        {
            try
            {
                if (!Directory.Exists(CustomFontsDirectory)) return;

                var families = Fonts.GetFontFamilies(CustomFontsDirectory);

                foreach (var family in families)
                {
                    string name = GetFontDisplayName(family);
                    fonts.Add(new FontEntry(name, family, family.Source, isBuiltIn: false));
                }
            }
            catch
            {
                // Failed to read custom fonts directory — silently ignore
            }
        }

        /// <summary>
        /// Opens the custom fonts folder in Explorer, creating it if needed.
        /// </summary>
        public static void OpenCustomFontsFolder()
        {
            try
            {
                Directory.CreateDirectory(CustomFontsDirectory);
                Process.Start(new ProcessStartInfo(CustomFontsDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "FontService.OpenCustomFontsFolder");
            }
        }

        /// <summary>
        /// Extracts a human-readable display name from a FontFamily.
        /// </summary>
        private static string GetFontDisplayName(FontFamily family)
        {
            // Try to get the English name from FamilyNames
            if (family.FamilyNames.Count > 0)
            {
                // Prefer en-US name
                var enUs = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
                if (family.FamilyNames.TryGetValue(enUs, out string? enName))
                    return enName;

                // Fall back to first available name
                return family.FamilyNames.Values.First();
            }

            // Fall back to parsing the Source string
            string source = family.Source;
            int hashIdx = source.LastIndexOf('#');
            if (hashIdx >= 0 && hashIdx < source.Length - 1)
                return source[(hashIdx + 1)..];

            return source;
        }
    }
}
