<#
  Builds Astra and packages it.

    .\build.ps1                 publish + installer  -> dist\Astra-Setup-<version>.exe
    .\build.ps1 -SkipInstaller  publish only         -> dist\publish\Astra.exe
    .\build.ps1 -Test           run the unit tests first

  Put your icon.ico in src\Astra.App\ (or src\Astra.App\Assets\, or this folder) before building.
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipInstaller,
    [switch]$Test
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

if ($Test) {
    dotnet test tests\Astra.Tests\Astra.Tests.csproj -c Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }
}

$publish = Join-Path $root "dist\publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Publishing Astra $Version (self-contained, win-x64)…" -ForegroundColor Cyan
dotnet publish src\Astra.App\Astra.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# Whisper ships native libraries for every OS; only the Windows x64 ones are needed.
Get-ChildItem (Join-Path $publish "runtimes") -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    Get-ChildItem $_.FullName -Directory | Where-Object { $_.Name -ne "win-x64" } | Remove-Item -Recurse -Force
    if (-not (Get-ChildItem $_.FullName)) { Remove-Item $_.FullName -Force }
}
Get-ChildItem $publish -Filter *.pdb -Recurse | Remove-Item -Force

$size = [math]::Round(((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
Write-Host "Published: $publish ($size MB)" -ForegroundColor Green

if ($SkipInstaller) { return }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe", "$env:ProgramFiles\Inno Setup 7\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Warning "Inno Setup not found; skipping the installer. The app is in $publish"; return }

$icon = @("src\Astra.App\icon.ico", "src\Astra.App\Assets\icon.ico", "icon.ico", "Assets\icon.ico") |
        ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
$defines = @("/DAppVersion=$Version")
if ($icon) { $defines += "/DIconFile=$icon"; Write-Host "Installer icon: $icon" }
else { Write-Warning "No icon.ico found; the installer and app use the default icon." }

Write-Host "Building installer…" -ForegroundColor Cyan
& $iscc @defines (Join-Path $root "installer\Astra.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }
Write-Host "Installer: dist\Astra-Setup-$Version.exe" -ForegroundColor Green
