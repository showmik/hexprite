using System;
using System.IO;

namespace Hexprite.ViewModels
{
    /// <summary>
    /// Represents a recently opened file on the Quick Start welcome screen.
    /// </summary>
    public class RecentFileItem
    {
        public string FullPath { get; } = string.Empty;
        public string FileName { get; } = string.Empty;
        public string DirectoryPath { get; } = string.Empty;
        public string FormatBadge { get; } = "SPRITE";
        public string RelativeTime { get; } = string.Empty;

        public RecentFileItem(string fullPath)
        {
            FullPath = fullPath ?? string.Empty;
            FileName = Path.GetFileName(fullPath) ?? string.Empty;
            DirectoryPath = Path.GetDirectoryName(fullPath) ?? string.Empty;

            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            FormatBadge = ext switch
            {
                ".hexpack" => "PACK",
                ".hexfont" or ".hexpfont" => "FONT",
                _ => "SPRITE"
            };

            if (File.Exists(fullPath))
            {
                try
                {
                    var lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);
                    var age = DateTime.UtcNow - lastWriteUtc;
                    if (age.TotalMinutes < 1)
                    {
                        RelativeTime = "Just now";
                    }
                    else if (age.TotalMinutes < 60)
                    {
                        RelativeTime = $"{(int)age.TotalMinutes}m ago";
                    }
                    else if (age.TotalHours < 24)
                    {
                        RelativeTime = $"{(int)age.TotalHours}h ago";
                    }
                    else if (age.TotalDays < 2)
                    {
                        RelativeTime = "Yesterday";
                    }
                    else if (age.TotalDays < 7)
                    {
                        RelativeTime = $"{(int)age.TotalDays}d ago";
                    }
                    else
                    {
                        RelativeTime = File.GetLastWriteTime(fullPath).ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    RelativeTime = string.Empty;
                }
            }
            else
            {
                RelativeTime = string.Empty;
            }
        }
    }
}
