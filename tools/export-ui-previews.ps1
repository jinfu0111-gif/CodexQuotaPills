param([string]$Executable, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Executable) { $Executable = Join-Path $taskRoot 'bin\CodexQuotaPills.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'docs\images' }
$taskExe = (Resolve-Path -LiteralPath $Executable).Path
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
# All modes exit before creating the live usage/automatic-resume services.
$taskExports = @(
    @('composer','composer-pills-preview.png'),
    @('hover-card','quota-hover-card-preview.png'),
    @('reset-credits','reset-credits-hover-preview.png'),
    @('placement','unified-plus-menu-preview.png'),
    @('codex-updater','codex-update-preview.png'),
    @('auto-resume','auto-resume-preview.png')
)
foreach ($taskExport in $taskExports) {
    $taskFile = Join-Path $taskOutput $taskExport[1]
    $taskArgument = '--export-' + $taskExport[0] + '-preview="' + $taskFile + '"'
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArgument -WindowStyle Hidden -Wait -PassThru
    if ($taskProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $taskFile)) {
        throw "Preview failed: $($taskExport[0])"
    }
}
# Compatibility filenames now point to the same current unified menu.
foreach ($taskAlias in @('placement-selector-preview.png','auto-resume-menu-preview.png')) {
    Copy-Item -LiteralPath (Join-Path $taskOutput 'unified-plus-menu-preview.png') -Destination (Join-Path $taskOutput $taskAlias)
}
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskHelper = Join-Path ([IO.Path]::GetTempPath()) ('quota-banner-preview-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    & $taskCompiler /nologo /target:exe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$taskHelper" (Join-Path $taskRoot 'tools\export-reset-banner-preview.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Banner preview helper build failed.' }
    & $taskHelper $taskExe $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Banner preview export failed.' }
}
finally {
    if (Test-Path -LiteralPath $taskHelper) { Remove-Item -LiteralPath $taskHelper }
}
Write-Output ('PreviewProgramVersion=' + (Get-Item -LiteralPath $taskExe).VersionInfo.FileVersion)
Write-Output ('PreviewOutput=' + $taskOutput)
