// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "NexonicDownloadManager",
    platforms: [.macOS(.v13), .iOS(.v17)],
    products: [
        .library(name: "NexonicTorrent", targets: ["NexonicTorrent"]),
        .library(name: "NexonicCore", targets: ["NexonicCore"]),
        .executable(name: "NexonicDownloadManager", targets: ["NexonicDownloadManager"]),
        .executable(name: "nexonic-host", targets: ["NexonicHost"]),
    ],
    targets: [
        .binaryTarget(name: "CNexonicTorrent", path: "Native/CNexonicTorrent.xcframework"),
        .target(name: "NexonicTorrent", dependencies: ["CNexonicTorrent"], linkerSettings: [
            .linkedLibrary("c++"), .linkedLibrary("resolv"), .linkedFramework("Security"), .linkedFramework("SystemConfiguration")
        ]),
        .target(name: "NexonicCore", dependencies: ["NexonicTorrent"]),
        .executableTarget(name: "NexonicDownloadManager", dependencies: ["NexonicCore"]),
        .executableTarget(name: "NexonicHost", dependencies: ["NexonicCore"]),
        .testTarget(name: "NexonicTorrentTests", dependencies: ["NexonicTorrent"]),
        .testTarget(name: "NexonicCoreTests", dependencies: ["NexonicCore"]),
    ]
)
