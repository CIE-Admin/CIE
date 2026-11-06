using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CIE;

public sealed class MinecraftVersion
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string Url { get; init; } = "";
    public string ReleaseTime { get; init; } = "";
    public string DisplayName => $"{Id}  ·  {Type}";
}

public sealed class VersionManifest
{
    public LatestVersions Latest { get; init; } = new();
    public List<MinecraftVersion> Versions { get; init; } = [];
}

public sealed class LatestVersions
{
    public string Release { get; init; } = "";
    public string Snapshot { get; init; } = "";
}

public sealed class MinecraftVersionService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<VersionManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await DownloadSource.GetSuccessfulResponseAsync(Client, ManifestUrl, cancellationToken: cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<VersionManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Mojang 返回的版本清单为空。");
    }
}

