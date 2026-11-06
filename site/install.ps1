# Nexonic Download Manager - one-line installer for Windows.
#
#   irm https://shahrukhalid.github.io/nexonic-download-manager/install.ps1 | iex
#
# Installs the latest release from GitHub. Re-running installs the newest
# version over the old one; downloads and settings are kept.
#
# Options (when saved and run as a script):
#   install.ps1 [-Portable] [-NoLaunch] [-Dir <path>]
#   -Portable  install the portable .zip instead of running the Setup (.exe)
#   -NoLaunch  do not start the app after installing
#   -Dir       where the portable version goes (default: $env:LOCALAPPDATA\Programs\Nexonic Download Manager)

param(
  [switch]$Portable,
  [switch]$NoLaunch,
  [string]$Dir = ""
)

$ErrorActionPreference = "Stop"
$Repo = "Shahrukhalid/nexonic-download-manager"
$BaseUrl = "https://github.com/$Repo/releases/latest/download"
$TermsUrl = "https://github.com/$Repo/blob/main/TERMS.md"
$SetupName = "NexonicDownloadManager-Windows-Setup.exe"
$PortableName = "NexonicDownloadManager-Windows-Portable.zip"

Write-Host "Installing Nexonic Download Manager (free and open source, MIT or Apache-2.0)."
Write-Host "By installing you agree to the Terms of Use: $TermsUrl"

function Get-ExpectedSha256([string]$AssetName) {
  try {
    $latest = Invoke-RestMethod -Uri "$BaseUrl/latest.json" -TimeoutSec 60
    foreach ($a in $latest.assets) {
      if ($a.name -eq $AssetName -and $a.digest -like "sha256:*") {
        return ($a.digest -replace "^sha256:", "").ToLower()
      }
    }
  } catch {
    Write-Warning "Could not fetch checksums; skipping verification ($($_.Exception.Message))"
  }
  return $null
}

function Test-Sha256([string]$Path, [string]$AssetName) {
  $want = Get-ExpectedSha256 $AssetName
  if (-not $want) { return }
  $got = (Get-FileHash $Path -Algorithm SHA256).Hash.ToLower()
  if ($got -ne $want) {
    throw "Checksum mismatch for $AssetName.`n  expected: $want`n  actual:   $got"
  }
  Write-Host "Checksum OK ($AssetName)."
}

$Temp = New-Item -ItemType Directory -Path (Join-Path $env:TEMP ("nexonic-" + [Guid]::NewGuid().ToString("N")))
try {
  if ($Portable) {
    if (-not $Dir) { $Dir = Join-Path $env:LOCALAPPDATA "Programs\Nexonic Download Manager" }
    Write-Host "Downloading the latest portable version…"
    $zip = Join-Path $Temp "portable.zip"
    Invoke-WebRequest -Uri "$BaseUrl/$PortableName" -OutFile $zip -UseBasicParsing
    Test-Sha256 $zip $PortableName

    # Close a running copy so files can be replaced.
    Get-Process -Name "NexonicDownloadManager" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    New-Item -ItemType Directory -Path $Dir -Force | Out-Null
    Expand-Archive -Path $zip -DestinationPath $Dir -Force
    $exe = Join-Path $Dir "NexonicDownloadManager.exe"
    if (-not (Test-Path $exe)) {
      # Fall back to whatever exe the zip contained.
      $exe = (Get-ChildItem $Dir -Filter "*.exe" -Recurse | Select-Object -First 1).FullName
    }
    Write-Host "Installed Nexonic Download Manager (portable) in $Dir."
    if (-not $NoLaunch -and $exe) {
      Write-Host "Opening the app…"
      Start-Process $exe
    }
  } else {
    Write-Host "Downloading the latest version…"
    $setup = Join-Path $Temp $SetupName
    Invoke-WebRequest -Uri "$BaseUrl/$SetupName" -OutFile $setup -UseBasicParsing
    Test-Sha256 $setup $SetupName

    # Close a running copy so the installer can replace it.
    Get-Process -Name "NexonicDownloadManager" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    Write-Host "Running the installer…"
    # Per-user install (no admin rights needed). Inno Setup flags.
    $installArgs = @("/SP-", "/SILENT", "/SUPPRESSMSGBOXES", "/TASKS=addtopath,desktopicon")
    if ($NoLaunch) { $installArgs += "/NORESTART" }
    $p = Start-Process -FilePath $setup -ArgumentList $installArgs -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "The installer exited with code $($p.ExitCode)." }

    $app = Join-Path $env:LOCALAPPDATA "Programs\Nexonic Download Manager\NexonicDownloadManager.exe"
    if (Test-Path $app) {
      Write-Host "Installed Nexonic Download Manager."
      if (-not $NoLaunch) {
        Write-Host "Opening the app…"
        Start-Process $app
      }
    } else {
      Write-Host "The installer finished."
    }
  }
} finally {
  Remove-Item $Temp -Recurse -Force -ErrorAction SilentlyContinue
}
