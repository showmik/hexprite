using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Hexprite.Controllers;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Moq.Protected;
using Xunit;

namespace Hexprite.Tests;

[Collection("WindowLayoutSettingsFile")]
[Trait("Category", "Unit")]
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _testSettingsFile;

    public UpdateServiceTests()
    {
        _testSettingsFile = System.IO.Path.Combine(AppContext.BaseDirectory, "UpdateSettings_" + Guid.NewGuid().ToString("N")[..8] + ".json");
        UserPreferencesService.SetCustomSettingsPath(_testSettingsFile);
    }

    public void Dispose()
    {
        UserPreferencesService.SetCustomSettingsPath(null);
        try { if (System.IO.File.Exists(_testSettingsFile)) System.IO.File.Delete(_testSettingsFile); } catch { }
        try { if (System.IO.File.Exists(_testSettingsFile + ".bak")) System.IO.File.Delete(_testSettingsFile + ".bak"); } catch { }
    }
    [Theory]
    [InlineData("0.2.0-beta", "0.2.0")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("0.1.0-alpha.5+build123", "0.1.0")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void StripPreReleaseSuffix_ReturnsExpectedNumericString(string? input, string expected)
    {
        string actual = UpdateService.StripPreReleaseSuffix(input!);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("0.1.0", "0.2.0", true)]
    [InlineData("0.1.0", "1.0.0", true)]
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.2.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("invalid", "0.2.0", false)]
    [InlineData("0.1.0", "invalid", false)]
    public void IsNewer_ComparesCorrectly(string current, string latest, bool expected)
    {
        bool actual = UpdateService.IsNewer(current, latest);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetCurrentVersionNumeric_ReturnsValidVersionPattern()
    {
        string version = UpdateService.GetCurrentVersionNumeric();
        Assert.NotNull(version);
        Assert.True(Version.TryParse(version, out _), $"'{version}' is not a valid Version string.");
    }

    [Fact]
    public async Task CheckForUpdateAsync_NewerRelease_ReturnsParsedUpdateInfo()
    {
        string json = """
        {
          "tag_name": "v99.0.0-beta",
          "html_url": "https://github.com/showmik/hexprite/releases/tag/v99.0.0-beta",
          "body": "Awesome new features!",
          "published_at": "2026-08-17T12:00:00Z",
          "assets": [
            {
              "name": "Hexprite-Setup.exe",
              "browser_download_url": "https://github.com/showmik/hexprite/releases/download/v99.0.0-beta/Hexprite-Setup.exe"
            }
          ]
        }
        """;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json),
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal("99.0.0", update.LatestVersion);
        Assert.Equal("99.0.0-beta", update.LatestVersionDisplay);
        Assert.Equal("https://github.com/showmik/hexprite/releases/tag/v99.0.0-beta", update.ReleasePageUrl);
        Assert.Equal("https://github.com/showmik/hexprite/releases/download/v99.0.0-beta/Hexprite-Setup.exe", update.InstallerDownloadUrl);
        Assert.Equal("Awesome new features!", update.ReleaseNotes);
        Assert.NotNull(update.PublishedAt);
    }

    [Fact]
    public async Task CheckForUpdateAsync_OlderRelease_ReturnsNull()
    {
        string json = """
        {
          "tag_name": "v0.0.1-beta",
          "html_url": "https://github.com/showmik/hexprite/releases/tag/v0.0.1-beta"
        }
        """;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json),
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_NotFound404_ReturnsNullWithoutThrowing()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_NetworkException_ReturnsNullWithoutThrowing()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network failure"));

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_MalformedJson_ReturnsNullWithoutThrowing()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("invalid json content"),
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

    [Fact]
    public void ShellViewModel_NotifyUpdateAvailable_ShowsBannerWhenNotDismissed()
    {
        var updateInfo = new UpdateInfo(
            LatestVersion: "99.0.0",
            LatestVersionDisplay: "99.0.0-beta",
            CurrentVersion: "0.1.0",
            ReleasePageUrl: "https://example.com",
            InstallerDownloadUrl: null,
            ReleaseNotes: null,
            PublishedAt: null);

        UserPreferencesService.Update(p => p.DismissedUpdateVersion = null);

        var shell = CreateShellViewModel(out _, out _);
        shell.NotifyUpdateAvailable(updateInfo);

        Assert.True(shell.IsUpdateBannerVisible);
        Assert.Same(updateInfo, shell.AvailableUpdate);
    }

    [Fact]
    public void ShellViewModel_NotifyUpdateAvailable_HidesBannerWhenDismissed()
    {
        var updateInfo = new UpdateInfo(
            LatestVersion: "99.0.0",
            LatestVersionDisplay: "99.0.0-beta",
            CurrentVersion: "0.1.0",
            ReleasePageUrl: "https://example.com",
            InstallerDownloadUrl: null,
            ReleaseNotes: null,
            PublishedAt: null);

        UserPreferencesService.Update(p => p.DismissedUpdateVersion = "99.0.0");

        var shell = CreateShellViewModel(out _, out _);
        shell.NotifyUpdateAvailable(updateInfo);

        Assert.False(shell.IsUpdateBannerVisible);
        Assert.Same(updateInfo, shell.AvailableUpdate);
    }

    [Fact]
    public void ShellViewModel_DismissUpdateCommand_HidesBannerAndPersistsDismissedVersion()
    {
        var updateInfo = new UpdateInfo(
            LatestVersion: "99.0.0",
            LatestVersionDisplay: "99.0.0-beta",
            CurrentVersion: "0.1.0",
            ReleasePageUrl: "https://example.com",
            InstallerDownloadUrl: null,
            ReleaseNotes: null,
            PublishedAt: null);

        UserPreferencesService.Update(p => p.DismissedUpdateVersion = null);

        var shell = CreateShellViewModel(out _, out _);
        shell.NotifyUpdateAvailable(updateInfo);
        Assert.True(shell.IsUpdateBannerVisible);

        shell.DismissUpdateCommand.Execute(parameter: null);

        Assert.False(shell.IsUpdateBannerVisible);
        var prefs = UserPreferencesService.Get();
        Assert.Equal("99.0.0", prefs.DismissedUpdateVersion);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0-rc.1", true)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", true)]
    [InlineData("1.0.0-beta", "1.0.0-alpha", true)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("0.2.0-beta", "0.1.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0-rc.1", "1.0.0", false)]
    public void SemanticVersion_Comparison_FollowsSemVerPrecedence(string newer, string older, bool expected)
    {
        bool newerParsed = SemanticVersion.TryParse(newer, out var vNewer);
        bool olderParsed = SemanticVersion.TryParse(older, out var vOlder);

        Assert.True(newerParsed);
        Assert.True(olderParsed);
        Assert.Equal(expected, vNewer > vOlder);
    }

    [Theory]
    [InlineData(14_520_192L, "13.8 MB")]
    [InlineData(850_000L, "830.1 KB")]
    [InlineData(1_500_000_000L, "1.4 GB")]
    [InlineData(null, "")]
    public void UpdateInfo_FormattedSize_ReturnsHumanReadableString(long? bytes, string expected)
    {
        var info = new UpdateInfo(
            LatestVersion: "1.0.0",
            LatestVersionDisplay: "v1.0.0",
            CurrentVersion: "0.1.0",
            ReleasePageUrl: "https://example.com",
            InstallerDownloadUrl: null,
            InstallerSizeBytes: bytes);

        Assert.Equal(expected, info.FormattedSize);
    }

    [Fact]
    public async Task CheckForUpdateAsync_EtagNotModified304_ReturnsNullWithoutThrowing()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotModified,
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();
        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SendsIfNoneMatchHeader_WhenETagExists()
    {
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotModified,
            });

        UserPreferencesService.Update(p => p.LastUpdateCheckETag = "\"12345\"");

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        await service.CheckForUpdateAsync();

        Assert.NotNull(capturedRequest);
        Assert.Contains(capturedRequest.Headers.IfNoneMatch, tag => tag.Tag == "\"12345\"");
    }

    [Fact]
    public async Task CheckForUpdateAsync_RateLimit403_ReturnsNullWithoutThrowing()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();
        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_PrioritizesSetupExeOverZip()
    {
        string json = """
        {
          "tag_name": "v99.0.0",
          "html_url": "https://github.com/showmik/hexprite/releases/tag/v99.0.0",
          "assets": [
            {
              "name": "Hexprite-portable.zip",
              "size": 1024000,
              "browser_download_url": "https://example.com/Hexprite-portable.zip"
            },
            {
              "name": "Hexprite-Setup-x64.exe",
              "size": 14000000,
              "browser_download_url": "https://example.com/Hexprite-Setup-x64.exe"
            }
          ]
        }
        """;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json),
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal("https://example.com/Hexprite-Setup-x64.exe", update.InstallerDownloadUrl);
        Assert.Equal("Hexprite-Setup-x64.exe", update.InstallerFileName);
        Assert.Equal(14000000, update.InstallerSizeBytes);
    }

    [Fact]
    public void GetCurrentVersion_ReturnsValidInformationalVersionPattern()
    {
        string version = UpdateService.GetCurrentVersion();
        Assert.NotNull(version);
        Assert.True(SemanticVersion.TryParse(version, out _), $"'{version}' is not a valid SemanticVersion string.");
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReleasesArray_SelectsHighestNewerRelease()
    {
        string json = """
        [
          {
            "tag_name": "v0.1.0-beta",
            "draft": false,
            "prerelease": true,
            "html_url": "https://github.com/showmik/hexprite/releases/tag/v0.1.0-beta",
            "assets": [
              {
                "name": "Hexprite-Setup-0.1.0-beta-x64.exe",
                "browser_download_url": "https://example.com/setup-0.1.0.exe"
              }
            ]
          },
          {
            "tag_name": "v99.1.0-beta",
            "draft": false,
            "prerelease": true,
            "html_url": "https://github.com/showmik/hexprite/releases/tag/v99.1.0-beta",
            "body": "Latest v99.1.0 release notes",
            "published_at": "2026-08-22T16:00:00Z",
            "assets": [
              {
                "name": "Hexprite-Setup-99.1.0-beta-x64.exe",
                "size": 46000000,
                "browser_download_url": "https://example.com/setup-99.1.0.exe"
              }
            ]
          },
          {
            "tag_name": "v99.0.0-beta",
            "draft": false,
            "prerelease": true,
            "html_url": "https://github.com/showmik/hexprite/releases/tag/v99.0.0-beta",
            "assets": [
              {
                "name": "Hexprite-Setup-99.0.0-beta-x64.exe",
                "browser_download_url": "https://example.com/setup-99.0.0.exe"
              }
            ]
          }
        ]
        """;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json),
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal("99.1.0", update.LatestVersion);
        Assert.Equal("99.1.0-beta", update.LatestVersionDisplay);
        Assert.Equal("https://github.com/showmik/hexprite/releases/tag/v99.1.0-beta", update.ReleasePageUrl);
        Assert.Equal("https://example.com/setup-99.1.0.exe", update.InstallerDownloadUrl);
        Assert.Equal("Latest v99.1.0 release notes", update.ReleaseNotes);
        Assert.True(update.IsPrerelease);
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresDraftReleases()
    {
        string json = """
        [
          {
            "tag_name": "v99.9.9-beta",
            "draft": true,
            "prerelease": true,
            "html_url": "https://github.com/showmik/hexprite/releases/tag/v99.9.9-beta",
            "assets": []
          }
        ]
        """;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json),
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WeakEtagHandling_SendsAndHandlesIfNoneMatch()
    {
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotModified,
            });

        UserPreferencesService.Update(p => p.LastUpdateCheckETag = "W/\"b9f84d70537459b8e52ba28fadf5abe3baebc7cd721e25fb4500b2e0005f26a1\"");

        using var httpClient = new HttpClient(handlerMock.Object);
        using var service = new UpdateService(httpClient);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
        Assert.NotNull(capturedRequest);
        Assert.Contains(capturedRequest.Headers.IfNoneMatch, tag => tag.IsWeak);
    }

    [Fact]
    public async Task ShellViewModel_CheckForUpdatesCommand_WhenNewer_ForcesBannerVisibility()
    {
        var updateInfo = new UpdateInfo(
            LatestVersion: "99.0.0",
            LatestVersionDisplay: "99.0.0-beta",
            CurrentVersion: "0.1.0",
            ReleasePageUrl: "https://example.com",
            InstallerDownloadUrl: null,
            ReleaseNotes: null,
            PublishedAt: null);

        UserPreferencesService.Update(p => p.DismissedUpdateVersion = "99.0.0");

        var updateMock = new Mock<IUpdateService>();
        updateMock.Setup(u => u.CheckForUpdateAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);

        var shell = CreateShellViewModel(out _, out _, updateMock.Object);

        await shell.CheckForUpdatesCommand.ExecuteAsync(parameter: null);

        Assert.True(shell.IsUpdateBannerVisible);
        Assert.Same(updateInfo, shell.AvailableUpdate);
    }

    [Fact]
    public async Task ShellViewModel_CheckForUpdatesCommand_WhenUpToDate_ShowsDialog()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock.Setup(u => u.CheckForUpdateAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UpdateInfo?)null);

        var shell = CreateShellViewModel(out var dialogMock, out _, updateMock.Object);

        await shell.CheckForUpdatesCommand.ExecuteAsync(parameter: null);

        dialogMock.Verify(d => d.ShowMessage(
            It.Is<string>(s => s.Contains("latest version", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string>(),
            It.IsAny<MessageBoxButton>(),
            It.IsAny<MessageBoxImage>()), Times.Once);
    }

    [Fact]
    public void ShellViewModel_DownloadInstallerCommand_CanExecute_OnlyWhenUpdateAvailable()
    {
        var shell = CreateShellViewModel(out _, out _);
        Assert.False(shell.DownloadInstallerCommand.CanExecute(null));

        var updateInfo = new UpdateInfo(
            LatestVersion: "99.0.0",
            LatestVersionDisplay: "v99.0.0",
            CurrentVersion: "0.1.0",
            ReleasePageUrl: "https://example.com",
            InstallerDownloadUrl: "https://example.com/setup.exe");

        shell.NotifyUpdateAvailable(updateInfo);
        Assert.True(shell.DownloadInstallerCommand.CanExecute(null));
    }

    private static ShellViewModel CreateShellViewModel(
        out Mock<IDialogService> dialogMock,
        out Mock<IServiceProvider> serviceProviderMock,
        IUpdateService? updateService = null)
    {
        var autosaveMock = new Mock<IAutosaveService>();
        serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

        var codeGenMock = new Mock<ICodeGeneratorService>();
        var drawingMock = new Mock<IDrawingService>();
        var clipboardMock = new Mock<IClipboardService>();
        var pixelClipboardMock = new Mock<IPixelClipboardService>();
        dialogMock = new Mock<IDialogService>();
        var themeMock = new Mock<IThemeService>();
        var bugReportMock = new Mock<IBugReportService>();
        var feedbackMock = new Mock<IUserFeedbackService>();
        var controllerFactory = new ControllerFactory();
        var exportMock = new Mock<IExportService>();
        var importExportMock = new Mock<IFileImportExportService>();
        var hardwarePreviewMock = new Mock<IHardwarePreviewService>();

        return new ShellViewModel(
            codeGenMock.Object,
            drawingMock.Object,
            clipboardMock.Object,
            pixelClipboardMock.Object,
            dialogMock.Object,
            themeMock.Object,
            bugReportMock.Object,
            feedbackMock.Object,
            controllerFactory,
            exportMock.Object,
            importExportMock.Object,
            hardwarePreviewMock.Object,
            serviceProviderMock.Object,
            updateService);
    }
}
