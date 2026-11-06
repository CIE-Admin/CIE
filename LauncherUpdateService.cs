using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CIE;

internal sealed record LauncherUpdate(string Version, string PackageUrl, string ChecksumUrl, Version ParsedVersion);

internal static class LauncherUpdateService
{
    private const string Owner = "CIE-Admin";
    private const string Repository = "CIE";
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CIE-Launcher", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    public static async Task<LauncherUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
        using var response = await Client.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new HttpRequestException("GitHub 仓库尚未发布过 CIE 版本。", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var versionText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var version)) return null;
        if (Normalize(version).CompareTo(Normalize(CurrentVersion)) <= 0) return null;

        string? packageUrl = null;
        string? checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (string.Equals(name, "CIE-Update.zip", StringComparison.OrdinalIgnoreCase)) packageUrl = downloadUrl;
            if (string.Equals(name, "CIE-Update.zip.sha256", StringComparison.OrdinalIgnoreCase)) checksumUrl = downloadUrl;
        }
        if (string.IsNullOrWhiteSpace(packageUrl) || string.IsNullOrWhiteSpace(checksumUrl))
            throw new InvalidDataException("GitHub Release 缺少启动器更新包或 SHA-256 校验文件。");
        return new LauncherUpdate(version.ToString(), packageUrl, checksumUrl, version);
    }

    private static Version Normalize(Version value) => new(value.Major, value.Minor,
        value.Build < 0 ? 0 : value.Build, value.Revision < 0 ? 0 : value.Revision);

    public static async Task<(string PackagePath, string Sha256)> DownloadAndVerifyAsync(LauncherUpdate update, CancellationToken cancellationToken = default)
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"CIE-Update-{update.Version}-{Guid.NewGuid():N}.zip");
        var checksumText = await Client.GetStringAsync(update.ChecksumUrl, cancellationToken);
        var expectedHash = checksumText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expectedHash is null || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("GitHub 更新包的 SHA-256 校验文件格式无效。");

        using (var response = await Client.GetAsync(update.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(destination, cancellationToken);
        }

        try
        {
            await using var file = File.OpenRead(packagePath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("启动器更新包校验失败，文件可能在下载中损坏；没有修改当前启动器。");
            return (packagePath, actualHash);
        }
        catch
        {
            try { File.Delete(packagePath); } catch { }
            throw;
        }
    }
}
