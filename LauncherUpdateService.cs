using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CIE;

internal sealed record LauncherUpdate(string Version, string PackageUrl, string ChecksumUrl, Version ParsedVersion, string ReleaseNotes, string Source);
public sealed record LauncherReleaseNote(string Version, string Notes, string PublishedLabel, string Url, Version ParsedVersion, string Source);

internal static class LauncherUpdateService
{
    private const string GitHubOwner = "CIE-Admin";
    private const string GitHubRepository = "CIE";
    private const string GiteeOwner = "cieadmin";
    private const string GiteeRepository = "CIE";
    private static readonly HttpClient GitHubClient = CreateClient("CIE-Launcher", "application/vnd.github+json");
    private static readonly HttpClient GiteeClient = CreateClient("CIE-Launcher", "application/json");

    private static HttpClient CreateClient(string product, string accept)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(product, CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return client;
    }

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    public static async Task<List<LauncherReleaseNote>> GetReleaseHistoryAsync(CancellationToken cancellationToken = default)
    {
        Exception? giteeError = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var gitee = await ReadReleaseHistoryAsync(GiteeClient,
                $"https://gitee.com/api/v5/repos/{GiteeOwner}/{GiteeRepository}/releases?per_page=100", "Gitee", timeout.Token);
            if (gitee.Count > 0) return gitee;
        }
        catch (Exception ex) { giteeError = ex; }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var github = await ReadReleaseHistoryAsync(GitHubClient,
                $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases?per_page=100", "GitHub", timeout.Token);
            if (github.Count > 0) return github;
        }
        catch (Exception ex)
        {
            if (giteeError is not null)
                throw new AggregateException("无法读取 CIE 历史更新日志（Gitee 与 GitHub 均失败）。", giteeError, ex);
            throw;
        }

        if (giteeError is not null) throw new InvalidDataException("更新源没有返回可用的历史发行版。", giteeError);
        return [];
    }

    private static async Task<List<LauncherReleaseNote>> ReadReleaseHistoryAsync(
        HttpClient client, string url, string source, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{source} 历史发行版格式无效。");

        var notes = new List<LauncherReleaseNote>();
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (IsDraftOrPrerelease(release)) continue;
            var tag = GetString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tag) || !TryGetVersion(tag, out var version)) continue;
            var body = GetString(release, "body") ?? "";
            var published = GetString(release, "published_at") ?? GetString(release, "created_at") ?? "";
            var url = GetString(release, "html_url") ?? "";
            var publishedLabel = DateTimeOffset.TryParse(published, out var date)
                ? date.ToLocalTime().ToString("yyyy-MM-dd")
                : published;
            notes.Add(new LauncherReleaseNote(version.ToString(), body.Trim(), publishedLabel, url, Normalize(version), source));
        }

        return notes
            .GroupBy(note => Normalize(note.ParsedVersion))
            .Select(group => group.First())
            .OrderByDescending(note => note.ParsedVersion)
            .ToList();
    }

    public static async Task<LauncherUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var releases = new List<LauncherUpdate>();
        var errors = new List<Exception>();

        try
        {
            var gitee = await GetGiteeLatestAsync(cancellationToken);
            if (gitee is not null) releases.Add(gitee);
        }
        catch (Exception ex) { errors.Add(ex); }

        try
        {
            var github = await GetGitHubLatestAsync(cancellationToken);
            if (github is not null) releases.Add(github);
        }
        catch (Exception ex) { errors.Add(ex); }

        if (releases.Count == 0)
        {
            if (errors.Count > 0) throw new AggregateException("无法连接 CIE 更新源（Gitee 与 GitHub 均失败）。", errors);
            return null;
        }

        // Select the newest published version; prefer Gitee when both hosts have the same tag.
        var latest = releases
            .OrderByDescending(update => Normalize(update.ParsedVersion))
            .ThenBy(update => update.Source == "Gitee" ? 0 : 1)
            .First();
        return Normalize(latest.ParsedVersion).CompareTo(Normalize(CurrentVersion)) > 0 ? latest : null;
    }

    private static async Task<LauncherUpdate?> GetGitHubLatestAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases/latest";
        using var response = await GitHubClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (IsDraftOrPrerelease(root)) return null;
        var tag = GetString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag) || !TryGetVersion(tag, out var version)) return null;
        var (packageUrl, checksumUrl) = ReadAssetUrls(root);
        if (string.IsNullOrWhiteSpace(packageUrl) || string.IsNullOrWhiteSpace(checksumUrl))
            throw new InvalidDataException("GitHub Release 缺少启动器更新包或 SHA-256 校验文件。");
        return new LauncherUpdate(version.ToString(), packageUrl, checksumUrl, version, GetString(root, "body") ?? "", "GitHub");
    }

    private static async Task<LauncherUpdate?> GetGiteeLatestAsync(CancellationToken cancellationToken)
    {
        var baseUrl = $"https://gitee.com/api/v5/repos/{GiteeOwner}/{GiteeRepository}";
        using var response = await GiteeClient.GetAsync($"{baseUrl}/releases/latest", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (IsDraftOrPrerelease(root)) return null;
        var tag = GetString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag) || !TryGetVersion(tag, out var version)) return null;

        var (packageUrl, checksumUrl) = ReadAssetUrls(root);
        if (string.IsNullOrWhiteSpace(packageUrl) || string.IsNullOrWhiteSpace(checksumUrl))
        {
            var releaseId = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(releaseId))
                throw new InvalidDataException("Gitee Release 缺少附件信息或发行版 ID。");
            using var assetsResponse = await GiteeClient.GetAsync($"{baseUrl}/releases/{Uri.EscapeDataString(releaseId)}/attach_files", cancellationToken);
            assetsResponse.EnsureSuccessStatusCode();
            using var assetsJson = await JsonDocument.ParseAsync(await assetsResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            (packageUrl, checksumUrl) = ReadAssetUrls(assetsJson.RootElement);
        }
        if (string.IsNullOrWhiteSpace(packageUrl) || string.IsNullOrWhiteSpace(checksumUrl))
            throw new InvalidDataException("Gitee Release 缺少启动器更新包或 SHA-256 校验文件。");
        return new LauncherUpdate(version.ToString(), packageUrl, checksumUrl, version, GetString(root, "body") ?? "", "Gitee");
    }

    private static bool IsDraftOrPrerelease(JsonElement root) =>
        (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
        || (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True);

    private static (string? PackageUrl, string? ChecksumUrl) ReadAssetUrls(JsonElement root)
    {
        JsonElement assets = default;
        var hasAssets = root.ValueKind == JsonValueKind.Array;
        if (!hasAssets && root.TryGetProperty("assets", out var nestedAssets) && nestedAssets.ValueKind == JsonValueKind.Array)
        {
            assets = nestedAssets;
            hasAssets = true;
        }
        if (!hasAssets && root.TryGetProperty("attach_files", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            assets = attachments;
            hasAssets = true;
        }
        if (root.ValueKind == JsonValueKind.Array) assets = root;
        if (!hasAssets) return (null, null);

        string? packageUrl = null;
        string? checksumUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var downloadUrl = GetString(asset, "browser_download_url")
                ?? GetString(asset, "download_url")
                ?? GetString(asset, "url");
            if (string.Equals(name, "CIE-Update.zip", StringComparison.OrdinalIgnoreCase)) packageUrl = downloadUrl;
            if (string.Equals(name, "CIE-Update.zip.sha256", StringComparison.OrdinalIgnoreCase)) checksumUrl = downloadUrl;
        }
        return (packageUrl, checksumUrl);
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool TryGetVersion(string tag, out Version version)
    {
        var value = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(value, out version!);
    }

    private static Version Normalize(Version value) => new(value.Major, value.Minor,
        value.Build < 0 ? 0 : value.Build, value.Revision < 0 ? 0 : value.Revision);

    private static HttpClient GetClient(string source) => source == "Gitee" ? GiteeClient : GitHubClient;

    public static async Task<(string PackagePath, string Sha256)> DownloadAndVerifyAsync(LauncherUpdate update, CancellationToken cancellationToken = default)
    {
        var client = GetClient(update.Source);
        var packagePath = Path.Combine(Path.GetTempPath(), $"CIE-Update-{update.Version}-{Guid.NewGuid():N}.zip");
        var checksumText = await client.GetStringAsync(update.ChecksumUrl, cancellationToken);
        var expectedHash = checksumText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expectedHash is null || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{update.Source} 更新包的 SHA-256 校验文件格式无效。");

        using (var response = await client.GetAsync(update.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
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
