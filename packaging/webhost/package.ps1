<#
  Lays out the Windows WebHost download from a finished `dotnet publish` of the WebHost for
  win-x64. Run on a Windows runner by release.yml and diagnostic-build.yml; the Linux and macOS
  downloads are package.sh.

  The publish is about 470 files in one folder, and the zip used to be that folder's contents at
  its root. It now has the desktop download's layout:

      AccessibleTrader-WebHost-<version>\
          Accessible Trader.exe   the launcher (AccessibleTrader.Launcher, -p:LauncherApp=WebHost)
          README.txt              which file to run, where your data is kept
          app\                    the publish, untouched

  The launcher starts app\AccessibleTrader.WebHost.exe from inside app\, because the server reads
  appsettings.json (its port among it) from its working directory, and says in a message box if
  the server stops as it starts.

  Output, in -OutDir: AccessibleTrader-WebHost-<version>-win-x64.zip, that folder at its root.

  Then it TRIES the zip: unzipped somewhere new, the launcher started from another folder must
  bring up the server on port 5145, which it binds only if it read its own appsettings.json, and
  the server must write nothing into its own folder. -SkipTrials skips that (never in release.yml).
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutDir = 'dist',
    [switch] $SkipTrials
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$PublishDir = (Resolve-Path $PublishDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$work = Join-Path $tempRoot 'att-webhost-package'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

$serverExeName = 'AccessibleTrader.WebHost.exe'
if (-not (Test-Path (Join-Path $PublishDir $serverExeName))) {
    throw "$serverExeName is not in $PublishDir. Point -PublishDir at the WebHost's win-x64 publish output."
}

# ── The icon, for the launcher ──────────────────────────────────────────────────────────────
# No .ico is in the repository (the desktop one is made by MAUI during its publish). An .ico may
# hold PNG images as they are, so the WebHost's own 32 and 192 pixel icons are wrapped in one.
$ico = Join-Path $work 'AccessibleTrader.ico'
try {
    $pngs = @('favicon-32.png', 'icon-192.png') | ForEach-Object {
        $p = Join-Path $repo "AccessibleTrader.WebHost/wwwroot/icons/$_"
        $b = [IO.File]::ReadAllBytes($p)
        # A PNG's width and height are big-endian at bytes 16 and 20.
        [pscustomobject]@{ Bytes = $b; Size = ($b[18] * 256 + $b[19]) }
    }
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $side = if ($p.Size -ge 256) { 0 } else { $p.Size }
        $w.Write([byte]$side); $w.Write([byte]$side); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$p.Bytes.Length); $w.Write([uint32]$offset)
        $offset += $p.Bytes.Length
    }
    foreach ($p in $pngs) { $w.Write($p.Bytes) }
    $w.Flush(); [IO.File]::WriteAllBytes($ico, $ms.ToArray())
    Write-Host "Launcher icon: $ico ($(($pngs | ForEach-Object Size) -join ' and ') pixels)"
}
catch {
    Write-Host "::warning::Could not make the launcher's icon ($($_.Exception.Message)); it gets Windows' default icon."
    $ico = $null
}

# ── The launcher ────────────────────────────────────────────────────────────────────────────
$launcherOut = Join-Path $work 'launcher'
$launcherArgs = @(
    'publish', (Join-Path $repo 'AccessibleTrader.Launcher/AccessibleTrader.Launcher.csproj'),
    '--configuration', 'Release', '--runtime', 'win-x64',
    '-p:PublishAot=true', '-p:LauncherApp=WebHost', "-p:Version=$Version", '--output', $launcherOut)
if ($ico) { $launcherArgs += "-p:ApplicationIcon=$ico" }
& dotnet @launcherArgs
if ($LASTEXITCODE -ne 0) { throw "The launcher did not publish (exit $LASTEXITCODE)." }
$launcherExe = Join-Path $launcherOut 'AccessibleTrader.Launcher.exe'
if (-not (Test-Path $launcherExe)) { throw "The launcher published, but $launcherExe is not there." }

# ── Stage ───────────────────────────────────────────────────────────────────────────────────
$top   = "AccessibleTrader-WebHost-$Version"
$stage = Join-Path (Join-Path $work 'stage') $top
$app   = Join-Path $stage 'app'
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $app -Recurse -Force
Copy-Item $launcherExe (Join-Path $stage 'Accessible Trader.exe')
(Get-Content (Join-Path $PSScriptRoot 'README-windows.txt') -Raw).Replace('{VERSION}', $Version) |
    Set-Content (Join-Path $stage 'README.txt') -NoNewline -Encoding utf8

$published = @(Get-ChildItem $PublishDir -Recurse -File).Count
$staged    = @(Get-ChildItem $app -Recurse -File).Count
if ($published -ne $staged) { throw "Staged $staged files into app\ from a publish of $published." }
$loose = @(Get-ChildItem $stage -File | ForEach-Object Name)
Write-Host "Staged '$top': $($loose -join ', ') and app\ ($staged files)"

# ── The zip ─────────────────────────────────────────────────────────────────────────────────
# -Path is the folder itself, so the zip's root is that one folder.
$zip = Join-Path $OutDir "AccessibleTrader-WebHost-$Version-win-x64.zip"
Compress-Archive -Path $stage -DestinationPath $zip -Force
Get-Item $zip | ForEach-Object { Write-Host ("{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

if ($SkipTrials) { Write-Host '::warning::Trial skipped: the launcher was not run.'; return }

# ── Trial: unzip, start from elsewhere, serve on 5145, write nothing into its own folder ────
$trial = Join-Path $work 'trial'
$elsewhere = Join-Path $trial 'elsewhere'
New-Item -ItemType Directory -Force -Path $elsewhere | Out-Null
Expand-Archive $zip -DestinationPath (Join-Path $trial 'unzipped')
$folder = Join-Path (Join-Path $trial 'unzipped') $top
$before = @(Get-ChildItem $folder -Recurse | ForEach-Object { $_.FullName }) | Sort-Object

function Get-Status([string] $url) {
    try { (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5).StatusCode } catch { $null }
}

# 5145 is the port in appsettings.json. Started anywhere but app\ the server does not read that
# file and binds 5000 instead, so an answer on 5145 is the proof it ran from app\, as long as
# nothing was answering there before.
$base = 'http://localhost:5145'
if (Get-Status "$base/") { throw "Something already answers on $base, so the trial cannot tell the server apart from it." }

$launcher = Start-Process -FilePath (Join-Path $folder 'Accessible Trader.exe') -ArgumentList '--no-launch' `
    -WorkingDirectory $elsewhere -PassThru
$page = $null
for ($i = 0; $i -lt 120 -and $page -ne 200; $i++) { Start-Sleep -Milliseconds 500; $page = Get-Status "$base/" }
$script = Get-Status "$base/_framework/blazor.web.js"

# The launcher first: it waits on the server for 30 seconds and would put up a message box (and
# hang this job) if the server it is watching were killed in that time.
if (-not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }
$server = Get-Process -Name 'AccessibleTrader.WebHost' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith((Join-Path $folder 'app'), [StringComparison]::OrdinalIgnoreCase) }
$server | Stop-Process -Force
Start-Sleep -Seconds 2

if ($page -ne 200 -or $script -ne 200) {
    throw "Accessible Trader.exe ran from another folder, and $base answered / with '$page' and blazor.web.js with '$script'. Port 5145 is set in app\appsettings.json, which the server reads only when it runs from app\."
}
if (-not $server) { throw "$base answered, but no $serverExeName from $folder\app was running." }
Write-Host "Trial: the launcher, started from another folder, ran $($server[0].Path) and it served the page and blazor.web.js on 5145"

$after = @(Get-ChildItem $folder -Recurse | ForEach-Object { $_.FullName }) | Sort-Object
$new = @(Compare-Object $before $after | Where-Object SideIndicator -eq '=>' | ForEach-Object InputObject)
if ($new.Count -gt 0) { throw "The server wrote into its own folder: $(($new | Select-Object -First 10) -join ', ')" }
Write-Host 'Trial: the server wrote nothing into its own folder'
