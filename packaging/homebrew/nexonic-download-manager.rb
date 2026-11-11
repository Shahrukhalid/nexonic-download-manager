# Created with scripts/package-manifests.py
cask "nexonic-download-manager" do
  version "0.29.0"
  sha256 "b36a87a51caf9f3e1fe552ad0247f9d9edf057369c52477d35e546b337d91b9b"

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
