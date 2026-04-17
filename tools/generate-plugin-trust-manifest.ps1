<#
.SYNOPSIS
    Generates plugins_trusted.manifest — the SHA-256 allow-list consumed by
    PluginTrustPolicy at startup.

.DESCRIPTION
    Scans the configured plugin output directories after a Release build,
    hashes every AccessibleTrader.Plugins.*.dll it finds, and writes a
    newline-separated manifest with one SHA-256 digest per line and a
    trailing `# filename.dll` comment.

    The generated file ships next to the app binary (AppContext.BaseDirectory)
    and is loaded by ServiceCollectionExtensions.AddDataPipeline at startup.

    Run this AFTER a clean Release build. Re-run whenever a first-party
    plugin DLL changes (any code change → recompile → new hash).

.EXAMPLE
    pwsh tools/generate-plugin-trust-manifest.ps1

.EXAMPLE
    pwsh tools/generate-plugin-trust-manifest.ps1 -OutputDir "AccessibleTrader.BlazorClient/bin/Release/net10.0-windows10.0.19041.0"
#>

[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $OutputFile,
    [string[]] $PluginRoots
)

if (-not $PluginRoots) {
    $PluginRoots = @(
        Join-Path $RepoRoot "Plugins\Providers",
        Join-Path $RepoRoot "Plugins\Analytics",
        Join-Path $RepoRoot "Plugins\Indicators"
    )
}

if (-not $OutputFile) {
    $OutputFile = Join-Path $RepoRoot "plugins_trusted.manifest"
}

Write-Host "Generating plugin trust manifest..."
Write-Host "  Repo root:     $RepoRoot"
Write-Host "  Plugin roots:  $($PluginRoots -join ', ')"
Write-Host "  Output file:   $OutputFile"
Write-Host ""

$entries = @()
foreach ($root in $PluginRoots) {
    if (-not (Test-Path $root)) { continue }
    # Only hash Release-build DLLs with the AccessibleTrader.Plugins.* naming
    # convention that PluginLoaderService scans for.
    $dlls = Get-ChildItem -Path $root -Filter "AccessibleTrader.Plugins.*.dll" -Recurse -File |
            Where-Object { $_.FullName -match "\\bin\\Release\\" } |
            Sort-Object FullName -Unique
    foreach ($dll in $dlls) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dll.FullName).Hash
        $entries += [pscustomobject]@{ Hash = $hash; Name = $dll.Name }
        Write-Host ("  {0}  {1}" -f $hash, $dll.Name)
    }
}

if ($entries.Count -eq 0) {
    Write-Warning "No plugin DLLs found — did you run a Release build first?"
    exit 1
}

# Deduplicate on (Hash, Name) — the same plugin may appear in multiple bin/
# subdirectories from different TFMs; we only want one entry per DLL.
$entries = $entries | Sort-Object Hash, Name -Unique

$header = @(
    "# AccessibleTrader plugin trust manifest",
    "# Generated: $(Get-Date -Format 'u')",
    "# One SHA-256 hex digest per line. '#' starts a comment.",
    "# Re-generate via tools/generate-plugin-trust-manifest.ps1 after each Release build.",
    ""
)

$lines = $header + ($entries | ForEach-Object { "$($_.Hash)  # $($_.Name)" })
Set-Content -Path $OutputFile -Value $lines -Encoding UTF8

Write-Host ""
Write-Host "Wrote $($entries.Count) trusted plugin hashes to $OutputFile"
