param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$Report, [switch]$AllowVisibleWindows)
$ErrorActionPreference = 'Stop'
if (-not $AllowVisibleWindows) { throw 'This validation displays native windows. Obtain user consent and explicitly pass -AllowVisibleWindows; use offline previews otherwise.' }
$taskRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$taskHelper = Join-Path ([IO.Path]::GetTempPath()) ('pills-popup-ui-' + [guid]::NewGuid().ToString('N') + '.exe')
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
try {
    & $taskCompiler /nologo /target:winexe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$taskHelper" (Join-Path $taskRoot 'tools\validate-popup-ui.cs')
    if ($LASTEXITCODE -ne 0) { throw 'UI validation helper build failed.' }
    $taskProcess = Start-Process -FilePath $taskHelper -ArgumentList ('"' + (Resolve-Path -LiteralPath $Executable).Path + '" "' + [IO.Path]::GetFullPath($Report) + '"') -WindowStyle Hidden -Wait -PassThru
    if ($taskProcess.ExitCode -ne 0) { if (Test-Path -LiteralPath $Report) { Get-Content -LiteralPath $Report }; throw 'UI validation failed.' }
    Get-Content -LiteralPath $Report
}
finally {
    if (Test-Path -LiteralPath $taskHelper) { Remove-Item -LiteralPath $taskHelper }
}
