import CryptoKit
import Foundation

public enum ChecksumAlgorithm: String, Codable, CaseIterable, Sendable {
    case md5 = "MD5", sha1 = "SHA-1", sha256 = "SHA-256", sha512 = "SHA-512"
}

public enum Checksum {
    /// Reads "sha256:abc…", "SHA-256 abc…" or a bare hex value; the length of the hex value picks the algorithm.
    public static func parse(_ text: String) -> (ChecksumAlgorithm, String)? {
        let hex = text.lowercased().components(separatedBy: CharacterSet(charactersIn: ": =")).last ?? ""
        guard !hex.isEmpty, hex.allSatisfy(\.isHexDigit) else { return nil }
        switch hex.count {
        case 32: return (.md5, hex)
        case 40: return (.sha1, hex)
        case 64: return (.sha256, hex)
        case 128: return (.sha512, hex)
        default: return nil
        }
    }

    public static func compute(_ algorithm: ChecksumAlgorithm, data: Data) -> String {
        switch algorithm {
        case .md5: return hex(Insecure.MD5.hash(data: data))
        case .sha1: return hex(Insecure.SHA1.hash(data: data))
        case .sha256: return hex(SHA256.hash(data: data))
        case .sha512: return hex(SHA512.hash(data: data))
        }
    }

    public static func compute(_ algorithm: ChecksumAlgorithm, file: URL) throws -> String {
        let h = try FileHandle(forReadingFrom: file)
        defer { try? h.close() }
        func run<H: HashFunction>(_ hasher: inout H) throws -> String {
            while let block = try h.read(upToCount: 4 << 20), !block.isEmpty { hasher.update(data: block) }
            return hex(hasher.finalize())
        }
        switch algorithm {
        case .md5: var x = Insecure.MD5(); return try run(&x)
        case .sha1: var x = Insecure.SHA1(); return try run(&x)
        case .sha256: var x = SHA256(); return try run(&x)
        case .sha512: var x = SHA512(); return try run(&x)
        }
    }

    private static func hex<D: Sequence>(_ digest: D) -> String where D.Element == UInt8 {
        digest.map { String(format: "%02x", $0) }.joined()
    }
}
