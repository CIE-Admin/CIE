using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class CieInstaller : Form
{
    private readonly Panel _body = new Panel();
    private readonly TextBox _pathBox = new TextBox();
    private readonly CheckBox _desktopShortcut = new CheckBox();
    private readonly CheckBox _runAfterInstall = new CheckBox();
    private readonly Label _upgradeNotice = new Label();
    private readonly Button _installButton = new Button();
    private readonly ProgressBar _progress = new ProgressBar();
    private readonly Label _status = new Label();
    private readonly BackgroundWorker _worker = new BackgroundWorker();
    private string _installPath;
    private string _detectedInstallPath;
    private bool _createDesktopShortcut;

    private CieInstaller()
    {
        Text = "CIE 启动器安装程序";
        Width = 620;
        Height = 430;
        MinimumSize = new Size(620, 430);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(247, 249, 252);
        Font = new Font("Microsoft YaHei UI", 9F);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Panel header = new Panel();
        header.Dock = DockStyle.Top;
        header.Height = 96;
        header.BackColor = Color.FromArgb(27, 38, 58);
        Label title = new Label();
        title.Text = "CIE 启动器";
        title.ForeColor = Color.White;
        title.Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);
        title.Location = new Point(28, 16);
        title.AutoSize = true;
        Label subtitle = new Label();
        subtitle.Text = "安装 Minecraft 启动器及所需运行环境";
        subtitle.ForeColor = Color.FromArgb(197, 209, 229);
        subtitle.Location = new Point(32, 60);
        subtitle.AutoSize = true;
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        _body.Dock = DockStyle.Fill;
        _body.Padding = Padding.Empty;
        Controls.Add(_body);
        Controls.Add(header);

        _worker.DoWork += delegate { InstallPayload(_installPath, _detectedInstallPath); };
        _worker.RunWorkerCompleted += InstallCompleted;
        ShowInstallPage();
    }

    private void ShowInstallPage()
    {
        _body.Controls.Clear();
        _detectedInstallPath = FindExistingInstallation();
        Label prompt = new Label();
        prompt.Text = "选择 CIE 的安装位置";
        prompt.Font = new Font(Font, FontStyle.Bold);
        prompt.Location = new Point(28, 6);
        prompt.AutoSize = true;

        _pathBox.Text = _detectedInstallPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CIE");
        _pathBox.Location = new Point(28, 42);
        _pathBox.Width = 397;
        _pathBox.Height = 28;
        _pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Button browse = new Button();
        browse.Text = "浏览…";
        browse.Location = new Point(438, 40);
        browse.Width = 90;
        browse.Height = 30;
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += BrowseClicked;

        _upgradeNotice.Location = new Point(28, 75);
        _upgradeNotice.Width = 500;
        _upgradeNotice.Height = 30;
        _pathBox.TextChanged += delegate { UpdateUpgradeNotice(); };

        _desktopShortcut.Text = "在桌面创建快捷方式";
        _desktopShortcut.Checked = true;
        _desktopShortcut.Location = new Point(28, 108);
        _desktopShortcut.AutoSize = true;

        Label note = new Label();
        note.Text = "CIE 自带运行环境，安装时无需另外配置 Java 或 .NET。游戏版本、存档和玩家资料会保存在独立的数据目录。";
        note.Location = new Point(28, 140);
        note.Width = 497;
        note.Height = 52;
        note.ForeColor = Color.FromArgb(86, 101, 122);

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 24;
        _progress.Location = new Point(28, 201);
        _progress.Width = 500;
        _progress.Visible = false;
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _status.Text = "";
        _status.Location = new Point(28, 230);
        _status.Width = 500;
        _status.Height = 34;
        _status.ForeColor = Color.FromArgb(86, 101, 122);

        _installButton.Text = "安装 CIE";
        _installButton.BackColor = Color.FromArgb(17, 17, 17);
        _installButton.ForeColor = Color.White;
        _installButton.FlatStyle = FlatStyle.Flat;
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.Font = new Font(Font, FontStyle.Bold);
        _installButton.Width = 150;
        _installButton.Height = 42;
        _installButton.Location = new Point(378, 268);
        _installButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _installButton.Click += InstallClicked;
        UpdateUpgradeNotice();

        _body.Controls.Add(prompt);
        _body.Controls.Add(_pathBox);
        _body.Controls.Add(browse);
        _body.Controls.Add(_upgradeNotice);
        _body.Controls.Add(_desktopShortcut);
        _body.Controls.Add(note);
        _body.Controls.Add(_progress);
        _body.Controls.Add(_status);
        _body.Controls.Add(_installButton);
    }

    private void BrowseClicked(object sender, EventArgs e)
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "选择 CIE 的安装文件夹";
            dialog.SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dialog.ShowNewFolderButton = true;
            if (dialog.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dialog.SelectedPath;
        }
    }

    private void UpdateUpgradeNotice()
    {
        string selected = "";
        try { selected = Path.GetFullPath(_pathBox.Text.Trim()); } catch { }
        if (IsCieInstallationDirectory(selected))
        {
            _upgradeNotice.Text = "检测到此目录已有 CIE，将替换启动器程序文件；游戏版本、存档和玩家资料会保留。";
            _upgradeNotice.ForeColor = Color.FromArgb(35, 112, 74);
            _installButton.Text = "更新 CIE";
        }
        else if (_detectedInstallPath != null)
        {
            _upgradeNotice.Text = "检测到旧版 CIE。安装到新位置后会清理旧版程序文件，游戏版本和存档会保留。";
            _upgradeNotice.ForeColor = Color.FromArgb(35, 112, 74);
            _installButton.Text = "迁移并更新";
        }
        else
        {
            _upgradeNotice.Text = "未检测到旧版 CIE。若已有安装，请选择原安装目录以便更新。";
            _upgradeNotice.ForeColor = Color.FromArgb(86, 101, 122);
            _installButton.Text = "安装 CIE";
        }
    }

    private void InstallClicked(object sender, EventArgs e)
    {
        string selected = _pathBox.Text.Trim();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "请选择安装位置。", "CIE 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            _installPath = Path.GetFullPath(selected);
            string driveRoot = Path.GetPathRoot(_installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (_installPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(driveRoot, StringComparison.OrdinalIgnoreCase))
                _installPath = Path.Combine(_installPath, "CIE");
            if (_detectedInstallPath != null)
            {
                string oldRoot = Path.GetFullPath(_detectedInstallPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string newRoot = Path.GetFullPath(_installPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!oldRoot.Equals(newRoot, StringComparison.OrdinalIgnoreCase)
                    && (oldRoot.StartsWith(newRoot, StringComparison.OrdinalIgnoreCase) || newRoot.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("新安装目录不能位于旧版 CIE 文件夹内，也不能是旧版文件夹的上级目录。请使用默认检测到的位置，或选择完全独立的文件夹。");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "安装位置无效：" + ex.Message, "CIE 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool replacing = IsCieInstallationDirectory(_installPath) || _detectedInstallPath != null;
        if (IsLauncherRunningAt(_installPath) || (_detectedInstallPath != null && IsLauncherRunningAt(_detectedInstallPath)))
        {
            MessageBox.Show(this, "请先完全退出正在运行的 CIE 启动器，再进行更新。", "CIE 正在运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (replacing && MessageBox.Show(this,
            "检测到旧版 CIE。继续后会替换或迁移启动器程序文件，游戏版本、存档和玩家资料会保留。是否继续？",
            "更新现有 CIE", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _pathBox.Enabled = false;
        _desktopShortcut.Enabled = false;
        _createDesktopShortcut = _desktopShortcut.Checked;
        _installButton.Enabled = false;
        _installButton.Text = "正在安装…";
        _progress.Visible = true;
        _status.Text = "正在解压程序和运行环境，请稍候。";
        _worker.RunWorkerAsync();
    }

    private void InstallCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        if (e.Error != null)
        {
            _pathBox.Enabled = true;
            _desktopShortcut.Enabled = true;
            _installButton.Enabled = true;
            _installButton.Text = "重试安装";
            _progress.Visible = false;
            _status.Text = "安装没有完成，请检查磁盘空间和文件夹权限。";
            MessageBox.Show(this, "安装失败：\r\n" + e.Error.GetBaseException().Message, "CIE 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ShowFinishedPage();
    }

    private void ShowFinishedPage()
    {
        _body.Controls.Clear();
        Label title = new Label();
        title.Text = "CIE 已安装完成";
        title.Font = new Font(Font, FontStyle.Bold);
        title.Location = new Point(28, 14);
        title.AutoSize = true;
        Label path = new Label();
        path.Text = "安装位置：\r\n" + _installPath;
        path.Location = new Point(28, 55);
        path.Width = 500;
        path.Height = 52;
        path.ForeColor = Color.FromArgb(86, 101, 122);
        _runAfterInstall.Text = "现在启动 CIE";
        _runAfterInstall.Checked = true;
        _runAfterInstall.Location = new Point(28, 126);
        _runAfterInstall.AutoSize = true;

        Button finish = new Button();
        finish.Text = "完成";
        finish.BackColor = Color.FromArgb(17, 17, 17);
        finish.ForeColor = Color.White;
        finish.FlatStyle = FlatStyle.Flat;
        finish.FlatAppearance.BorderSize = 0;
        finish.Font = new Font(Font, FontStyle.Bold);
        finish.Width = 150;
        finish.Height = 42;
        finish.Location = new Point(378, 216);
        finish.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        finish.Click += delegate
        {
            if (_runAfterInstall.Checked) LaunchCie();
            Close();
        };
        _body.Controls.Add(title);
        _body.Controls.Add(path);
        _body.Controls.Add(_runAfterInstall);
        _body.Controls.Add(finish);
    }

    private void InstallPayload(string installPath, string previousInstallPath)
    {
        Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("CIE.Payload.zip");
        if (payload == null) throw new InvalidDataException("安装包没有包含 CIE 程序文件。");
        PrepareInstallDirectory(installPath);
        Directory.CreateDirectory(installPath);
        string root = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (payload)
        using (ZipArchive archive = new ZipArchive(payload, ZipArchiveMode.Read, false))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装数据包含无效路径。");
                if (relative.Length == 0) continue;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                string parent = Path.GetDirectoryName(destination);
                if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                using (Stream input = entry.Open())
                using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
            }
        }

        File.WriteAllText(Path.Combine(installPath, ".cie-install-marker"), "CIE launcher installation");
        if (!String.IsNullOrWhiteSpace(previousInstallPath) && !String.Equals(Path.GetFullPath(previousInstallPath).TrimEnd('\\'), Path.GetFullPath(installPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            RemovePreviousProgramFiles(previousInstallPath);
        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "CIE");
        Directory.CreateDirectory(startMenu);
        CreateShortcut(Path.Combine(startMenu, "CIE 启动器.lnk"), Path.Combine(installPath, "Launch-CIE.vbs"), installPath, Path.Combine(installPath, "CIE.exe"), "");
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        string uninstall = Path.Combine(installPath, "Uninstall-CIE.ps1");
        CreateShortcut(Path.Combine(startMenu, "卸载 CIE.lnk"), powershell, installPath, Path.Combine(installPath, "CIE.exe"), "-NoProfile -ExecutionPolicy Bypass -File \"" + uninstall + "\"");
        if (_createDesktopShortcut)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktop, "CIE 启动器.lnk"), Path.Combine(installPath, "Launch-CIE.vbs"), installPath, Path.Combine(installPath, "CIE.exe"), "");
        }
        else
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcut = Path.Combine(desktop, "CIE 启动器.lnk");
            if (File.Exists(shortcut)) File.Delete(shortcut);
        }
    }

    private static string FindExistingInstallation()
    {
        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CIE");
        string startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "CIE", "CIE 启动器.lnk");
        string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CIE 启动器.lnk");
        string[] candidates = { ResolveShortcutDirectory(startMenuShortcut), ResolveShortcutDirectory(desktopShortcut), defaultPath };
        foreach (string candidate in candidates)
            if (IsCieInstallationDirectory(candidate)) return Path.GetFullPath(candidate);
        return null;
    }

    private static string ResolveShortcutDirectory(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;
        object shell = null;
        try
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            shell = Activator.CreateInstance(shellType);
            dynamic scriptShell = shell;
            dynamic shortcut = scriptShell.CreateShortcut(shortcutPath);
            string target = Convert.ToString(shortcut.TargetPath);
            if (String.IsNullOrWhiteSpace(target)) return null;
            if (String.Equals(Path.GetFileName(target), "Launch-CIE.vbs", StringComparison.OrdinalIgnoreCase)
                || String.Equals(Path.GetFileName(target), "CIE.exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(target);
        }
        catch { }
        finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell); }
        return null;
    }

    private static bool IsCieInstallationDirectory(string path)
    {
        if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        if (File.Exists(Path.Combine(path, ".cie-install-marker"))) return true;
        return File.Exists(Path.Combine(path, "CIE.exe"))
            && File.Exists(Path.Combine(path, "CIE.dll"))
            && (File.Exists(Path.Combine(path, "CIE.runtimeconfig.json"))
                || File.Exists(Path.Combine(path, "Launch-CIE.vbs"))
                || File.Exists(Path.Combine(path, "runtime", "dotnet.exe")));
    }

    private static bool IsLauncherRunningAt(string installPath)
    {
        if (String.IsNullOrWhiteSpace(installPath)) return false;
        string root = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (Process process in Process.GetProcessesByName("CIE"))
        {
            try
            {
                string executable = process.MainModule.FileName;
                if (Path.GetFullPath(executable).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            finally { process.Dispose(); }
        }
        return false;
    }

    private static void PrepareInstallDirectory(string installPath)
    {
        if (!IsCieInstallationDirectory(installPath)) return;
        string runtime = Path.Combine(installPath, "runtime");
        if (Directory.Exists(runtime)) Directory.Delete(runtime, true);
        string symbols = Path.Combine(installPath, "CIE.pdb");
        if (File.Exists(symbols)) File.Delete(symbols);
    }

    private static void RemovePreviousProgramFiles(string previousInstallPath)
    {
        if (!IsCieInstallationDirectory(previousInstallPath)) return;
        string[] files = { "CIE.exe", "CIE.dll", "CIE.deps.json", "CIE.runtimeconfig.json", "CIEUpdater.exe", "CIE.pdb", "Launch-CIE.vbs", "Uninstall-CIE.ps1", ".cie-install-marker" };
        foreach (string file in files)
        {
            string path = Path.Combine(previousInstallPath, file);
            if (File.Exists(path)) File.Delete(path);
        }
        string runtime = Path.Combine(previousInstallPath, "runtime");
        if (Directory.Exists(runtime)) Directory.Delete(runtime, true);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string arguments)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) throw new InvalidOperationException("无法创建快捷方式：Windows Script Host 不可用。");
        object shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic scriptShell = shell;
            dynamic shortcut = scriptShell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.IconLocation = iconPath;
            shortcut.Arguments = arguments;
            shortcut.Save();
        }
        finally
        {
            if (Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private void LaunchCie()
    {
        ProcessStartInfo start = new ProcessStartInfo(Path.Combine(_installPath, "CIE.exe"));
        start.WorkingDirectory = _installPath;
        start.UseShellExecute = false;
        start.EnvironmentVariables["DOTNET_ROOT"] = Path.Combine(_installPath, "runtime");
        start.EnvironmentVariables["DOTNET_ROOT_X64"] = Path.Combine(_installPath, "runtime");
        Process.Start(start);
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CieInstaller());
    }
}
