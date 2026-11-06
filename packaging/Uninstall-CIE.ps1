param(
    [int]$ParentPid = 0,
    [switch]$RemoveProfile,
    [switch]$RemoveGameFiles,
    [switch]$FromSettings
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$installPath = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$marker = Join-Path $installPath '.cie-install-marker'
$localCie = Join-Path $env:LOCALAPPDATA 'CIE'
$catalog = Join-Path $localCie 'instances.json'

$legacyExecutable = Join-Path $installPath 'CIE.exe'
$legacyAssembly = Join-Path $installPath 'CIE.dll'
$isLegacyCieInstall = (Test-Path -LiteralPath $legacyExecutable -PathType Leaf) -and (Test-Path -LiteralPath $legacyAssembly -PathType Leaf)
if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -and -not $isLegacyCieInstall) {
    [System.Windows.Forms.MessageBox]::Show('未识别到 CIE 安装标记或旧版 CIE 程序文件。为保护此文件夹，已取消自动卸载。', '无法自动卸载', 'OK', 'Warning') | Out-Null
    exit 1
}

if (-not $FromSettings) {
    $message = "删除 CIE 启动器程序和设置吗？`n`n安装目录：$installPath`n`nMinecraft 版本和存档默认保留。"
    if ([System.Windows.Forms.MessageBox]::Show($message, '确认卸载 CIE', 'YesNo', 'Warning') -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }
    if ([System.Windows.Forms.MessageBox]::Show('是否同时删除 CIE 下载的 Minecraft 版本、MOD、设置和存档？`n`n导入的 PCL 原始 .minecraft 永远不会由此选项删除。', '删除游戏文件', 'YesNo', 'Warning') -eq [System.Windows.Forms.DialogResult]::Yes) {
        $RemoveGameFiles = $true
    }
    $RemoveProfile = $true
}

# Let the launcher release its own executable and files before removal.
if ($ParentPid -gt 0) {
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline) {
        $parent = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
        if (-not $parent) { break }
        Start-Sleep -Milliseconds 400
    }
}

$profiles = @()
if (Test-Path -LiteralPath $catalog -PathType Leaf) {
    try { $profiles = @(Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json) } catch { $profiles = @() }
}

if ($RemoveGameFiles) {
    $defaultInstancesRoot = Join-Path $localCie 'instances'
    foreach ($profile in $profiles) {
        $gameDirectory = [string]$profile.GameDirectory
        if ([string]::IsNullOrWhiteSpace($gameDirectory) -or -not (Test-Path -LiteralPath $gameDirectory -PathType Container)) { continue }
        $allowedRoot = if ($profile.Imported) { $defaultInstancesRoot } else { [string]$profile.InstallRootDirectory }
        if ([string]::IsNullOrWhiteSpace($allowedRoot)) { $allowedRoot = $defaultInstancesRoot }
        $fullRoot = [System.IO.Path]::GetFullPath($allowedRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $fullGame = [System.IO.Path]::GetFullPath($gameDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        $ownershipMarker = Join-Path $fullGame '.cie-instance-marker'
        if ($fullGame.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $ownershipMarker -PathType Leaf)) {
            Remove-Item -LiteralPath $fullGame -Recurse -Force
        }
    }
}

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\CIE'
foreach ($shortcutName in @('CIE 启动器.lnk', '卸载 CIE.lnk')) {
    $shortcut = Join-Path $startMenu $shortcutName
    if (Test-Path -LiteralPath $shortcut -PathType Leaf) { Remove-Item -LiteralPath $shortcut -Force }
}
if ((Test-Path -LiteralPath $startMenu -PathType Container) -and ((Get-ChildItem -LiteralPath $startMenu -Force | Measure-Object).Count -eq 0)) {
    Remove-Item -LiteralPath $startMenu -Force
}
$desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) 'CIE 启动器.lnk'
if (Test-Path -LiteralPath $desktopShortcut -PathType Leaf) { Remove-Item -LiteralPath $desktopShortcut -Force }

if ($RemoveProfile) {
    foreach ($name in @('profile', 'logs')) {
        $dataPath = Join-Path $localCie $name
        if (Test-Path -LiteralPath $dataPath -PathType Container) { Remove-Item -LiteralPath $dataPath -Recurse -Force }
    }
    if ($RemoveGameFiles -and (Test-Path -LiteralPath $catalog -PathType Leaf)) { Remove-Item -LiteralPath $catalog -Force }
}

Set-Location -LiteralPath $env:TEMP
Remove-Item -LiteralPath $installPath -Recurse -Force
if ($RemoveGameFiles) {
    [System.Windows.Forms.MessageBox]::Show('CIE 启动器、设置和登记的游戏版本已删除。PCL 原始 .minecraft 未被触碰。', 'CIE 已删除', 'OK', 'Information') | Out-Null
} else {
    [System.Windows.Forms.MessageBox]::Show('CIE 启动器与设置已删除。Minecraft 游戏文件和存档已保留。', 'CIE 已删除', 'OK', 'Information') | Out-Null
}
