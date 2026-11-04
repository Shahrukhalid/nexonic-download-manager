# Created with scripts/package-manifests.py
cask "nexonic-download-manager" do
  version "0.8.1"
  sha256 "d52b006ef610f4889fcabff1d8c508785cc873e54154794a5a3b73942d6ddec9"

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
