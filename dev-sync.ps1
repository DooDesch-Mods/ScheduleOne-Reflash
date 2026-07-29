# Copy the web bundles into the test install's Mods folder, where Sideload prefers them over the ones compiled into
# the DLL. That override is what makes the loop bearable: a stylesheet change reaches both the in-game phone and the
# companion after a reload, with no rebuild and no restart.
#
#   pwsh -File Reflash/dev-sync.ps1               # the default test install
#   pwsh -File Reflash/dev-sync.ps1 -Game "D:\..." # somewhere else

param(
    [string] $Game = "F:\Projects\Mods\Schedule1\_MPTest\Schedule I"
)

$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "Assets"
$target = Join-Path $Game "Mods"

if (-not (Test-Path $target)) { throw "no Mods folder at $target" }

foreach ($bundle in Get-ChildItem -Path $source -Directory) {
    # The shell is not a Sideload app - it is the companion's own page. A Debug build reads it from
    # Mods/reflash-shell if that folder is there, so it gets the same treatment under a name of its own.
    $into = if ($bundle.Name -eq "shell") { Join-Path $target "reflash-shell" } else { Join-Path $target $bundle.Name }
    New-Item -ItemType Directory -Force -Path $into | Out-Null

    Copy-Item -Path (Join-Path $bundle.FullName "*") -Destination $into -Recurse -Force
    Write-Host "$($bundle.Name) -> $into"
}
