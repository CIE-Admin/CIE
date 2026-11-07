param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$AssetDirectory,
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = 'Stop'
$owner = 'cieadmin'
$repository = 'CIE'
$token = $env:GITEE_TOKEN
$username = $env:GITEE_USERNAME
if ([string]::IsNullOrWhiteSpace($token) -or [string]::IsNullOrWhiteSpace($username)) {
    throw 'Gitee credentials are missing. Add GITEE_USERNAME and GITEE_TOKEN as GitHub Actions secrets.'
}

$assetNames = @('CIE-Installer.exe', 'CIE-Update.zip', 'CIE-Update.zip.sha256')
$assetPaths = foreach ($name in $assetNames) {
    $path = Join-Path $AssetDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required release asset is missing: $path" }
    $path
}

# Keep the Gitee source mirror current and publish the same tag users will install.
$tagRef = "refs/tags/$Tag"
& git fetch origin ("{0}:{1}" -f $tagRef, $tagRef)
if ($LASTEXITCODE -ne 0) { throw "Could not fetch GitHub release tag $Tag." }
$credentialText = '{0}:{1}' -f $username, $token
$authorization = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($credentialText))
$gitUrl = "https://gitee.com/$owner/$repository.git"
& git -c "http.extraheader=AUTHORIZATION: basic $authorization" push $gitUrl ("{0}:{1}" -f $tagRef, $tagRef)
if ($LASTEXITCODE -ne 0) { throw "Could not push tag $Tag to Gitee." }
& git -c "http.extraheader=AUTHORIZATION: basic $authorization" push $gitUrl ("{0}:refs/heads/main" -f $tagRef)
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'The release tag was mirrored, but the Gitee main branch could not be fast-forwarded. The tagged source and release assets remain available.'
}

$api = "https://gitee.com/api/v5/repos/$owner/$repository"
$encodedTag = [Uri]::EscapeDataString($Tag)
$release = $null
try {
    $release = Invoke-RestMethod -Method Get -Uri "$api/releases/tags/$encodedTag?access_token=$([Uri]::EscapeDataString($token))"
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
    $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -ContentType 'application/x-www-form-urlencoded' -Body $createBody
}
$releaseId = $release.id
if (-not $releaseId) { throw "Gitee did not return a release ID for $Tag." }

$attachments = @()
try {
    $attachments = @(Invoke-RestMethod -Method Get -Uri "$api/releases/$releaseId/attach_files?access_token=$([Uri]::EscapeDataString($token))")
}
catch {
    Write-Warning "Could not list existing Gitee release files: $($_.Exception.Message)"
}

foreach ($path in $assetPaths) {
    $name = [IO.Path]::GetFileName($path)
    if ($attachments | Where-Object { $_.name -eq $name }) {
        Write-Host "Already present on Gitee: $name"
        continue
    }
    $form = @{ access_token = $token; file = Get-Item -LiteralPath $path }
    Invoke-RestMethod -Method Post -Uri "$api/releases/$releaseId/attach_files" -Form $form | Out-Null
    Write-Host "Uploaded to Gitee: $name"
}
Write-Host "Gitee release ready: https://gitee.com/$owner/$repository/releases/tag/$Tag"
