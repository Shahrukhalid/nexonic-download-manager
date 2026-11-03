// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "NexonicDownloadManager",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "NexonicCore", targets: ["NexonicCore"]),
        .executable(name: "NexonicDownloadManager", targets: ["NexonicDownloadManager"]),
        .executable(name: "nexonic-host", targets: ["NexonicHost"]),
    ],
    targets: [
        .target(name: "NexonicCore"),
        .executableTarget(name: "NexonicDownloadManager", dependencies: ["NexonicCore"]),
        .executableTarget(name: "NexonicHost", dependencies: ["NexonicCore"]),
        .testTarget(name: "NexonicCoreTests", dependencies: ["NexonicCore"]),
    ]
)
