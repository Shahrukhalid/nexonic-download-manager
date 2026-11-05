import NexonicKit
import SwiftUI
import UIKit

final class AppDelegate: NSObject, UIApplicationDelegate {
    /// iOS wakes the app when background downloads (its own or the Share extension's) finish.
    func application(_ application: UIApplication, handleEventsForBackgroundURLSession identifier: String,
                     completionHandler: @escaping () -> Void) {
        Downloader.shared(identifier).finishedEvents = {
            Library.importFinished()
            completionHandler()
        }
    }
}

@main
struct NexonicApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var model = AppModel()
    @State private var browser = BrowserState()
    @Environment(\.scenePhase) private var phase

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
                .environment(browser)
                .onOpenURL { url in model.open(url) }
                .onChange(of: phase) { _, p in
                    if p == .active { model.refresh(importing: true); TorrentStore.shared.enterForeground() }
                    if p == .background { TorrentStore.shared.enterBackground() }
                }
                .tint(Color(red: 0.15, green: 0.39, blue: 0.92))
        }
    }
}

enum AppTab: Hashable { case downloads, browser, settings }

struct RootView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model
        TabView(selection: $model.tab) {
            DownloadsView()
                .tabItem { Label("Downloads", systemImage: "arrow.down.circle") }
                .badge(model.activeCount)
                .tag(AppTab.downloads)
            BrowserView()
                .tabItem { Label("Browser", systemImage: "globe") }
                .tag(AppTab.browser)
            SettingsView()
                .tabItem { Label("Settings", systemImage: "gearshape") }
                .tag(AppTab.settings)
        }
        .sheet(item: $model.addInput) { input in
            DownloadWindow(input: input, sessionID: Downloader.appSessionID) { message in
                model.addInput = nil
                if let message { model.toast = message }
                model.refresh()
            }
        }
        .sheet(isPresented: $model.showSetup) { NavigationStack { SetupGuideView() } }
        .sheet(item: $model.torrentRequest) { request in
            TorrentAddView(source: request.source) { message in if let message { model.toast = message } }
        }
        .overlay(alignment: .bottom) {
            if let toast = model.toast {
                Text(toast)
                    .font(.callout).padding(.horizontal, 16).padding(.vertical, 10)
                    .background(.thickMaterial, in: Capsule())
                    .padding(.bottom, 64)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .task { try? await Task.sleep(for: .seconds(2.5)); withAnimation { model.toast = nil } }
            }
        }
        .animation(.default, value: model.toast)
        .onAppear { model.firstLaunch() }
    }
}
