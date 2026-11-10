import Foundation

/// Scheduled wake through pmset. Setting a wake needs administrator rights, so it goes through an AppleScript
/// "with administrator privileges" prompt; nothing runs as root otherwise.
enum MacWake {
    static var available: Bool { FileManager.default.isExecutableFile(atPath: "/usr/bin/pmset") }

    /// Nil when pmset accepted the schedule, else why not (including a cancelled password prompt).
    static func apply(_ args: [String]) -> String? {
        guard available else { return "This Mac cannot schedule a wake." }
        // Only fixed words, day letters and a time reach the shell (made by QueueClock.wakeArguments), each quoted.
        guard args.allSatisfy({ $0.allSatisfy { $0.isLetter || $0.isNumber || $0 == ":" } }) else { return "The wake time is not valid." }
        let command = "/usr/bin/pmset " + args.map { "'\($0)'" }.joined(separator: " ")
        var error: NSDictionary?
        NSAppleScript(source: "do shell script \"\(command)\" with administrator privileges")?.executeAndReturnError(&error)
        if let error { return (error[NSAppleScript.errorMessage] as? String) ?? "macOS did not allow the wake." }
        return nil
    }
}
