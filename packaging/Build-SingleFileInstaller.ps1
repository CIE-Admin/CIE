$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'CIE-Installer-Stage'
$payloadZip = Join-Path $dist 'CIE-Installer-Payload.zip'
$installer = Join-Path $dist 'CIE-Installer.exe'
$appIcon = Join-Path $root 'Assets\CIE.ico'
$hostSource = Join-Path $PSScriptRoot 'InstallerHost.cs'
$updaterSource = Join-Path $PSScriptRoot 'UpdaterHost.cs'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) { throw '未找到 Windows .NET Framework 编译器。' }
if (-not (Test-Path -LiteralPath $appIcon -PathType Leaf)) { throw '未找到 Assets\CIE.ico。' }
if (-not (Test-Path -LiteralPath $hostSource -PathType Leaf)) { throw '未找到安装程序源码。' }
if (-not (Test-Path -LiteralPath $updaterSource -PathType Leaf)) { throw '未找到更新程序源码。' }

$distFullPath = [System.IO.Path]::GetFullPath($dist).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$stageFullPath = [System.IO.Path]::GetFullPath($stage)
if (-not $stageFullPath.StartsWith($distFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '临时打包目录不在 dist 文件夹内，已停止以保护其他文件。'
}
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$publishArguments = @((Join-Path $root 'CIE.csproj'), '-c', 'Release', '--no-restore', '-o', $stage, '-p:PublishSingleFile=false')
if ($env:CIE_RELEASE_VERSION) {
    $assemblyVersion = $env:CIE_RELEASE_VERSION + '.0'
    $publishArguments += @('-p:Version=' + $env:CIE_RELEASE_VERSION, '-p:AssemblyVersion=' + $assemblyVersion, '-p:FileVersion=' + $assemblyVersion, '-p:InformationalVersion=' + $env:CIE_RELEASE_VERSION)
}
dotnet publish @publishArguments
if ($LASTEXITCODE -ne 0) { throw "CIE 发布失败（退出码 $LASTEXITCODE）。" }

$runtimeSource = Join-Path $dist 'CIE-Test\App\runtime'
if (-not (Test-Path -LiteralPath (Join-Path $runtimeSource 'host\fxr'))) {
    throw '没有找到之前准备好的独立 .NET 运行环境。'
}
Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $stage 'runtime') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Launch-CIE.vbs') -Destination (Join-Path $stage 'Launch-CIE.vbs') -Force
# Windows PowerShell 5.1 does not reliably treat UTF-8 without a BOM as UTF-8.
# Normalize the installed script to UTF-16LE with BOM so it parses correctly on older Windows installs.
$uninstallSource = Join-Path $PSScriptRoot 'Uninstall-CIE.ps1'
$uninstallContent = [System.IO.File]::ReadAllText($uninstallSource, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText((Join-Path $stage 'Uninstall-CIE.ps1'), $uninstallContent, [System.Text.Encoding]::Unicode)
$updater = Join-Path $stage 'CIEUpdater.exe'
& $csc /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ "/out:$updater" `
    /reference:System.Windows.Forms.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $updaterSource
if ($LASTEXITCODE -ne 0) { throw "更新程序编译失败（退出码 $LASTEXITCODE）。" }
if (-not (Test-Path -LiteralPath $updater -PathType Leaf)) { throw '更新程序没有生成。' }
if (Test-Path -LiteralPath (Join-Path $stage 'CIE.pdb')) {
    Remove-Item -LiteralPath (Join-Path $stage 'CIE.pdb') -Force
}

if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

& $csc /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ "/out:$installer" "/win32icon:$appIcon" "/resource:$payloadZip,CIE.Payload.zip" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll $hostSource
if ($LASTEXITCODE -ne 0) { throw "安装程序编译失败（退出码 $LASTEXITCODE）。" }

$builtAssembly = [System.Reflection.Assembly]::LoadFile($installer)
if ($builtAssembly.GetManifestResourceNames() -notcontains 'CIE.Payload.zip') {
    throw '生成的安装程序没有嵌入游戏文件，已停止交付。'
}
Remove-Item -LiteralPath $payloadZip -Force
Remove-Item -LiteralPath $stage -Recurse -Force

Write-Host "单文件安装程序已生成：$installer"
Write-Host ("文件大小：{0:N1} MB" -f ((Get-Item -LiteralPath $installer).Length / 1MB))
