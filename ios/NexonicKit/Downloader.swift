import Foundation
import Security
import UserNotifications

/// Runs downloads in an iOS background session: they keep going when the app or the Share extension is closed,
/// and iOS wakes the app when they finish. Pause keeps iOS's resume data, so a download continues where it stopped.
/// (iOS manages background transfers itself, so each file uses one connection here.)
public final class Downloader: NSObject, URLSessionDownloadDelegate {
    public static let appSessionID = "com.nexonic.downloadmanager.bg.app"
    public static let shareSessionID = "com.nexonic.downloadmanager.bg.share"

    private static var all: [String: Downloader] = [:]
    private static let lock = NSLock()

    /// The one downloader for a session identifier in this process.
    public static func shared(_ identifier: String) -> Downloader {
        lock.lock(); defer { lock.unlock() }
        if let d = all[identifier] { return d }
        let d = Downloader(identifier: identifier)
        all[identifier] = d
        return d
    }

    public let identifier: String
    public private(set) var session: URLSession!
    /// Called once iOS has delivered all events of a background launch.
    public var finishedEvents: (() -> Void)?
    private var speeds: [UUID: Int64] = [:]
    /// Live speeds (bytes per second) by download.
    public var currentSpeeds: [UUID: Int64] { sampleLock.lock(); defer { sampleLock.unlock() }; return speeds }
    private var samples: [UUID: (time: Date, bytes: Int64)] = [:]
    private let sampleLock = NSLock()

    private init(identifier: String) {
        self.identifier = identifier
        super.init()
        // Screenshots (debug builds only): the CI simulator fails background transfers with -1, so the demo uses a normal session.
        #if DEBUG
        let demo = UserDefaults.standard.string(forKey: "ndm_demo") == "1"
        #else
        let demo = false
        #endif
        let config = demo ? URLSessionConfiguration.default : URLSessionConfiguration.background(withIdentifier: identifier)
        // The Share extension's downloads finish in the shared folder; without the App Group iOS would refuse the session.
        if AppGroup.available && !demo { config.sharedContainerIdentifier = AppGroup.id }
        if !demo { config.sessionSendsLaunchEvents = true }
        config.isDiscretionary = false
        config.httpCookieStorage = HTTPCookieStorage.shared // cookies are sent only to their own domains
        config.httpMaximumConnectionsPerHost = 6
        session = URLSession(configuration: config, delegate: self, delegateQueue: nil)
    }

    // MARK: Actions

    public func start(_ job: DownloadJob) {
        let task: URLSessionDownloadTask
        if let data = Store.shared.takeResumeData(job.id) {
            task = session.downloadTask(withResumeData: data)
        } else {
            var req = URLRequest(url: job.url)
            req.allowsCellularAccess = !SharedSettings.wifiOnly
            if let r = job.referer, Net.sameSite(job.url, r) { req.setValue(r.absoluteString, forHTTPHeaderField: "Referer") }
            if let ua = job.userAgent { req.setValue(ua, forHTTPHeaderField: "User-Agent") }
            task = session.downloadTask(with: req)
        }
        task.taskDescription = job.id.uuidString
        if let size = job.size { task.countOfBytesClientExpectsToReceive = size }
        Store.shared.update(job.id) { j in
            j.status = .downloading; j.error = nil; j.sessionID = self.identifier
        }
        task.resume()
    }

    /// Stops and keeps what was downloaded.
    public func pause(_ id: UUID, completion: (() -> Void)? = nil) {
        session.getAllTasks { tasks in
            guard let task = tasks.first(where: { $0.taskDescription == id.uuidString }) as? URLSessionDownloadTask else {
                Store.shared.update(id) { if $0.status == .downloading || $0.status == .queued { $0.status = .paused } }
                completion?(); return
            }
            task.cancel(byProducingResumeData: { data in
                if let data { Store.shared.saveResumeData(data, for: id) }
                Store.shared.update(id) { $0.status = .paused }
                completion?()
            })
        }
    }

    public func cancel(_ id: UUID) {
        session.getAllTasks { tasks in tasks.filter { $0.taskDescription == id.uuidString }.forEach { $0.cancel() } }
        Store.shared.dropResumeData(id)
    }

    /// Downloads still running in this session (after a restart the tasks are still there).
    public func runningIDs(_ completion: @escaping (Set<UUID>) -> Void) {
        session.getAllTasks { tasks in
            completion(Set(tasks.filter { $0.state == .running }.compactMap { $0.taskDescription.flatMap(UUID.init(uuidString:)) }))
        }
    }

    // MARK: Delegate

    private func jobID(_ task: URLSessionTask) -> UUID? { task.taskDescription.flatMap(UUID.init(uuidString:)) }

    public func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                           totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard let id = jobID(downloadTask) else { return }
        let now = Date()
        sampleLock.lock()
        let last = samples[id]
        var save = false
        if last == nil || now.timeIntervalSince(last!.time) >= 0.8 {
            if let last { speeds[id] = Int64(Double(totalBytesWritten - last.bytes) / now.timeIntervalSince(last.time)) }
            samples[id] = (now, totalBytesWritten)
            save = true
        }
        sampleLock.unlock()
        guard save else { return }
        Store.shared.update(id) { j in
            j.written = totalBytesWritten
            if totalBytesExpectedToWrite > 0 { j.size = totalBytesExpectedToWrite }
            if j.status != .paused { j.status = .downloading }
        }
    }

    public func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        guard let id = jobID(downloadTask) else { return }
        if let http = downloadTask.response as? HTTPURLResponse, !(200...299).contains(http.statusCode) {
            Store.shared.update(id) { $0.status = .failed; $0.error = Downloader.message(status: http.statusCode) }
            return
        }
        // Move it out of iOS's temporary place right away; the app then puts it into Files.
        let dir = AppGroup.folder.appendingPathComponent("Incoming/\(id.uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let name = Store.shared.job(id)?.fileName ?? FileNames.sanitize(downloadTask.response?.suggestedFilename ?? "download")
        let target = dir.appendingPathComponent(name)
        try? FileManager.default.removeItem(at: target)
        do {
            try FileManager.default.moveItem(at: location, to: target)
            let size = (try? target.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init)
            Store.shared.update(id) { j in
                j.status = .complete; j.error = nil; j.finishedAt = Date()
                j.incomingPath = "Incoming/\(id.uuidString)/\(name)"
                if let size { j.size = size; j.written = size }
            }
            Notify.finished(name)
        } catch {
            Store.shared.update(id) { $0.status = .failed; $0.error = "Could not keep the file: \(error.localizedDescription)" }
        }
    }

    public func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard let id = jobID(task) else { return }
        sampleLock.lock(); speeds[id] = nil; samples[id] = nil; sampleLock.unlock()
        guard let error = error as NSError? else { return }
        if error.code == NSURLErrorCancelled { return } // paused or removed
        if let data = error.userInfo[NSURLSessionDownloadTaskResumeData] as? Data { Store.shared.saveResumeData(data, for: id) }
        Store.shared.update(id) { $0.status = .failed; $0.error = Downloader.message(error) }
    }

    /// A short, useful message instead of iOS's generic ones.
    static func message(_ error: NSError) -> String {
        switch error.code {
        case NSURLErrorNotConnectedToInternet, NSURLErrorNetworkConnectionLost: return "No internet connection. Tap Try Again when you're online."
        case NSURLErrorTimedOut: return "The server didn't answer in time. Tap Try Again."
        case NSURLErrorCannotFindHost, NSURLErrorCannotConnectToHost: return "Could not reach the server."
        case NSURLErrorAppTransportSecurityRequiresSecureConnection: return "This server needs a secure (https) link."
        case NSURLErrorServerCertificateUntrusted, NSURLErrorSecureConnectionFailed: return "The server's secure connection can't be trusted."
        case NSURLErrorUnknown: return "The download stopped. Tap Try Again."
        default: return "The download stopped (\(error.code)). Tap Try Again."
        }
    }

    /// Logins go only to the download's own server, and only over https when the download is https.
    public func urlSession(_ session: URLSession, task: URLSessionTask, didReceive challenge: URLAuthenticationChallenge,
                           completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        let space = challenge.protectionSpace
        let methods = [NSURLAuthenticationMethodHTTPBasic, NSURLAuthenticationMethodHTTPDigest, NSURLAuthenticationMethodNTLM]
        guard methods.contains(space.authenticationMethod), challenge.previousFailureCount == 0,
              let id = jobID(task), let job = Store.shared.job(id),
              Net.sameSpace(host: space.host, port: space.port, proto: space.protocol, origin: job.url),
              let login = Keychain.login(for: id) else {
            completionHandler(.performDefaultHandling, nil); return
        }
        completionHandler(.useCredential, URLCredential(user: login.user, password: login.password, persistence: .forSession))
    }

    public func urlSessionDidFinishEvents(forBackgroundURLSession session: URLSession) {
        DispatchQueue.main.async {
            self.finishedEvents?()
            self.finishedEvents = nil
        }
    }

    static func message(status: Int) -> String {
        switch status {
        case 401, 403: return "The server refused the download (\(status)). Try again from the page, or add a login."
        case 404, 410: return "The file is no longer there (\(status))."
        default: return "The server answered \(status)."
        }
    }
}

/// Download logins, kept in the Keychain (this device only, never in the download list).
public enum Keychain {
    private static let service = "com.nexonic.downloadmanager.login"

    public static func setLogin(user: String, password: String, for id: UUID) {
        remove(id)
        guard !user.isEmpty else { return }
        let value = try? JSONEncoder().encode([user, password])
        let q: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString, kSecValueData as String: value ?? Data(),
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
        ]
        SecItemAdd(q as CFDictionary, nil)
    }

    public static func login(for id: UUID) -> (user: String, password: String)? {
        let q: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString, kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let data = out as? Data,
              let pair = try? JSONDecoder().decode([String].self, from: data), pair.count == 2 else { return nil }
        return (pair[0], pair[1])
    }

    public static func remove(_ id: UUID) {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                kSecAttrAccount as String: id.uuidString]
        SecItemDelete(q as CFDictionary)
    }
}

public enum Notify {
    public static func requestPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in }
    }

    static func finished(_ name: String) {
        let content = UNMutableNotificationContent()
        content.title = name
        content.body = "Download complete. It's in Files › Nexonic."
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }
}
