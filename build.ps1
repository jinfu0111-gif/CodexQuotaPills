$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $projectRoot 'bin'
$iconPath = Join-Path $projectRoot 'installer-assets\app-icon.ico'
$defaultCachePath = Join-Path $projectRoot 'installer-assets\usage-cache.ini'
$manifestPath = Join-Path $projectRoot 'app.manifest'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkDir = Split-Path -Parent $compiler
$winMetadataDir = Join-Path $env:WINDIR 'System32\WinMetadata'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Missing .NET Framework compiler: $compiler"
}
if (-not (Test-Path -LiteralPath $iconPath) -or
    -not (Test-Path -LiteralPath $defaultCachePath) -or
    -not (Test-Path -LiteralPath $manifestPath)) {
    throw 'Missing installer asset.'
}
foreach ($required in @(
    (Join-Path $frameworkDir 'System.Runtime.dll'),
    (Join-Path $frameworkDir 'System.Runtime.WindowsRuntime.dll'),
    (Join-Path $winMetadataDir 'Windows.Management.winmd'),
    (Join-Path $winMetadataDir 'Windows.Foundation.winmd'),
    (Join-Path $winMetadataDir 'Windows.ApplicationModel.winmd'),
    (Join-Path $winMetadataDir 'Windows.Storage.winmd'),
    (Join-Path $winMetadataDir 'Windows.System.winmd')
)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing Windows MSIX build dependency: $required"
    }
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    "/out:$outputDir\CodexQuotaPills.exe" `
    "/win32icon:$iconPath" `
    "/win32manifest:$manifestPath" `
    "/resource:$projectRoot\tools\apply-pills-update.ps1,CodexUsageOverlay.ApplyPillsUpdate.ps1" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Xml.dll `
    /reference:System.Xml.Linq.dll `
    "/reference:$frameworkDir\System.Runtime.dll" `
    "/reference:$frameworkDir\System.Runtime.WindowsRuntime.dll" `
    "/reference:$winMetadataDir\Windows.Management.winmd" `
    "/reference:$winMetadataDir\Windows.Foundation.winmd" `
    "/reference:$winMetadataDir\Windows.ApplicationModel.winmd" `
    "/reference:$winMetadataDir\Windows.Storage.winmd" `
    "/reference:$winMetadataDir\Windows.System.winmd" `
    (Join-Path $projectRoot 'AssemblyInfo.cs') `
    (Join-Path $projectRoot 'UiRendering.cs') `
    (Join-Path $projectRoot 'UpdateMenuVisuals.cs') `
    (Join-Path $projectRoot 'OverlayInteraction.cs') `
    (Join-Path $projectRoot 'ComposerUsagePills.cs') `
    (Join-Path $projectRoot 'QuotaHoverCardForm.cs') `
    (Join-Path $projectRoot 'PlacementMenuLayout.cs') `
    (Join-Path $projectRoot 'PopupAnchorPlacement.cs') `
    (Join-Path $projectRoot 'PlacementSelectorForm.cs') `
    (Join-Path $projectRoot 'ExitConfirmationForm.cs') `
    (Join-Path $projectRoot 'UsageData.cs') `
    (Join-Path $projectRoot 'UsageTrustPolicy.cs') `
    (Join-Path $projectRoot 'GitHubReleaseUpdateService.cs') `
    (Join-Path $projectRoot 'GitHubUpdateInstaller.cs') `
    (Join-Path $projectRoot 'GitHubUpdateForm.cs') `
    (Join-Path $projectRoot 'FirstRunGuideForm.cs') `
    (Join-Path $projectRoot 'CodexLifecycle.cs') `
    (Join-Path $projectRoot 'CodexLifecycleLauncher.cs') `
    (Join-Path $projectRoot 'Program.cs') `
    (Join-Path $projectRoot 'OverlaySettings.cs') `
    (Join-Path $projectRoot 'QuotaPillsSettingsForm.cs') `
    (Join-Path $projectRoot 'ResetRadarService.cs') `
    (Join-Path $projectRoot 'ResetRadarBannerPolicy.cs') `
    (Join-Path $projectRoot 'ResetRadarBannerVisuals.cs') `
    (Join-Path $projectRoot 'ResetRadarBannerForm.cs') `
    (Join-Path $projectRoot 'CodexTaskStatusMonitor.cs') `
    (Join-Path $projectRoot 'CodexAppServerClient.cs') `
    (Join-Path $projectRoot 'AutoResumePolicy.cs') `
    (Join-Path $projectRoot 'CodexDesktopResumeClient.cs') `
    (Join-Path $projectRoot 'CodexDesktopContract.cs') `
    (Join-Path $projectRoot 'AutoResumeService.cs') `
    (Join-Path $projectRoot 'AutoResumeForm.cs') `
    (Join-Path $projectRoot 'CodexClientUpdateForm.cs') `
    (Join-Path $projectRoot 'MsixUpdater\AppxInstaller.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Downloader.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Fe3Client.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Http.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Logger.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Models.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Settings.cs') `
    (Join-Path $projectRoot 'MsixUpdater\StoreApi.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Util.cs') `
    (Join-Path $projectRoot 'MsixUpdater\WinRtAppx.cs')

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $defaultCachePath -Destination (Join-Path $outputDir 'usage-cache.ini') -Force
Get-Item -LiteralPath (Join-Path $outputDir 'CodexQuotaPills.exe')
