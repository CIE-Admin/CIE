$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$payload = Join-Path $PSScriptRoot 'App'
$sourceExe = Join-Path $payload 'CIE.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $payload 'runtime\host\fxr'))) {
    [System.Windows.Forms.MessageBox]::Show('没有找到完整的 CIE 程序和内置运行时。请解压完整安装包后再运行。', 'CIE 安装失败', 'OK', 'Error') | Out-Null
    exit 1
}

$defaultPath = Join-Path $env:LOCALAPPDATA 'Programs\CIE'
$dialog = [System.Windows.Forms.FolderBrowserDialog]::new()
$dialog.Description = '选择 CIE 的安装文件夹'
$dialog.SelectedPath = $defaultPath
$dialog.ShowNewFolderButton = $true
if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }

$installPath = [System.IO.Path]::GetFullPath($dialog.SelectedPath)
$driveRoot = [System.IO.Path]::GetPathRoot($installPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
if ($installPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar).Equals($driveRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    $installPath = Join-Path $installPath 'CIE'
}
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Copy-Item -Path (Join-Path $payload '*') -Destination $installPath -Recurse -Force
$uninstallSource = Join-Path $PSScriptRoot 'Uninstall-CIE.ps1'
$uninstallContent = [System.IO.File]::ReadAllText($uninstallSource, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText((Join-Path $installPath 'Uninstall-CIE.ps1'), $uninstallContent, [System.Text.Encoding]::Unicode)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Launch-CIE.vbs') -Destination (Join-Path $installPath 'Launch-CIE.vbs') -Force
Set-Content -LiteralPath (Join-Path $installPath '.cie-install-marker') -Value 'CIE launcher installation' -Encoding ASCII

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\CIE'
New-Item -ItemType Directory -Path $startMenu -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $startMenu 'CIE 启动器.lnk'))
$shortcut.TargetPath = Join-Path $installPath 'Launch-CIE.vbs'
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = Join-Path $installPath 'CIE.exe'
$shortcut.Save()
$uninstallShortcut = $shell.CreateShortcut((Join-Path $startMenu '卸载 CIE.lnk'))
$uninstallShortcut.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$uninstallShortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installPath 'Uninstall-CIE.ps1') + '"'
$uninstallShortcut.WorkingDirectory = $installPath
$uninstallShortcut.Save()
[System.Windows.Forms.MessageBox]::Show("CIE 已安装到：`n$installPath`n`n启动器快捷方式已添加到开始菜单。你的游戏版本、玩家昵称和皮肤数据不会被安装包覆盖。", 'CIE 安装完成', 'OK', 'Information') | Out-Null
