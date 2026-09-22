using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Hexprite.Services;

/// <summary>
/// Information about an available update from GitHub Releases.
/// </summary>
public sealed record UpdateInfo(
    string LatestVersion,
    string LatestVersionDisplay,
    string CurrentVersion,
    string ReleasePageUrl,
    string? InstallerDownloadUrl,
    string? ReleaseNotes = null,
    DateTimeOffset? PublishedAt = null,
    string? InstallerFileName = null,
    long? InstallerSizeBytes = null,
    bool IsPrerelease = false)
{
    /// <summary>
    /// Returns a human-friendly size string (e.g. "14.2 MB", "850 KB").
    /// </summary>
    public string FormattedSize
    {
        get
        {
            if (!InstallerSizeBytes.HasValue || InstallerSizeBytes.Value <= 0)
            {
                return string.Empty;
            }

            double bytes = InstallerSizeBytes.Value;
            if (bytes >= 1024 * 1024 * 1024)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024 * 1024 * 1024):0.0} GB");
            }
            if (bytes >= 1024 * 1024)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024 * 1024):0.0} MB");
            }
            if (bytes >= 1024)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024:0.0} KB");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }
    }
}

public interface IUpdateService
{
    /// <summary>
    /// Checks GitHub for a newer release. Returns null if up-to-date or on error.
    /// Never throws — all failures are logged and swallowed.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks GitHub for a newer release with optional forced cache invalidation.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync(bool force, CancellationToken ct = default);
}

