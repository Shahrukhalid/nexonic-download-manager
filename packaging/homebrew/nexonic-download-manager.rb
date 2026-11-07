# Created with scripts/package-manifests.py
cask "nexonic-download-manager" do
  version "0.18.1"
  sha256 "d8a8a9ab67e2d8a5de41a15abd90d57628d56df3db183e7ea6a72eb361615852"

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
