import Foundation
import NexonicCore

// Browser helper (native messaging) and the `ndm` command line: both hand downloads to the app through the inbox.
let version = "0.6.3"

func openApp(_ url: String) {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/usr/bin/open")
    p.arguments = ["-g", url]
    try? p.run()
    p.waitUntilExit()
}

let arguments = Array(CommandLine.arguments.dropFirst())
if CommandLineTool.isCommand(arguments) {
    switch CommandLineTool.parse(arguments) {
    case .help:
        print(CommandLineTool.usage)
    case .failure(let message):
        FileHandle.standardError.write(Data("ndm: \(message)\n".utf8))
        exit(2)
    case .jobs(let jobs):
        do {
            for job in jobs { try Inbox().drop(job) }
            openApp("nexonicdm://inbox")
            print("Added \(jobs.count) download\(jobs.count == 1 ? "" : "s")")
        } catch {
            FileHandle.standardError.write(Data("ndm: \(error.localizedDescription)\n".utf8))
            exit(1)
        }
    }
    exit(0)
}

let input = FileHandle.standardInput
let output = FileHandle.standardOutput

func reply(_ object: [String: Any]) {
    if let data = try? NativeMessaging.encode(object) { output.write(data) }
}

/// The proxy set in the app, so lookups take the same route as downloads. Its password is read from the Keychain only
/// when the proxy needs a login. The settings file is only read here, never changed.
func savedProxy() -> ProxySettings? {
    guard let data = try? Data(contentsOf: AppPaths.settings), var s = try? JSONDecoder().decode(AppSettings.self, from: data) else {
        return nil
    }
    if !s.proxy.username.isEmpty, s.proxy.password.isEmpty, let p = KeychainStore().read()?[Secrets.proxyKey] { s.proxy.password = p }
    return s.proxy
}

/// The picture qualities a video site offers for one link, looked up with the video engine shipped next to this helper.
func qualities(_ m: [String: Any]) -> [String: Any] {
    guard let url = DownloadJob(url: m["url"] as? String ?? "").validURL, SiteVideo.isSupported(url) else {
        return ["ok": false, "error": "Not a video site link"]
    }
    let fm = FileManager.default
    let tools = [SiteVideoTools.ytdlp.path, SiteVideoTools.bundled?.appendingPathComponent("yt-dlp").path].compactMap { $0 }
    guard let tool = tools.first(where: fm.isExecutableFile(atPath:)) else { return ["ok": false, "error": "The video engine is missing"] }
    let args = SiteVideo.infoArguments(url: url, quickJS: SiteVideoTools.quickJS, audioOnly: false, referrer: m["referrer"] as? String,
                                       cookies: m["cookies"] as? String, userAgent: m["userAgent"] as? String, username: nil, password: nil)
    guard let prepared = try? SiteVideo.prepare(args, proxy: savedProxy()) else { return ["ok": false, "error": "Could not prepare the lookup"] }
    defer { if let f = prepared.configFile { try? fm.removeItem(at: f) } }
    let p = Process()
    p.executableURL = URL(fileURLWithPath: tool)
    p.arguments = prepared.args
    let out = Pipe()
    p.standardOutput = out
    p.standardError = FileHandle.nullDevice
    guard (try? p.run()) != nil else { return ["ok": false, "error": "The video engine could not start"] }
    let timer = DispatchWorkItem { if p.isRunning { p.terminate() } }  // the browser stops waiting after 45 s
    DispatchQueue.global().asyncAfter(deadline: .now() + 40, execute: timer)
    let data = out.fileHandleForReading.readDataToEndOfFile()
    p.waitUntilExit()
    timer.cancel()
    guard p.terminationStatus == 0, let c = SiteVideo.choices(data) else { return ["ok": false, "error": "The video could not be looked up"] }
    return ["ok": true, "title": c.title, "soundSize": c.soundSize ?? 0,
            "qualities": c.video.prefix(12).map { ["height": $0.height, "size": $0.size ?? 0] }]
}

while let message = try? NativeMessaging.read(from: input) {
    switch message["type"] as? String {
    case "ping":
        reply(["ok": true, "version": version])
    case "download":
        var job = DownloadJob(
            url: message["url"] as? String ?? "",
            fileName: message["fileName"] as? String,
            referrer: message["referrer"] as? String,
            cookies: message["cookies"] as? String,
            userAgent: message["userAgent"] as? String
        )
        if let later = message["later"] as? Bool, later { job.start = false }
        if let stream = message["stream"] as? Bool, stream { job.stream = true }
        if let site = message["site"] as? Bool, site { job.site = true }
        if let audio = message["audioOnly"] as? Bool, audio { job.audioOnly = true }
        if let h = (message["maxHeight"] as? NSNumber)?.intValue, (100...10_000).contains(h) { job.maxHeight = h }
        if let size = (message["size"] as? NSNumber)?.int64Value, size > 0 { job.size = size }
        if let browser = message["browser"] as? Bool, browser { job.fromBrowser = true }
        guard job.validURL != nil else {
            reply(["ok": false, "error": "Only http and https links can be downloaded"])
            continue
        }
        do {
            try Inbox().drop(job)
            openApp("nexonicdm://inbox")
            reply(["ok": true])
        } catch {
            reply(["ok": false, "error": error.localizedDescription])
        }
    case "qualities":
        reply(qualities(message))
    case "open":
        openApp("nexonicdm://show")
        reply(["ok": true])
    default:
        reply(["ok": false, "error": "Unknown message"])
    }
}
