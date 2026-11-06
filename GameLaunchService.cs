using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;

namespace CIE;

public sealed class InstanceProfile
{
    public string Id { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "原版";
    public string LoaderVersion { get; set; } = "";
    public string BaseDirectory { get; set; } = "";
    public string GameDirectory { get; set; } = "";
    public string InstallRootDirectory { get; set; } = "";
    public string VersionJsonPath { get; set; } = "";
    public bool Imported { get; set; }
    public bool UsePclFiles { get; set; }
    public bool SourceModeConfigured { get; set; }
    public string CustomName { get; set; } = "";
    public string VirtualFolder { get; set; } = "";
    public DateTime LastPlayed { get; set; }
    [JsonIgnore]
    public BitmapSource? IconSource { get; set; }
    public string DisplayName
    {
        get
        {
            if (Imported)
            {
                var folderName = Path.GetFileName(Path.GetDirectoryName(VersionJsonPath) ?? "");
                var display = string.IsNullOrWhiteSpace(CustomName)
                    ? Loader == "原版" ? $"{folderName} · PCL" : $"{folderName} · {Loader} {LoaderVersion}"
                    : CustomName;
                return string.IsNullOrWhiteSpace(VirtualFolder) ? display : $"[{VirtualFolder}] {display}";
            }
            var name = !string.IsNullOrWhiteSpace(CustomName) ? CustomName
                : Loader == "原版" ? GameVersion : $"{GameVersion} · {Loader} {LoaderVersion}";
            return string.IsNullOrWhiteSpace(VirtualFolder) ? name : $"[{VirtualFolder}] {name}";
        }
    }
}

public sealed class InstanceCatalog
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE");
    private string CatalogPath => Path.Combine(_root, "instances.json");
    public List<InstanceProfile> Load()
    {
        Directory.CreateDirectory(_root);
        if (!File.Exists(CatalogPath)) return [];
        try { return JsonSerializer.Deserialize<List<InstanceProfile>>(File.ReadAllText(CatalogPath)) ?? []; }
        catch { return []; }
    }
    public void Save(IEnumerable<InstanceProfile> profiles)
    {
        Directory.CreateDirectory(_root);
        var temp = CatalogPath + ".part";
        File.WriteAllText(temp, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, CatalogPath, true);
    }
    public InstanceProfile RegisterLocal(string version, string loader = "原版", string loaderVersion = "", string? instanceKey = null, string? instancesRoot = null)
    {
        var installRoot = instancesRoot ?? Path.Combine(_root, "instances");
        var root = Path.Combine(installRoot, SafeName(instanceKey ?? version));
        var id = loader == "原版" ? version : FindLoaderId(root, version, loader, loaderVersion) ?? version;
        if (loaderVersion.Length == 0 && id != version)
        {
            var prefix = version + (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ? "-fabric-" : "-forge-");
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) loaderVersion = id[prefix.Length..];
        }
        var profile = new InstanceProfile
        {
            Id = instanceKey is null ? $"{loader.ToLowerInvariant()}-{version}-{loaderVersion}".TrimEnd('-')
                : $"{loader.ToLowerInvariant()}-{SafeName(instanceKey)}", GameVersion = version,
            Loader = loader, LoaderVersion = loaderVersion, BaseDirectory = root, GameDirectory = root,
            InstallRootDirectory = Path.GetFullPath(installRoot),
            VersionJsonPath = Path.Combine(root, "versions", id, id + ".json")
        };
        Upsert(profile);
        return profile;
    }
    public List<InstanceProfile> ImportMinecraftFolder(string path)
    {
        var versionsRoot = Path.Combine(path, "versions");
        if (!Directory.Exists(versionsRoot)) throw new InvalidDataException("请选 PCL2 的 .minecraft 文件夹（里面应有 versions 文件夹）。");
        var sourceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..8].ToLowerInvariant();
        var result = new List<InstanceProfile>();
        foreach (var folder in Directory.EnumerateDirectories(versionsRoot))
        {
            var id = Path.GetFileName(folder);
            var json = Path.Combine(folder, id + ".json");
            if (!File.Exists(json)) continue;
            var ownJar = Path.Combine(folder, id + ".jar");
            if (!File.Exists(ownJar) && !InheritsFromExistingVersion(json, path)) continue;
            var gameVersion = GetGameVersion(json, id);
            var gameDir = Path.Combine(_root, "instances", "pcl-imports", sourceKey, SafeName(id));
            Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
            var oldMods = Path.Combine(folder, "mods");
            if (!Directory.Exists(oldMods)) oldMods = Path.Combine(path, "mods");
            if (Directory.Exists(oldMods))
                foreach (var mod in Directory.EnumerateFiles(oldMods).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
                {
                    var target = Path.Combine(gameDir, "mods", Path.GetFileName(mod));
                    if (!File.Exists(target)) File.Copy(mod, target);
                }
            var loader = DetectLoader(id, json);
            var profile = new InstanceProfile
            {
                Id = "pcl-" + sourceKey + "-" + id, GameVersion = gameVersion, Loader = loader, LoaderVersion = GetLoaderVersion(loader, json),
                BaseDirectory = path, GameDirectory = gameDir, VersionJsonPath = json, Imported = true,
                UsePclFiles = true, SourceModeConfigured = true
            };
            Upsert(profile);
            result.Add(profile);
        }
        if (result.Count == 0) throw new InvalidDataException("没有找到可导入的版本（每个版本需要同名 JSON 和 JAR 文件）。");
        return result;
    }
    public InstanceProfile? FindByGameVersion(string version) => Load().FirstOrDefault(p => p.GameVersion == version && !p.Imported);
    private void Upsert(InstanceProfile profile)
    {
        var items = Load();
        var i = items.FindIndex(p => p.Id == profile.Id);
        if (i >= 0) items[i] = profile; else items.Add(profile);
        Save(items);
    }
    private static string? FindLoaderId(string root, string gameVersion, string loader, string loaderVersion)
    {
        var directory = Path.Combine(root, "versions");
        if (!Directory.Exists(directory)) return null;
        var prefix = gameVersion + (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ? "-fabric-" : "-forge-");
        return Directory.EnumerateDirectories(directory).Select(Path.GetFileName)
            .FirstOrDefault(name => name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
                && (loaderVersion.Length == 0 || name.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase)));
    }
    private static string GetGameVersion(string jsonPath, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.TryGetProperty("inheritsFrom", out var parent) && !string.IsNullOrWhiteSpace(parent.GetString())) return parent.GetString()!;
            if (doc.RootElement.TryGetProperty("clientVersion", out var clientVersion) && !string.IsNullOrWhiteSpace(clientVersion.GetString())) return clientVersion.GetString()!;
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? fallback : fallback;
        }
        catch { return fallback; }
    }
    private static bool InheritsFromExistingVersion(string jsonPath, string root)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (!doc.RootElement.TryGetProperty("inheritsFrom", out var parent)) return false;
            var id = parent.GetString() ?? "";
            return File.Exists(Path.Combine(root, "versions", id, id + ".jar"));
        }
        catch { return false; }
    }
    public bool RefreshImportedMetadata(InstanceProfile profile)
    {
        if (!profile.Imported || !File.Exists(profile.VersionJsonPath)) return false;
        var id = Path.GetFileName(Path.GetDirectoryName(profile.VersionJsonPath) ?? "");
        var gameVersion = GetGameVersion(profile.VersionJsonPath, id);
        var loader = DetectLoader(id, profile.VersionJsonPath);
        var loaderVersion = GetLoaderVersion(loader, profile.VersionJsonPath);
        var changed = profile.GameVersion != gameVersion || profile.Loader != loader || profile.LoaderVersion != loaderVersion;
        profile.GameVersion = gameVersion;
        profile.Loader = loader;
        profile.LoaderVersion = loaderVersion;
        return changed;
    }

    private static string DetectLoader(string id, string jsonPath)
    {
        if (id.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
        if (id.Contains("forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = document.RootElement;
            if (root.TryGetProperty("mainClass", out var mainClass))
            {
                var value = mainClass.GetString() ?? "";
                if (value.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
                if (value.Contains("forge", StringComparison.OrdinalIgnoreCase) || value.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase)) return "Forge";
            }
            if (root.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array)
                foreach (var library in libraries.EnumerateArray())
                    if (library.TryGetProperty("name", out var name))
                    {
                        var coordinate = name.GetString() ?? "";
                        if (coordinate.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase)) return "Fabric";
                        if (coordinate.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase)
                            || coordinate.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase)) return "Forge";
                    }
        }
        catch { }
        return "原版";
    }

    private static string GetLoaderVersion(string loader, string jsonPath)
    {
        if (loader == "原版") return "";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (document.RootElement.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array)
                foreach (var library in libraries.EnumerateArray())
                    if (library.TryGetProperty("name", out var name))
                    {
                        var coordinate = name.GetString() ?? "";
                        if (loader == "Fabric" && coordinate.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase))
                            return coordinate[(coordinate.LastIndexOf(':') + 1)..];
                        if (loader == "Forge" && (coordinate.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase)
                            || coordinate.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase)))
                            return coordinate[(coordinate.LastIndexOf(':') + 1)..];
                    }
        }
        catch { }
        return "PCL";
    }
    private static string SafeName(string value) => string.Concat(value.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
}

public sealed class GameLaunchService
{
    public async Task LaunchOfflineAsync(InstanceProfile profile, string name, CancellationToken cancellationToken = default, GamePerformanceSettings? performance = null)
    {
        if (!File.Exists(profile.VersionJsonPath)) throw new FileNotFoundException("找不到此版本的启动配置。", profile.VersionJsonPath);
        var id = Path.GetFileNameWithoutExtension(profile.VersionJsonPath);
        var documents = new List<JsonDocument>();
        try
        {
        var chain = LoadVersionChain(profile, documents);
        var root = chain[0];
        var clientJar = FindClientJar(profile, chain, id);
        if (!File.Exists(clientJar)) throw new FileNotFoundException("找不到 Minecraft 游戏文件。", clientJar);
        var java = ResolveJava(profile, chain);
        if (!File.Exists(java)) throw new FileNotFoundException("找不到此版本所需的 Java。请重新安装版本，或确认 PCL2 的 Java 路径。", java);
        Directory.CreateDirectory(profile.GameDirectory);
        ApplyGameOptions(profile, performance);
        var natives = Path.Combine(profile.GameDirectory, "natives", id);
        Directory.CreateDirectory(natives);
        ExtractNatives(chain, profile.BaseDirectory, natives);
        var clientLibraries = GetLibraryPaths(chain, profile.BaseDirectory);
        var classpath = string.Join(Path.PathSeparator, clientLibraries.Append(clientJar));
        var uuid = CreateOfflineUuid(name);
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${auth_player_name}"] = name, ["${version_name}"] = id, ["${game_directory}"] = GetLaunchDirectory(profile),
            ["${assets_root}"] = Path.Combine(profile.BaseDirectory, "assets"), ["${assets_index_name}"] = ReadText(chain, "assets", "legacy"),
            ["${auth_uuid}"] = uuid, ["${auth_access_token}"] = "0", ["${clientid}"] = "0", ["${auth_xuid}"] = "0",
            ["${user_type}"] = "legacy", ["${version_type}"] = "release", ["${classpath}"] = classpath,
            ["${library_directory}"] = Path.Combine(profile.BaseDirectory, "libraries"), ["${natives_directory}"] = natives,
            ["${launcher_name}"] = "CIE", ["${launcher_version}"] = "0.1.0", ["${resolution_width}"] = "1280",
            ["${resolution_height}"] = "720", ["${primary_jar}"] = clientJar
        };
        var jvm = GetArgs(chain, "jvm", substitutions, true);
        var game = GetArgs(chain, "game", substitutions, false);
        SetArg(game, "--username", name); SetArg(game, "--uuid", uuid); SetArg(game, "--accessToken", "0");
        SetArg(game, "--userType", "legacy"); SetArg(game, "--versionType", "release");
        if (!jvm.Contains("-cp") && !jvm.Contains("-classpath")) { jvm.Add("-cp"); jvm.Add(classpath); }
        AddJvmArg(jvm, "-Djava.library.path=", "-Djava.library.path=" + natives);
        AddJvmArg(jvm, "-Dminecraft.launcher.brand=", "-Dminecraft.launcher.brand=CIE");
        AddJvmArg(jvm, "-Dminecraft.launcher.version=", "-Dminecraft.launcher.version=0.1.0");
        if (!jvm.Any(a => a.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase))) jvm.Insert(0, "-Xmx2G");
        var mainClass = ReadText(chain, "mainClass", "net.minecraft.client.main.Main");
        var start = new ProcessStartInfo(java) { WorkingDirectory = GetLaunchDirectory(profile), UseShellExecute = false };
        foreach (var arg in jvm.Concat([mainClass]).Concat(game)) start.ArgumentList.Add(arg);
        _ = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Minecraft 进程。");
        }
        finally { foreach (var document in documents) document.Dispose(); }
    }

    private static void ApplyGameOptions(InstanceProfile profile, GamePerformanceSettings? performance)
    {
        var optionsPath = Path.Combine(GetLaunchDirectory(profile), "options.txt");
        var lines = File.Exists(optionsPath) ? File.ReadAllLines(optionsPath).ToList() : [];
        var languageIndex = lines.FindIndex(line => line.StartsWith("lang:", StringComparison.OrdinalIgnoreCase));
        if (languageIndex < 0)
        {
            var culture = CultureInfo.CurrentUICulture.Name;
            if (string.IsNullOrWhiteSpace(culture)) culture = CultureInfo.CurrentCulture.Name;
            var supportedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "en_us", "en_gb", "zh_cn", "zh_tw", "ja_jp", "ko_kr", "fr_fr", "de_de", "es_es", "es_mx",
                "it_it", "pt_br", "pt_pt", "ru_ru", "pl_pl", "nl_nl", "sv_se", "da_dk", "fi_fi", "nb_no",
                "cs_cz", "hu_hu", "tr_tr", "uk_ua", "el_gr", "bg_bg", "ro_ro", "sk_sk", "hr_hr", "sr_sp",
                "lt_lt", "lv_lv", "et_ee", "id_id", "ms_my", "th_th", "vi_vn", "he_il", "ar_sa", "fa_ir", "hi_in"
            };
            var requestedLanguage = culture.Replace('-', '_').ToLowerInvariant();
            var language = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? culture.Contains("TW", StringComparison.OrdinalIgnoreCase) || culture.Contains("HK", StringComparison.OrdinalIgnoreCase) ? "zh_tw" : "zh_cn"
                : supportedLanguages.Contains(requestedLanguage) ? requestedLanguage
                : supportedLanguages.FirstOrDefault(candidate => candidate.StartsWith(culture.Split('-')[0] + "_", StringComparison.OrdinalIgnoreCase)) ?? "en_us";
            lines.Add("lang:" + language);
        }

        if (performance is not null)
        {
            SetOption(lines, "maxFps", performance.MaxFps.ToString(CultureInfo.InvariantCulture));
            if (performance.Optimize)
            {
                SetOption(lines, "graphics", "fast");
                SetOption(lines, "renderDistance", "8");
                SetOption(lines, "simulationDistance", "6");
                SetOption(lines, "enableVsync", "false");
                SetOption(lines, "entityShadows", "false");
                SetOption(lines, "particles", "2");
                SetOption(lines, "biomeBlendRadius", "0");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(optionsPath)!);
        File.WriteAllLines(optionsPath, lines);
    }

    private static void SetOption(List<string> lines, string key, string value)
    {
        var prefix = key + ":";
        var index = lines.FindIndex(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) lines[index] = prefix + value;
        else lines.Add(prefix + value);
    }

    private static string GetLaunchDirectory(InstanceProfile profile) =>
        profile.Imported && profile.UsePclFiles ? Path.GetDirectoryName(profile.VersionJsonPath)! : profile.GameDirectory;

    private static List<JsonElement> LoadVersionChain(InstanceProfile profile, List<JsonDocument> documents)
    {
        var result = new List<JsonElement>();
        var path = profile.VersionJsonPath;
        for (var depth = 0; depth < 8 && File.Exists(path); depth++)
        {
            var document = JsonDocument.Parse(File.ReadAllText(path));
            documents.Add(document);
            var root = document.RootElement;
            result.Add(root);
            if (!root.TryGetProperty("inheritsFrom", out var parent)) break;
            var parentId = parent.GetString() ?? "";
            path = Path.Combine(profile.BaseDirectory, "versions", parentId, parentId + ".json");
        }
        return result;
    }

    private static string FindClientJar(InstanceProfile profile, IReadOnlyList<JsonElement> chain, string versionId)
    {
        foreach (var metadata in chain)
        {
            var id = ReadText(metadata, "id", versionId);
            var path = Path.Combine(profile.BaseDirectory, "versions", id, id + ".jar");
            if (File.Exists(path)) return path;
        }
        var fallback = Path.Combine(Path.GetDirectoryName(profile.VersionJsonPath)!, versionId + ".jar");
        return fallback;
    }

    private static string ResolveJava(InstanceProfile profile, IReadOnlyList<JsonElement> chain)
    {
        var root = chain.FirstOrDefault(e => TryObjectProperty(e, "javaVersion", out _));
        if (root.ValueKind == JsonValueKind.Undefined) root = chain[^1];
        var hasJava = TryObjectProperty(root, "javaVersion", out var java);
        var component = hasJava && TryObjectProperty(java, "component", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(component)) component = InferJavaComponent(profile.GameVersion);
        var expectedMajor = hasJava && TryObjectProperty(java, "majorVersion", out var majorValue) ? majorValue.GetInt32()
            : component switch { "jre-legacy" => 8, "java-runtime-alpha" => 16, "java-runtime-delta" => 21, _ => 17 };
        if (profile.Imported && profile.UsePclFiles)
            return FindPclJava(profile, component) ?? FindSystemJava(expectedMajor)
                ?? Path.Combine(profile.BaseDirectory, "runtime", component, "bin", "java.exe");
        var path = Path.Combine(profile.GameDirectory, "runtime", component, "bin", "java.exe");
        if (File.Exists(path)) return path;
        path = Path.Combine(profile.BaseDirectory, "runtime", component, "bin", "java.exe");
        if (File.Exists(path)) return path;
        return FindSystemJava(expectedMajor) ?? path;
    }

    private static string? FindPclJava(InstanceProfile profile, string component)
    {
        var expectedGameDirectory = Path.GetFullPath(Path.GetDirectoryName(profile.VersionJsonPath)!);
        var launcherDirectory = Directory.GetParent(Path.GetFullPath(profile.BaseDirectory))?.FullName;
        if (launcherDirectory is not null)
        {
            var latestLaunch = Path.Combine(launcherDirectory, "LatestLaunch.bat");
            if (File.Exists(latestLaunch))
            {
                var command = File.ReadAllText(latestLaunch);
                var gameDir = System.Text.RegularExpressions.Regex.Match(command,
                    "(?i)--gameDir(?:=|\\s+)(?:\"(?<path>[^\"]+)\"|(?<path>[^\\s&]+))");
                if (gameDir.Success && PathEquals(gameDir.Groups["path"].Value, expectedGameDirectory))
                {
                    var java = System.Text.RegularExpressions.Regex.Match(command,
                        "(?i)\"(?<path>[A-Z]:\\[^\"\\r\\n]*?javaw?\\.exe)\"|(?<path>[A-Z]:\\[^\\s&\"]*?javaw?\\.exe)");
                    if (java.Success && File.Exists(java.Groups["path"].Value)) return java.Groups["path"].Value;
                }
            }
        }

        var roots = new[]
        {
            Path.Combine(profile.BaseDirectory, "runtime", component, "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime", component, "bin")
        };
        foreach (var root in roots)
            foreach (var name in new[] { "java.exe", "javaw.exe" })
            {
                var candidate = Path.Combine(root, name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    private static bool PathEquals(string first, string second)
    {
        try
        {
            return Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string? FindSystemJava(int expectedMajor)
    {
        var candidates = new List<string>();
        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(home)) candidates.Add(Path.Combine(home, "bin", "java.exe"));
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(s => s.Length > 0).Select(s => Path.Combine(s, "java.exe")));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var start = new ProcessStartInfo(candidate, "-version")
                {
                    UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true
                };
                using var process = Process.Start(start);
                if (process is null) continue;
                var output = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                var match = System.Text.RegularExpressions.Regex.Match(output, "version \\\"(?:1\\.)?(\\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var actualMajor) && actualMajor >= expectedMajor)
                    return candidate;
            }
            catch { }
        }
        return null;
    }

    private static string InferJavaComponent(string version)
    {
        var parts = version.Split('.', '-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && int.TryParse(parts[1], out var minor))
            return minor <= 16 ? "jre-legacy" : minor == 17 ? "java-runtime-alpha" : minor <= 20 ? "java-runtime-gamma" : "java-runtime-delta";
        return "java-runtime-gamma";
    }

    private static List<string> GetLibraryPaths(IReadOnlyList<JsonElement> roots, string baseDir)
    {
        var paths = new List<string>();
        foreach (var root in roots.Reverse())
        {
            if (!TryObjectProperty(root, "libraries", out var libs) || libs.ValueKind != JsonValueKind.Array) continue;
            foreach (var lib in libs.EnumerateArray())
            {
                if (!Allowed(lib)) continue;
                if (TryObjectProperty(lib, "downloads", out var d) && TryObjectProperty(d, "artifact", out var artifact) && TryObjectProperty(artifact, "path", out var p)) Add(p.GetString());
                else if (TryObjectProperty(lib, "name", out var name)) Add(PathFromCoordinate(name.GetString() ?? ""));
            }
        }
        return paths;
        void Add(string? relative)
        {
            if (relative is null) return;
            var path = Path.GetFullPath(Path.Combine(baseDir, "libraries", relative.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(path) && !paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        }
    }

    private static void ExtractNatives(IReadOnlyList<JsonElement> roots, string baseDir, string targetDir)
    {
        var safeRoot = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        foreach (var root in roots.Reverse())
        {
            if (!TryObjectProperty(root, "libraries", out var libs) || libs.ValueKind != JsonValueKind.Array) continue;
            foreach (var lib in libs.EnumerateArray())
            {
                if (!Allowed(lib) || !TryObjectProperty(lib, "downloads", out var d) || !TryObjectProperty(d, "classifiers", out var classifiers)
                    || !TryObjectProperty(lib, "natives", out var natives) || !TryObjectProperty(natives, "windows", out var pattern)) continue;
                var key = pattern.GetString()!.Replace("${arch}", Environment.Is64BitProcess ? "64" : "32");
                if (!TryObjectProperty(classifiers, key, out var native) || !TryObjectProperty(native, "path", out var path)) continue;
                var archivePath = Path.Combine(baseDir, "libraries", path.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(archivePath)) continue;
                using var zip = ZipFile.OpenRead(archivePath);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                    var destination = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                    if (!destination.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("原生库包含越界文件路径。");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (!entry.FullName.EndsWith('/')) entry.ExtractToFile(destination, true);
                }
            }
        }
    }

    private static bool Allowed(JsonElement item)
    {
        if (!TryObjectProperty(item, "rules", out var rules) || rules.ValueKind != JsonValueKind.Array) return true;
        var allow = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (TryObjectProperty(rule, "os", out var os) && TryObjectProperty(os, "name", out var name) && name.GetString() != "windows") continue;
            var processArch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64", Architecture.X86 => "x86", Architecture.Arm64 => "aarch64", _ => "x86_64"
            };
            if (TryObjectProperty(rule, "os", out os) && TryObjectProperty(os, "arch", out var arch)
                && !processArch.Equals(arch.GetString(), StringComparison.OrdinalIgnoreCase)) continue;
            if (TryObjectProperty(rule, "features", out var features) && features.ValueKind == JsonValueKind.Object
                && features.EnumerateObject().Any(feature => feature.Value.ValueKind == JsonValueKind.True)) continue;
            allow = TryObjectProperty(rule, "action", out var action) && action.GetString() == "allow";
        }
        return allow;
    }

    private static string PathFromCoordinate(string coordinate)
    {
        var parts = coordinate.Split(':');
        if (parts.Length < 3) return "";
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return Path.Combine(parts[0].Replace('.', Path.DirectorySeparatorChar), parts[1], parts[2], $"{parts[1]}-{parts[2]}{classifier}.jar");
    }

    private static List<string> GetArgs(IReadOnlyList<JsonElement> roots, string group, IReadOnlyDictionary<string, string> map, bool jvm)
    {
        var args = new List<string>();
        var hasModernArgs = false;
        foreach (var root in roots.Reverse())
        {
            if (!TryObjectProperty(root, "arguments", out var all)) continue;
            JsonElement array;
            if (all.ValueKind == JsonValueKind.Object)
            {
                if (!TryObjectProperty(all, group, out array) || array.ValueKind != JsonValueKind.Array) continue;
            }
            else if (all.ValueKind == JsonValueKind.Array)
            {
                // Some third-party/PCL-generated profiles store game arguments directly as an array.
                // Treat that shape as game arguments; JVM defaults are added below.
                if (jvm || group != "game") continue;
                array = all;
            }
            else continue;
            hasModernArgs = true;
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) args.Add(Expand(item.GetString()!, map));
                else if (item.ValueKind == JsonValueKind.Object && Allowed(item) && item.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String) args.Add(Expand(value.GetString()!, map));
                    else if (value.ValueKind == JsonValueKind.Array) args.AddRange(value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => Expand(v.GetString()!, map)));
                }
            }
        }
        if (!jvm && !hasModernArgs)
        {
            var legacyRoot = roots.FirstOrDefault(r => TryObjectProperty(r, "minecraftArguments", out _));
            if (legacyRoot.ValueKind != JsonValueKind.Undefined && TryObjectProperty(legacyRoot, "minecraftArguments", out var legacy))
                args.AddRange(System.Text.RegularExpressions.Regex.Matches(Expand(legacy.GetString() ?? "", map), "(?:\\\"([^\\\"]*)\\\"|([^\\s]+))")
                    .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
        }
        return args;
    }

    private static string Expand(string value, IReadOnlyDictionary<string, string> map)
    { foreach (var pair in map) value = value.Replace(pair.Key, pair.Value, StringComparison.Ordinal); return value; }
    private static void SetArg(List<string> args, string key, string value)
    {
        var i = args.IndexOf(key);
        if (i >= 0 && i + 1 < args.Count) args[i + 1] = value; else { args.Add(key); args.Add(value); }
    }
    private static void AddJvmArg(List<string> args, string prefix, string value)
    { if (!args.Any(a => a.StartsWith(prefix, StringComparison.Ordinal))) args.Add(value); }
    private static string CreateOfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30); bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
    private static bool TryObjectProperty(JsonElement element, string key, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object) return element.TryGetProperty(key, out value);
        value = default;
        return false;
    }
    private static string ReadText(JsonElement root, string key, string fallback) => TryObjectProperty(root, key, out var value) ? value.ToString() : fallback;
    private static string ReadText(IReadOnlyList<JsonElement> roots, string key, string fallback)
    {
        foreach (var root in roots) if (TryObjectProperty(root, key, out var value)) return value.ToString();
        return fallback;
    }
}

public sealed record GamePerformanceSettings(int MaxFps, bool Optimize);
