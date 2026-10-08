#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$InstallRoot,
    [string]$PackagePath,
    [switch]$PinnedRelease,
    [switch]$NoLaunch,
    [switch]$NoShortcuts,
    [switch]$Startup,
    [switch]$FunctionsOnly
)
$ErrorActionPreference = 'Stop'

function Assert-InstallPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $cursor = $resolved
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Install path contains a reparse point.'
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $resolved
}

function Assert-InstallRelease($Release) {
    if ($Release.version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $Release.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        $Release.size -le 0 -or $Release.size -gt 64MB) { throw 'Invalid release metadata.' }
    $expectedUrl = 'https://github.com/jinfu0111-gif/CodexQuotaPills/releases/download/v{0}/CodexQuotaPills-{0}-portable.zip' -f $Release.version
    if ($Release.url -cne $expectedUrl) { throw 'Release URL does not belong to the expected asset.' }
    return $Release
}

function Convert-InstallRelease($Metadata) {
    if ($Metadata.draft -ne $false -or $Metadata.prerelease -ne $false -or
        $Metadata.tag_name -notmatch '^v(\d+\.\d+\.\d+)$' -or
        $Metadata.html_url -cne ('https://github.com/jinfu0111-gif/CodexQuotaPills/releases/tag/' + $Metadata.tag_name)) {
        throw 'Not a stable release of this repository.'
    }
    $releaseVersion = $Metadata.tag_name.Substring(1)
    $assets = @($Metadata.assets | Where-Object { $_.name -ceq "CodexQuotaPills-$releaseVersion-portable.zip" })
    if ($assets.Count -ne 1 -or $assets[0].state -cne 'uploaded' -or
        $assets[0].digest -notmatch '^sha256:[0-9a-fA-F]{64}$') { throw 'Missing or ambiguous verified portable asset.' }
    return Assert-InstallRelease ([pscustomobject]@{
        version = $releaseVersion; url = $assets[0].browser_download_url
        size = $assets[0].size; sha256 = $assets[0].digest.Substring(7)
    })
}

# Same boundaries as GitHubUpdateInstaller: fixed GitHub asset, bounded HTTPS
# redirects, no credentials, bounded download, full archive validation before writes.
function Receive-InstallFile([string]$Url, [string]$Destination, [long]$Size, [switch]$Metadata) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    $current = [uri]$Url
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    for ($hop = 0; $hop -lt 5; $hop++) {
        if ($current.Scheme -cne 'https' -or $current.Port -ne 443 -or $current.UserInfo) { throw 'Invalid HTTPS destination.' }
        $request = [Net.HttpWebRequest]::Create($current)
        $request.UserAgent = 'CodexQuotaPills-Installer'
        $request.AllowAutoRedirect = $false
        $request.UseDefaultCredentials = $false
        $request.Credentials = $null
        $request.Timeout = 15000
        $request.ReadWriteTimeout = 15000
        $response = $null
        try {
            $response = $request.GetResponse()
            if ([int]$response.StatusCode -in @(301,302,303,307,308)) {
                $next = [uri]::new($current, $response.Headers['Location'])
                if ($Metadata -or $next.Host -cne 'release-assets.githubusercontent.com') { throw 'Untrusted redirect.' }
                $current = $next
                continue
            }
            if ([int]$response.StatusCode -ne 200) { throw 'Download failed.' }
            if (-not $Metadata -and $response.ContentLength -ge 0 -and $response.ContentLength -ne $Size) { throw 'Asset size mismatch.' }
            $stream = $response.GetResponseStream()
            $output = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $buffer = New-Object byte[] 32768
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    if ($output.Length + $read -gt $Size -or [DateTime]::UtcNow -gt $deadline) { throw 'Download exceeded size or time limit.' }
                    $output.Write($buffer, 0, $read)
                }
                if (-not $Metadata -and $output.Length -ne $Size) { throw 'Incomplete asset.' }
            } finally { $output.Dispose(); $stream.Dispose() }
            return
        } finally { if ($response) { $response.Dispose() } }
    }
    throw 'Too many redirects.'
}

function Expand-InstallPackage([string]$Zip, [string]$Target, [string]$Version) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $targetRoot = (Assert-InstallPath $Target).TrimEnd('\') + '\'
    if (Test-Path -LiteralPath $Target) { throw 'Extraction target must be new.' }
    $prefix = "CodexQuotaPills-$Version-portable/"
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 500) { throw 'Invalid archive entry count.' }
        $names = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $total = 0L
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if (-not $name.StartsWith($prefix, [StringComparison]::Ordinal) -or
                $name.Contains('\') -or $name.Contains(':') -or $name.Contains([char]0) -or
                -not $names.Add($name) -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw 'Unsafe archive entry.'
            }
            $relative = $name.Substring($prefix.Length)
            if ($relative.TrimEnd('/').Contains('//')) { throw 'Non-canonical archive path.' }
            foreach ($segment in $relative.Split('/')) {
                if ($segment -in @('.','..') -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
                    $segment -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' -or
                    $segment.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw 'Unsafe Windows archive path.' }
            }
            $resolved = [IO.Path]::GetFullPath((Join-Path $Target $relative.Replace('/','\')))
            if ($relative -and -not $resolved.StartsWith($targetRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive path escapes target.' }
            $total += $entry.Length
            if ($entry.Length -gt 32MB -or $total -gt 128MB) { throw 'Archive extraction size exceeded.' }
        }
        New-Item -ItemType Directory -Path $Target | Out-Null
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Substring($prefix.Length)
            if (-not $relative) { continue }
            $destination = Join-Path $Target $relative.Replace('/','\')
            if ($entry.FullName.EndsWith('/')) { New-Item -ItemType Directory -Path $destination -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
        }
    } finally { $archive.Dispose() }
}

function Get-InstallDiagnostics([string]$Exe) {
    $process = Start-Process -FilePath $Exe -ArgumentList '--snapshot' -WorkingDirectory (Split-Path -Parent $Exe) -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Read-only diagnostic timed out.' }
        $data = @{}
        foreach ($line in (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $Exe) 'snapshot.txt'))) {
            if ($line -match '^(CodexWindow|DataSource|LastError)=(.*)$') { $data[$Matches[1]] = $Matches[2] }
        }
        return [pscustomobject]@{
            WindowFound = ($data.CodexWindow -eq 'found')
            ConnectionReady = ($process.ExitCode -eq 0 -and $data.DataSource -eq 'Codex CLI app-server' -and $data.ContainsKey('LastError') -and -not $data.LastError)
        }
    } finally { $process.Dispose() }
}

function Initialize-InstallShortcuts {
    if ('QuotaInstall.Shortcuts' -as [type]) { return }
    # IShellLinkW and IPersistFile use Unicode even on an English Windows host.
    # WScript.Shell's shortcut persistence can convert paths via the ANSI locale.
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
namespace QuotaInstall {
 [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
 internal class ShellLink {}
 [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 internal interface IShellLinkW {
  void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr data, uint flags);
  void GetIDList(out IntPtr id);
  void SetIDList(IntPtr id);
  void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
  void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
  void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length);
  void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
  void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
  void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
  void GetHotkey(out short key);
  void SetHotkey(short key);
  void GetShowCmd(out int command);
  void SetShowCmd(int command);
  void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, out int index);
  void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
  void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
  void Resolve(IntPtr window, uint flags);
  void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
 }
 public static class Shortcuts {
  public static string[] Read(string file) {
   object instance = new ShellLink();
   try {
    ((IPersistFile)instance).Load(file, 0);
    IShellLinkW link = (IShellLinkW)instance;
    StringBuilder target = new StringBuilder(32768), args = new StringBuilder(32768), working = new StringBuilder(32768);
    link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
    link.GetArguments(args, args.Capacity);
    link.GetWorkingDirectory(working, working.Capacity);
    return new string[] { target.ToString(), args.ToString(), working.ToString() };
   } finally { Marshal.FinalReleaseComObject(instance); }
  }
  public static void Save(string file, string target) {
   object instance = new ShellLink();
   try {
    IShellLinkW link = (IShellLinkW)instance;
    link.SetPath(target);
    link.SetArguments("");
    link.SetWorkingDirectory(Path.GetDirectoryName(target));
    ((IPersistFile)instance).Save(file, true);
   } finally { Marshal.FinalReleaseComObject(instance); }
  }
 }
}
'@
}

function Get-InstallShortcut([string]$Path) {
    $null = Assert-InstallPath $Path
    Initialize-InstallShortcuts
    $fields = [QuotaInstall.Shortcuts]::Read($Path)
    return [pscustomobject]@{ TargetPath=$fields[0]; Arguments=$fields[1]; WorkingDirectory=$fields[2] }
}

function Save-InstallShortcut([string]$Path, [string]$Exe) {
    $null = Assert-InstallPath $Path
    Initialize-InstallShortcuts
    if (Test-Path -LiteralPath $Path) {
        $old = Get-InstallShortcut $Path
        if ([IO.Path]::GetFileName($old.TargetPath) -ine 'CodexQuotaPills.exe') { throw 'Shortcut name is already used by another program.' }
        if ($old.TargetPath -eq $Exe -and -not $old.Arguments) { return }
        Copy-Item -LiteralPath $Path -Destination ($Path + '.' + [guid]::NewGuid().ToString('N') + '.bak')
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [QuotaInstall.Shortcuts]::Save($Path, $Exe)
}

function Initialize-InstallLedger([string]$Path) {
    $null = Assert-InstallPath $Path
    if (Test-Path -LiteralPath $Path) { return 'unchanged' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $content = '{"version":2,"enabled":false,"accountHash":null,"outcomes":{},"counts":{},"fingerprints":{}}'
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $bytes = [Text.Encoding]::UTF8.GetBytes($content); $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    return 'paused-on-first-install'
}

if ($FunctionsOnly) { return }
if ($env:OS -ne 'Windows_NT') { throw 'Windows 10/11 is required.' }
if ($Startup -and $NoShortcuts) { throw 'Startup and NoShortcuts cannot be combined.' }
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
if (-not $framework -or $framework.Release -lt 461808) { throw '.NET Framework 4.7.2 or later is required.' }
$isolatedRoot = -not [string]::IsNullOrWhiteSpace($InstallRoot)
if (-not $InstallRoot) { $InstallRoot = Join-Path $env:LOCALAPPDATA 'Programs\Codex Quota Pills' }
$InstallRoot = Assert-InstallPath $InstallRoot
$stage = Join-Path $InstallRoot ('.staging\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$selection = 'latest-release'
if ($PinnedRelease -or $PackagePath) {
    $release = Assert-InstallRelease (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'install-release.json') -Raw | ConvertFrom-Json)
    $selection = 'pinned-release'
} else {
    $metadataFile = Join-Path $stage 'release.json'
    try { Receive-InstallFile 'https://api.github.com/repos/jinfu0111-gif/CodexQuotaPills/releases/latest' $metadataFile 1MB -Metadata }
    catch {
        Write-Warning 'Latest release lookup unavailable. Using the explicitly pinned release; this is not a latest-version confirmation.'
        $PinnedRelease = $true
        $selection = 'pinned-release-fallback'
    }
    if ($PinnedRelease) { $release = Assert-InstallRelease (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'install-release.json') -Raw | ConvertFrom-Json) }
    else { $release = Convert-InstallRelease (Get-Content -LiteralPath $metadataFile -Raw | ConvertFrom-Json) }
}
$zip = Join-Path $stage 'download.zip'
if ($PackagePath) { Copy-Item -LiteralPath $PackagePath -Destination $zip }
else { Receive-InstallFile $release.url $zip $release.size }
if ((Get-Item -LiteralPath $zip).Length -ne $release.size -or
    (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ine $release.sha256) { throw 'Package SHA-256 or size mismatch. Installation stopped.' }
$expanded = Join-Path $stage 'package'
Expand-InstallPackage $zip $expanded $release.version
$stagedExe = Join-Path $expanded 'CodexQuotaPills.exe'
if (-not (Test-Path -LiteralPath $stagedExe) -or (Get-Item -LiteralPath $stagedExe).VersionInfo.FileVersion -ne ($release.version + '.0')) { throw 'Executable version mismatch.' }
foreach ($required in @('LICENSE','NOTICE.md','THIRD_PARTY_NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $expanded $required) -PathType Leaf)) { throw 'Required license notice missing.' }
}
$exeHash = (Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash
$candidates = @()
$startupLinks = @()
if (-not $isolatedRoot) {
    foreach ($link in (Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('Startup')) -Filter '*.lnk')) {
        $shortcut = Get-InstallShortcut $link.FullName
        if ([IO.Path]::GetFileName($shortcut.TargetPath) -ieq 'CodexQuotaPills.exe') {
            $startupLinks += $link.FullName
            $candidates += $shortcut.TargetPath
        }
    }
    $candidates += Join-Path $InstallRoot 'CodexQuotaPills.exe'
    $menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Quota Pills.lnk'
    if (Test-Path -LiteralPath $menuLink) {
        $menuTarget = (Get-InstallShortcut $menuLink).TargetPath
        if ([IO.Path]::GetFileName($menuTarget) -ieq 'CodexQuotaPills.exe') { $candidates += $menuTarget }
    }
    foreach ($instance in @(Get-Process -Name CodexQuotaPills -ErrorAction SilentlyContinue)) {
        if ($instance.Path) { $candidates += $instance.Path }
    }
}
$versionDir = Join-Path $InstallRoot ('versions\' + $release.version + '-' + $exeHash.Substring(0,12).ToLowerInvariant())
$candidates += Join-Path $versionDir 'CodexQuotaPills.exe'
$exe = $null
$previousDir = $null
foreach ($candidate in ($candidates | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
    $candidate = Assert-InstallPath $candidate
    $candidateVersion = (Get-Item -LiteralPath $candidate).VersionInfo.FileVersion
    if ($candidateVersion -match '^\d+\.\d+\.\d+\.\d+$' -and [version]$candidateVersion -gt [version]($release.version + '.0')) { throw 'A newer local installation exists. Refusing to downgrade or change its settings.' }
    if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -eq $exeHash) { $exe = $candidate; break }
    if (-not $previousDir) { $previousDir = Split-Path -Parent $candidate }
}
$reused = $null -ne $exe
if (-not $exe) {
    $null = Assert-InstallPath $versionDir
    if (Test-Path -LiteralPath $versionDir) { throw 'Existing install directory failed verification; preserved for inspection.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $versionDir) -Force | Out-Null
    Move-Item -LiteralPath $expanded -Destination $versionDir
    $exe = Join-Path $versionDir 'CodexQuotaPills.exe'
    if ($previousDir -and (Test-Path -LiteralPath (Join-Path $previousDir 'settings.ini'))) {
        $settingsFile = Assert-InstallPath (Join-Path $previousDir 'settings.ini')
        Copy-Item -LiteralPath $settingsFile -Destination $versionDir
    }
}
$result = [ordered]@{
    Installed = $true; Version = $release.version; ReleaseSelection = $selection; ReusedExisting = $reused
    Executable = $exe; PackageVerified = $true; ConnectionReady = $null; WindowFound = $null
    LaunchRequested = $false; Running = $false; Visible = $false; AutoResume = 'unchanged'
    NextAction = 'Launch skipped.'
}
if (-not $NoShortcuts) {
    Save-InstallShortcut (Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Quota Pills.lnk') $exe
    if ($Startup) {
        if ($startupLinks.Count -gt 1) { throw 'Multiple existing Startup links found; preserved for inspection.' }
        $startupPath = if ($startupLinks.Count -eq 1) { $startupLinks[0] } else { Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Quota Pills.lnk' }
        Save-InstallShortcut $startupPath $exe
    }
}
if (-not $NoLaunch) {
    $diagnostics = Get-InstallDiagnostics $exe
    $result.ConnectionReady = $diagnostics.ConnectionReady
    $result.WindowFound = $diagnostics.WindowFound
    # Fresh installations pause automatic messaging. Existing records, account
    # hashes, counters, cancellations and explicit preferences are never reset.
    $ledger = Join-Path $env:LOCALAPPDATA 'CodexQuotaPills\AutoResume\ledger.json'
    $result.AutoResume = Initialize-InstallLedger $ledger
    $processes = @(Get-Process -Name CodexQuotaPills -ErrorAction SilentlyContinue)
    $otherInstance = @($processes | Where-Object { $_.Path -ne $exe })
    if ($otherInstance.Count -gt 0) {
        $result.NextAction = 'Another installation is running. Exit it using its PLUS menu, then run this installer again.'
    } else {
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden | Out-Null
        $result.LaunchRequested = $true
        Start-Sleep -Seconds 3
        $processes = @(Get-Process -Name CodexQuotaPills -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
        $result.Running = $processes.Count -gt 0
        if (-not ('QuotaInstall.Windows' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace QuotaInstall {
 public static class Windows {
  private delegate bool EnumProc(IntPtr window, IntPtr param);
  [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr param);
  [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
  public static bool Visible(int pid) {
   bool found = false;
   EnumWindows(delegate(IntPtr window, IntPtr param) {
    uint owner; GetWindowThreadProcessId(window, out owner);
    if (owner == pid && IsWindowVisible(window)) { found = true; return false; }
    return true;
   }, IntPtr.Zero);
   return found;
  }
 }
}
'@
        }
        foreach ($process in $processes) { if ([QuotaInstall.Windows]::Visible($process.Id)) { $result.Visible = $true } }
        if ($result.Visible -and $result.ConnectionReady) { $result.NextAction = 'Ready. Open the plan pill menu to change display settings.' }
        elseif (-not $result.ConnectionReady) { $result.NextAction = 'Installed, but quota connection is not ready. Open and sign in to Windows Codex, then retry the read-only snapshot.' }
        else { $result.NextAction = 'Installed and quota connection verified; visibility not confirmed. Bring Codex to the foreground. If still hidden, double-click the executable from Explorer and inspect %LOCALAPPDATA%\CodexQuotaPills\lifecycle.log.' }
    }
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'install-result.json') -Encoding UTF8
[pscustomobject]$result
