using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Hexprite.Services;

/// <summary>
/// Lightweight, self-contained semantic version parser supporting SemVer 2.0.0 precedence.
/// </summary>
public readonly struct SemanticVersion(int major, int minor, int patch, int revision = 0, string preRelease = "", string raw = "") : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public int Major { get; } = major;
    public int Minor { get; } = minor;
    public int Patch { get; } = patch;
    public int Revision { get; } = revision;
    public string PreRelease { get; } = preRelease ?? string.Empty;
    public string Raw { get; } = string.IsNullOrWhiteSpace(raw) ? string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{patch}") : raw;

    public bool IsPrerelease => !string.IsNullOrEmpty(PreRelease);

    public static bool TryParse(string? input, out SemanticVersion result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string s = input.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        // Drop build metadata (+...)
        int plusIdx = s.IndexOf('+', StringComparison.Ordinal);
        if (plusIdx >= 0)
        {
            s = s[..plusIdx];
        }

        // Split pre-release (-...)
        string pre = string.Empty;
        int dashIdx = s.IndexOf('-', StringComparison.Ordinal);
        if (dashIdx >= 0)
        {
            pre = s[(dashIdx + 1)..].Trim();
            s = s[..dashIdx].Trim();
        }

        // Parse numeric components
        string[] parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int major) || major < 0)
        {
            return false;
        }

        int minor = 0;
        if (parts.Length > 1 && (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor) || minor < 0))
        {
            return false;
        }

        int patch = 0;
        if (parts.Length > 2 && (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out patch) || patch < 0))
        {
            return false;
        }

        int revision = 0;
        if (parts.Length > 3 && (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out revision) || revision < 0))
        {
            return false;
        }

        result = new SemanticVersion(major, minor, patch, revision, pre, input.Trim());
        return true;
    }

    public static SemanticVersion Parse(string input)
    {
        if (TryParse(input, out var ver))
        {
            return ver;
        }
        throw new FormatException($"Invalid semantic version: '{input}'");
    }

    public int CompareTo(SemanticVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        if (Revision != other.Revision) return Revision.CompareTo(other.Revision);

        // Pre-release precedence: A version without pre-release is HIGHER than a version with pre-release.
        // e.g. 1.0.0 > 1.0.0-rc.1
        if (string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
            return 1;
        if (!string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease))
            return -1;
        if (string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease))
            return 0;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string a, string b)
    {
        var aParts = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var bParts = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int len = Math.Max(aParts.Length, bParts.Length);

        for (int i = 0; i < len; i++)
        {
            if (i >= aParts.Length) return -1;
            if (i >= bParts.Length) return 1;

            string ap = aParts[i];
            string bp = bParts[i];

            bool aIsNum = int.TryParse(ap, NumberStyles.Integer, CultureInfo.InvariantCulture, out int an);
            bool bIsNum = int.TryParse(bp, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bn);

            if (aIsNum && bIsNum)
            {
                if (an != bn) return an.CompareTo(bn);
            }
            else if (aIsNum)
            {
                return -1; // Numeric has lower precedence than string
            }
            else if (bIsNum)
            {
                return 1;
            }
            else
            {
                int cmp = string.Compare(ap, bp, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
        }

        return 0;
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Revision, PreRelease.ToUpperInvariant());
    public override string ToString() => Raw;
}

public sealed class UpdateService : IUpdateService, IDisposable
{
    private const string DefaultGitHubOwner = "showmik";
    private const string DefaultGitHubRepo = "hexprite";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly IConfiguration? _configuration;
    private UpdateInfo? _lastKnownUpdate;
    private string? _cachedETag;

    public UpdateService()
        : this(configuration: null, new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, ownsHttpClient: true)
    {
    }

    public UpdateService(IConfiguration? configuration)
        : this(configuration, new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, ownsHttpClient: true)
    {
    }

    internal UpdateService(HttpClient httpClient, bool ownsHttpClient = false)
        : this(configuration: null, httpClient, ownsHttpClient)
    {
    }

    internal UpdateService(IConfiguration? configuration, HttpClient httpClient, bool ownsHttpClient = false)
    {
        _configuration = configuration;
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;

        if (_ownsHttpClient)
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Hexprite", GetCurrentVersionNumeric()));
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
    }

    public Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        return CheckForUpdateAsync(force: false, ct);
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(bool force, CancellationToken ct = default)
    {
        using var operation = LoggingService.BeginOperation("UpdateService.CheckForUpdate");

        if (_configuration != null && bool.TryParse(_configuration["Updates:Enabled"], out bool enabled) && !enabled)
        {
            Log.Information("Update check disabled by configuration.");
            return null;
        }

        string owner = _configuration?["Updates:GitHubOwner"] ?? DefaultGitHubOwner;
        string repo = _configuration?["Updates:GitHubRepo"] ?? DefaultGitHubRepo;
        string currentVersion = GetCurrentVersion();
        bool isCurrentPrerelease = SemanticVersion.TryParse(currentVersion, out var currentSemVer) && currentSemVer.IsPrerelease;
        bool includePrereleases = isCurrentPrerelease || UserPreferencesService.Get().IncludePrereleases;

        var apiUrl = new Uri($"https://api.github.com/repos/{owner}/{repo}/releases?per_page=10");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

            string? etag = force ? null : (_cachedETag ?? UserPreferencesService.Get().LastUpdateCheckETag);
            if (!string.IsNullOrWhiteSpace(etag))
            {
                if (EntityTagHeaderValue.TryParse(etag, out var entityTag) ||
                    EntityTagHeaderValue.TryParse(etag.StartsWith('"') ? etag : $"\"{etag}\"", out entityTag))
                {
                    request.Headers.IfNoneMatch.Add(entityTag);
                }
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            // 304 Not Modified -> Up-to-date according to GitHub releases, 0 rate-limit cost
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                Log.Debug("Update check returned 304 Not Modified (ETag matched).");
                if (_lastKnownUpdate != null && IsNewer(currentVersion, _lastKnownUpdate.LatestVersionDisplay))
                {
                    return _lastKnownUpdate;
                }
                return null;
            }

            // Handle rate limiting explicitly
            if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            {
                if (response.Headers.TryGetValues("x-ratelimit-remaining", out var rem) && rem.FirstOrDefault() == "0")
                {
                    Log.Warning("GitHub API rate limit reached during update check.");
                }
                else
                {
                    Log.Warning("GitHub API returned {StatusCode} during update check.", response.StatusCode);
                }
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("Update check returned {StatusCode}", response.StatusCode);
                return null;
            }

            // Cache new ETag
            string? newETag = response.Headers.ETag?.ToString();
            if (!string.IsNullOrWhiteSpace(newETag))
            {
                _cachedETag = newETag;
                UserPreferencesService.Update(p => p.LastUpdateCheckETag = newETag);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement? bestRelease = null;
            SemanticVersion? bestVersion = null;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var releaseElement in root.EnumerateArray())
                {
                    if (TryEvaluateRelease(releaseElement, currentVersion, includePrereleases, out var semVer))
                    {
                        if (bestVersion == null || semVer > bestVersion.Value)
                        {
                            bestVersion = semVer;
                            bestRelease = releaseElement;
                        }
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryEvaluateRelease(root, currentVersion, includePrereleases, out var semVer))
                {
                    bestVersion = semVer;
                    bestRelease = root;
                }
            }

            if (bestRelease == null || bestVersion == null)
            {
                Log.Debug("No newer release found (current: {CurrentVersion})", currentVersion);
                return null;
            }

            var release = bestRelease.Value;
            string tagName = release.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String
                ? tagProp.GetString() ?? string.Empty
                : string.Empty;

            string latestDisplay = tagName.StartsWith('v') || tagName.StartsWith('V') ? tagName[1..] : tagName;
            string latestNumeric = StripPreReleaseSuffix(latestDisplay);

            var assetCandidate = FindInstallerAsset(release);
            string releasePageUrl = release.TryGetProperty("html_url", out var htmlProp) && htmlProp.ValueKind == JsonValueKind.String
                ? htmlProp.GetString() ?? string.Empty
                : $"https://github.com/{owner}/{repo}/releases";

            string? releaseNotes = release.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String
                ? bodyProp.GetString()
                : null;

            DateTimeOffset? publishedAt = null;
            if (release.TryGetProperty("published_at", out var pubProp) &&
                pubProp.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(pubProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                publishedAt = parsedDate;
            }

            bool isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;

            var update = new UpdateInfo(
                LatestVersion: latestNumeric,
                LatestVersionDisplay: latestDisplay,
                CurrentVersion: currentVersion,
                ReleasePageUrl: releasePageUrl,
                InstallerDownloadUrl: assetCandidate.DownloadUrl,
                ReleaseNotes: releaseNotes,
                PublishedAt: publishedAt,
                InstallerFileName: assetCandidate.FileName,
                InstallerSizeBytes: assetCandidate.SizeBytes,
                IsPrerelease: isPrerelease);

            _lastKnownUpdate = update;
            Log.Information("New update available: {LatestVersionDisplay} (current: {CurrentVersion})", update.LatestVersionDisplay, update.CurrentVersion);
            return update;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or TaskCanceledException
                                       or JsonException
                                       or FormatException)
        {
            Log.Warning(ex, "Update check failed silently");
            return null;
        }
    }

    private static bool TryEvaluateRelease(
        JsonElement release,
        string currentVersion,
        bool includePrereleases,
        out SemanticVersion semVer)
    {
        semVer = default;

        if (release.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True)
        {
            return false;
        }

        bool isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;
        if (isPrerelease && !includePrereleases)
        {
            return false;
        }

        if (!release.TryGetProperty("tag_name", out var tagProp) || tagProp.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string tagName = tagProp.GetString() ?? string.Empty;
        string display = tagName.StartsWith('v') || tagName.StartsWith('V') ? tagName[1..] : tagName;

        if (!SemanticVersion.TryParse(display, out semVer))
        {
            if (Version.TryParse(StripPreReleaseSuffix(display), out var v))
            {
                semVer = new SemanticVersion(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision), "", display);
            }
            else
            {
                return false;
            }
        }

        return IsNewer(currentVersion, display);
    }

    private readonly record struct AssetCandidate(string? DownloadUrl, string? FileName, long? SizeBytes);

    private static AssetCandidate FindInstallerAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return default;

        AssetCandidate setupExe = default;
        AssetCandidate generalExe = default;
        AssetCandidate winZip = default;
        AssetCandidate generalZip = default;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameProp) ||
                !asset.TryGetProperty("browser_download_url", out var urlProp) ||
                nameProp.ValueKind != JsonValueKind.String ||
                urlProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string name = nameProp.GetString() ?? string.Empty;
            string url = urlProp.GetString() ?? string.Empty;
            long? size = asset.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                ? sizeProp.GetInt64()
                : null;

            var candidate = new AssetCandidate(url, name, size);

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("install", StringComparison.OrdinalIgnoreCase))
                {
                    setupExe = candidate;
                }
                else if (generalExe.DownloadUrl == null)
                {
                    generalExe = candidate;
                }
            }
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("win", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("x64", StringComparison.OrdinalIgnoreCase))
                {
                    winZip = candidate;
                }
                else if (generalZip.DownloadUrl == null)
                {
                    generalZip = candidate;
                }
            }
        }

        if (setupExe.DownloadUrl != null) return setupExe;
        if (generalExe.DownloadUrl != null) return generalExe;
        if (winZip.DownloadUrl != null) return winZip;
        if (generalZip.DownloadUrl != null) return generalZip;

        return default;
    }

    internal static string StripPreReleaseSuffix(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        string v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V'))
        {
            v = v[1..];
        }

        // Drop build metadata
        int plusIndex = v.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            v = v[..plusIndex];
        }

        int dashIndex = v.IndexOf('-', StringComparison.Ordinal);
        return dashIndex >= 0 ? v[..dashIndex] : v;
    }

    internal static bool IsNewer(string current, string latest)
    {
        if (SemanticVersion.TryParse(current, out var currentSemVer) &&
            SemanticVersion.TryParse(latest, out var latestSemVer))
        {
            return latestSemVer > currentSemVer;
        }

        if (Version.TryParse(StripPreReleaseSuffix(current), out var currentVer) &&
            Version.TryParse(StripPreReleaseSuffix(latest), out var latestVer))
        {
            return latestVer > currentVer;
        }

        return false;
    }

    public static string GetCurrentVersion()
    {
        var infoVersion = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            int plusIdx = infoVersion.IndexOf('+', StringComparison.Ordinal);
            string v = plusIdx >= 0 ? infoVersion[..plusIdx].Trim() : infoVersion.Trim();
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v.StartsWith('v') || v.StartsWith('V') ? v[1..] : v;
            }
        }

        return GetCurrentVersionNumeric();
    }

    internal static string GetCurrentVersionNumeric()
    {
        var version = typeof(UpdateService).Assembly.GetName().Version;
        if (version == null)
            return "0.0.0";

        int build = version.Build >= 0 ? version.Build : 0;
        int minor = version.Minor >= 0 ? version.Minor : 0;
        int major = version.Major >= 0 ? version.Major : 0;

        return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{build}");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}

