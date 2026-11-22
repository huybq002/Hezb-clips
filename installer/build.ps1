# Builds the installer: dotnet publish (self-contained, win-x64) + Inno Setup.
# Usage: powershell -File installer\build.ps1 [-Iscc "C:\path\to\ISCC.exe"]
param([string]$Iscc = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not $Iscc) {
  $Iscc = @("$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "H:\Tools\InnoSetup6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path $Iscc)) { throw "Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or pass -Iscc." }

$version = ([xml](Get-Content src\HezbClips.App\HezbClips.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Version $version"

Get-Process HezbClips -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path artifacts\publish) { Remove-Item artifacts\publish -Recurse -Force }
dotnet publish src\HezbClips.App -c Release -r win-x64 --self-contained true -o artifacts\publish -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

& $Iscc "/DAppVersion=$version" installer\HezbClips.iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = "artifacts\installer\HezbClips-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
Set-Content "$setup.sha256" "$hash  $(Split-Path $setup -Leaf)"
"{0}  {1:N0} MB`nSHA-256 {2}" -f $setup, ((Get-Item $setup).Length / 1MB), $hash
