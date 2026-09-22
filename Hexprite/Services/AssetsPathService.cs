using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Serilog;

namespace Hexprite.Services
{
    /// <summary>
    /// Resolves shipped on-disk asset folders (HexpritePreview library, preview sketches).
    /// Extracts embedded assets to %AppData%/Hexprite/Assets/ for fully portable and installed execution.
    /// </summary>
    public static class AssetsPathService
    {
        public const string HexpritePreviewFolderName = "HexpritePreview";
        public const string HexpritePreviewStandaloneFolderName = "HexpritePreview-Standalone-Arduino";
        public const string HexpritePreviewPlatformIOFolderName = "HexpritePreview-Standalone-PlatformIO";
        internal const string StandaloneSketchFileName = "HexpritePreview-Standalone-Arduino.ino";
        private const string PlatformIOFileName = "platformio.ini";
        private const string EmbeddedResourcePrefix = "EmbeddedAssets/";

        private static readonly Lock ExtractLock = new();

        /// <summary>
        /// Gets the platform-independent AppData directory for extracted Hexprite assets.
        /// </summary>
        public static string AppDataAssetsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite", "Assets");

        /// <summary>
        /// Returns the absolute path to the HexpritePreview Arduino library folder, or null if not found.
        /// </summary>
        public static string? ResolveHexpritePreviewLibraryPath()
        {
            foreach (string? candidate in EnumerateHexpritePreviewCandidates())
            {
                if (candidate != null && IsValidHexpritePreviewLibrary(candidate))
                {
                    return candidate;
                }
            }

            // Fallback: Attempt extraction if missing, then re-check AppData path
            EnsureAssetsExtracted();
            string appDataPath = Path.Combine(AppDataAssetsDirectory, HexpritePreviewFolderName);
            if (IsValidHexpritePreviewLibrary(appDataPath))
            {
                return appDataPath;
            }

            return null;
        }

        /// <summary>
        /// Returns the absolute path to the standalone (no-library) HexpritePreview Arduino sketch folder, or null if not found.
        /// </summary>
        public static string? ResolveHexpritePreviewStandalonePath()
        {
            foreach (string? candidate in EnumerateAssetCandidates(HexpritePreviewStandaloneFolderName))
            {
                if (candidate != null && IsValidHexpritePreviewStandalone(candidate))
                {
                    return candidate;
                }
            }

            // Fallback: Attempt extraction if missing, then re-check AppData path
            EnsureAssetsExtracted();
            string appDataPath = Path.Combine(AppDataAssetsDirectory, HexpritePreviewStandaloneFolderName);
            if (IsValidHexpritePreviewStandalone(appDataPath))
            {
                return appDataPath;
            }

            return null;
        }

        /// <summary>
        /// Returns the absolute path to the standalone PlatformIO preview project folder, or null if not found.
        /// </summary>
        public static string? ResolveHexpritePreviewPlatformIOPath()
        {
            foreach (string? candidate in EnumerateAssetCandidates(HexpritePreviewPlatformIOFolderName))
            {
                if (candidate != null && IsValidHexpritePreviewPlatformIO(candidate))
                {
                    return candidate;
                }
            }

            // Fallback: Attempt extraction if missing, then re-check AppData path
            EnsureAssetsExtracted();
            string appDataPath = Path.Combine(AppDataAssetsDirectory, HexpritePreviewPlatformIOFolderName);
            if (IsValidHexpritePreviewPlatformIO(appDataPath))
            {
                return appDataPath;
            }

            return null;
        }

        /// <summary>
        /// Ensures all embedded preview assets are extracted to the user's AppData assets directory.
        /// Extracts only if missing, if the version stamp is outdated, or if force is true.
        /// </summary>
        public static void EnsureAssetsExtracted(bool force = false)
        {
            try
            {
                string targetDir = AppDataAssetsDirectory;
                string versionFile = Path.Combine(targetDir, ".version");
                string currentVersion = typeof(AssetsPathService).Assembly.GetName().Version?.ToString() ?? "0.1.0";

                string libraryIndicator = Path.Combine(targetDir, HexpritePreviewFolderName, "library.json");
                string standaloneIndicator = Path.Combine(targetDir, HexpritePreviewStandaloneFolderName, StandaloneSketchFileName);

                if (!force &&
                    File.Exists(versionFile) &&
                    File.Exists(libraryIndicator) &&
                    File.Exists(standaloneIndicator))
                {
                    string existingVersion = File.ReadAllText(versionFile).Trim();
                    if (string.Equals(existingVersion, currentVersion, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                lock (ExtractLock)
                {
                    if (!force &&
                        File.Exists(versionFile) &&
                        File.Exists(libraryIndicator) &&
                        File.Exists(standaloneIndicator))
                    {
                        string existingVersion = File.ReadAllText(versionFile).Trim();
                        if (string.Equals(existingVersion, currentVersion, StringComparison.Ordinal))
                        {
                            return;
                        }
                    }

                    Directory.CreateDirectory(targetDir);

                    var assembly = typeof(AssetsPathService).Assembly;
                    string[] resourceNames = assembly.GetManifestResourceNames();

                    foreach (string resName in resourceNames)
                    {
                        string relativePath;
                        if (resName.StartsWith(EmbeddedResourcePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = resName[EmbeddedResourcePrefix.Length..];
                        }
                        else
                        {
                            continue;
                        }

                        // Normalize slashes to OS separator
                        string normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                                                 .Replace('\\', Path.DirectorySeparatorChar)
                                                                 .TrimStart(Path.DirectorySeparatorChar);

                        string destinationFilePath = Path.Combine(targetDir, normalizedRelative);
                        string? destinationDirectory = Path.GetDirectoryName(destinationFilePath);
                        if (!string.IsNullOrEmpty(destinationDirectory))
                        {
                            Directory.CreateDirectory(destinationDirectory);
                        }

                        using Stream? resourceStream = assembly.GetManifestResourceStream(resName);
                        if (resourceStream != null)
                        {
                            using FileStream fileStream = File.Create(destinationFilePath);
                            resourceStream.CopyTo(fileStream);
                        }
                    }

                    File.WriteAllText(versionFile, currentVersion);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to extract embedded assets to AppData directory");
            }
        }

        internal static IEnumerable<string?> EnumerateHexpritePreviewCandidates()
            => EnumerateAssetCandidates(HexpritePreviewFolderName);

        private static IEnumerable<string?> EnumerateAssetCandidates(string folderName)
        {
            // 1. AppData directory (extracted and user-writable)
            yield return Path.Combine(AppDataAssetsDirectory, folderName);

            string baseDirectory = AppContext.BaseDirectory;

            // 2. Installed / debug output: Assets/<folder> beside the exe
            yield return Path.Combine(baseDirectory, "Assets", folderName);

            // 3. Legacy dev fallback used by OpenSketchFolderCommand
            yield return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "Assets", folderName));

            // 4. Walk up from output dir looking for Hexprite/Assets/<folder> (source tree)
            string? current = baseDirectory;
            for (int i = 0; i < 8 && current != null; i++)
            {
                yield return Path.Combine(current, "Assets", folderName);

                string projectAssets = Path.Combine(current, "Hexprite", "Assets", folderName);
                yield return projectAssets;

                current = Directory.GetParent(current)?.FullName;
            }
        }

        public static bool IsValidHexpritePreviewLibrary(string path)
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            return File.Exists(Path.Combine(path, "library.json"))
                && File.Exists(Path.Combine(path, "src", "HexpritePreview.h"));
        }

        public static bool IsValidHexpritePreviewStandalone(string path)
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            return File.Exists(Path.Combine(path, StandaloneSketchFileName));
        }

        public static bool IsValidHexpritePreviewPlatformIO(string path)
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            return File.Exists(Path.Combine(path, PlatformIOFileName));
        }
    }
}
