import Foundation

/// The `ndm` command: turns command-line arguments into download jobs for the app.
public enum CommandLineTool {
    public static let usage = """
    Nexonic Download Manager command line

      ndm <link> [<link> …] [options]      add one or more downloads
      ndm batch <pattern> [options]        add a numbered series, e.g. "https://example.com/img[001-100].jpg"
      ndm list [--json]                    show the download list (--json: for scripts)
      ndm help                             show this help

    Options:
      --folder <folder>     save to this folder
      --name <file name>    file name (one link only)
      --queue <name>        add to this queue (it is created if needed)
      --later               add without starting
      --referrer <link>     page the link came from
      --user <name>         user name for password-protected downloads
      --password <pass>     password for password-protected downloads
      --checksum <value>    expected MD5, SHA-1, SHA-256 or SHA-512 (checked when finished)
      --limit <KB/s>        speed limit for these downloads
      --audio               video sites: save the sound only
      --json                print the result as JSON (for scripts)

    IDM-style switches work too:
      /d <link>  /p <folder>  /f <file name>  /a (add without starting)
      /n /q /h /s are accepted and ignored (the app never asks questions for added downloads).
    """

    public enum Outcome: Equatable {
        case help
        case jobs([DownloadJob])
        case failure(String)
        case list
    }

    static let idmSwitches: Set<String> = ["/d", "/p", "/f", "/a", "/n", "/q", "/h", "/s"]

    /// `--json` anywhere: the answer is printed as JSON. Returns the arguments without it.
    public static func json(_ args: [String]) -> (args: [String], json: Bool) {
        (args.filter { $0 != "--json" }, args.contains("--json"))
    }

    /// True when the arguments are a command rather than a browser starting the helper.
    public static func isCommand(_ args: [String]) -> Bool {
        guard let first = args.first?.lowercased() else { return false }
        return first.hasPrefix("http://") || first.hasPrefix("https://") || ["batch", "help", "add", "list", "--help", "-h"].contains(first)
            || idmSwitches.contains(first)
    }

    public static func parse(_ args: [String]) -> Outcome {
        var args = args
        if args.first?.lowercased() == "add" { args.removeFirst() }
        guard let first = args.first, !["help", "--help", "-h"].contains(first.lowercased()) else { return .help }
        if first.lowercased() == "list" { return args.count == 1 ? .list : .failure("list takes no other options than --json") }
        var links: [String] = []
        var batch: String?
        var template = DownloadJob(url: "")
        var i = 0
        if first.lowercased() == "batch" {
            guard args.count > 1 else { return .failure("batch needs a pattern") }
            batch = args[1]
            i = 2
        }
        while i < args.count {
            let a = args[i]
            // IDM's switches: /d link, /p folder, /f file name, /a = add without starting; /n /q /h /s need nothing here.
            if idmSwitches.contains(a.lowercased()) {
                let s = a.lowercased()
                if ["/n", "/q", "/h", "/s"].contains(s) { i += 1; continue }
                if s == "/a" { template.start = false; i += 1; continue }
                guard i + 1 < args.count else { return .failure("\(a) needs a value") }
                let v = args[i + 1]
                if s == "/d" { links.append(v) } else if s == "/p" { template.folder = (v as NSString).expandingTildeInPath } else { template.fileName = v }
                i += 2
                continue
            }
            if a.hasPrefix("--") {
                if a == "--later" { template.start = false; i += 1; continue }
                if a == "--audio" { template.audioOnly = true; i += 1; continue }
                guard i + 1 < args.count else { return .failure("\(a) needs a value") }
                let v = args[i + 1]
                switch a {
                case "--folder": template.folder = (v as NSString).expandingTildeInPath
                case "--name": template.fileName = v
                case "--queue": template.queue = v
                case "--referrer": template.referrer = v
                case "--user": template.username = v
                case "--password": template.password = v
                case "--checksum":
                    guard Checksum.parse(v) != nil else { return .failure("\(v) is not an MD5, SHA-1, SHA-256 or SHA-512 value") }
                    template.checksum = v
                case "--limit":
                    guard let n = Int(v), n >= 0 else { return .failure("--limit needs a number of KB/s") }
                    template.speedLimitKBps = n
                default: return .failure("Unknown option \(a)")
                }
                i += 2
            } else {
                links.append(a)
                i += 1
            }
        }
        if let batch {
            do { links += try BatchPattern.expand(batch) } catch BatchPattern.Failure.tooMany(let n) {
                return .failure("The pattern makes \(n) links; the limit is \(BatchPattern.limit)")
            } catch { return .failure(error.localizedDescription) }
        }
        guard !links.isEmpty else { return .failure("No links given") }
        if template.fileName != nil && links.count > 1 { return .failure("--name works with one link only") }
        var jobs: [DownloadJob] = []
        for link in links {
            var job = template
            job.url = link
            guard job.validURL != nil else { return .failure("Not a web link: \(link)") }
            jobs.append(job)
        }
        return .jobs(jobs)
    }

    /// One download for `ndm list`: no cookies, passwords or private settings.
    public static func listItem(_ r: DownloadRecord) -> [String: Any] {
        var d: [String: Any] = [
            "id": r.id.uuidString, "url": r.url.absoluteString, "fileName": r.fileName, "folder": ListColumns.folder(r),
            "status": r.status.rawValue, "received": r.received, "progress": (r.progress * 1000).rounded() / 10, "queue": r.queue,
            "added": ISO8601DateFormatter().string(from: r.added),
        ]
        if let s = r.size { d["size"] = s }
        if let f = r.finished { d["finished"] = ISO8601DateFormatter().string(from: f) }
        if let e = r.error { d["error"] = e }
        return d
    }

    public static func listJSON(_ list: [DownloadRecord]) -> String {
        let data = (try? JSONSerialization.data(withJSONObject: list.map(listItem), options: [.prettyPrinted, .sortedKeys])) ?? Data("[]".utf8)
        return String(decoding: data, as: UTF8.self)
    }

    public static func listText(_ list: [DownloadRecord]) -> String {
        list.isEmpty ? "No downloads." : list.map { "\($0.status.rawValue.padding(toLength: 11, withPad: " ", startingAt: 0)) \(Int(($0.progress * 100).rounded()))%  \($0.fileName)" }.joined(separator: "\n")
    }

    /// Splits a command such as `"/opt/scan tool/scan" --quiet` into words (double or single quotes group words).
    public static func words(_ text: String) -> [String] {
        var out: [String] = []
        var current = ""
        var quote: Character?
        var has = false
        for ch in text {
            if let q = quote {
                if ch == q { quote = nil } else { current.append(ch) }
            } else if ch == "\"" || ch == "'" {
                quote = ch
                has = true
            } else if ch.isWhitespace {
                if has || !current.isEmpty { out.append(current) }
                current = ""
                has = false
            } else {
                current.append(ch)
            }
        }
        if has || !current.isEmpty { out.append(current) }
        return out
    }
}
