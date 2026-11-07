import Foundation

/// Checks before a big download is written: enough free space, and a drive that can hold a file that size.
public enum DiskSpace {
    /// The largest file a FAT32 drive can hold (4 GB minus one byte).
    public static let fat32Limit: Int64 = 4 * 1024 * 1024 * 1024 - 1
    private static let margin: Int64 = 64 * 1024 * 1024

    /// Why a file of `size` bytes can't be saved in `folder`, or nil when it can (or the drive can't be checked).
    /// `already` is what this download has written there so far.
    public static func problem(_ folder: URL, size: Int64, already: Int64 = 0) -> String? {
        let keys: Set<URLResourceKey> = [.volumeAvailableCapacityForImportantUsageKey, .volumeAvailableCapacityKey,
                                         .volumeLocalizedFormatDescriptionKey, .volumeNameKey]
        guard let values = try? folder.resourceValues(forKeys: keys) else { return nil }
        let name = values.volumeName ?? "this drive"
        let format = values.volumeLocalizedFormatDescription ?? ""
        if size > fat32Limit, format.localizedCaseInsensitiveContains("FAT32") || format.localizedCaseInsensitiveContains("MS-DOS") {
            return "\(name) can't store files over 4 GB because it is formatted as FAT32 (MS-DOS). "
                + "Choose a folder on another drive, or reformat the drive as exFAT or APFS"
        }
        let free = values.volumeAvailableCapacityForImportantUsage ?? values.volumeAvailableCapacity.map(Int64.init)
        let need = size - already
        if let free, need > 0, free < need + margin {
            return "Not enough free space on \(name): this download needs \(bytes(need)) more, \(bytes(free)) is free"
        }
        return nil
    }

    public static func bytes(_ n: Int64) -> String {
        n >= 1 << 30 ? String(format: "%.1f GB", Double(n) / Double(1 << 30)) : "\(max(1, n / (1 << 20))) MB"
    }
}
