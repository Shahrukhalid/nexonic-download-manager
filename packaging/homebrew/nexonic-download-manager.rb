# Created with scripts/package-manifests.py
cask "nexonic-download-manager" do
  version "0.26.0"
  sha256 "4717d1fa6571f55939a10754d5c05fb4aa704fc6c668f2936605a90280a2c788"

  url "https://github.com/Shahrukhalid/nexonic-download-manager/releases/download/v#{version}/NexonicDownloadManager-Mac.dmg"
  name "Nexonic Download Manager"
  desc "Download manager with queues, a scheduler and video downloads"
  homepage "https://github.com/Shahrukhalid/nexonic-download-manager"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: ">= :ventura"

  app "Nexonic Download Manager.app"
  binary "#{appdir}/Nexonic Download Manager.app/Contents/MacOS/nexonic-host", target: "ndm"

  zap trash: [
    "~/Library/Application Support/Nexonic Download Manager",
    "~/Library/Preferences/com.nexonic.downloadmanager.plist",
  ]
end
