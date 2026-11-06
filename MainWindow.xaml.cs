using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace CIE;

public partial class MainWindow : Window
{
    private readonly MinecraftVersionService _versionService = new();
    private readonly MinecraftInstaller _installer = new();
    private readonly InstanceCatalog _catalog = new();
    private readonly GameLaunchService _launchService = new();
    private List<MinecraftVersion> _versions = [];
    private List<InstanceProfile> _instances = [];
    private List<ModrinthProjectHit> _modrinthHits = [];
    private ModrinthVersionInfo? _selectedModrinthVersion;
    private List<string> _modrinthPrerequisiteNames = [];
    private bool _isInstalling;
    private bool _launcherUpdateCheckInProgress;
    private bool _loadingGameSettings;
    internal static readonly string ProfileDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "profile");
    private static readonly HttpClient ModrinthClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HttpClient McmodClient = new() { Timeout = TimeSpan.FromSeconds(7) };
    private static readonly Dictionary<string, string[]> ModrinthChineseAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["投影"] = ["litematica", "schematica", "schematic"],
        ["小地图"] = ["xaero minimap", "journeymap", "voxelmap"],
        ["世界地图"] = ["xaero world map", "journeymap", "voxelmap"],
        ["地图"] = ["xaero world map", "journeymap", "voxelmap"],
        ["光影"] = ["iris shaders", "oculus shaders", "shader"],
        ["着色器"] = ["iris shaders", "oculus shaders", "shader"],
        ["优化"] = ["sodium", "lithium", "ferritecore", "modernfix"],
        ["性能"] = ["sodium", "lithium", "ferritecore", "modernfix"],
        ["物品管理"] = ["jei", "rei", "emi"],
        ["合成表"] = ["jei", "rei", "emi"],
        ["苹果皮"] = ["appleskin"],
        ["连锁挖矿"] = ["veinminer", "ultimine"],
        ["连锁"] = ["veinminer", "ultimine"],
        ["背包整理"] = ["inventory profiles next", "inventory sorter"],
        ["整理背包"] = ["inventory profiles next", "inventory sorter"],
        ["动态光源"] = ["dynamic lights", "lambdynamiclights"],
        ["光源"] = ["dynamic lights", "lambdynamiclights"],
        ["玉"] = ["jade"],
        ["农夫乐事"] = ["farmers delight"],
        ["小地图模组"] = ["xaero minimap", "journeymap", "voxelmap"]
    };
    private string _offlineName = "Player";
    private bool _isBlueTheme;
    private bool _isVioletTheme;
    private string _installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "instances");

    public MainWindow()
    {
        InitializeComponent();
        LoadInstallRoot();
        LoadThemePreference();
        LoadOfflineProfile();
        Loaded += MainWindow_Loaded;
    }

    private void LoadInstallRoot()
    {
        var path = Path.Combine(ProfileDirectory, "install-root.txt");
        if (File.Exists(path))
        {
            var saved = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(saved) && Path.IsPathRooted(saved)) _installRoot = saved;
        }
        InstallRootTextBox.Text = _installRoot;
    }

    private void ChooseInstallRootButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 CIE 游戏文件保存位置", Multiselect = false };
        if (Directory.Exists(_installRoot)) dialog.FolderName = _installRoot;
        if (dialog.ShowDialog(this) != true) return;
        _installRoot = Path.GetFullPath(dialog.FolderName);
        Directory.CreateDirectory(ProfileDirectory);
        File.WriteAllText(Path.Combine(ProfileDirectory, "install-root.txt"), _installRoot);
        InstallRootTextBox.Text = _installRoot;
        InstallStatusText.Text = $"新版本将安装到：{_installRoot}";
    }

    private void LoadThemePreference()
    {
        var preferencePath = Path.Combine(ProfileDirectory, "theme.txt");
        var saved = File.Exists(preferencePath) ? File.ReadAllText(preferencePath).Trim() : "mono";
        _isBlueTheme = saved.Equals("blue", StringComparison.OrdinalIgnoreCase);
        _isVioletTheme = saved.Equals("violet", StringComparison.OrdinalIgnoreCase);
        ApplyTheme();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isVioletTheme) { _isVioletTheme = false; _isBlueTheme = false; }
        else _isBlueTheme = !_isBlueTheme;
        Directory.CreateDirectory(ProfileDirectory);
        File.WriteAllText(Path.Combine(ProfileDirectory, "theme.txt"), _isBlueTheme ? "blue" : "mono");
        ApplyTheme();
    }

    private void VioletThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _isVioletTheme = !_isVioletTheme;
        _isBlueTheme = false;
        Directory.CreateDirectory(ProfileDirectory);
        File.WriteAllText(Path.Combine(ProfileDirectory, "theme.txt"), _isVioletTheme ? "violet" : "mono");
        ApplyTheme();
    }

    private void WikiLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开 Wiki 页面：{ex.Message}", "CIE", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }

    private void ApplyTheme()
    {
        var colors = _isVioletTheme
            ? new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#F8F4FB", ["NavBackgroundBrush"] = "#24152D",
                ["CardBrush"] = "#FFFFFF", ["SurfaceBrush"] = "#F3EBF7", ["BorderBrushColor"] = "#D8C6E0",
                ["SurfaceAltBrush"] = "#EBDDF1", ["InputBrush"] = "#FFFFFF", ["MutedDimBrush"] = "#786582",
                ["AccentBrush"] = "#7C3FA1", ["AccentHoverBrush"] = "#633184", ["AccentLightBrush"] = "#985DB8",
                ["PrimaryTextBrush"] = "#35213F", ["SecondaryTextBrush"] = "#654F70", ["SubtleTextBrush"] = "#756180",
                ["BrandNameBrush"] = "#FFFFFF", ["MutedBrush"] = "#A58BAF", ["NavTextBrush"] = "#EDE2F2", ["OnAccentTextBrush"] = "#FFFFFF"
            }
            : _isBlueTheme
            ? new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#F3F8FC", ["NavBackgroundBrush"] = "#E7F2F9",
                ["CardBrush"] = "#FFFFFF", ["SurfaceBrush"] = "#EFF7FC", ["BorderBrushColor"] = "#C4DCEB",
                ["SurfaceAltBrush"] = "#DCECF6", ["InputBrush"] = "#FFFFFF", ["MutedDimBrush"] = "#6B8293",
                ["AccentBrush"] = "#287FAE", ["AccentHoverBrush"] = "#216C96", ["AccentLightBrush"] = "#358BB8",
                ["PrimaryTextBrush"] = "#19384E", ["SecondaryTextBrush"] = "#496579", ["SubtleTextBrush"] = "#627D90",
                ["BrandNameBrush"] = "#19384E", ["MutedBrush"] = "#557286", ["NavTextBrush"] = "#29465B", ["OnAccentTextBrush"] = "#FFFFFF"
            }
            : new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#F4F4F4", ["NavBackgroundBrush"] = "#101010",
                ["CardBrush"] = "#FFFFFF", ["SurfaceBrush"] = "#F1F1F1", ["BorderBrushColor"] = "#CCCCCC",
                ["SurfaceAltBrush"] = "#E8E8E8", ["InputBrush"] = "#FFFFFF", ["MutedDimBrush"] = "#666666",
                ["AccentBrush"] = "#111111", ["AccentHoverBrush"] = "#333333", ["AccentLightBrush"] = "#555555",
                ["PrimaryTextBrush"] = "#111111", ["SecondaryTextBrush"] = "#444444", ["SubtleTextBrush"] = "#555555",
                ["BrandNameBrush"] = "#FFFFFF", ["MutedBrush"] = "#666666", ["NavTextBrush"] = "#DDDDDD", ["OnAccentTextBrush"] = "#FFFFFF"
            };
        foreach (var (key, color) in colors)
            Resources[key] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

        Resources["ThemeToggleHoverBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isBlueTheme || _isVioletTheme ? "#111111" : "#287FAE"));
        VioletEasterEggText.Visibility = _isVioletTheme ? Visibility.Visible : Visibility.Collapsed;
        VioletThemeScene.Visibility = _isVioletTheme ? Visibility.Visible : Visibility.Collapsed;
        ThemeToggleButton.ToolTip = _isBlueTheme ? "当前为蓝白主题，点击切换到黑白主题" : _isVioletTheme ? "当前为紫罗兰主题，点击切换到黑白主题" : "当前为黑白主题，点击切换到蓝白主题";
        var logo = LoadBrandLogo(_isBlueTheme ? "CIE金属.png" : "CIE.png");
        if (logo is not null)
        {
            BrandLogoImage.Source = logo;
            BrandLogoImage.Visibility = Visibility.Visible;
            BrandLogoFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            BrandLogoImage.Source = null;
            BrandLogoImage.Visibility = Visibility.Collapsed;
            BrandLogoFallback.Visibility = Visibility.Visible;
        }
    }

    private static BitmapImage? LoadBrandLogo(string fileName)
    {
        // WPF Resource files are embedded in the application assembly and travel with CIE.
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"pack://application:,,,/Assets/{Uri.EscapeDataString(fileName)}", UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }
        catch (IOException) { return null; }
        catch (System.Windows.Markup.XamlParseException) { return null; }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        FitWindowToWorkArea();
        LoadDownloadSourcePreference();
        LoadGamePerformanceSettings();
        LoadInstances();
        await RefreshVersionsAsync();
        _ = CheckLauncherUpdatesAsync(true, false);
    }

    private async void CheckLauncherUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckLauncherUpdatesAsync(true, true);
    }

    private async Task CheckLauncherUpdatesAsync(bool promptToInstall, bool reportUpToDate)
    {
        if (_launcherUpdateCheckInProgress) return;
        var lastCheckPath = Path.Combine(ProfileDirectory, "last-launcher-update-check.txt");
        _launcherUpdateCheckInProgress = true;
        CheckLauncherUpdatesButton.IsEnabled = false;
        LauncherUpdateStatusText.Text = "正在检查 GitHub 更新…";
        try
        {
            Directory.CreateDirectory(ProfileDirectory);
            File.WriteAllText(lastCheckPath, DateTime.UtcNow.ToString("O"));
            var update = await LauncherUpdateService.CheckAsync();
            if (update is null)
            {
                LauncherUpdateStatusText.Text = $"CIE 已是最新版本（{LauncherUpdateService.CurrentVersion}）。";
                if (reportUpToDate)
                    MessageBox.Show(this, LauncherUpdateStatusText.Text, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LauncherUpdateStatusText.Text = $"发现新版本 {update.Version}。";
            if (!promptToInstall) return;
            var releaseNotes = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                ? "本次版本暂无更新说明。"
                : update.ReleaseNotes.Trim();
            if (releaseNotes.Length > 1200) releaseNotes = releaseNotes[..1200] + "…";
            if (MessageBox.Show(this,
                $"发现 CIE 新版本 {update.Version}，当前版本为 {LauncherUpdateService.CurrentVersion}。\n\n更新内容：\n{releaseNotes}\n\n现在下载并更新吗？更新完成后会重新启动 CIE。游戏、模组和存档不会更改。",
                "发现 CIE 更新", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            var updaterPath = Path.Combine(AppContext.BaseDirectory, "CIEUpdater.exe");
            if (!File.Exists(updaterPath))
                throw new FileNotFoundException("当前安装缺少安全更新组件。请先用最新的 CIE 安装器更新一次，之后即可通过 GitHub 自动更新。", updaterPath);

            CheckLauncherUpdatesButton.Content = "正在下载…";
            LauncherUpdateStatusText.Text = "正在下载并验证更新包…";
            var downloaded = await LauncherUpdateService.DownloadAndVerifyAsync(update);
            var start = new ProcessStartInfo(updaterPath)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(AppContext.BaseDirectory);
            start.ArgumentList.Add(downloaded.PackagePath);
            start.ArgumentList.Add(downloaded.Sha256);
            using var updaterProcess = Process.Start(start) ?? throw new InvalidOperationException("无法启动 CIE 更新组件。");
            LauncherUpdateStatusText.Text = "更新已准备好，正在重新启动…";
            Close();
        }
        catch (Exception ex)
        {
            if (ex is HttpRequestException httpError && httpError.StatusCode == System.Net.HttpStatusCode.NotFound)
                LauncherUpdateStatusText.Text = "GitHub 尚无已发布版本；完成首次发布后即可自动更新。";
            else
            {
                DiagnosticLogger.WriteError("CIE 自动更新失败", ex.ToString());
                LauncherUpdateStatusText.Text = "更新检查或安装失败，请检查网络后重试。";
            }
            if (promptToInstall && !(ex is HttpRequestException notFound && notFound.StatusCode == System.Net.HttpStatusCode.NotFound))
                MessageBox.Show(this, $"CIE 更新没有完成：{ex.Message}", "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _launcherUpdateCheckInProgress = false;
            if (IsLoaded)
            {
                CheckLauncherUpdatesButton.IsEnabled = true;
                CheckLauncherUpdatesButton.Content = "检查更新";
            }
        }
    }

    private void FitWindowToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(960, workArea.Width);
        MinHeight = Math.Min(620, workArea.Height);
        Width = Math.Min(1280, workArea.Width * 0.92);
        Height = Math.Min(780, workArea.Height * 0.92);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void LoadDownloadSourcePreference()
    {
        var path = Path.Combine(ProfileDirectory, "download-source.txt");
        DownloadSource.Mode = File.Exists(path) ? File.ReadAllText(path).Trim().ToLowerInvariant() : "official";
        DownloadSourceComboBox.SelectedIndex = DownloadSource.Mode switch { "bmclapi" => 1, "tuna" => 2, _ => 0 };
        UpdateDownloadSourceHint();
    }

    private void DownloadSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || DownloadSourceComboBox.SelectedIndex < 0) return;
        DownloadSource.Mode = DownloadSourceComboBox.SelectedIndex switch { 1 => "bmclapi", 2 => "tuna", _ => "official" };
        Directory.CreateDirectory(ProfileDirectory);
        File.WriteAllText(Path.Combine(ProfileDirectory, "download-source.txt"), DownloadSource.Mode);
        UpdateDownloadSourceHint();
    }

    private void UpdateDownloadSourceHint()
    {
        DownloadSourceHintText.Text = DownloadSource.Mode switch
        {
            "bmclapi" => "中国内地推荐：BMCLAPI 优先，失败时自动尝试官方源。",
            "tuna" => "清华 TUNA 目前没有镜像 Minecraft 文件，自动使用官方源以避免下载 404。",
            _ => "官方源：直接连接 Mojang 与加载器官网。"
        };
    }

    private string GameSettingsPath => Path.Combine(ProfileDirectory, "game-options.json");

    private void LoadGamePerformanceSettings()
    {
        GamePerformanceSettings settings = new(144, false);
        try
        {
            if (File.Exists(GameSettingsPath))
                settings = System.Text.Json.JsonSerializer.Deserialize<GamePerformanceSettings>(File.ReadAllText(GameSettingsPath)) ?? settings;
        }
        catch { }
        _loadingGameSettings = true;
        GameFpsComboBox.SelectedItem = GameFpsComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => int.TryParse(item.Tag?.ToString(), out var fps) && fps == settings.MaxFps)
            ?? GameFpsComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
        OptimizePerformanceCheckBox.IsChecked = settings.Optimize;
        _loadingGameSettings = false;
    }

    private void GamePerformanceSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingGameSettings || !IsLoaded) return;
        var fps = GameFpsComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value) ? value : 144;
        Directory.CreateDirectory(ProfileDirectory);
        File.WriteAllText(GameSettingsPath, System.Text.Json.JsonSerializer.Serialize(new GamePerformanceSettings(fps, OptimizePerformanceCheckBox.IsChecked == true)));
    }

    private async void RefreshVersionsButton_Click(object sender, RoutedEventArgs e) => await RefreshVersionsAsync();

    private async void InstallVersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        if (VersionsListView.SelectedItem is not MinecraftVersion version)
        {
            InstallStatusText.Text = "请先在版本列表中选择一个版本。";
            return;
        }

        _isInstalling = true;
        InstallVersionButton.IsEnabled = false;
        RefreshVersionsButton.IsEnabled = false;
        InstallProgressBar.Visibility = Visibility.Visible;
        InstallProgressBar.Value = 0;
        var progress = new Progress<InstallProgress>(state =>
        {
            InstallStatusText.Text = state.Message;
            InstallProgressBar.Value = state.Percent;
        });

        try
        {
            var loader = InstallLoaderComboBox.SelectedIndex switch { 1 => "Fabric", 2 => "Forge", _ => "原版" };
            await _installer.InstallAsync(version.Id, version.Url, progress, loader, installRoot: _installRoot);
            if (loader == "原版") _catalog.RegisterLocal(version.Id, instancesRoot: _installRoot);
            else _catalog.RegisterLocal(version.Id, loader, "", instancesRoot: _installRoot);
            LoadInstances();
            GameStatusText.Text = $"Minecraft {version.Id}（{loader}）已安装。";
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = $"安装失败：{ex.Message}。检查网络和磁盘空间后可重试。";
            InstallStatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            ShowDiagnosticsPopup(null, ex);
        }
        finally
        {
            _isInstalling = false;
            InstallVersionButton.IsEnabled = true;
            RefreshVersionsButton.IsEnabled = true;
        }
    }

    private async Task RefreshVersionsAsync()
    {
        RefreshVersionsButton.IsEnabled = false;
        VersionsStatusText.Text = "正在从 Mojang 获取版本清单…";
        try
        {
            var manifest = await _versionService.GetManifestAsync();
            _versions = manifest.Versions;
            VersionsListView.ItemsSource = _versions;
            RefreshHomeInstances();
            VersionCategoryComboBox.SelectedIndex = 0;
            FilterVersions();
            VersionsStatusText.Text = $"已加载 {_versions.Count} 个版本。最新正式版：{manifest.Latest.Release}；最新快照：{manifest.Latest.Snapshot}。";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.WriteError("版本清单下载失败", ex.ToString());
            VersionsStatusText.Text = $"版本清单加载失败：{ex.Message}\n请检查网络后点击“刷新版本”重试。";
            if (_versions.Count == 0)
                GameStatusText.Text = "无法加载版本列表，请检查网络连接。";
        }
        finally
        {
            RefreshVersionsButton.IsEnabled = true;
        }
    }

    private void VersionCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => FilterVersions();

    private void InstallLoaderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoaderChoiceHintText is null || InstallLoaderComboBox.SelectedItem is not ComboBoxItem item) return;
        LoaderChoiceHintText.Text = item.Content?.ToString() switch
        {
            var text when text?.StartsWith("Fabric", StringComparison.Ordinal) == true => "自动安装最新稳定 Fabric",
            var text when text?.StartsWith("Forge", StringComparison.Ordinal) == true => "自动安装推荐 Forge",
            _ => "新手推荐：原版 Minecraft"
        };
    }

    private void FilterVersions()
    {
        if (VersionsListView is null || VersionCategoryComboBox?.SelectedItem is not ComboBoxItem category) return;
        var label = category.Content?.ToString();
        IEnumerable<MinecraftVersion> filtered = label switch
        {
            "正式版" => _versions.Where(v => v.Type == "release"),
            "快照版" => _versions.Where(v => v.Type == "snapshot" && !IsSpecialVersion(v.Id)),
            "节日/特别版" => _versions.Where(v => IsSpecialVersion(v.Id)),
            _ => []
        };
        VersionsListView.ItemsSource = filtered.ToList();
        if (_versions.Count > 0)
            VersionsStatusText.Text = $"{label}：{((System.Collections.ICollection)VersionsListView.ItemsSource).Count} 个版本。最新正式版仍默认选中首页。";
    }

    private static bool IsSpecialVersion(string id) =>
        id.Contains("infinite", StringComparison.OrdinalIgnoreCase)
        || id.Contains("or_b", StringComparison.OrdinalIgnoreCase)
        || id.Contains("potato", StringComparison.OrdinalIgnoreCase)
        || id.Contains("vote", StringComparison.OrdinalIgnoreCase)
        || id.Contains("minecraftcomplete", StringComparison.OrdinalIgnoreCase);

    private async void ModsButton_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "MOD 管理";
        HomePage.Visibility = Visibility.Collapsed;
        VersionsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Visible;
        InstancesPage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Collapsed;
        await LoadModsAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "设置";
        HomePage.Visibility = Visibility.Collapsed;
        VersionsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        InstancesPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        PlaceholderPage.Visibility = Visibility.Collapsed;
    }

    private void UninstallLauncherButton_Click(object sender, RoutedEventArgs e)
    {
        var installPath = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var marker = Path.Combine(installPath, ".cie-install-marker");
        var script = Path.Combine(installPath, "Uninstall-CIE.ps1");
        if (!File.Exists(marker) || !File.Exists(script))
        {
            MessageBox.Show(this, "此 CIE 副本没有安装标记，无法安全地自我删除。请从已安装的开始菜单或桌面快捷方式启动后再操作。", "无法删除 CIE", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Window
        {
            Title = "删除 CIE 启动器", Owner = this, Width = 470, Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F3F8FC")!
        };
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock { Text = "这会删除 CIE 程序、玩家设置、皮肤和日志。", FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(new TextBlock { Text = "导入的 PCL 原始 .minecraft 不会被删除。", Margin = new Thickness(0, 10, 0, 12), TextWrapping = TextWrapping.Wrap });
        var deleteGames = new CheckBox
        {
            Content = "同时删除 CIE 下载的游戏版本、MOD、设置和存档",
            Margin = new Thickness(0, 4, 0, 16),
            IsChecked = false
        };
        layout.Children.Add(deleteGames);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "删除 CIE", IsDefault = true, Padding = new Thickness(14, 7, 14, 7) };
        buttons.Children.Add(cancel); buttons.Children.Add(remove); layout.Children.Add(buttons);
        dialog.Content = layout;
        remove.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) return;

        try
        {
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -ParentPid {Environment.ProcessId} -FromSettings -RemoveProfile";
            if (deleteGames.IsChecked == true) arguments += " -RemoveGameFiles";
            Process.Start(new ProcessStartInfo(powershell, arguments)
            {
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true
            });
            MessageBox.Show(this, "卸载程序已准备就绪。CIE 将关闭，随后会移除程序文件。", "正在卸载 CIE", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.WriteError("启动自卸载程序失败", ex.ToString());
            MessageBox.Show(this, $"无法启动卸载程序：{ex.Message}", "卸载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "故障排查";
        HomePage.Visibility = Visibility.Collapsed;
        VersionsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        InstancesPage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Visible;
        RefreshDiagnosticsButton_Click(this, new RoutedEventArgs());
    }

    private void RefreshDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = VersionComboBox.SelectedItem as InstanceProfile ?? SelectedModsInstance;
        DiagnosticsTextBox.Text = BuildDiagnosticReport(profile, null);
        DiagnosticsStatusText.Text = "已扫描本机环境和最近的启动器/游戏日志。内容只保留在本机，发送给他人前请检查是否包含个人路径或信息。";
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(DiagnosticsTextBox.Text); DiagnosticsStatusText.Text = "诊断摘要已复制；发送前请确认内容不含不想公开的信息。"; }
        catch (Exception ex) { DiagnosticsStatusText.Text = $"无法复制：{ex.Message}"; }
    }

    private void OpenDiagnosticsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "logs");
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private string BuildDiagnosticReport(InstanceProfile? profile, Exception? currentError)
    {
        var lines = new List<string>
        {
            "CIE 本机诊断摘要",
            $"时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"启动器版本：{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}",
            $"系统：{Environment.OSVersion}",
            $"系统架构：{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}; 进程：{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $"可用内存上限：{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MB",
            $".NET：{Environment.Version}"
        };
        if (profile is not null)
        {
            lines.Add($"版本：{profile.DisplayName}（Minecraft {profile.GameVersion} / {profile.Loader} {profile.LoaderVersion}）");
            lines.Add($"配置文件：{profile.VersionJsonPath}");
            lines.Add($"游戏目录：{(profile.Imported && profile.UsePclFiles ? Path.GetDirectoryName(profile.VersionJsonPath) : profile.GameDirectory)}");
        }
        if (currentError is not null) lines.Add($"本次错误：{currentError}");
        var appLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "logs");
        AddLogTail(lines, Path.Combine(appLogs, "last-error.txt"), "CIE 最近错误记录", 120);
        AddLogTail(lines, Path.Combine(appLogs, "last-launch-error.txt"), "CIE 启动错误日志", 120);
        if (profile is not null)
        {
            AddLogTail(lines, Path.Combine(profile.BaseDirectory, "installer-cache", "last-loader-install.log"), "Mod Loader 安装器日志", 120);
            AddLogTail(lines, Path.Combine(profile.BaseDirectory, "logs", "latest.log"), "Minecraft latest.log（末尾）", 100);
        }
        var combined = string.Join(Environment.NewLine, lines);
        lines.Add(""); lines.Add("初步建议：");
        if (Regex.IsMatch(combined, "UnsupportedClassVersionError|class file version", RegexOptions.IgnoreCase))
            lines.Add("• Java 版本可能不匹配：检查该 Minecraft/加载器所需 Java，并让 CIE 为此版本重新配置专用运行时。");
        if (Regex.IsMatch(combined, "OutOfMemoryError|GC overhead limit", RegexOptions.IgnoreCase))
            lines.Add("• 内存不足：关闭其他程序、减少高负载模组，并检查启动器的内存分配设置。");
        if (Regex.IsMatch(combined, "NoClassDefFoundError|ModResolutionException|missing or unsupported mandatory dependencies|depends on", RegexOptions.IgnoreCase))
            lines.Add("• 可能缺少前置或模组版本冲突：核对 Minecraft、加载器和模组要求，并暂时关闭最近添加的模组。");
        if (Regex.IsMatch(combined, "Connection timed out|Name or service not known|HttpRequestException|network", RegexOptions.IgnoreCase))
            lines.Add("• 可能是网络或下载源连接失败：检查网络后重试；不要删除未损坏的版本文件。");
        if (Regex.IsMatch(combined, "HTTP 404|404 \\(NotFound\\)|Response status code.*404", RegexOptions.IgnoreCase))
            lines.Add("• 下载地址返回 404：加载器版本或镜像中的文件可能不存在；请切回官方源重试，并把 last-error.txt 与下载地址发给求助对象。");
        if (Regex.IsMatch(combined, "Mod Loader 安装器失败|last-loader-install.log", RegexOptions.IgnoreCase))
            lines.Add("• 加载器安装器失败：请打开本版本 installer-cache/last-loader-install.log，查看安装器输出的具体原因。");
        if (lines[^1] == "初步建议：")
            lines.Add("• 暂未识别常见原因。可将本摘要和 CIE logs 文件夹中的日志发给可信任的求助对象。");
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddLogTail(List<string> lines, string path, string title, int count)
    {
        lines.Add(""); lines.Add($"--- {title} ---");
        if (!File.Exists(path)) { lines.Add("（尚无此日志）"); return; }
        try { lines.AddRange(File.ReadLines(path).TakeLast(count)); }
        catch (Exception ex) { lines.Add($"（读取失败：{ex.Message}）"); }
    }

    private void ShowDiagnosticsPopup(InstanceProfile? profile, Exception error)
    {
        var diagnostic = BuildDiagnosticReport(profile, error);
        var errorLog = DiagnosticLogger.WriteError("启动或安装问题", diagnostic);
        if (!string.IsNullOrWhiteSpace(errorLog)) diagnostic += Environment.NewLine + Environment.NewLine + "错误记录：" + errorLog;
        var window = new Window { Title = "CIE 故障诊断", Owner = this, Width = 840, Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F3F8FC")! };
        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = "检测到启动或安装问题", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(title, 0); layout.Children.Add(title);
        var report = new TextBox { Text = diagnostic, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Padding = new Thickness(10) };
        Grid.SetRow(report, 1); layout.Children.Add(report);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var copy = new Button { Content = "复制诊断摘要", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        var open = new Button { Content = "打开日志目录", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "关闭", Padding = new Thickness(12, 7, 12, 7), IsCancel = true };
        copy.Click += (_, _) => { Clipboard.SetText(report.Text); copy.Content = "已复制"; };
        open.Click += (_, _) => OpenDiagnosticsFolderButton_Click(this, new RoutedEventArgs());
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(copy); buttons.Children.Add(open); buttons.Children.Add(close);
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons); window.Content = layout;
        window.ShowDialog();
    }

    private void LoadOfflineProfile()
    {
        Directory.CreateDirectory(ProfileDirectory);
        var skinsDirectory = Path.Combine(ProfileDirectory, "skins");
        Directory.CreateDirectory(skinsDirectory);
        var legacySkin = Path.Combine(ProfileDirectory, "offline-skin.png");
        if (File.Exists(legacySkin) && !File.Exists(Path.Combine(skinsDirectory, "offline-skin.png")))
            File.Copy(legacySkin, Path.Combine(skinsDirectory, "offline-skin.png"));
        var namePath = Path.Combine(ProfileDirectory, "offline-name.txt");
        if (File.Exists(namePath))
        {
            var saved = File.ReadAllText(namePath).Trim();
            if (Regex.IsMatch(saved, "^[A-Za-z0-9_]{3,16}$")) _offlineName = saved;
        }
        OfflineNameTextBox.Text = _offlineName;
        LoadPlayerHistory();
        var selectedSkinPath = Path.Combine(ProfileDirectory, "selected-skin.txt");
        var skinPath = File.Exists(selectedSkinPath) ? File.ReadAllText(selectedSkinPath).Trim() : "";
        if (!File.Exists(skinPath)) skinPath = Directory.EnumerateFiles(skinsDirectory, "*.png").FirstOrDefault() ?? "";
        if (File.Exists(skinPath)) ShowSkinPreview(skinPath);
    }

    private void SaveOfflineNameButton_Click(object sender, RoutedEventArgs e)
    {
        var name = OfflineNameTextBox.Text.Trim();
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
        {
            AccountStatusText.Text = "昵称无效：请输入 3–16 位英文字母、数字或下划线。";
            return;
        }
        _offlineName = name;
        File.WriteAllText(Path.Combine(ProfileDirectory, "offline-name.txt"), name);
        AddPlayerHistory(name);
        AccountStatusText.Text = $"当前模式：离线单机（{name}）";
        GameStatusText.Text = $"离线玩家：{name}";
    }

    private void LoadPlayerHistory()
    {
        var path = Path.Combine(ProfileDirectory, "player-history.json");
        List<string> history = [];
        try { if (File.Exists(path)) history = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? []; } catch { }
        PlayerHistoryComboBox.ItemsSource = history;
        PlayerHistoryComboBox.SelectedItem = history.FirstOrDefault(n => n.Equals(_offlineName, StringComparison.OrdinalIgnoreCase));
    }

    private void AddPlayerHistory(string name)
    {
        var path = Path.Combine(ProfileDirectory, "player-history.json");
        List<string> history = [];
        try { if (File.Exists(path)) history = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? []; } catch { }
        history.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        history.Insert(0, name);
        history = history.Where(n => Regex.IsMatch(n, "^[A-Za-z0-9_]{3,16}$")).Take(30).ToList();
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(history));
        PlayerHistoryComboBox.ItemsSource = history;
        PlayerHistoryComboBox.SelectedItem = name;
    }

    private void DeletePlayerHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerHistoryComboBox.SelectedItem is not string name) return;
        var path = Path.Combine(ProfileDirectory, "player-history.json");
        List<string> history = [];
        try { if (File.Exists(path)) history = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? []; } catch { }
        history.RemoveAll(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(history));
        PlayerHistoryComboBox.ItemsSource = history;
        PlayerHistoryComboBox.SelectedItem = null;
        AccountStatusText.Text = $"已从历史列表删除 {name}；当前玩家昵称未更改。";
    }

    private void PlayerHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || PlayerHistoryComboBox.SelectedItem is not string name) return;
        OfflineNameTextBox.Text = name;
        _offlineName = name;
        File.WriteAllText(Path.Combine(ProfileDirectory, "offline-name.txt"), name);
        AccountStatusText.Text = $"当前模式：离线单机（{name}）";
        GameStatusText.Text = $"离线玩家：{name}";
    }

    private void ChooseSkinButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Minecraft 皮肤 PNG", Filter = "PNG 图片 (*.png)|*.png" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var bitmap = new BitmapImage(new Uri(dialog.FileName));
            if (bitmap.PixelWidth != 64 || bitmap.PixelHeight is not (64 or 32))
            {
                SkinStatusText.Text = "皮肤尺寸须为 64×64 或旧版 64×32 PNG。";
                return;
            }
            var skinsDirectory = Path.Combine(ProfileDirectory, "skins");
            Directory.CreateDirectory(skinsDirectory);
            var destination = Path.Combine(skinsDirectory, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, destination, true);
            File.WriteAllText(Path.Combine(ProfileDirectory, "selected-skin.txt"), destination);
            ShowSkinPreview(destination);
        }
        catch (Exception ex) { SkinStatusText.Text = $"无法读取图片：{ex.Message}"; }
    }

    private void ShowSkinPreview(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        SkinPreviewImage.Source = new CroppedBitmap(bitmap, new System.Windows.Int32Rect(8, 8, 8, 8));
        SkinStatusText.Text = $"当前预览：{Path.GetFileName(path)}（离线模式本地预览）";
    }

    private void OpenSkinWardrobeButton_Click(object sender, RoutedEventArgs e)
    {
        var skins = Path.Combine(ProfileDirectory, "skins");
        Directory.CreateDirectory(skins);
        var dialog = new Window { Title = "皮肤衣柜", Owner = this, Width = 520, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#20242D")! };
        var layout = new DockPanel { Margin = new Thickness(18) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var upload = new Button { Content = "上传皮肤 PNG", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "使用所选皮肤", Padding = new Thickness(12, 7, 12, 7) };
        var delete = new Button { Content = "删除皮肤", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); buttons.Children.Add(delete); buttons.Children.Add(upload); buttons.Children.Add(apply); layout.Children.Add(buttons);
        var list = new ListBox { Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#292D36")!, Foreground = System.Windows.Media.Brushes.White };
        layout.Children.Add(list); dialog.Content = layout;
        void Refresh()
        {
            list.Items.Clear();
            var selectedPath = File.Exists(Path.Combine(ProfileDirectory, "selected-skin.txt"))
                ? File.ReadAllText(Path.Combine(ProfileDirectory, "selected-skin.txt")).Trim() : "";
            var fullSelectedPath = string.IsNullOrWhiteSpace(selectedPath) ? "" : Path.GetFullPath(selectedPath);
            foreach (var path in Directory.EnumerateFiles(skins, "*.png").OrderBy(item => Path.GetFileName(item)))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
                row.Children.Add(new Image { Source = CreateSkinPreview(path), Width = 64, Height = 64, Margin = new Thickness(0, 0, 12, 0) });
                row.Children.Add(new TextBlock { Text = Path.GetFileName(path), VerticalAlignment = VerticalAlignment.Center });
                var item = new ListBoxItem { Content = row, Tag = Path.GetFileName(path) };
                list.Items.Add(item);
                if (fullSelectedPath.Length > 0 && Path.GetFullPath(path).Equals(fullSelectedPath, StringComparison.OrdinalIgnoreCase)) list.SelectedItem = item;
            }
        }
        Refresh();
        upload.Click += (_, _) => { ChooseSkinButton_Click(sender, e); Refresh(); };
        apply.Click += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem item || item.Tag is not string file) return;
            var path = Path.Combine(skins, file);
            File.WriteAllText(Path.Combine(ProfileDirectory, "selected-skin.txt"), path);
            ShowSkinPreview(path); dialog.Close();
        };
        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem item || item.Tag is not string file) return;
            var path = Path.GetFullPath(Path.Combine(skins, file));
            if (!Path.GetDirectoryName(path)!.Equals(Path.GetFullPath(skins), StringComparison.OrdinalIgnoreCase)) return;
            if (MessageBox.Show(dialog, $"删除皮肤“{item.Name}”？", "删除皮肤", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            File.Delete(path);
            var selectedSkin = Path.Combine(ProfileDirectory, "selected-skin.txt");
            if (File.Exists(selectedSkin) && Path.GetFullPath(File.ReadAllText(selectedSkin).Trim()).Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                var next = Directory.EnumerateFiles(skins, "*.png").FirstOrDefault();
                if (next is null) { File.Delete(selectedSkin); SkinPreviewImage.Source = null; SkinStatusText.Text = "衣柜中还没有皮肤。"; }
                else { File.WriteAllText(selectedSkin, next); ShowSkinPreview(next); }
            }
            Refresh();
        };
        list.MouseDoubleClick += (_, _) => apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.ShowDialog();
    }

    private static BitmapSource CreateSkinPreview(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.UriSource = new Uri(path); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
        var face = new CroppedBitmap(bitmap, new System.Windows.Int32Rect(8, 8, 8, 8));
        var scaled = new System.Windows.Media.Imaging.TransformedBitmap(face, new System.Windows.Media.ScaleTransform(8, 8));
        scaled.Freeze();
        return scaled;
    }

    private string GetSelectedInstanceId() => (VersionComboBox.SelectedItem as InstanceProfile)?.GameVersion ?? "";

    private InstanceProfile? SelectedInstance => InstancesListView?.SelectedItem as InstanceProfile;

    private InstanceProfile? SelectedModsInstance => ModsInstanceComboBox?.SelectedItem as InstanceProfile;

    private void RefreshHomeInstances()
    {
        var previous = (VersionComboBox.SelectedItem as InstanceProfile)?.Id;
        VersionComboBox.ItemsSource = _instances;
        VersionComboBox.SelectedItem = _instances.FirstOrDefault(p => p.Id == previous)
            ?? _instances.FirstOrDefault();
        var hasInstances = _instances.Count > 0;
        HomeLaunchButton.Content = hasInstances ? "▶   启动游戏" : "前往安装游戏版本";
        GameStatusText.Text = hasInstances
            ? $"已安装 {_instances.Count} 个游戏版本"
            : "还没有已安装版本；点击左侧按钮开始安装。";
    }

    private string GetModsDirectory()
    {
        var profile = SelectedModsInstance ?? SelectedInstance ?? _catalog.FindByGameVersion(GetSelectedInstanceId());
        if (profile?.Imported == true && profile.UsePclFiles)
        {
            var versionMods = Path.Combine(Path.GetDirectoryName(profile.VersionJsonPath)!, "mods");
            var sharedMods = Path.Combine(profile.BaseDirectory, "mods");
            return Directory.Exists(versionMods) || !Directory.Exists(sharedMods) ? versionMods : sharedMods;
        }
        return profile?.GameDirectory is { Length: > 0 } gameDirectory
            ? Path.Combine(gameDirectory, "mods")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "instances", GetSelectedInstanceId(), "mods");
    }

    private async Task LoadModsAsync()
    {
        if (ModsInstanceComboBox.ItemsSource is null || !ReferenceEquals(ModsInstanceComboBox.ItemsSource, _instances))
            ModsInstanceComboBox.ItemsSource = _instances;
        if (SelectedModsInstance is null && _instances.Count > 0)
            ModsInstanceComboBox.SelectedItem = (VersionComboBox.SelectedItem as InstanceProfile) ?? _instances[0];
        var profile = SelectedModsInstance;
        if (profile is null)
        {
            ModsListView.ItemsSource = null;
            ModsSourceComboBox.IsEnabled = false;
            ModsStatusText.Text = "还没有已安装版本。请先到版本管理页安装，或在已安装版本页导入 PCL2。";
            return;
        }
        var sourceIndex = profile.Imported && profile.UsePclFiles ? 1 : 0;
        ModsSourceComboBox.IsEnabled = profile.Imported;
        if (ModsSourceComboBox.SelectedIndex != sourceIndex) ModsSourceComboBox.SelectedIndex = sourceIndex;
        var version = profile.GameVersion;
        var directory = GetModsDirectory();
        Directory.CreateDirectory(directory);
        var mods = Directory.EnumerateFiles(directory)
            .Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(p => InspectMod(p, version)).ToList();
        CheckModDependenciesAndOrder(mods);
        ModsStatusText.Text = "正在读取 MOD 信息并检查可识别的 Modrinth 更新…";
        ModsListView.ItemsSource = mods;
        foreach (var mod in mods.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.ModrinthProjectId)))
        {
            try
            {
                var game = Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(new[] { version }));
                var loaderFilter = GetModrinthLoader(profile);
                var loaderQuery = string.IsNullOrWhiteSpace(loaderFilter) ? "" : $"&loaders={Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(new[] { loaderFilter }))}";
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(mod.ModrinthProjectId!)}/version?game_versions={game}{loaderQuery}");
                request.Headers.UserAgent.ParseAdd("CIE-Launcher/0.1.0 (desktop launcher)");
                using var response = await ModrinthClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                var latest = json.RootElement.EnumerateArray().FirstOrDefault();
                if (latest.ValueKind == System.Text.Json.JsonValueKind.Object
                    && latest.TryGetProperty("version_number", out var latestVersion))
                    mod.UpdateStatus = latestVersion.GetString() == mod.ModVersion ? "已是最新" : $"可更新：{latestVersion.GetString()}";
                else mod.UpdateStatus = "未找到匹配版本";
            }
            catch { mod.UpdateStatus = "暂时无法检查"; }
        }
        ModsListView.Items.Refresh();
        ModsStatusText.Text = mods.Count == 0
            ? $"{profile.DisplayName}：尚未发现 MOD。{(profile.UsePclFiles ? "请将 .jar 放入 PCL 的 .minecraft\\mods 文件夹。" : "请将 .jar 放入 CIE 独立 mods 文件夹。") }"
            : $"{profile.DisplayName}：发现 {mods.Count} 个 MOD（{mods.Count(m => m.Enabled)} 个启用）。{(profile.UsePclFiles ? "正在使用 PCL 原目录，开关会直接影响 PCL 文件。" : "MOD、设置与存档由 CIE 独立管理。")}";
    }

    private static ModEntry InspectMod(string path, string gameVersion)
    {
        var enabled = path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        var displayName = Path.GetFileName(path).Replace(".jar.disabled", "", StringComparison.OrdinalIgnoreCase).Replace(".jar", "", StringComparison.OrdinalIgnoreCase);
        var version = "未知";
        var compatibility = "未发现版本信息";
        var modId = "";
        var dependencies = new List<string>();
        var loadAfter = new List<string>();
        var loadBefore = new List<string>();
        string? modrinthProjectId = null;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var fabric = archive.GetEntry("fabric.mod.json");
            if (fabric is not null)
            {
                using var stream = fabric.Open();
                using var json = System.Text.Json.JsonDocument.Parse(stream);
                var root = json.RootElement;
                if (root.TryGetProperty("id", out var id)) modId = id.GetString() ?? "";
                if (root.TryGetProperty("version", out var ver)) version = ver.ToString();
                if (root.TryGetProperty("depends", out var depends) && depends.TryGetProperty("minecraft", out var mc))
                    compatibility = RangeSupports(mc.ToString(), gameVersion) ? "版本范围匹配" : "Minecraft 版本可能不兼容";
                if (root.TryGetProperty("depends", out depends) && depends.ValueKind == System.Text.Json.JsonValueKind.Object)
                    dependencies.AddRange(depends.EnumerateObject().Select(p => p.Name).Where(id => id is not ("minecraft" or "java")));
                if (root.TryGetProperty("breaks", out var breaks) && breaks.ValueKind == System.Text.Json.JsonValueKind.Object)
                    dependencies.AddRange(breaks.EnumerateObject().Select(p => "!" + p.Name));
                if (root.TryGetProperty("suggests", out var suggests) && suggests.ValueKind == System.Text.Json.JsonValueKind.Object)
                    dependencies.AddRange(suggests.EnumerateObject().Select(p => "?" + p.Name));
                if (root.TryGetProperty("load_after", out var after) && after.ValueKind == System.Text.Json.JsonValueKind.Array)
                    loadAfter.AddRange(after.EnumerateArray().Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String).Select(e => e.GetString()!));
                if (root.TryGetProperty("load_before", out var before) && before.ValueKind == System.Text.Json.JsonValueKind.Array)
                    loadBefore.AddRange(before.EnumerateArray().Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String).Select(e => e.GetString()!));
                if (root.TryGetProperty("update", out var update) && update.TryGetProperty("modrinth", out var projectId))
                    modrinthProjectId = projectId.GetString();
            }
            else if (archive.GetEntry("META-INF/mods.toml") is not null)
            {
                using var stream = archive.GetEntry("META-INF/mods.toml")!.Open();
                using var reader = new StreamReader(stream);
                var toml = reader.ReadToEnd();
                var ownMod = Regex.Match(toml, @"(?is)\[\[mods\]\](?<section>.*?)(?=\[\[|\z)");
                var ownId = Regex.Match(ownMod.Groups["section"].Value, @"(?im)^\s*modId\s*=\s*['""](?<id>[^'""]+)['""]");
                if (ownId.Success) modId = ownId.Groups["id"].Value;
                var mismatch = false;
                var hasMinecraftDependency = false;
                foreach (Match section in Regex.Matches(toml, @"(?is)\[\[dependencies\.[^\]]+\]\](?<section>.*?)(?=\[\[|\z)"))
                {
                    var text = section.Groups["section"].Value;
                    var targetId = Regex.Match(text, @"(?im)^\s*modId\s*=\s*['""](?<id>[^'""]+)['""]");
                    if (!targetId.Success) continue;
                    var depId = targetId.Groups["id"].Value;
                    var range = Regex.Match(text, @"(?im)^\s*versionRange\s*=\s*['""](?<range>[^'""]+)['""]");
                    if (depId.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                    {
                        hasMinecraftDependency = true;
                        if (range.Success && !RangeSupports(range.Groups["range"].Value, gameVersion)) mismatch = true;
                    }
                    else
                    {
                        var mandatory = !Regex.IsMatch(text, @"(?im)^\s*mandatory\s*=\s*false");
                        if (mandatory) dependencies.Add(depId);
                        var ordering = Regex.Match(text, @"(?im)^\s*ordering\s*=\s*['""](?<order>BEFORE|AFTER)['""]");
                        if (ordering.Success && ordering.Groups["order"].Value.Equals("AFTER", StringComparison.OrdinalIgnoreCase)) loadAfter.Add(depId);
                        if (ordering.Success && ordering.Groups["order"].Value.Equals("BEFORE", StringComparison.OrdinalIgnoreCase)) loadBefore.Add(depId);
                    }
                }
                compatibility = mismatch ? "Minecraft 版本不匹配" : hasMinecraftDependency ? "Forge 版本范围匹配" : "Forge MOD；未声明 Minecraft 版本范围";
            }
        }
        catch { compatibility = "MOD 文件读取失败"; }
        return new ModEntry(path, displayName, version, enabled, compatibility, modrinthProjectId, modId, dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), loadAfter.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), loadBefore.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void CheckModDependenciesAndOrder(List<ModEntry> mods)
    {
        var enabledIds = mods.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.ModId))
            .Select(m => m.ModId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (!mod.Enabled) { mod.DependencyStatus = "未启用"; mod.LoadOrderStatus = "未参与加载"; continue; }
            var missing = mod.Dependencies.Where(dep => !dep.StartsWith('!') && !dep.StartsWith('?') && !enabledIds.Contains(dep)).ToList();
            var disabledConflicts = mod.Dependencies.Where(dep => dep.StartsWith('!') && enabledIds.Contains(dep[1..])).Select(dep => dep[1..]).ToList();
            var optionalMissing = mod.Dependencies.Where(dep => dep.StartsWith('?') && !enabledIds.Contains(dep[1..])).ToList();
            mod.DependencyStatus = missing.Count > 0 ? $"缺少：{string.Join(", ", missing)}"
                : disabledConflicts.Count > 0 ? $"冲突：{string.Join(", ", disabledConflicts)}"
                : optionalMissing.Count > 0 ? $"可选未装：{string.Join(", ", optionalMissing.Select(x => x[1..]))}"
                : "依赖齐全";
            var absentOrderTargets = mod.LoadAfter.Concat(mod.LoadBefore).Where(id => !enabledIds.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            mod.LoadOrderStatus = absentOrderTargets.Count > 0 ? $"顺序目标未装：{string.Join(", ", absentOrderTargets)}"
                : mod.LoadAfter.Length + mod.LoadBefore.Length > 0 ? "依赖已标注顺序；由加载器排序"
                : "未声明顺序；由加载器处理";
        }
    }

    private static bool RangeSupports(string range, string gameVersion)
    {
        if (range.Contains(gameVersion, StringComparison.OrdinalIgnoreCase) || range.Contains('*') || range is "" or "*") return true;
        var bracket = Regex.Match(range, @"^[\[(]\s*(?<min>[^,]*)\s*,\s*(?<max>.*?)\s*[\])]$");
        if (bracket.Success)
        {
            var lower = bracket.Groups["min"].Value.Trim();
            var upper = bracket.Groups["max"].Value.Trim();
            var lowerCompare = lower.Length == 0 ? 1 : CompareVersions(gameVersion, lower);
            var upperCompare = upper.Length == 0 ? -1 : CompareVersions(gameVersion, upper);
            return (lower.Length == 0 || lowerCompare > 0 || (range[0] == '[' && lowerCompare == 0))
                && (upper.Length == 0 || upperCompare < 0 || (range[^1] == ']' && upperCompare == 0));
        }
        var constraints = Regex.Matches(range, @"(?<op>>=|<=|>|<|=)\s*(?<version>\d+(?:\.\d+){1,3})");
        if (constraints.Count == 0) return false;
        foreach (Match constraint in constraints)
        {
            var comparison = CompareVersions(gameVersion, constraint.Groups["version"].Value);
            var ok = constraint.Groups["op"].Value switch
            {
                ">=" => comparison >= 0, "<=" => comparison <= 0, ">" => comparison > 0, "<" => comparison < 0, _ => comparison == 0
            };
            if (!ok) return false;
        }
        return true;
    }

    private static int CompareVersions(string left, string right)
    {
        var a = Regex.Matches(left, @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        var b = Regex.Matches(right, @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            var comparison = av.CompareTo(bv);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private async void ModsInstanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && ModsPage.Visibility == Visibility.Visible && ModsInstanceComboBox.SelectedItem is not null)
        {
            await LoadModsAsync();
            _modrinthHits = []; _selectedModrinthVersion = null;
            ModrinthResultsListView.ItemsSource = null;
            ModrinthInstallButton.IsEnabled = false; ModrinthWikiButton.IsEnabled = false;
            ModrinthDependencyText.Text = "目标版本已切换，请重新搜索以获取对应版本和加载器的模组。";
            ModrinthSearchStatusText.Text = $"当前目标：{SelectedModsInstance?.DisplayName}。";
        }
    }

    private async void ModsSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profile = SelectedModsInstance;
        if (!IsLoaded || ModsPage.Visibility != Visibility.Visible || profile is null) return;
        var usePcl = profile.Imported && ModsSourceComboBox.SelectedIndex == 1;
        if (profile.UsePclFiles == usePcl) return;
        profile.UsePclFiles = usePcl;
        _catalog.Save(_instances);
        await LoadModsAsync();
    }

    private async void ToggleModButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ModEntry mod) return;
        var destination = mod.Enabled ? mod.Path + ".disabled" : mod.Path[..^".disabled".Length];
        File.Move(mod.Path, destination, true);
        await LoadModsAsync();
    }

    private void OpenModsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModsInstance is null) { ModsStatusText.Text = "请先选择一个已安装版本。"; return; }
        var directory = GetModsDirectory();
        Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private async void ModrinthSearchButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedModsInstance;
        if (profile is null) { ModrinthSearchStatusText.Text = "请先安装或导入一个版本，再搜索兼容模组。"; return; }
        var query = ModrinthSearchTextBox.Text.Trim();
        if (query.Length == 0) { ModrinthSearchStatusText.Text = "请输入模组名称或关键词。"; return; }
        try
        {
            ModrinthSearchStatusText.Text = $"正在搜索 Minecraft {profile.GameVersion}（{profile.Loader}）兼容模组…";
            var facets = new List<string[]> { new[] { "project_type:mod" }, new[] { $"versions:{profile.GameVersion}" } };
            var loader = GetModrinthLoader(profile);
            if (!string.IsNullOrWhiteSpace(loader)) facets.Add(new[] { $"categories:{loader}" });
            var facetJson = Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(facets));
            var terms = GetModrinthSearchTerms(query);
            var batches = await Task.WhenAll(terms.Select(term => SearchModrinthProjectsAsync(term, facetJson)));
            _modrinthHits = batches.SelectMany(hits => hits)
                .GroupBy(hit => hit.ProjectId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(40)
                .ToList();
            ModrinthResultsListView.ItemsSource = _modrinthHits;
            ModrinthDependencyText.Text = "选择模组查看适配版本与必需前置。";
            ModrinthInstallButton.IsEnabled = false; ModrinthWikiButton.IsEnabled = false; _selectedModrinthVersion = null;
            ModrinthSearchStatusText.Text = _modrinthHits.Count == 0
                ? "没有找到支持此 Minecraft 版本与加载器的模组。"
                : $"找到 {_modrinthHits.Count} 个兼容结果；已搜索中文关键词及常见英文名称，并按当前版本和加载器筛选。";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.WriteError("Modrinth 搜索失败", ex.ToString());
            ModrinthSearchStatusText.Text = $"搜索失败：{ex.Message}。请检查网络后重试。";
        }
    }

    private void ModrinthSearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        ModrinthSearchButton_Click(sender, e);
    }

    private static List<string> GetModrinthSearchTerms(string query)
    {
        var terms = new List<string> { query };
        foreach (var (chinese, aliases) in ModrinthChineseAliases)
        {
            if (query.Contains(chinese, StringComparison.OrdinalIgnoreCase))
                terms.AddRange(aliases);
        }

        return terms.Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static async Task<List<ModrinthProjectHit>> SearchModrinthProjectsAsync(string term, string encodedFacets)
    {
        var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(term)}&facets={encodedFacets}&index=relevance&limit=40";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CIE-Launcher/0.1.0 (desktop launcher)");
        using var response = await ModrinthClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ModrinthProjectHit
        {
            ProjectId = hit.GetProperty("project_id").GetString() ?? "",
            Slug = hit.TryGetProperty("slug", out var slug) ? slug.GetString() ?? "" : "",
            Title = hit.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            Description = hit.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "",
            Author = hit.TryGetProperty("author", out var author) ? author.GetString() ?? "" : "",
            Downloads = hit.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64().ToString("N0") : "—",
            IconUrl = hit.TryGetProperty("icon_url", out var icon) && icon.ValueKind == System.Text.Json.JsonValueKind.String
                ? icon.GetString()
                : null
        }).Where(hit => !string.IsNullOrWhiteSpace(hit.ProjectId)).ToList();
    }

    private async void ModrinthResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModrinthResultsListView.SelectedItem is not ModrinthProjectHit hit || SelectedModsInstance is not { } profile) return;
        _selectedModrinthVersion = null;
        _modrinthPrerequisiteNames = [];
        ModrinthInstallButton.IsEnabled = false; ModrinthWikiButton.IsEnabled = false;
        ModrinthDependencyText.Text = "正在检查适配版本与前置模组…";
        try
        {
            var versionsTask = GetModrinthVersionsAsync(hit.ProjectId, profile);
            var wikiTask = FindMcmodEntryAsync(hit);
            await Task.WhenAll(versionsTask, wikiTask);
            if (!ReferenceEquals(ModrinthResultsListView.SelectedItem, hit)) return;
            hit.WikiUrl = wikiTask.Result;
            ModrinthWikiButton.IsEnabled = !string.IsNullOrWhiteSpace(hit.WikiUrl);
            var versions = versionsTask.Result;
            _selectedModrinthVersion = versions.FirstOrDefault();
            if (_selectedModrinthVersion is null)
            {
                ModrinthDependencyText.Text = "当前 Minecraft 版本与加载器没有可用文件。";
                return;
            }
            var required = _selectedModrinthVersion.Dependencies.Where(d => d.DependencyType == "required").ToList();
            _modrinthPrerequisiteNames = await ResolveDependencyNamesAsync(required);
            if (!ReferenceEquals(ModrinthResultsListView.SelectedItem, hit)) return;
            var optionalCount = _selectedModrinthVersion.Dependencies.Count(d => d.DependencyType == "optional");
            ModrinthDependencyText.Text = $"适配版本：{_selectedModrinthVersion.VersionNumber}（MC {profile.GameVersion} / {profile.Loader}）。"
                + (required.Count == 0 ? "必需前置：无。" : $"必需前置：{string.Join("、", _modrinthPrerequisiteNames)}。")
                + (optionalCount > 0 ? $"另有 {optionalCount} 个可选依赖。" : "");
            ModrinthInstallButton.IsEnabled = !profile.Loader.Equals("原版", StringComparison.OrdinalIgnoreCase);
            if (!ModrinthInstallButton.IsEnabled)
                ModrinthDependencyText.Text += " 请先安装 Fabric、Forge 或其他模组加载器版本。";
        }
        catch (Exception ex)
        {
            ModrinthDependencyText.Text = $"无法读取兼容版本：{ex.Message}";
        }
    }

    private async Task<List<ModrinthVersionInfo>> GetModrinthVersionsAsync(string projectId, InstanceProfile profile)
    {
        var gameVersions = Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(new[] { profile.GameVersion }));
        var loader = GetModrinthLoader(profile);
        var query = string.IsNullOrWhiteSpace(loader) ? $"game_versions={gameVersions}" :
            $"game_versions={gameVersions}&loaders={Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(new[] { loader }))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?{query}");
        request.Headers.UserAgent.ParseAdd("CIE-Launcher/0.1.0 (desktop launcher)");
        using var response = await ModrinthClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await System.Text.Json.JsonSerializer.DeserializeAsync<List<ModrinthVersionInfo>>(
            await response.Content.ReadAsStreamAsync(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private static string GetModrinthLoader(InstanceProfile profile) => profile.Loader.ToLowerInvariant() switch
    {
        "fabric" => "fabric", "forge" => "forge", "quilt" => "quilt", "neoforge" => "neoforge", _ => ""
    };

    private async Task<List<string>> ResolveDependencyNamesAsync(List<ModrinthDependencyInfo> dependencies)
    {
        var names = new List<string>();
        foreach (var dependency in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.ProjectId))
            {
                names.Add(dependency.FileName ?? dependency.VersionId ?? "指定版本前置");
                continue;
            }
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(dependency.ProjectId)}");
                request.Headers.UserAgent.ParseAdd("CIE-Launcher/0.1.0 (desktop launcher)");
                using var response = await ModrinthClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                using var project = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                names.Add(project.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? dependency.ProjectId : dependency.ProjectId);
            }
            catch { names.Add(dependency.FileName ?? dependency.ProjectId); }
        }
        return names;
    }

    private async void ModrinthInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedModsInstance is not { } profile || _selectedModrinthVersion is null) return;
        try
        {
            var includePrerequisites = true;
            if (_modrinthPrerequisiteNames.Count > 0)
            {
                includePrerequisites = MessageBox.Show(this,
                    $"此模组需要以下前置：\n\n{string.Join("、", _modrinthPrerequisiteNames)}\n\n是否一并下载？",
                    "需要前置模组", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            }
            ModrinthInstallButton.IsEnabled = false;
            ModrinthSearchStatusText.Text = includePrerequisites ? "正在安装模组和所选前置…" : "正在安装所选模组…";
            var count = await InstallModrinthDependencyTreeAsync(_selectedModrinthVersion, profile,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), includePrerequisites);
            ModrinthSearchStatusText.Text = $"安装完成：写入 {count} 个模组文件到 {profile.DisplayName}。";
            await LoadModsAsync();
        }
        catch (Exception ex) { DiagnosticLogger.WriteError("Modrinth 模组安装失败", ex.ToString()); ModrinthSearchStatusText.Text = $"安装失败：{ex.Message}"; }
        finally { ModrinthInstallButton.IsEnabled = _selectedModrinthVersion is not null; }
    }

    private async Task<int> InstallModrinthDependencyTreeAsync(ModrinthVersionInfo version, InstanceProfile profile, HashSet<string> visited, bool includeRequiredDependencies = true)
    {
        var identity = version.ProjectId ?? version.Id;
        if (string.IsNullOrWhiteSpace(identity) || !visited.Add(identity)) return 0;
        var count = 0;
        foreach (var dependency in includeRequiredDependencies ? version.Dependencies.Where(d => d.DependencyType == "required") : [])
        {
            ModrinthVersionInfo? dependencyVersion = null;
            if (!string.IsNullOrWhiteSpace(dependency.VersionId))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(dependency.VersionId)}");
                request.Headers.UserAgent.ParseAdd("CIE-Launcher/0.1.0 (desktop launcher)");
                using var response = await ModrinthClient.SendAsync(request); response.EnsureSuccessStatusCode();
                dependencyVersion = await System.Text.Json.JsonSerializer.DeserializeAsync<ModrinthVersionInfo>(
                    await response.Content.ReadAsStreamAsync(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (dependencyVersion is not null && !IsVersionCompatible(dependencyVersion, profile))
                    throw new InvalidDataException($"前置 {dependency.FileName ?? dependency.ProjectId ?? dependency.VersionId} 没有适配此 Minecraft/加载器的版本。");
            }
            else if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
                dependencyVersion = (await GetModrinthVersionsAsync(dependency.ProjectId, profile)).FirstOrDefault();
            if (dependencyVersion is null)
                throw new InvalidDataException($"无法找到必需前置：{dependency.FileName ?? dependency.ProjectId ?? dependency.VersionId ?? "未知"}。");
            count += await InstallModrinthDependencyTreeAsync(dependencyVersion, profile, visited, true);
        }
        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault(f => f.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
        if (file is null || string.IsNullOrWhiteSpace(file.Url))
            throw new InvalidDataException($"Modrinth 版本 {version.VersionNumber} 没有可安装的 JAR 文件。");
        await DownloadModrinthJarAsync(file, GetModsDirectory());
        return count + 1;
    }

    private static bool IsVersionCompatible(ModrinthVersionInfo version, InstanceProfile profile)
    {
        if (!version.GameVersions.Contains(profile.GameVersion, StringComparer.OrdinalIgnoreCase)) return false;
        var loader = GetModrinthLoader(profile);
        return string.IsNullOrEmpty(loader) || version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task DownloadModrinthJarAsync(ModrinthFileInfo file, string modsDirectory)
    {
        Directory.CreateDirectory(modsDirectory);
        if (!file.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Modrinth 返回了非 JAR 文件：{file.FileName}");
        var path = Path.Combine(modsDirectory, Path.GetFileName(file.FileName));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var temp = path + ".part";
        try
        {
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await response.Content.CopyToAsync(output);
            var bytes = await File.ReadAllBytesAsync(temp);
            if (!string.IsNullOrWhiteSpace(file.Hashes?.Sha512)
                && !Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(bytes)).Equals(file.Hashes.Sha512, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"模组文件 SHA-512 校验失败：{file.FileName}");
            if (!string.IsNullOrWhiteSpace(file.Hashes?.Sha1)
                && !Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).Equals(file.Hashes.Sha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"模组文件 SHA-1 校验失败：{file.FileName}");
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void ModrinthWikiButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModrinthResultsListView.SelectedItem is not ModrinthProjectHit { WikiUrl: { Length: > 0 } url }) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static async Task<string?> FindMcmodEntryAsync(ModrinthProjectHit hit)
    {
        var queries = new[] { hit.Title, hit.Slug }.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var query in queries)
        {
            try
            {
                using var response = await McmodClient.GetAsync($"https://search.mcmod.cn/s?key={Uri.EscapeDataString(query)}&filter=1&mold=1");
                if (!response.IsSuccessStatusCode) continue;
                var html = await response.Content.ReadAsStringAsync();
                var anchors = Regex.Matches(html,
                    "<a[^>]+href=[\\\"'](?<url>[^\\\"']*/class/\\d+\\.html[^\\\"']*)[\\\"'][^>]*>(?<title>.*?)</a>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match anchor in anchors)
                {
                    if (!anchor.Success) continue;
                    var title = System.Net.WebUtility.HtmlDecode(Regex.Replace(anchor.Groups["title"].Value, "<.*?>", "")).Trim();
                    var normTitle = Regex.Replace(title, "[^\\p{L}\\p{N}]", "").ToLowerInvariant();
                    var normQuery = Regex.Replace(query, "[^\\p{L}\\p{N}]", "").ToLowerInvariant();
                    if (normTitle.Length > 0 && normQuery.Length > 2 && (normTitle.Contains(normQuery) || normQuery.Contains(normTitle)))
                        return Uri.TryCreate(anchor.Groups["url"].Value, UriKind.Absolute, out var absolute)
                            ? absolute.ToString() : new Uri(new Uri("https://www.mcmod.cn"), anchor.Groups["url"].Value).ToString();
                }
            }
            catch { }
        }
        return null;
    }

    private void ModsPage_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ModsPage_Drop(object sender, DragEventArgs e)
    {
        if (SelectedModsInstance is not { } profile) { ModsStatusText.Text = "先选择要安装模组的游戏版本，再拖入 .jar 文件。"; return; }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var jars = files.Where(f => File.Exists(f) && f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (jars.Length == 0) { ModsStatusText.Text = "这里只接受 .jar 模组文件。"; return; }
        try
        {
            var target = GetModsDirectory(); Directory.CreateDirectory(target);
            foreach (var jar in jars) File.Copy(jar, Path.Combine(target, Path.GetFileName(jar)), true);
            ModsStatusText.Text = $"已安装 {jars.Length} 个模组到 {profile.DisplayName}。正在刷新列表…";
            await LoadModsAsync();
        }
        catch (Exception ex) { DiagnosticLogger.WriteError("本地模组安装失败", ex.ToString()); ModsStatusText.Text = $"安装模组失败：{ex.Message}"; }
    }

    private void HomePage_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ImportModpackButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Minecraft 整合包",
            Filter = "整合包 ZIP 或 MRPACK (*.zip;*.mrpack)|*.zip;*.mrpack|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) _ = InstallModrinthPackAsync(dialog.FileName);
    }

    private void ModpackDrop_DragOver(object sender, DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
        e.Effects = files?.Any(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ModpackDrop_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var archive = files.FirstOrDefault(path => File.Exists(path)
            && (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)));
        if (archive is null) { InstallStatusText.Text = "请拖入 .zip 或 .mrpack 整合包。"; return; }
        await InstallModrinthPackAsync(archive);
    }

    private async void HomePage_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var archive = files.FirstOrDefault(f => File.Exists(f)
            && (f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)));
        if (archive is null) { GameStatusText.Text = "主页支持拖入 .zip 或 .mrpack 整合包。"; return; }
        await InstallModrinthPackAsync(archive);
    }

    private async Task InstallModrinthPackAsync(string archive)
    {
        GameStatusText.Text = "正在检查整合包…";
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            var indexEntry = zip.GetEntry("modrinth.index.json");
            if (indexEntry is null)
            {
                if (zip.GetEntry("manifest.json") is { } curseForgeManifest)
                {
                    await InstallCurseForgePackAsync(zip, curseForgeManifest);
                    return;
                }
                throw new InvalidDataException("ZIP 中未找到 Modrinth 或 CurseForge 整合包清单。请选择正式导出的 .mrpack/.zip，或包含 manifest.json 的整合包。");
            }
            using var indexStream = indexEntry.Open();
            using var index = await System.Text.Json.JsonDocument.ParseAsync(indexStream);
            var root = index.RootElement;
            if (!root.TryGetProperty("game", out var game) || game.GetString() != "minecraft"
                || !root.TryGetProperty("dependencies", out var dependencies)
                || !dependencies.TryGetProperty("minecraft", out var mcValue))
                throw new InvalidDataException("整合包没有有效的 Minecraft 版本信息。");
            var gameVersion = mcValue.GetString() ?? throw new InvalidDataException("整合包的 Minecraft 版本为空。");
            if (_versions.Count == 0) await RefreshVersionsAsync();
            var metadata = _versions.FirstOrDefault(v => v.Id == gameVersion)
                ?? throw new InvalidDataException($"当前版本清单中找不到 Minecraft {gameVersion}。请先刷新版本清单后重试。");
            var loader = "原版";
            string? requestedLoaderVersion = null;
            if (dependencies.TryGetProperty("fabric-loader", out var fabricVersion))
            { loader = "Fabric"; requestedLoaderVersion = fabricVersion.GetString(); }
            else if (dependencies.TryGetProperty("forge", out var forgeVersion))
            {
                loader = "Forge";
                requestedLoaderVersion = forgeVersion.GetString();
                var prefix = gameVersion + "-";
                if (requestedLoaderVersion?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
                    requestedLoaderVersion = requestedLoaderVersion[prefix.Length..];
            }
            else if (dependencies.TryGetProperty("quilt-loader", out _) || dependencies.TryGetProperty("neoforge", out _))
                throw new InvalidDataException("此整合包使用 Quilt 或 NeoForge；当前版本的 CIE 仅可自动安装原版、Fabric、Forge 整合包。暂未更改本机文件。");

            var packName = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var instanceKey = $"pack-{Guid.NewGuid():N}";
            var packProgress = new Progress<InstallProgress>(p => GameStatusText.Text = $"安装整合包：{p.Message}");
            await _installer.InstallAsync(gameVersion, metadata.Url, packProgress, loader, instanceKey, requestedLoaderVersion, _installRoot);
            var profile = _catalog.RegisterLocal(gameVersion, loader, "", instanceKey, _installRoot);
            if (!string.IsNullOrWhiteSpace(packName)) profile.CustomName = packName.Trim();

            var instanceRoot = profile.BaseDirectory;
            var overridesPrefix = root.TryGetProperty("overrides", out var overridesElement) ? overridesElement.GetString() ?? "overrides" : "overrides";
            foreach (var entry in zip.Entries.Where(z => z.FullName.StartsWith(overridesPrefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var relative = entry.FullName[(overridesPrefix.TrimEnd('/').Length + 1)..];
                var destination = SafePackPath(instanceRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(output);
            }

            var requiredFiles = 0;
            if (root.TryGetProperty("files", out var packFiles) && packFiles.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                foreach (var file in packFiles.EnumerateArray())
                {
                    if (file.TryGetProperty("env", out var environment) && environment.ValueKind == System.Text.Json.JsonValueKind.Object
                        && environment.TryGetProperty("client", out var clientEnvironment)
                        && (clientEnvironment.GetString() == "unsupported" || clientEnvironment.GetString() == "optional"))
                        continue;
                    if (!file.TryGetProperty("path", out var pathElement) || !file.TryGetProperty("downloads", out var urls)
                        || urls.ValueKind != System.Text.Json.JsonValueKind.Array || urls.GetArrayLength() == 0)
                        throw new InvalidDataException("整合包有无法自动下载的模组文件；未能完整安装。请尝试使用 Modrinth 导出的完整 .mrpack。");
                    var relative = pathElement.GetString() ?? throw new InvalidDataException("整合包中的文件路径为空。");
                    var destination = SafePackPath(instanceRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Exception? lastError = null;
                    foreach (var url in urls.EnumerateArray())
                    {
                        try
                        {
                            using var response = await http.GetAsync(url.GetString(), HttpCompletionOption.ResponseHeadersRead);
                            response.EnsureSuccessStatusCode();
                            var bytes = await response.Content.ReadAsByteArrayAsync();
                            if (file.TryGetProperty("hashes", out var hashes) && hashes.TryGetProperty("sha512", out var hash))
                            {
                                var actual = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(bytes)).ToLowerInvariant();
                                if (!actual.Equals(hash.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"文件校验失败：{Path.GetFileName(relative)}");
                            }
                            await File.WriteAllBytesAsync(destination, bytes); lastError = null; requiredFiles++; break;
                        }
                        catch (Exception ex) { lastError = ex; }
                    }
                    if (lastError is not null) throw new InvalidDataException($"无法下载整合包文件 {Path.GetFileName(relative)}：{lastError.Message}", lastError);
                }
            }
            _catalog.Save(_catalog.Load().Select(p => p.Id == profile.Id ? profile : p));
            LoadInstances();
            GameStatusText.Text = $"整合包“{profile.DisplayName}”已安装（Minecraft {gameVersion}，{loader}，下载 {requiredFiles} 个文件）。";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.WriteError("整合包导入失败", ex.ToString());
            GameStatusText.Text = $"整合包安装失败：{ex.Message}";
            ShowDiagnosticsPopup(null, ex);
        }
    }

    private async Task InstallCurseForgePackAsync(ZipArchive zip, ZipArchiveEntry manifestEntry)
    {
        using var stream = manifestEntry.Open();
        using var manifest = await System.Text.Json.JsonDocument.ParseAsync(stream);
        var root = manifest.RootElement;
        var gameVersion = root.GetProperty("minecraft").GetProperty("version").GetString();
        if (string.IsNullOrWhiteSpace(gameVersion)) throw new InvalidDataException("CurseForge 清单没有 Minecraft 版本号。");
        var loader = "原版";
        string? loaderVersion = null;
        if (root.GetProperty("minecraft").TryGetProperty("modLoaders", out var modLoaders))
        {
            foreach (var modLoader in modLoaders.EnumerateArray())
            {
                var id = modLoader.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
                if (id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase)) { loader = "Fabric"; loaderVersion = id[7..]; break; }
                if (id.StartsWith("forge-", StringComparison.OrdinalIgnoreCase)) { loader = "Forge"; loaderVersion = id[6..]; break; }
                if (id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase) || id.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"目前不能自动安装 {id.Split('-')[0]} 整合包；尚未修改本机文件。");
            }
        }

        var requiredCount = root.TryGetProperty("files", out var files) && files.ValueKind == System.Text.Json.JsonValueKind.Array
            ? files.EnumerateArray().Count(file => !file.TryGetProperty("required", out var required) || required.GetBoolean())
            : 0;
        var bundledMods = zip.Entries.Where(entry => entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.Replace('\\', '/').Split('/').Any(part => part.Equals("mods", StringComparison.OrdinalIgnoreCase))).ToList();
        if (requiredCount > bundledMods.Count)
            throw new InvalidDataException($"此 CurseForge ZIP 只包含 {requiredCount} 个模组的下载清单，但压缩包内没有完整包含这些 JAR。为保护文件未开始安装。请使用包含实际 mods 文件夹的 ZIP，或下载 Modrinth 格式的 .mrpack。CIE 不会绕过 CurseForge 官方 API 的授权限制。");

        if (_versions.Count == 0) await RefreshVersionsAsync();
        var metadata = _versions.FirstOrDefault(version => version.Id == gameVersion)
            ?? throw new InvalidDataException($"找不到 Minecraft {gameVersion} 的版本信息，请刷新版本清单后重试。");
        var packName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        var instanceKey = $"curseforge-{Guid.NewGuid():N}";
        var progress = new Progress<InstallProgress>(state => GameStatusText.Text = $"安装整合包：{state.Message}");
        await _installer.InstallAsync(gameVersion, metadata.Url, progress, loader, instanceKey, loaderVersion, _installRoot);
        var profile = _catalog.RegisterLocal(gameVersion, loader, loaderVersion ?? "", instanceKey, _installRoot);
        if (!string.IsNullOrWhiteSpace(packName)) profile.CustomName = packName.Trim();

        var overridesPrefix = root.TryGetProperty("overrides", out var overrides) ? overrides.GetString() ?? "overrides" : "overrides";
        await CopyPackPrefixAsync(zip, overridesPrefix, profile.BaseDirectory);
        foreach (var entry in bundledMods)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var modIndex = normalized.LastIndexOf("mods/", StringComparison.OrdinalIgnoreCase);
            var relative = normalized[(modIndex + 5)..];
            if (relative.Length == 0) continue;
            var destination = SafePackPath(Path.Combine(profile.BaseDirectory, "mods"), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output);
        }
        _catalog.Save(_catalog.Load().Select(item => item.Id == profile.Id ? profile : item));
        LoadInstances();
        GameStatusText.Text = $"整合包“{profile.DisplayName}”已安装（Minecraft {gameVersion}，{loader}，模组 {bundledMods.Count} 个）。";
    }

    private static async Task CopyPackPrefixAsync(ZipArchive zip, string prefix, string destinationRoot)
    {
        var normalizedPrefix = prefix.Trim('/', '\\');
        foreach (var entry in zip.Entries.Where(item => item.FullName.Replace('\\', '/').StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var normalized = entry.FullName.Replace('\\', '/');
            var relative = normalized[(normalizedPrefix.Length + 1)..];
            var destination = SafePackPath(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(output);
        }
    }

    private static string SafePackPath(string root, string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("整合包包含越界文件路径，已停止安装。");
        return fullPath;
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowHomePage();

    private void VersionsButton_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "版本管理";
        HomePage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        VersionsPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        InstancesPage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Collapsed;
    }

    private void InstancesButton_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "已安装版本";
        HomePage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        VersionsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        InstancesPage.Visibility = Visibility.Visible;
        PlaceholderPage.Visibility = Visibility.Collapsed;
        LoadInstances();
    }

    private void LoadInstances()
    {
        var savedProfiles = _catalog.Load();
        var profilesChanged = false;
        var defaultInstancesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "instances");
        foreach (var profile in savedProfiles)
        {
            var ownedRoot = profile.Imported ? defaultInstancesRoot
                : string.IsNullOrWhiteSpace(profile.InstallRootDirectory) ? defaultInstancesRoot : profile.InstallRootDirectory;
            try
            {
                if (!File.Exists(profile.VersionJsonPath)) continue;
                if (!profile.Imported && !SamePath(profile.GameDirectory, profile.BaseDirectory)) continue;
                if (!IsPathInside(profile.GameDirectory, ownedRoot)) continue;
                Directory.CreateDirectory(profile.GameDirectory);
                var ownershipMarker = Path.Combine(profile.GameDirectory, ".cie-instance-marker");
                if (!File.Exists(ownershipMarker)) File.WriteAllText(ownershipMarker, "CIE managed instance");
            }
            catch { }
        }
        foreach (var profile in savedProfiles.Where(p => p.Imported))
        {
            if (_catalog.RefreshImportedMetadata(profile)) profilesChanged = true;
            if (!profile.SourceModeConfigured)
            {
                profile.UsePclFiles = true;
                profile.SourceModeConfigured = true;
                profilesChanged = true;
            }
        }
        if (profilesChanged) _catalog.Save(savedProfiles);
        _instances = savedProfiles
            .Where(p => File.Exists(p.VersionJsonPath))
            .OrderByDescending(p => p.LastPlayed)
            .ThenBy(p => p.GameVersion, StringComparer.OrdinalIgnoreCase)
            .ToList();
        InstancesListView.ItemsSource = _instances;
        RefreshInstanceFolderChoices();
        RefreshHomeInstances();
        ModsInstanceComboBox.ItemsSource = _instances;
        if (SelectedModsInstance is null || !_instances.Any(p => p.Id == SelectedModsInstance.Id))
            ModsInstanceComboBox.SelectedItem = _instances.FirstOrDefault();
        InstancesStatusText.Text = _instances.Count == 0
            ? "还没有已安装版本。请先安装，或导入 PCL2 的 .minecraft 文件夹。"
            : $"已发现 {_instances.Count} 个可启动版本。导入时会为每个版本复制一份独立 MOD 文件夹。";
    }

    private void ImportMinecraftFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 PCL2 的 .minecraft 文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = _catalog.ImportMinecraftFolder(dialog.FolderName);
            LoadInstances();
            InstancesStatusText.Text = $"已导入 {imported.Count} 个版本。原 PCL2 文件未移动或删除；MOD 已复制到各自独立目录。";
        }
        catch (Exception ex) { DiagnosticLogger.WriteError("PCL 版本导入失败", ex.ToString()); InstancesStatusText.Text = $"导入失败：{ex.Message}"; }
    }

    private void InstancesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedInstance is { } profile)
        {
            ModsStatusText.Text = $"当前管理：{profile.DisplayName}（MOD 与其他实例隔离）";
            if (profile.Imported)
            {
                var gameDirectory = profile.UsePclFiles ? Path.GetDirectoryName(profile.VersionJsonPath)! : profile.GameDirectory;
                InstancesStatusText.Text = profile.UsePclFiles
                    ? $"PCL 版本目录：{gameDirectory}。将沿用此版本的 MOD、设置、存档和 PCL Java。原 PCL 文件不会被卸载。"
                    : $"来自 PCL2：{profile.BaseDirectory}。当前使用 CIE 独立游戏目录与 MOD 文件。";
            }
            else
            {
                InstancesStatusText.Text = $"CIE 独立版本：{profile.DisplayName}。MOD、设置、存档与 Java 均按版本隔离。";
            }
        }
    }

    private string InstanceFoldersPath => Path.Combine(ProfileDirectory, "version-folders.json");

    private List<string> LoadInstanceFolders()
    {
        try { return File.Exists(InstanceFoldersPath) ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(InstanceFoldersPath)) ?? [] : []; }
        catch { return []; }
    }

    private void RefreshInstanceFolderChoices()
    {
        if (InstanceFolderComboBox is null) return;
        var selected = InstanceFolderComboBox.SelectedItem as string ?? "全部版本";
        var folders = LoadInstanceFolders();
        var choices = new[] { "全部版本" }.Concat(folders).ToList();
        InstanceFolderComboBox.ItemsSource = choices;
        InstanceFolderComboBox.SelectedItem = choices.Contains(selected) ? selected : "全部版本";
        FilterInstalledInstances();
    }

    private void InstanceFolderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => FilterInstalledInstances();

    private void FilterInstalledInstances()
    {
        if (InstancesListView is null || InstanceFolderComboBox?.SelectedItem is not string folder) return;
        var selectedId = SelectedInstance?.Id;
        var visible = folder == "全部版本" ? _instances : _instances.Where(profile => profile.VirtualFolder == folder).ToList();
        InstancesListView.ItemsSource = visible;
        InstancesListView.SelectedItem = visible.FirstOrDefault(profile => profile.Id == selectedId);
    }

    private void CreateInstanceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptText("新建虚拟文件夹", "输入分类名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var folders = LoadInstanceFolders();
        if (!folders.Contains(name, StringComparer.OrdinalIgnoreCase)) folders.Add(name);
        File.WriteAllText(InstanceFoldersPath, System.Text.Json.JsonSerializer.Serialize(folders));
        RefreshInstanceFolderChoices();
        InstanceFolderComboBox.SelectedItem = name;
        InstancesStatusText.Text = $"已创建分类“{name}”。选择版本后点击“归入分类”即可整理。";
    }

    private void AssignInstanceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance is not { } profile) { InstancesStatusText.Text = "请先选择一个版本。"; return; }
        if (InstanceFolderComboBox.SelectedItem is not string folder) return;
        profile.VirtualFolder = folder == "全部版本" ? "" : folder;
        _catalog.Save(_instances);
        InstancesListView.Items.Refresh(); VersionComboBox.Items.Refresh(); ModsInstanceComboBox.Items.Refresh();
        FilterInstalledInstances();
        InstancesStatusText.Text = string.IsNullOrEmpty(profile.VirtualFolder)
            ? $"已将 {profile.DisplayName} 移出分类。" : $"已将版本归入“{profile.VirtualFolder}”。";
    }

    private void RenameInstanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance is not { } profile) { InstancesStatusText.Text = "请先选择一个版本。"; return; }
        var name = PromptText("重命名版本", "输入启动器中显示的名称：", profile.CustomName);
        if (name is null) return;
        profile.CustomName = name.Trim();
        _catalog.Save(_instances);
        InstancesListView.Items.Refresh(); VersionComboBox.Items.Refresh(); ModsInstanceComboBox.Items.Refresh();
        InstancesStatusText.Text = $"版本显示名称已更新为“{profile.DisplayName}”。磁盘上的版本文件没有移动或改名。";
    }

    private string? PromptText(string title, string label, string initial = "")
    {
        var window = new Window { Title = title, Owner = this, Width = 390, Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        var text = new TextBox { Text = initial, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "确定", IsDefault = true, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(12, 5, 12, 5) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons); window.Content = panel;
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? text.Text : null;
    }

    private void OpenInstanceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance is not { } profile) { InstancesStatusText.Text = "请先选择一个已安装版本。"; return; }
        var directory = profile.Imported && profile.UsePclFiles
            ? Path.GetDirectoryName(profile.VersionJsonPath)!
            : profile.Imported ? profile.BaseDirectory : profile.GameDirectory;
        Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void UninstallSelectedInstanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance is not { } profile)
        {
            InstancesStatusText.Text = "请先选择要卸载的版本。";
            return;
        }

        var prompt = profile.Imported
            ? $"从 CIE 移除“{profile.DisplayName}”吗？\n\n只会移除 CIE 的版本记录和导入副本。PCL 原目录、MOD、存档与设置都会保留。"
            : $"卸载“{profile.DisplayName}”吗？\n\n会删除 CIE 为该版本下载的游戏文件和本地数据。此操作无法撤销。";
        if (MessageBox.Show(this, prompt, "确认卸载版本", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var profiles = _catalog.Load();
            var instancesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "instances");
            if (profile.Imported)
            {
                DeleteCieOwnedDirectory(profile.GameDirectory, instancesRoot);
            }
            else
            {
                var ownedInstallRoot = string.IsNullOrWhiteSpace(profile.InstallRootDirectory) ? instancesRoot : profile.InstallRootDirectory;
                var remainingProfiles = profiles.Where(p => p.Id != profile.Id && !p.Imported).ToList();
                var sharedRoot = remainingProfiles.Any(p => SamePath(p.BaseDirectory, profile.BaseDirectory));
                if (sharedRoot)
                {
                    var versionDirectory = Path.GetDirectoryName(profile.VersionJsonPath)!;
                    var versionsRoot = Path.Combine(profile.BaseDirectory, "versions");
                    if (!IsPathInside(versionDirectory, versionsRoot))
                        throw new InvalidDataException("版本目录不在 CIE 的游戏文件夹内，已取消卸载以保护其他文件。");
                    if (Directory.Exists(versionDirectory)) Directory.Delete(versionDirectory, true);
                }
                else
                {
                    DeleteCieOwnedDirectory(profile.BaseDirectory, ownedInstallRoot);
                }
            }

            _catalog.Save(profiles.Where(p => p.Id != profile.Id));
            LoadInstances();
            InstancesStatusText.Text = profile.Imported
                ? $"已从 CIE 移除 {profile.DisplayName}；PCL 原文件保留完整。"
                : $"已卸载 {profile.DisplayName}。";
        }
        catch (Exception ex)
        {
            InstancesStatusText.Text = $"卸载失败：{ex.Message}";
        }
    }

    private static void DeleteCieOwnedDirectory(string path, string instancesRoot)
    {
        if (!IsPathInside(path, instancesRoot))
            throw new InvalidDataException("目标文件夹不属于 CIE 的版本目录，已取消卸载以保护原文件。");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static bool IsPathInside(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        // Only allow deleting descendants; the ownership root itself must never be removable.
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string first, string second) =>
        Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private async void LaunchSelectedInstanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance is not { } profile) { InstancesStatusText.Text = "请先选择一个已安装版本。"; return; }
        await LaunchProfileAsync(profile);
    }

    private async Task LaunchProfileAsync(InstanceProfile profile)
    {
        if (!Regex.IsMatch(_offlineName, "^[A-Za-z0-9_]{3,16}$"))
        {
            GameStatusText.Text = "请先在玩家资料页设置有效的离线昵称。";
            return;
        }
        try
        {
            GameStatusText.Text = $"正在启动 {profile.DisplayName}…";
            if (!profile.Imported || !profile.UsePclFiles)
            {
                var javaProgress = new Progress<InstallProgress>(state => GameStatusText.Text = state.Message);
                await _installer.EnsureJavaRuntimeAsync(profile, javaProgress);
            }
            var maxFps = GameFpsComboBox.SelectedItem is ComboBoxItem option && int.TryParse(option.Tag?.ToString(), out var parsedFps) ? parsedFps : 144;
            await _launchService.LaunchOfflineAsync(profile, _offlineName,
                performance: new GamePerformanceSettings(maxFps, OptimizePerformanceCheckBox.IsChecked == true));
            profile.LastPlayed = DateTime.Now;
            _catalog.Save(_catalog.Load().Select(p => p.Id == profile.Id ? profile : p));
            LoadInstances();
            GameStatusText.Text = $"已启动 {profile.DisplayName}，玩家 {_offlineName}";
        }
        catch (Exception ex)
        {
            GameStatusText.Text = "启动失败。";
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "logs");
                Directory.CreateDirectory(logDirectory);
                File.WriteAllText(Path.Combine(logDirectory, "last-launch-error.txt"), ex.ToString());
            }
            catch { }
            ShowDiagnosticsPopup(profile, ex);
        }
    }

    private void DownloadsButton_Click(object sender, RoutedEventArgs e) => ShowPlaceholderPage(
        "下载管理", "在这里查看游戏文件下载进度。\n\n下载管理功能正在开发中。");

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (VersionComboBox.SelectedItem is InstanceProfile profile)
        {
            await LaunchProfileAsync(profile);
        }
        else
        {
            VersionsButton_Click(sender, e);
            if (_versions.Count == 0) await RefreshVersionsAsync();
            InstallStatusText.Text = "这里可以选择要安装的游戏版本。安装后回到主页启动。";
        }
    }

    private void ShowHomePage()
    {
        PageTitle.Text = "主页";
        HomePage.Visibility = Visibility.Visible;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        VersionsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        InstancesPage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Collapsed;
    }

    private void ShowPlaceholderPage(string title, string description)
    {
        PageTitle.Text = title;
        PlaceholderTitle.Text = title;
        PlaceholderDescription.Text = description;
        HomePage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        VersionsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        InstancesPage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Visible;
    }
}

internal sealed record ModEntry(string Path, string Name, string ModVersion, bool Enabled, string Compatibility, string? ModrinthProjectId,
    string ModId, string[] Dependencies, string[] LoadAfter, string[] LoadBefore)
{
    public string UpdateStatus { get; set; } = string.IsNullOrWhiteSpace(ModrinthProjectId) ? "手动检查" : "待检查";
    public string DependencyStatus { get; set; } = "待检查";
    public string LoadOrderStatus { get; set; } = "待检查";
    public string EnabledLabel => Enabled ? "已启用" : "已关闭";
    public string ToggleLabel => Enabled ? "关闭" : "启用";
    public string Summary => $"{ModId} · {ModVersion}{Environment.NewLine}{Compatibility} · {DependencyStatus} · {LoadOrderStatus}";
}

internal sealed class ModrinthProjectHit
{
    public string ProjectId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string Downloads { get; set; } = "";
    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }
    public string? WikiUrl { get; set; }
}

internal sealed class ModrinthVersionInfo
{
    public string Id { get; set; } = "";
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }
    public string Name { get; set; } = "";
    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = "";
    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];
    public List<string> Loaders { get; set; } = [];
    public List<ModrinthDependencyInfo> Dependencies { get; set; } = [];
    public List<ModrinthFileInfo> Files { get; set; } = [];
}

internal sealed class ModrinthDependencyInfo
{
    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }
    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }
    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = "";
}

internal sealed class ModrinthFileInfo
{
    public string Url { get; set; } = "";
    [JsonPropertyName("filename")]
    public string FileName { get; set; } = "";
    public bool Primary { get; set; }
    public ModrinthHashes? Hashes { get; set; } = new();
}

internal sealed class ModrinthHashes
{
    public string? Sha1 { get; set; }
    public string? Sha512 { get; set; }
}

public sealed record InstallProgress(string Message, int Percent);

internal sealed class MinecraftInstaller
{
    private const string RuntimeIndexUrl = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIE", "instances");
    private static readonly HttpClient InstallerClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadPathLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task InstallAsync(string versionId, string metadataUrl, IProgress<InstallProgress> progress, string loader = "原版", string? instanceKey = null, string? requestedLoaderVersion = null, string? installRoot = null)
    {
        progress.Report(new("读取版本信息…", 1));
        using var metadataResponse = await DownloadSource.GetSuccessfulResponseAsync(Client, metadataUrl);
        var metadataJson = await metadataResponse.Content.ReadAsStringAsync();
        var metadata = System.Text.Json.JsonSerializer.Deserialize<VersionDetails>(metadataJson, JsonOptions)
            ?? throw new InvalidDataException("Minecraft 版本 JSON 为空。");
        var id = SafeName(string.IsNullOrWhiteSpace(metadata.Id) ? versionId : metadata.Id);
        var instance = Path.Combine(installRoot ?? Root, SafeName(instanceKey ?? versionId));
        var versionDirectory = Path.Combine(instance, "versions", id);
        var librariesDirectory = Path.Combine(instance, "libraries");
        var nativesDirectory = Path.Combine(instance, "natives", id);
        Directory.CreateDirectory(versionDirectory);
        Directory.CreateDirectory(librariesDirectory);
        Directory.CreateDirectory(nativesDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, id + ".json"), metadataJson);

        var downloads = new List<DownloadTask>();
        if (metadata.Downloads?.Client is not { Url.Length: > 0 } client)
            throw new InvalidDataException($"版本 {versionId} 没有客户端文件下载地址。");
        downloads.Add(new(client.Url, Path.Combine(versionDirectory, id + ".jar"), client.Sha1));
        var nativeJars = new List<string>();
        foreach (var library in metadata.Libraries)
        {
            if (!IsAllowed(library)) continue;
            if (library.Downloads?.Artifact is { Url.Length: > 0 } artifact)
                downloads.Add(new(artifact.Url, SafeCombine(librariesDirectory, artifact.Path ?? CoordinatePath(library.Name)), artifact.Sha1));
            var classifierKey = GetNativeClassifier(library);
            if (classifierKey is not null && library.Downloads?.Classifiers?.TryGetValue(classifierKey, out var native) == true)
            {
                var nativePath = SafeCombine(librariesDirectory, native.Path ?? CoordinatePath(library.Name));
                downloads.Add(new(native.Url, nativePath, native.Sha1));
                nativeJars.Add(nativePath);
            }
        }

        if (metadata.AssetIndex is { Url.Length: > 0 } assetIndex)
            downloads.Add(new(assetIndex.Url, Path.Combine(instance, "assets", "indexes", SafeName(assetIndex.Id) + ".json"), assetIndex.Sha1));
        await DownloadManyAsync(downloads, "下载客户端与库文件", 2, 48, progress);
        foreach (var jar in nativeJars) ExtractNatives(jar, nativesDirectory);

        if (metadata.AssetIndex is { } index)
        {
            progress.Report(new("读取资源索引…", 49));
            var indexPath = Path.Combine(instance, "assets", "indexes", SafeName(index.Id) + ".json");
            var parsedIndex = await ReadJsonAsync<AssetIndex>(indexPath);
            var assets = parsedIndex.Objects.Values.Where(x => x.Hash.Length >= 2)
                .Select(x => new DownloadTask($"https://resources.download.minecraft.net/{x.Hash[..2]}/{x.Hash}",
                    Path.Combine(instance, "assets", "objects", x.Hash[..2], x.Hash), x.Hash)).ToList();
            await DownloadManyAsync(assets, "下载游戏资源", 50, 84, progress);
        }

        var component = metadata.JavaVersion?.Component;
        if (string.IsNullOrWhiteSpace(component)) component = InferRuntime(id);
        await InstallRuntimeAsync(instance, component, progress);
        var loaderVersion = "";
        if (loader == "Fabric") loaderVersion = await InstallFabricAsync(instance, id, component, progress, requestedLoaderVersion);
        else if (loader == "Forge") loaderVersion = await InstallForgeAsync(instance, id, component, progress, requestedLoaderVersion);
        await File.WriteAllTextAsync(Path.Combine(instance, "installed.txt"), $"{id}\nJava runtime: {component}\nLoader: {loader} {loaderVersion}\n");
        progress.Report(new($"Minecraft {id}（{loader} {loaderVersion}）安装完成；Java 已按版本隔离。", 100));
    }

    public async Task EnsureJavaRuntimeAsync(InstanceProfile profile, IProgress<InstallProgress> progress)
    {
        string? component = null;
        var path = profile.VersionJsonPath;
        for (var depth = 0; depth < 8 && File.Exists(path); depth++)
        {
            using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = document.RootElement;
            if (root.TryGetProperty("javaVersion", out var java) && java.ValueKind == System.Text.Json.JsonValueKind.Object
                && java.TryGetProperty("component", out var componentValue))
            {
                component = componentValue.GetString();
                if (!string.IsNullOrWhiteSpace(component)) break;
            }
            if (!root.TryGetProperty("inheritsFrom", out var parent)) break;
            var parentId = parent.GetString() ?? "";
            path = Path.Combine(profile.BaseDirectory, "versions", parentId, parentId + ".json");
        }
        component ??= InferRuntime(profile.GameVersion);
        await InstallRuntimeAsync(profile.GameDirectory, component, progress);
    }

    private async Task<string> InstallFabricAsync(string instance, string gameVersion, string javaComponent, IProgress<InstallProgress> progress, string? requestedVersion = null)
    {
        progress.Report(new("查找适用于此 Minecraft 版本的 Fabric…", 86));
        using var gameResponse = await DownloadSource.GetSuccessfulResponseAsync(InstallerClient, "https://meta.fabricmc.net/v2/versions/game");
        using var games = await System.Text.Json.JsonDocument.ParseAsync(await gameResponse.Content.ReadAsStreamAsync());
        var supportedGames = games.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
            ? games.RootElement.EnumerateArray()
                .Where(item => item.TryGetProperty("version", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(item => item.GetProperty("version").GetString()!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!supportedGames.Contains(gameVersion))
            throw new InvalidDataException($"Fabric 尚未提供 Minecraft {gameVersion} 的版本。请改选“原版”或 Forge；可用 Fabric 版本以官方清单为准。");
        using var response = await DownloadSource.GetSuccessfulResponseAsync(InstallerClient, $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}");
        using var versions = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var loader = versions.RootElement.EnumerateArray().FirstOrDefault(item =>
            item.TryGetProperty("loader", out var info)
            && (string.IsNullOrWhiteSpace(requestedVersion)
                ? info.TryGetProperty("stable", out var stable) && stable.GetBoolean()
                : info.TryGetProperty("version", out var version) && version.GetString() == requestedVersion));
        if (loader.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidDataException($"Fabric 暂不支持 Minecraft {gameVersion}。");
        var loaderVersion = loader.GetProperty("loader").GetProperty("version").GetString()!;
        using var installerResponse = await DownloadSource.GetSuccessfulResponseAsync(InstallerClient, "https://meta.fabricmc.net/v2/versions/installer");
        using var installers = await System.Text.Json.JsonDocument.ParseAsync(await installerResponse.Content.ReadAsStreamAsync());
        var installer = installers.RootElement.EnumerateArray().FirstOrDefault(item =>
            item.TryGetProperty("stable", out var stable) && stable.GetBoolean());
        if (installer.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidDataException("暂时无法取得 Fabric 安装器版本。");
        var installerVersion = installer.GetProperty("version").GetString()!;
        var installerUrl = installer.GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(installerUrl))
            throw new InvalidDataException("Fabric 安装器清单没有提供下载地址。");
        var installerJar = Path.Combine(instance, "installer-cache", $"fabric-installer-{installerVersion}.jar");
        await DownloadOneAsync(new DownloadTask(installerUrl, installerJar, null));
        var java = Path.Combine(instance, "runtime", javaComponent, "bin", "java.exe");
        var arguments = new List<string> { "client", "-dir", instance, "-mcversion", gameVersion, "-loader", loaderVersion, "-noprofile" };
        if (DownloadSource.Mode == "bmclapi")
        {
            arguments.Add("-metaurl"); arguments.Add("https://bmclapi2.bangbang93.com/fabric-meta/");
            arguments.Add("-mavenurl"); arguments.Add("https://bmclapi2.bangbang93.com/maven/");
        }
        await RunInstallerAsync(java, installerJar, arguments, instance);
        progress.Report(new($"Fabric {loaderVersion} 已安装到独立实例。", 97));
        return loaderVersion;
    }

    private async Task<string> InstallForgeAsync(string instance, string gameVersion, string javaComponent, IProgress<InstallProgress> progress, string? requestedVersion = null)
    {
        progress.Report(new("查找 Forge 推荐版本…", 86));
        using var response = await DownloadSource.GetSuccessfulResponseAsync(InstallerClient, "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
        using var promotions = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (!promotions.RootElement.TryGetProperty("promos", out var promoMap))
            throw new InvalidDataException("Forge 推荐版本清单格式无效。");
        var builds = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            if (!Regex.IsMatch(requestedVersion, "^[0-9A-Za-z.+_-]+$")) throw new InvalidDataException("整合包声明的 Forge 版本格式无效。");
            builds.Add(requestedVersion);
        }
        else
        {
            var promoKey = gameVersion + "-recommended";
            if (promoMap.TryGetProperty(promoKey, out var recommended)) builds.Add(recommended.GetString() ?? "");
            if (promoMap.TryGetProperty(gameVersion + "-latest", out var latest)) builds.Add(latest.GetString() ?? "");
            builds = builds.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (builds.Count == 0)
                throw new InvalidDataException($"Forge 尚未为 Minecraft {gameVersion} 发布推荐版本。");
        }
        string build = "";
        string installerJar = "";
        Exception? downloadFailure = null;
        foreach (var candidateBuild in builds)
        {
            var coordinate = $"{gameVersion}-{candidateBuild}";
            var candidateJar = Path.Combine(instance, "installer-cache", $"forge-{coordinate}-installer.jar");
            try
            {
                await DownloadOneAsync(new DownloadTask(
                    $"https://maven.minecraftforge.net/net/minecraftforge/forge/{coordinate}/forge-{coordinate}-installer.jar",
                    candidateJar, null));
                build = candidateBuild;
                installerJar = candidateJar;
                break;
            }
            catch (HttpRequestException ex) { downloadFailure = ex; }
        }
        if (installerJar.Length == 0)
            throw new InvalidDataException($"Forge 安装器下载失败。尝试版本：{string.Join("、", builds)}。{downloadFailure?.Message}", downloadFailure);
        var java = Path.Combine(instance, "runtime", javaComponent, "bin", "java.exe");
        await RunInstallerAsync(java, installerJar, ["--installClient", instance], instance);
        progress.Report(new($"Forge {build} 已安装到独立实例。", 97));
        return build;
    }

    private static async Task RunInstallerAsync(string java, string installerJar, IEnumerable<string> arguments, string workingDirectory)
    {
        if (!File.Exists(java)) throw new FileNotFoundException("找不到此游戏版本专用的 Java。", java);
        var argumentList = arguments.ToList();
        var start = new System.Diagnostics.ProcessStartInfo(java)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(installerJar);
        foreach (var arg in argumentList) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("无法启动 Mod Loader 安装器。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdoutTask;
        var errorOutput = await stderrTask;
        var log = $"时间：{DateTimeOffset.Now:O}\r\nJava：{java}\r\n安装器：{installerJar}\r\n参数：{string.Join(" ", argumentList)}\r\n退出代码：{process.ExitCode}\r\n\r\n--- 标准输出 ---\r\n{output}\r\n--- 错误输出 ---\r\n{errorOutput}";
        var logPath = Path.Combine(workingDirectory, "installer-cache", "last-loader-install.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await File.WriteAllTextAsync(logPath, log);
        if (process.ExitCode != 0)
        {
            DiagnosticLogger.WriteError("Mod Loader 安装器", log);
            var tail = string.Join(Environment.NewLine, (output + Environment.NewLine + errorOutput).Split(Environment.NewLine).TakeLast(15));
            throw new InvalidOperationException($"Mod Loader 安装器失败（代码 {process.ExitCode}）。详细日志：{logPath}{(string.IsNullOrWhiteSpace(tail) ? "" : Environment.NewLine + tail)}");
        }
    }

    private static async Task InstallRuntimeAsync(string instance, string component, IProgress<InstallProgress> progress)
    {
        progress.Report(new("获取 Java 运行时清单…", 85));
        using var response = await DownloadSource.GetSuccessfulResponseAsync(Client, RuntimeIndexUrl);
        using var runtimeIndex = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var platformKey = "windows-" + (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64"
        });
        if (runtimeIndex.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
            || !runtimeIndex.RootElement.TryGetProperty(platformKey, out var platform)
            || platform.ValueKind != System.Text.Json.JsonValueKind.Object
            || !platform.TryGetProperty(component, out var runtimeVersions))
            throw new InvalidDataException($"Mojang 的 Java 清单中没有 {platformKey} / {component}。");
        System.Text.Json.JsonElement selected = default;
        if (runtimeVersions.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            selected = runtimeVersions.EnumerateArray().FirstOrDefault(item =>
                item.ValueKind == System.Text.Json.JsonValueKind.Object
                && item.TryGetProperty("manifest", out var manifest)
                && manifest.ValueKind == System.Text.Json.JsonValueKind.Object
                && manifest.TryGetProperty("url", out var url)
                && url.ValueKind == System.Text.Json.JsonValueKind.String);
        }
        else if (runtimeVersions.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            selected = runtimeVersions;
        }
        if (selected.ValueKind != System.Text.Json.JsonValueKind.Object
            || !selected.TryGetProperty("manifest", out var manifestInfo)
            || manifestInfo.ValueKind != System.Text.Json.JsonValueKind.Object
            || !manifestInfo.TryGetProperty("url", out var manifestUrl)
            || manifestUrl.ValueKind != System.Text.Json.JsonValueKind.String)
            throw new InvalidDataException($"Mojang 没有提供 {platformKey} 的 {component} Java 下载清单。");

        var runtimeDirectory = Path.Combine(instance, "runtime", SafeName(component));
        var javaPath = Path.Combine(runtimeDirectory, "bin", "java.exe");
        if (File.Exists(javaPath)) { progress.Report(new($"已存在此版本专用 Java（{component}）。", 98)); return; }
        var runtime = await GetJsonAsync<RuntimeManifest>(manifestUrl.GetString()!);
        var files = runtime.Files ?? [];
        var runtimeDownloads = new List<DownloadTask>();
        foreach (var (relative, file) in files)
        {
            var destination = SafeCombine(runtimeDirectory, relative);
            if (file.Type == "directory") { Directory.CreateDirectory(destination); continue; }
            if (file.Type == "file" && file.Downloads?.Raw is { Url.Length: > 0 } raw)
                runtimeDownloads.Add(new(raw.Url, destination, raw.Sha1));
        }
        await DownloadManyAsync(runtimeDownloads, $"安装独立 Java（{component}）", 86, 98, progress);
        if (!File.Exists(javaPath)) throw new InvalidDataException("Java 运行时缺少 bin\\java.exe 文件。");
    }

    private static async Task DownloadManyAsync(List<DownloadTask> tasks, string phase, int start, int end, IProgress<InstallProgress> progress)
    {
        if (tasks.Count == 0) { progress.Report(new(phase, end)); return; }
        var completed = 0;
        using var semaphore = new SemaphoreSlim(16);
        await Task.WhenAll(tasks.Select(async task =>
        {
            await semaphore.WaitAsync();
            try
            {
                if (!File.Exists(task.Path) || !await VerifySha1Async(task.Path, task.Sha1))
                    await DownloadOneAsync(task);
                var n = Interlocked.Increment(ref completed);
                progress.Report(new($"{phase}（{n}/{tasks.Count}）", start + (int)((end - start) * (double)n / tasks.Count)));
            }
            finally { semaphore.Release(); }
        }));
    }

    private static async Task DownloadOneAsync(DownloadTask task)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(task.Path)!);
        var pathLock = DownloadPathLocks.GetOrAdd(Path.GetFullPath(task.Path), static _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync();
        var temp = task.Path + ".part";
        try
        {
            if (File.Exists(task.Path) && await VerifySha1Async(task.Path, task.Sha1)) return;
            using var response = await DownloadSource.GetSuccessfulResponseAsync(Client, task.Url, HttpCompletionOption.ResponseHeadersRead);
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await response.Content.CopyToAsync(output);
            if (!await VerifySha1Async(temp, task.Sha1)) throw new InvalidDataException($"SHA-1 校验失败：{Path.GetFileName(task.Path)}");
            File.Move(temp, task.Path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { /* A failed cleanup must not hide the original download error. */ }
            pathLock.Release();
        }
    }

    private static async Task<bool> VerifySha1Async(string path, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA1.HashDataAsync(stream);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> GetJsonAsync<T>(string url)
    {
        using var response = await DownloadSource.GetSuccessfulResponseAsync(Client, url);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), JsonOptions)
            ?? throw new InvalidDataException("Mojang 返回了空数据。");
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
            ?? throw new InvalidDataException("资源索引内容无效。");
    }

    private static bool IsAllowed(Library library)
    {
        if (library.Rules is not { Count: > 0 } rules) return true;
        var allowed = false;
        foreach (var rule in rules)
        {
            if (rule.Os?.Name is { Length: > 0 } os && os != "windows") continue;
            allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private static string? GetNativeClassifier(Library library)
    {
        if (library.Natives?.TryGetValue("windows", out var value) != true || string.IsNullOrEmpty(value)) return null;
        var classifier = value.Replace("${arch}", Environment.Is64BitProcess ? "64" : "32");
        if (library.Downloads?.Classifiers?.ContainsKey(classifier) == true) return classifier;
        return library.Downloads?.Classifiers?.ContainsKey("natives-windows") == true ? "natives-windows" : null;
    }

    private static void ExtractNatives(string jar, string destination)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(jar);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("原生库压缩包路径无效。");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!entry.FullName.EndsWith('/')) entry.ExtractToFile(path, true);
        }
    }

    private static string InferRuntime(string version)
    {
        var parts = version.Split('.', '-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && int.TryParse(parts[1], out var minor))
            return minor <= 16 ? "jre-legacy" : minor == 17 ? "java-runtime-alpha" : minor <= 20 ? "java-runtime-gamma" : "java-runtime-delta";
        return "java-runtime-gamma";
    }

    private static string CoordinatePath(string coordinate)
    {
        var parts = coordinate.Split(':');
        if (parts.Length < 3) throw new InvalidDataException($"库坐标无效：{coordinate}");
        return Path.Combine(parts[0].Replace('.', Path.DirectorySeparatorChar), parts[1], parts[2], $"{parts[1]}-{parts[2]}.jar");
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\') || value is "." or "..")
            throw new InvalidDataException("版本名称不安全。");
        return value;
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("下载清单含有越界路径。");
        return path;
    }

    private sealed record DownloadTask(string Url, string Path, string? Sha1);
    private sealed class VersionDetails { public string Id { get; set; } = ""; public VersionDownloads? Downloads { get; set; } public List<Library> Libraries { get; set; } = []; public AssetIndexInfo? AssetIndex { get; set; } public JavaVersion? JavaVersion { get; set; } }
    private sealed class VersionDownloads { public Artifact? Client { get; set; } }
    private sealed class Artifact { public string Url { get; set; } = ""; public string? Sha1 { get; set; } public string? Path { get; set; } }
    private sealed class AssetIndexInfo { public string Id { get; set; } = ""; public string Url { get; set; } = ""; public string? Sha1 { get; set; } }
    private sealed class JavaVersion { public int? MajorVersion { get; set; } public string? Component { get; set; } }
    private sealed class Library { public string Name { get; set; } = ""; public List<Rule>? Rules { get; set; } public Dictionary<string,string>? Natives { get; set; } public LibraryDownloads? Downloads { get; set; } }
    private sealed class Rule { public string Action { get; set; } = ""; public OsRule? Os { get; set; } }
    private sealed class OsRule { public string? Name { get; set; } }
    private sealed class LibraryDownloads { public Artifact? Artifact { get; set; } public Dictionary<string,Artifact>? Classifiers { get; set; } }
    private sealed class AssetIndex { public Dictionary<string,AssetObject> Objects { get; set; } = []; }
    private sealed class AssetObject { public string Hash { get; set; } = ""; }
    private sealed class RuntimeManifest { public Dictionary<string,RuntimeFile>? Files { get; set; } }
    private sealed class RuntimeFile { public string Type { get; set; } = ""; public RuntimeDownloads? Downloads { get; set; } }
    private sealed class RuntimeDownloads { public Artifact? Raw { get; set; } }
}
