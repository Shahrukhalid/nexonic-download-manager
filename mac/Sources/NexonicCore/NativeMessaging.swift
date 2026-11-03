import Foundation

/// Browser native messaging: each message is a 4-byte little-endian length followed by UTF-8 JSON.
public enum NativeMessaging {
    public static let hostName = "com.nexonic.downloadmanager"
    /// Extension IDs allowed to talk to the helper. Add the store ID here once the extension is listed.
    public static let allowedExtensionIDs = ["lbmdjnbmkidgmabcjdkhfldclkifidmi"]
    /// The Firefox add-on's ID.
    public static let firefoxAddonID = "{dec29d70-4c6f-43f7-aacd-af2ae04cd4c2}"
    public static let maxMessage = 1 << 20

    public static func encode(_ object: Any) throws -> Data {
        let body = try JSONSerialization.data(withJSONObject: object)
        var length = UInt32(body.count).littleEndian
        return Data(bytes: &length, count: 4) + body
    }

    /// Reads one message; nil at end of input.
    public static func read(from handle: FileHandle) throws -> [String: Any]? {
        let header = handle.readData(ofLength: 4)
        guard header.count == 4 else { return nil }
        let length = Int(header.withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) }.littleEndian)
        guard length > 0, length <= maxMessage else { throw CocoaError(.coderReadCorrupt) }
        let body = handle.readData(ofLength: length)
        guard body.count == length else { return nil }
        return try JSONSerialization.jsonObject(with: body) as? [String: Any]
    }

    public static func manifest(hostPath: String) -> [String: Any] {
        [
            "name": hostName,
            "description": "Nexonic Download Manager",
            "path": hostPath,
            "type": "stdio",
            "allowed_origins": allowedExtensionIDs.map { "chrome-extension://\($0)/" },
        ]
    }

    public static func firefoxManifest(hostPath: String) -> [String: Any] {
        [
            "name": hostName,
            "description": "Nexonic Download Manager",
            "path": hostPath,
            "type": "stdio",
            "allowed_extensions": [firefoxAddonID],
        ]
    }
}
