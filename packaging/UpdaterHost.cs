using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;

internal static class CieUpdater
{
    private static readonly string[] AllowedFiles = { "CIE.exe", "CIE.dll", "CIE.deps.json", "CIE.runtimeconfig.json" };

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        try
        {
            if (args.Length != 5 || args[0] != "--apply-update")
                throw new ArgumentException("更新程序参数无效。");
            int processId;
            if (!int.TryParse(args[1], out processId)) throw new ArgumentException("启动器进程编号无效。");
            ApplyUpdate(processId, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), args[4]);
        }
        catch (Exception ex)
        {
            MessageBox.Show("CIE 更新没有完成：\r\n" + ex.GetBaseException().Message + "\r\n\r\n当前文件未完成替换时，更新程序会尝试恢复原版本。", "CIE 更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ApplyUpdate(int processId, string installPath, string packagePath, string expectedHash)
    {
        using (Process app = TryGetProcess(processId))
        {
            if (app != null && !app.WaitForExit(120000))
                throw new IOException("等待 CIE 关闭超时。请关闭启动器后重新检查更新。");
        }
        if (!Directory.Exists(installPath)) throw new DirectoryNotFoundException("找不到 CIE 安装目录：" + installPath);
        if (!File.Exists(packagePath)) throw new FileNotFoundException("找不到下载的更新包。", packagePath);
        VerifyPackageHash(packagePath, expectedHash);

        string transactionRoot = Path.Combine(installPath, ".cie-update-" + Guid.NewGuid().ToString("N"));
        string stage = Path.Combine(transactionRoot, "stage");
        string backup = Path.Combine(transactionRoot, "backup");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(backup);
        bool committed = false;
        bool preserveRecoveryFiles = false;
        try
        {
            ExtractAllowedFiles(packagePath, stage);
            foreach (string file in AllowedFiles)
                if (!File.Exists(Path.Combine(stage, file))) throw new InvalidDataException("更新包缺少文件：" + file);

            string[] replaced = AllowedFiles.Where(file => File.Exists(Path.Combine(installPath, file))).ToArray();
            foreach (string file in replaced) File.Copy(Path.Combine(installPath, file), Path.Combine(backup, file), true);
            try
            {
                foreach (string file in AllowedFiles)
                    File.Copy(Path.Combine(stage, file), Path.Combine(installPath, file), true);
                committed = true;
            }
            catch (Exception updateError)
            {
                try
                {
                    foreach (string file in AllowedFiles)
                    {
                        string previous = Path.Combine(backup, file);
                        string current = Path.Combine(installPath, file);
                        if (File.Exists(previous)) File.Copy(previous, current, true);
                        else if (File.Exists(current)) File.Delete(current);
                    }
                }
                catch (Exception rollbackError)
                {
                    preserveRecoveryFiles = true;
                    throw new AggregateException("更新失败，而且自动恢复没有完全完成。旧文件备份保留在：" + backup, updateError, rollbackError);
                }
                throw;
            }
        }
        finally
        {
            if (!preserveRecoveryFiles)
                try { if (Directory.Exists(transactionRoot)) Directory.Delete(transactionRoot, true); } catch { }
        }

        if (!committed) throw new IOException("更新文件没有成功写入。");
        try { File.Delete(packagePath); } catch { }
        ProcessStartInfo start = new ProcessStartInfo(Path.Combine(installPath, "CIE.exe"));
        start.WorkingDirectory = installPath;
        start.UseShellExecute = false;
        start.EnvironmentVariables["DOTNET_ROOT"] = Path.Combine(installPath, "runtime");
        start.EnvironmentVariables["DOTNET_ROOT_X64"] = Path.Combine(installPath, "runtime");
        Process.Start(start);
    }

    private static Process TryGetProcess(int processId)
    {
        try { return Process.GetProcessById(processId); }
        catch (ArgumentException) { return null; }
    }

    private static void VerifyPackageHash(string path, string expected)
    {
        if (expected == null || expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("更新包校验值无效。");
        using (FileStream file = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包 SHA-256 校验失败；当前启动器未修改。");
        }
    }

    private static void ExtractAllowedFiles(string packagePath, string stage)
    {
        using (ZipArchive archive = ZipFile.OpenRead(packagePath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.Contains("/") || !AllowedFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新包包含不允许的路径或文件：" + name);
                using (Stream source = entry.Open())
                using (FileStream output = File.Create(Path.Combine(stage, name)))
                    source.CopyTo(output);
            }
        }
    }
}
