using System.Net.Http;

namespace CIE;

public static class DownloadSource
{
    private static string _mode = "official";

    public static string Mode
    {
        get => Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, value is "bmclapi" or "tuna" ? value : "official");
    }

    public static IReadOnlyList<string> GetCandidates(string url)
    {
        if (Mode != "bmclapi") return [url];
        var mirrored = RewriteForBmclApi(url);
        return mirrored.Equals(url, StringComparison.OrdinalIgnoreCase) ? [url] : [mirrored, url];
    }

    public static async Task<HttpResponseMessage> GetSuccessfulResponseAsync(
        HttpClient client,
        string url,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        var failures = new List<string>();
        foreach (var candidate in GetCandidates(url))
        {
            try
            {
                var response = await client.GetAsync(candidate, completion, cancellationToken);
                if (response.IsSuccessStatusCode) return response;
                var status = response.StatusCode;
                response.Dispose();
                var message = $"HTTP {(int)status} ({status})，请求地址：{candidate}";
                failures.Add(message);
                lastError = new HttpRequestException(message, null, status);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                failures.Add(ex.Message);
            }
        }
        if (failures.Count > 1)
            throw new HttpRequestException("所选下载源及官方备用源均失败：" + string.Join("；", failures), lastError);
        throw lastError ?? new HttpRequestException("所有下载地址均不可用：" + url);
    }

    public static string RewriteForBmclApi(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;
        var basePath = host switch
        {
            "piston-meta.mojang.com" or "launchermeta.mojang.com" or "launcher.mojang.com" => "",
            "resources.download.minecraft.net" => "/assets",
            "libraries.minecraft.net" or "maven.minecraftforge.net" or "maven.fabricmc.net" => "/maven",
            "files.minecraftforge.net" => "/maven",
            "meta.fabricmc.net" => "/fabric-meta",
            _ => ""
        };
        if (basePath.Length == 0 && host is not ("piston-meta.mojang.com" or "launchermeta.mojang.com" or "launcher.mojang.com"))
            return url;
        if (host == "resources.download.minecraft.net") path = "/assets" + path;
        else if (basePath == "/assets") path = "/assets" + path;
        else if (basePath == "/maven") path = "/maven" + path;
        else if (basePath == "/fabric-meta") path = "/fabric-meta" + path;
        return new UriBuilder(Uri.UriSchemeHttps, "bmclapi2.bangbang93.com")
        {
            Path = path,
            Query = uri.Query.TrimStart('?'),
            Fragment = uri.Fragment.TrimStart('#')
        }.Uri.ToString();
    }
}
