$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $projectRoot 'tests\bin'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkDir = Split-Path -Parent $compiler
$winMetadataDir = Join-Path $env:WINDIR 'System32\WinMetadata'

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& $compiler /nologo /target:exe /optimize+ /platform:anycpu /define:AUTO_RESUME_TESTS `
    "/out:$outputDir\ResetRadarTests.exe" `
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
    (Join-Path $projectRoot 'PlacementMenuLayout.cs') `
    (Join-Path $projectRoot 'PopupAnchorPlacement.cs') `
    (Join-Path $projectRoot 'UsageData.cs') `
    (Join-Path $projectRoot 'UsageTrustPolicy.cs') `
    (Join-Path $projectRoot 'CodexAppServerClient.cs') `
    (Join-Path $projectRoot 'AutoResumePolicy.cs') `
    (Join-Path $projectRoot 'CodexDesktopResumeClient.cs') `
    (Join-Path $projectRoot 'CodexDesktopContract.cs') `
    (Join-Path $projectRoot 'AutoResumeService.cs') `
    (Join-Path $projectRoot 'tests\AutoResumeTests.cs') `
    (Join-Path $projectRoot 'GitHubReleaseUpdateService.cs') `
    (Join-Path $projectRoot 'GitHubUpdateInstaller.cs') `
    (Join-Path $projectRoot 'tests\ResetRadarBannerTests.cs') `
    (Join-Path $projectRoot 'FirstRunGuideForm.cs') `
    (Join-Path $projectRoot 'CodexLifecycle.cs') `
    (Join-Path $projectRoot 'CodexLifecycleLauncher.cs') `
    (Join-Path $projectRoot 'MsixUpdater\AppxInstaller.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Downloader.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Fe3Client.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Http.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Logger.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Models.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Settings.cs') `
    (Join-Path $projectRoot 'MsixUpdater\StoreApi.cs') `
    (Join-Path $projectRoot 'MsixUpdater\Util.cs') `
    (Join-Path $projectRoot 'MsixUpdater\WinRtAppx.cs') `
    (Join-Path $projectRoot 'OverlaySettings.cs') `
    (Join-Path $projectRoot 'ResetRadarService.cs') `
    (Join-Path $projectRoot 'ResetRadarBannerPolicy.cs') `
    (Join-Path $projectRoot 'ResetRadarBannerVisuals.cs') `
    (Join-Path $projectRoot 'tests\RenderingCompatibilityTests.cs') `
    (Join-Path $projectRoot 'tests\UpdateMenuVisualsTests.cs') `
    (Join-Path $projectRoot 'tests\OverlayInteractionTests.cs') `
    (Join-Path $projectRoot 'tests\ComposerUsagePillsTests.cs') `
    (Join-Path $projectRoot 'tests\PlacementMenuLayoutTests.cs') `
    (Join-Path $projectRoot 'tests\PopupAnchorPlacementTests.cs') `
    (Join-Path $projectRoot 'tests\UsageTrustPolicyTests.cs') `
    (Join-Path $projectRoot 'tests\UsageDisplayTextTests.cs') `
    (Join-Path $projectRoot 'tests\GitHubReleaseUpdateTests.cs') `
    (Join-Path $projectRoot 'tests\CodexLifecycleTests.cs') `
    (Join-Path $projectRoot 'tests\OverlaySettingsTests.cs') `
    (Join-Path $projectRoot 'tests\MsixUpdaterTests.cs') `
    (Join-Path $projectRoot 'tests\ResetRadarTests.cs')

if ($LASTEXITCODE -ne 0) {
    throw "Test build failed with exit code $LASTEXITCODE"
}

& (Join-Path $outputDir 'ResetRadarTests.exe')
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}
