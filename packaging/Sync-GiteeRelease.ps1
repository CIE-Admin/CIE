param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$AssetDirectory,
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = 'Stop'
$owner = 'cieadmin'
$repository = 'CIE'
$token = $env:GITEE_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Gitee credentials are missing. Add GITEE_TOKEN as a GitHub Actions secret.'
}

$assetNames = @('CIE-Installer.exe', 'CIE-Update.zip', 'CIE-Update.zip.sha256')
$assetPaths = foreach ($name in $assetNames) {
    $path = Join-Path $AssetDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required release asset is missing: $path" }
    $path
}

# Try to mirror the source tag as well. Some imported Gitee repositories reject Git pushes;
# release creation and asset uploads use the Gitee API below and should still proceed.
$tagRef = "refs/tags/$Tag"
& git fetch origin ("{0}:{1}" -f $tagRef, $tagRef)
if ($LASTEXITCODE -ne 0) { throw "Could not fetch GitHub release tag $Tag." }
$credentialText = 'oauth2:{0}' -f $token
$authorization = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($credentialText))
$gitUrl = "https://gitee.com/$owner/$repository.git"
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'Never'
& git -c 'credential.helper=' -c 'credential.interactive=never' -c "http.extraheader=Authorization: Basic $authorization" push $gitUrl ("{0}:{1}" -f $tagRef, $tagRef)
$tagPushSucceeded = $LASTEXITCODE -eq 0
if (-not $tagPushSucceeded) {
    Write-Warning "Could not push source tag $Tag to Gitee; continuing with release and attachment synchronization through the Gitee API."
}
else {
    & git -c 'credential.helper=' -c 'credential.interactive=never' -c "http.extraheader=Authorization: Basic $authorization" push $gitUrl ("{0}:refs/heads/main" -f $tagRef)
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'The release tag was mirrored, but the Gitee main branch could not be fast-forwarded. Release asset synchronization will continue.'
    }
}

$api = "https://gitee.com/api/v5/repos/$owner/$repository"
$encodedTag = [Uri]::EscapeDataString($Tag)
$release = $null
try {
    $release = Invoke-RestMethod -Method Get -Uri "$api/releases/tags/$encodedTag?access_token=$([Uri]::EscapeDataString($token))" -TimeoutSec 90
}
catch {
    $status = 0
    try { $status = [int]$_.Exception.Response.StatusCode } catch { }
    if ($status -ne 404) { throw }
}

if ($null -eq $release) {
    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        $ReleaseNotes = "CIE Launcher $Tag - Windows installer and in-app update package."
    }
    $createBody = @{
        access_token = $token
        tag_name = $Tag
        name = $Tag
        body = $ReleaseNotes
        target_commitish = 'main'
        prerelease = 'false'
    }
    Write-Host "Creating Gitee release $Tag..."
    $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -ContentType 'application/x-www-form-urlencoded' -Body $createBody -TimeoutSec 90
}
$releaseId = $release.id
if (-not $releaseId) { throw "Gitee did not return a release ID for $Tag." }

$attachments = @()
Write-Host "Checking existing Gitee release attachments..."
try {
    $attachments = @(Invoke-RestMethod -Method Get -Uri "$api/releases/$releaseId/attach_files?access_token=$([Uri]::EscapeDataString($token))" -TimeoutSec 90)
}
catch {
    throw "Could not list Gitee release attachments: $($_.Exception.Message)"
}

foreach ($path in $assetPaths) {
    $name = [IO.Path]::GetFileName($path)
    if ($attachments | Where-Object { $_.name -eq $name }) {
        Write-Host "Already present on Gitee: $name"
        continue
    }
    $file = Get-Item -LiteralPath $path
    $form = @{ access_token = $token; file = $file }
    Write-Host "Uploading to Gitee: $name (request timeout: 15 minutes)"
    Invoke-RestMethod -Method Post -Uri "$api/releases/$releaseId/attach_files" -Form $form -TimeoutSec 900 | Out-Null
    Write-Host "Uploaded to Gitee: $name"
}
Write-Host "Gitee release ready: https://gitee.com/$owner/$repository/releases/tag/$Tag"
