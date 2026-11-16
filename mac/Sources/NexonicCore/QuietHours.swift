import Foundation

/// Quiet hours for sounds, as on Windows and Linux: "22:00-07:00" (the range may pass midnight); empty means no quiet hours.
public enum QuietHours {
    /// Minutes after midnight for "HH:mm", or nil.
    static func minutes(_ text: Substring) -> Int? {
        let p = text.trimmingCharacters(in: .whitespaces).split(separator: ":")
        guard p.count == 2, p[0].count == 2, p[1].count == 2, let h = Int(p[0]), let m = Int(p[1]),
              (0..<24).contains(h), (0..<60).contains(m) else { return nil }
        return h * 60 + m
    }

    /// True when the range is valid ("HH:mm-HH:mm", two different times).
    public static func isValid(_ range: String) -> Bool {
        let p = range.split(separator: "-")
        guard p.count == 2, let a = minutes(p[0]), let b = minutes(p[1]) else { return false }
        return a != b
    }

    /// True when `date` falls inside the quiet hours, in the given calendar's time zone.
    public static func contains(_ range: String, at date: Date, calendar: Calendar = .current) -> Bool {
        let p = range.split(separator: "-")
        guard p.count == 2, let a = minutes(p[0]), let b = minutes(p[1]), a != b else { return false }
        let c = calendar.dateComponents([.hour, .minute], from: date)
        let t = (c.hour ?? 0) * 60 + (c.minute ?? 0)
        return a < b ? (t >= a && t < b) : (t >= a || t < b)
    }
}
