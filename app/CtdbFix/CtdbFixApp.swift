import AppKit
import SwiftUI

@main
struct CtdbFixApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    @StateObject private var session = SessionModel()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(session)
                .environmentObject(appDelegate)
                .frame(minWidth: 480, minHeight: 360)
        }
        .commands {
            CommandGroup(replacing: .newItem) {}
        }
        .onChange(of: appDelegate.pendingCueURL) { _, url in
            guard let url else { return }
            session.openCue(url)
            appDelegate.pendingCueURL = nil
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, ObservableObject {
    @Published var pendingCueURL: URL?

    func application(_ application: NSApplication, open urls: [URL]) {
        if let cue = urls.first(where: { $0.pathExtension.lowercased() == "cue" }) {
            DispatchQueue.main.async { self.pendingCueURL = cue }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}
