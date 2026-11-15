# Created with scripts/package-manifests.py
cask "nexonic-download-manager" do
  version "0.31.0"
  sha256 "7955a43bc9db3b68c999f9dd68f1e20ec2ac94af994f79955351370536b8079c"

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
