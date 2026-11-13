# Microsoft Store edition: packs build\win\ (made by windows\build.ps1) into an MSIX for Partner Center.
# The package is not signed here: the Store signs it after certification. Identity values come from Partner Center →
# the app → Product identity, passed as parameters (the Store workflow takes them from repository variables).
param(
  [Parameter(Mandatory)] [string] $IdentityName,
  [Parameter(Mandatory)] [string] $Publisher,
  [Parameter(Mandatory)] [string] $PublisherName,
  [string] $Arch = "x64",
  [string] $Source = "build\win"
)
$ErrorActionPreference = "Stop"
$version = (Get-Content VERSION).Trim()
if (-not (Test-Path "$Source\NexonicDownloadManager.exe")) { throw "Build the Windows app first (windows\build.ps1)" }

$layout = "build\msix-$Arch"
Remove-Item $layout -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $Source $layout -Recurse
# The Store edition updates through the Store: no installer leftovers in the package.
Get-ChildItem $layout -Filter "unins*" | Remove-Item -Force

# Tile and Store pictures from the app icon.
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force "$layout\Assets" | Out-Null
$icon = [System.Drawing.Image]::FromFile((Resolve-Path "packaging\icon-1024.png"))
function Picture([string]$name, [int]$w, [int]$h, [double]$scale = 1.0) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.Clear([System.Drawing.Color]::Transparent)
  $side = [int]([Math]::Min($w, $h) * $scale)
  $g.DrawImage($icon, [int](($w - $side) / 2), [int](($h - $side) / 2), $side, $side)
  $g.Dispose()
  $bmp.Save("$layout\Assets\$name", [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}
Picture "StoreLogo.png" 50 50
Picture "Square44x44Logo.png" 44 44
Picture "Square44x44Logo.targetsize-256_altform-unplated.png" 256 256
Picture "Square150x150Logo.png" 150 150 0.75
Picture "Wide310x150Logo.png" 310 150 0.75
$icon.Dispose()

$manifest = (Get-Content packaging\msix\AppxManifest.xml -Raw).
  Replace("{IdentityName}", [Security.SecurityElement]::Escape($IdentityName)).
  Replace("{Publisher}", [Security.SecurityElement]::Escape($Publisher)).
  Replace("{PublisherName}", [Security.SecurityElement]::Escape($PublisherName)).
  Replace("{Version}", $version).
  Replace("{Arch}", $Arch)
Set-Content "$layout\AppxManifest.xml" $manifest -Encoding utf8

$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdk) { throw "makeappx.exe (Windows SDK) not found" }
$out = "build\NexonicDownloadManager-$version-$Arch.msix"
& $sdk.FullName pack /o /d $layout /p $out
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
Write-Host "Store package: $out"
