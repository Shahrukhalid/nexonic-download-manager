import Foundation

/// The app's data (settings, the list, unfinished parts) can live in another folder, such as a bigger disk. The usual
/// folder keeps only a pointer. A move is asked for while the app runs and done by the app at its next start, before
/// anything is open: everything is copied and checked, then the pointer is switched and the old copies are removed.
public enum DataLocation {
    public static let pointerName = "data-location.txt", pendingName = "data-move.txt"
    /// Why the last move failed (shown in Settings), or nil.
    public static var lastProblem: String?

    public static func current(_ usual: URL) -> URL {
        let p = (try? String(contentsOf: usual.appendingPathComponent(pointerName), encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        var dir: ObjCBool = false
        if p.hasPrefix("/"), FileManager.default.fileExists(atPath: p, isDirectory: &dir), dir.boolValue { return URL(fileURLWithPath: p, isDirectory: true) }
        return usual
    }

    static func clean(_ u: URL) -> String { u.standardizedFileURL.path.replacingOccurrences(of: "/+$", with: "", options: .regularExpression) }

    /// Why a folder can't take the data, or nil when it can.
    public static func problem(_ target: String, current: URL) -> String? {
        guard target.hasPrefix("/") else { return "Choose a full folder path." }
        let to = clean(URL(fileURLWithPath: target)), from = clean(current)
        if to.lowercased() == from.lowercased() { return "The data is already in this folder." }
        if to.lowercased().hasPrefix(from.lowercased() + "/") || from.lowercased().hasPrefix(to.lowercased() + "/") {
            return "Choose a folder that is not inside the current data folder (or the other way round)."
        }
        let entries = ((try? FileManager.default.contentsOfDirectory(atPath: to)) ?? []).filter { ![pointerName, pendingName, ".DS_Store"].contains($0) }
        return entries.isEmpty ? nil : "Choose an empty folder."
    }

    public static func request(usual: URL, target: String) -> String? {
        if let why = problem(target, current: current(usual)) { return why }
        try? FileManager.default.createDirectory(at: usual, withIntermediateDirectories: true)
        do { try target.write(to: usual.appendingPathComponent(pendingName), atomically: true, encoding: .utf8) } catch { return error.localizedDescription }
        return nil
    }

    public static func pending(_ usual: URL) -> String? {
        (try? String(contentsOf: usual.appendingPathComponent(pendingName), encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Does a requested move. Call first thing when the app starts (never from the browser helper).
    @discardableResult public static func applyPending(_ usual: URL) -> String? {
        guard let target = pending(usual) else { return nil }
        let fm = FileManager.default
        try? fm.removeItem(at: usual.appendingPathComponent(pendingName))
        let from = current(usual).resolvingSymlinksInPath()
        if let why = problem(target, current: from) { lastProblem = why; return why }
        let to = URL(fileURLWithPath: target, isDirectory: true)
        var copied: [URL] = []
        // Paths inside the data folder, relative to it (the pointer and the request stay in the usual folder).
        let base = from.path
        let files = (fm.enumerator(at: from, includingPropertiesForKeys: [.isRegularFileKey])?.compactMap { $0 as? URL } ?? [])
            .map { $0.resolvingSymlinksInPath() }
            .filter { (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true && $0.path.hasPrefix(base + "/") }
            .filter { ![pointerName, pendingName].contains(String($0.path.dropFirst(base.count + 1))) }
        do {
            try fm.createDirectory(at: to, withIntermediateDirectories: true)
            for f in files {
                let rel = String(f.path.dropFirst(base.count + 1))
                let dest = to.appendingPathComponent(rel)
                try fm.createDirectory(at: dest.deletingLastPathComponent(), withIntermediateDirectories: true)
                try fm.copyItem(at: f, to: dest)
                copied.append(dest)
                let a = (try fm.attributesOfItem(atPath: f.path)[.size] as? NSNumber)?.int64Value
                let b = (try fm.attributesOfItem(atPath: dest.path)[.size] as? NSNumber)?.int64Value
                if a != b { throw CocoaError(.fileWriteUnknown, userInfo: [NSLocalizedDescriptionKey: "\(rel) was not copied completely."]) }
            }
        } catch {
            for c in copied { try? fm.removeItem(at: c) }
            lastProblem = "The data could not be moved: " + error.localizedDescription
            return lastProblem
        }
        if clean(to.resolvingSymlinksInPath()).lowercased() == clean(usual.resolvingSymlinksInPath()).lowercased() { try? fm.removeItem(at: usual.appendingPathComponent(pointerName)) }
        else { try? to.path.write(to: usual.appendingPathComponent(pointerName), atomically: true, encoding: .utf8) }
        for f in files { try? fm.removeItem(at: f) }
        // Then the folders left empty, deepest first; a chosen data folder goes too, the usual one stays (it keeps the pointer).
        let dirs = (fm.enumerator(at: from, includingPropertiesForKeys: nil)?.compactMap { ($0 as? URL)?.resolvingSymlinksInPath() } ?? [])
            .filter { var d: ObjCBool = false; return fm.fileExists(atPath: $0.path, isDirectory: &d) && d.boolValue }
            .sorted { $0.path.count > $1.path.count }
        for d in dirs where ((try? fm.contentsOfDirectory(atPath: d.path)) ?? ["x"]).filter({ $0 != ".DS_Store" }).isEmpty { try? fm.removeItem(at: d) }
        if clean(from) != clean(usual.resolvingSymlinksInPath()), ((try? fm.contentsOfDirectory(atPath: from.path)) ?? ["x"]).filter({ $0 != ".DS_Store" }).isEmpty {
            try? fm.removeItem(at: from)
        }
        return nil
    }
}
