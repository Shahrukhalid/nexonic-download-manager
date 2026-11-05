# CI only: install the pinned public v0.8.1, then this candidate, using a fresh hosted runner profile.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) { throw 'Run this upgrade check only on a GitHub Actions Windows runner.' }
$support = Join-Path $env:LOCALAPPDATA 'Nexonic Download Manager'
# Core tests can already have created this folder. Preserve it rather than overwrite it.
$backup = $null
if (Test-Path $support) {
    $backup = Join-Path $env:RUNNER_TEMP ("nexonic-before-upgrade-" + [Guid]::NewGuid().ToString('N'))
    Move-Item $support $backup
}
try {
$version = (Get-Content VERSION).Trim()
$candidate = (Resolve-Path "build/NexonicDownloadManager-Setup-$version.exe").Path
$old = Join-Path $env:RUNNER_TEMP 'Nexonic-0.8.1-Setup.exe'
Write-Host 'Downloading checksum-pinned v0.8.1 installer (bounded retries)'
& curl.exe --fail --location --retry 3 --retry-delay 3 --connect-timeout 15 --max-time 180 --output $old 'https://github.com/Shahrukhalid/nexonic-download-manager/releases/download/v0.8.1/NexonicDownloadManager-Windows-Setup.exe'
if ($LASTEXITCODE -ne 0) { throw 'Cannot download the pinned v0.8.1 installer within the retry limit.' }
if ((Get-FileHash $old -Algorithm SHA256).Hash.ToLower() -ne '3cf58fecc3d2674ff2eb0c2f2202c86fa090af586987cdc073dbe2ed4ea38f19') { throw 'v0.8.1 installer checksum mismatch.' }
New-Item -ItemType Directory -Force $support | Out-Null
$folder = Join-Path $env:RUNNER_TEMP 'upgrade-downloads'
New-Item -ItemType Directory -Force $folder | Out-Null
$id = '08100000-0000-0000-0000-000000000001'
$parts = Join-Path $support 'Parts/08100000000000000000000000000001'
New-Item -ItemType Directory -Force $parts | Out-Null
$part = Join-Path $parts '0.part'
[IO.File]::WriteAllBytes($part, [byte[]](0..249))
$partHash = (Get-FileHash $part -Algorithm SHA256).Hash
@{ Folder = $folder; MaxActive = 1; CategoryFolders = $false; CheckForUpdates = $false; MonitorClipboard = $false; PlaySound = $false; Automation = @{ Enabled = $false } } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $support 'settings.json')
@(@{Id=$id;Url='https://example.invalid/upgrade.zip';FileName='upgrade.zip';Folder=$folder;Size=1000;Status=3;QueueId='night-v081';Validator='"fixture-etag"';Segments=@(@{Id=0;Start=0;End=999;Written=250})}) | ConvertTo-Json -Depth 5 -AsArray | Set-Content (Join-Path $support 'downloads.json')
@(@{Id='main';Name='Main queue';Running=$false;MaxActive=1},@{Id='night-v081';Name='Night';Running=$false;MaxActive=1}) | ConvertTo-Json -Depth 5 -AsArray | Set-Content (Join-Path $support 'queues.json')
$exe = Join-Path $env:LOCALAPPDATA 'Programs/Nexonic Download Manager/NexonicDownloadManager.exe'
function Install-And-Check([string]$Setup, [string]$Expected) {
    $log = Join-Path $env:RUNNER_TEMP "nexonic-upgrade-$Expected.log"
    Write-Host "Installing and checking $Expected"
    $setupProcess = Start-Process $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$log`"") -PassThru
    # Start-Process -Wait includes the auto-launched app's lifetime. Wait only for the installer.
    if (-not $setupProcess.WaitForExit(180000)) {
        $setupProcess.Kill($true)
        throw "Installer timed out: $log"
    }
    if ($setupProcess.ExitCode -ne 0) { throw "Installer returned $($setupProcess.ExitCode): $log" }
    # Inno's silent update path starts the app. Require it to stay alive before inspecting persisted data.
    $app = $null
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        $app = Get-Process NexonicDownloadManager -ErrorAction SilentlyContinue
        if ($app) { break }
        Start-Sleep -Seconds 1
    }
    if (-not $app) { throw "The $Expected app did not start after installation." }
    Start-Sleep -Seconds 5
    $app = Get-Process NexonicDownloadManager -ErrorAction SilentlyContinue
    if (-not $app) { throw "The $Expected app quit during startup." }
    $app | Stop-Process -Force
    $app | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    $found = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    if (-not $found.StartsWith($Expected)) { throw "Expected $Expected installed, found $found" }
    $data = @(Get-Content (Join-Path $support 'downloads.json') -Raw | ConvertFrom-Json)
    if ($data.Count -ne 1 -or $data[0].Id -ne $id -or $data[0].QueueId -ne 'night-v081' -or $data[0].Status -ne 3 -or $data[0].Segments[0].Written -ne 250) { throw "The $Expected app changed paused download history or progress." }
    if ((Get-FileHash $part -Algorithm SHA256).Hash -ne $partHash) { throw "The $Expected installer/app changed an existing part file." }
    $settings = Get-Content (Join-Path $support 'settings.json') -Raw | ConvertFrom-Json
    if ($settings.Folder -ne $folder -or $settings.CheckForUpdates -ne $false -or $settings.CategoryFolders -ne $false) { throw "The $Expected app lost existing settings." }
    if ($settings.TorrentsEnabled -eq $true) { throw 'Upgrade enabled experimental torrents.' }
    $queues = @(Get-Content (Join-Path $support 'queues.json') -Raw | ConvertFrom-Json)
    if (-not ($queues | Where-Object { $_.Id -eq 'night-v081' -and $_.Name -eq 'Night' -and $_.Running -eq $false })) { throw 'Upgrade lost the saved queue.' }
    if (-not (Test-Path 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{6F1C2E4B-8B7D-4C55-9E2A-3D9B1F0A7C21}_is1')) { throw 'Installer identity changed.' }
    Write-Host "PASS $Expected installed and starts; paused history, queue, settings, part-file hash and installer identity preserved."
}
Install-And-Check $old '0.8.1'
Install-And-Check $candidate $version

} finally {
    $cleanup = Get-Process NexonicDownloadManager -ErrorAction SilentlyContinue
    if ($cleanup) {
        $cleanup | Stop-Process -Force -ErrorAction SilentlyContinue
        $cleanup | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
    if (Test-Path $support) {
        $result = Join-Path $env:RUNNER_TEMP ("nexonic-upgrade-result-" + [Guid]::NewGuid().ToString('N'))
        Move-Item $support $result
    }
    if ($backup -and (Test-Path $backup)) { Move-Item $backup $support }
}
