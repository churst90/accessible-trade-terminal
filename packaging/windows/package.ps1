<#
  Lays out the Windows desktop download and builds its installer, from a finished
  `dotnet publish` of the BlazorClient. Run on a Windows runner by release.yml and
  diagnostic-build.yml.

  The publish is about 1,070 files, 480 of them loose beside the app's exe, and three exes among
  them, so the old zip made people hunt for the one to run. Both downloads now share one layout:

      Accessible Trader <version>\
          Accessible Trader.exe   the launcher (AccessibleTrader.Launcher, NativeAOT)
          README.txt              says which file to run
          app\                    the publish, untouched

  Outputs, in -OutDir:
      <ZipName>                                     the portable zip, that folder at its root
      AccessibleTrader-<version>-Windows-Setup.exe  the Inno Setup installer (per-user, no admin)

  Then it TRIES them, because a packaging step nobody ran is how v2.12.0 shipped payloads it did
  not contain: the launcher must start the app; the installer must install every staged file
  byte for byte, make the Start menu entry, clear an older version's leftovers when run again,
  and uninstall cleanly. -SkipTrials skips that (never in release.yml).
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $Version,
    [string] $NumericVersion = ($Version -replace '-.*$', ''),
    [string] $OutDir = 'dist',
    [string] $ZipName = "AccessibleTrader-Windows-$Version-win-x64.zip",
    [switch] $SkipTrials
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$PublishDir = (Resolve-Path $PublishDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$work = Join-Path $tempRoot 'att-package'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

$appExeName = 'AccessibleTrader.BlazorClient.exe'
if (-not (Test-Path (Join-Path $PublishDir $appExeName))) {
    throw "$appExeName is not in $PublishDir. Point -PublishDir at the BlazorClient's publish output."
}

# ── The app's icon, for the launcher and the setup ──────────────────────────────────────────
# MAUI's Resizetizer makes it from Resources/AppIcon/appicon.svg during the publish. Without it
# both still work, with Windows' plain program icon.
$ico = Get-ChildItem (Join-Path $repo 'AccessibleTrader.BlazorClient/obj') -Recurse -Filter '*.ico' -ErrorAction SilentlyContinue |
       Where-Object { $_.Name -like 'appicon*' } | Select-Object -First 1
if ($ico) { Write-Host "App icon: $($ico.FullName)" }
else      { Write-Host "::warning::No appicon .ico under the BlazorClient's obj folder; the launcher and the setup get Windows' default icon." }

# ── The launcher ────────────────────────────────────────────────────────────────────────────
$launcherOut = Join-Path $work 'launcher'
$launcherArgs = @(
    'publish', (Join-Path $repo 'AccessibleTrader.Launcher/AccessibleTrader.Launcher.csproj'),
    '--configuration', 'Release', '--runtime', 'win-x64',
    '-p:PublishAot=true', "-p:Version=$Version", '--output', $launcherOut)
if ($ico) { $launcherArgs += "-p:ApplicationIcon=$($ico.FullName)" }
& dotnet @launcherArgs
if ($LASTEXITCODE -ne 0) { throw "The launcher did not publish (exit $LASTEXITCODE)." }
$launcherExe = Join-Path $launcherOut 'AccessibleTrader.Launcher.exe'
if (-not (Test-Path $launcherExe)) { throw "The launcher published, but $launcherExe is not there." }

# ── Stage ───────────────────────────────────────────────────────────────────────────────────
$top   = "Accessible Trader $Version"
$stage = Join-Path (Join-Path $work 'stage') $top
$app   = Join-Path $stage 'app'
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $app -Recurse -Force
Copy-Item $launcherExe (Join-Path $stage 'Accessible Trader.exe')
(Get-Content (Join-Path $PSScriptRoot 'README.txt') -Raw).Replace('{VERSION}', $Version) |
    Set-Content (Join-Path $stage 'README.txt') -NoNewline -Encoding utf8

$published = @(Get-ChildItem $PublishDir -Recurse -File).Count
$staged    = @(Get-ChildItem $app -Recurse -File).Count
if ($published -ne $staged) { throw "Staged $staged files into app\ from a publish of $published." }
$loose = @(Get-ChildItem $stage -File | ForEach-Object Name)
Write-Host "Staged '$top': $($loose -join ', ') and app\ ($staged files)"

# ── The portable zip ────────────────────────────────────────────────────────────────────────
# -Path is the folder itself, so the zip's root is that one folder.
$zip = Join-Path $OutDir $ZipName
Compress-Archive -Path $stage -DestinationPath $zip -Force

# ── The installer ───────────────────────────────────────────────────────────────────────────
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    Write-Host 'Inno Setup is not on this runner; installing 6.7.1 from Chocolatey.'
    choco install innosetup --version 6.7.1 -y --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Could not install Inno Setup.' }
}
$setupBase = "AccessibleTrader-$Version-Windows-Setup"
$isccArgs = @(
    "/DAppVersion=$Version", "/DNumericVersion=$NumericVersion",
    "/DSourceDir=$stage", "/DOutputBaseName=$setupBase", "/O$OutDir")
if ($ico) { $isccArgs += "/DSetupIcon=$($ico.FullName)" }
& $iscc @isccArgs (Join-Path $PSScriptRoot 'AccessibleTrader.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }
$setup = Join-Path $OutDir "$setupBase.exe"
if (-not (Test-Path $setup)) { throw "Inno Setup succeeded, but $setup is not there." }

Get-ChildItem $OutDir | ForEach-Object { Write-Host ("{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

if ($SkipTrials) { Write-Host '::warning::Trials skipped: the launcher and the installer were not run.'; return }

# ── Trial 1: the launcher starts the app ────────────────────────────────────────────────────
function Find-App([string] $folder) {
    Get-Process -Name 'AccessibleTrader.BlazorClient' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase) }
}

Start-Process -FilePath (Join-Path $stage 'Accessible Trader.exe')
$started = $null
for ($i = 0; $i -lt 150 -and -not $started; $i++) { Start-Sleep -Milliseconds 200; $started = Find-App $app }
if (-not $started) { throw "Accessible Trader.exe ran, and no $appExeName from $app started within 30 seconds." }
Write-Host "Trial: the launcher started $($started[0].Path)"
$started | Stop-Process -Force
Start-Sleep -Seconds 2

# ── Trial 2: install, upgrade over the top, uninstall ───────────────────────────────────────
$dir = Join-Path $work 'installed'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Accessible Trader.lnk'

function Install([string] $log) {
    $p = Start-Process -FilePath $setup -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$dir`"", "/LOG=`"$log`"")
    if ($p.ExitCode -ne 0) { Get-Content $log -Tail 30; throw "The setup exited with $($p.ExitCode)." }
}

function Hashes([string] $root) {
    $h = @{}
    Get-ChildItem $root -Recurse -File | Where-Object { $_.Name -notlike 'unins*' } | ForEach-Object {
        $h[$_.FullName.Substring($root.Length).TrimStart('\')] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    }
    $h
}

function Assert-SameTree([string] $when) {
    $want = Hashes $stage
    $got  = Hashes $dir
    $missing = @($want.Keys | Where-Object { -not $got.ContainsKey($_) })
    $extra   = @($got.Keys  | Where-Object { -not $want.ContainsKey($_) })
    $changed = @($want.Keys | Where-Object { $got.ContainsKey($_) -and $got[$_] -ne $want[$_] })
    if ($missing.Count + $extra.Count + $changed.Count -gt 0) {
        $m = ($missing | Select-Object -First 5) -join ', '
        $e = ($extra   | Select-Object -First 5) -join ', '
        $c = ($changed | Select-Object -First 5) -join ', '
        throw "$when, the installed folder is not the staged one. Missing: $m; extra: $e; different: $c"
    }
    Write-Host "Trial: $when, all $($want.Count) files installed byte for byte"
}

Install (Join-Path $work 'install-1.log')
Assert-SameTree 'after installing'
if (-not (Test-Path $shortcut)) { throw "No Start menu entry at $shortcut." }
$target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath
if ($target -ne (Join-Path $dir "app\$appExeName")) { throw "The Start menu entry points at $target." }
Write-Host "Trial: Start menu entry -> $target"

# An older version's file left in app\ must be gone after the next setup runs.
$stale = Join-Path $dir 'app\AccessibleTrader.Plugins.LeftOver.dll'
Set-Content $stale 'left by an older version'
Install (Join-Path $work 'install-2.log')
if (Test-Path $stale) { throw 'Running the setup again left an older file in app\.' }
Assert-SameTree 'after installing again over the top'

# The uninstaller copies itself to a temp folder and returns at once, so wait for the folder.
& (Join-Path $dir 'unins000.exe') /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
for ($i = 0; $i -lt 120 -and (Test-Path (Join-Path $dir 'app')); $i++) { Start-Sleep -Milliseconds 500 }
if (Test-Path (Join-Path $dir 'app')) { throw 'The uninstaller left the app folder behind.' }
for ($i = 0; $i -lt 20 -and (Test-Path $shortcut); $i++) { Start-Sleep -Milliseconds 500 }
if (Test-Path $shortcut) { throw 'The uninstaller left the Start menu entry behind.' }
Write-Host 'Trial: uninstalled; the app folder and the Start menu entry are gone'
