# Builds build\win\ (app, browser helper, extension), the portable zip and the installer .exe.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$version = (Get-Content VERSION).Trim()
python scripts/build-torrent-native.py win-x64
if ($LASTEXITCODE -ne 0) { throw "Native torrent engine build failed" }
$out = "build\win"
if (Test-Path build) { Remove-Item build -Recurse -Force }
# The app and the browser helper (also the ndm command) share one .NET runtime in the app folder, as on Linux:
# the helper adds only its own files instead of carrying a second copy of the runtime.
$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$version")
dotnet publish windows\App\App.csproj @common -o $out
if ($LASTEXITCODE -ne 0) { throw "App build failed" }
dotnet publish windows\Host\Host.csproj @common -o build\host
if ($LASTEXITCODE -ne 0) { throw "Helper build failed" }
foreach ($f in "nexonic-host.exe", "nexonic-host.dll", "nexonic-host.deps.json", "nexonic-host.runtimeconfig.json") {
  Copy-Item "build\host\$f" $out
}
# The helper uses the app's copy of the shared code; both come from the same build.
if ((Get-FileHash build\host\Nexonic.Core.dll).Hash -ne (Get-FileHash "$out\Nexonic.Core.dll").Hash) { throw "Helper and app were built from different code" }
Remove-Item build\host -Recurse -Force
Get-ChildItem $out -Filter *.pdb | Remove-Item
# Small bundled ffmpeg (re-packs streamed video into .mp4), cross-built on Linux by scripts/build-ffmpeg.sh windows.
if (Test-Path .ffmpeg-cache\windows\ffmpeg.exe) {
  Copy-Item .ffmpeg-cache\windows\ffmpeg.exe, .ffmpeg-cache\windows\ffprobe.exe $out
} else {
  Write-Warning "No .ffmpeg-cache\windows\ffmpeg.exe: streamed video will be saved without joining picture and sound"
}
# Video engine for YouTube and other sites (scripts/fetch-video-tools.sh windows .tools-cache/windows).
if (Test-Path .tools-cache\windows\yt-dlp\yt-dlp.exe) {
  # The unpacked engine folder (starts at once; the one-file edition unpacked itself before every video).
  Copy-Item .tools-cache\windows\yt-dlp "$out\yt-dlp" -Recurse
  Copy-Item .tools-cache\windows\qjs.exe $out
  Get-Content packaging\THIRD-PARTY-NOTICES.txt, .ffmpeg-cache\windows\NOTICE-video-tools-1.txt, .tools-cache\windows\NOTICE-video-tools-2.txt |
    Set-Content "$out\THIRD-PARTY-NOTICES.txt"
} else {
  Write-Warning "No .tools-cache\windows\yt-dlp\yt-dlp.exe: videos from YouTube and other sites will not work in this build"
}
Copy-Item .native-cache\sdk\win-x64\bin\nexonic_torrent.dll $out
Copy-Item .native-cache\sdk\win-x64\notices "$out\torrent-licenses" -Recurse
Copy-Item .native-cache\sdk\win-x64\native-components.json "$out\torrent-components.json"
Copy-Item packaging\TERMS.txt $out
python scripts\build-extensions.py
if ($LASTEXITCODE -ne 0) { throw "Extension build failed" }
Copy-Item build\extension-chrome "$out\Extension" -Recurse
Copy-Item "build\NexonicFirefoxAddon-$version.xpi" "$out\Firefox Add-on.xpi"
# The ndm command runs the helper in command-line mode; the installer puts this folder on the user's PATH.
Set-Content -Path "$out\ndm.cmd" -Value "@echo off`r`n`"%~dp0nexonic-host.exe`" %*" -Encoding ascii -NoNewline
Compress-Archive -Path "$out\*" -DestinationPath "build\NexonicDownloadManager-$version-windows-portable.zip"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { choco install innosetup -y --no-progress | Out-Null }
$env:NDM_VERSION = $version
& $iscc windows\installer.iss
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Write-Host "Built build\NexonicDownloadManager-Setup-$version.exe and the portable zip"
