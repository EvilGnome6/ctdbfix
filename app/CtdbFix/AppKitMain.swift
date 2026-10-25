import AppKit
import UniformTypeIdentifiers

@main
enum CtdbFixMain {
    static func main() {
        let app = NSApplication.shared
        app.setActivationPolicy(.regular)
        let delegate = AppController()
        // NSApplication.delegate is weak — keep a strong ref for the run loop.
        app.delegate = delegate
        withExtendedLifetime(delegate) {
            app.run()
        }
    }
}

@MainActor
final class AppController: NSObject, NSApplicationDelegate {
    private var window: NSWindow!
    private let session = SessionModel()
    private var ui: MainViewController!

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.mainMenu = Self.makeMainMenu()

        ui = MainViewController(session: session)

        let contentRect = NSRect(x: 0, y: 0, width: 580, height: 480)
        window = NSWindow(
            contentRect: contentRect,
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "ctdbfix"
        window.minSize = NSSize(width: 440, height: 380)
        window.contentView = ui.view
        window.setContentSize(contentRect.size)
        window.center()
        window.isReleasedWhenClosed = false
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// Raw NSApplication apps get no menu bar unless we build one — without it, ⌘Q does nothing.
    private static func makeMainMenu() -> NSMenu {
        let mainMenu = NSMenu()

        let appMenuItem = NSMenuItem()
        mainMenu.addItem(appMenuItem)
        let appMenu = NSMenu(title: "ctdbfix")
        appMenuItem.submenu = appMenu

        appMenu.addItem(withTitle: "About ctdbfix", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide ctdbfix", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        let hideOthers = appMenu.addItem(withTitle: "Hide Others", action: #selector(NSApplication.hideOtherApplications(_:)), keyEquivalent: "h")
        hideOthers.keyEquivalentModifierMask = [.command, .option]
        appMenu.addItem(withTitle: "Show All", action: #selector(NSApplication.unhideAllApplications(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Quit ctdbfix", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

        let editMenuItem = NSMenuItem()
        mainMenu.addItem(editMenuItem)
        let editMenu = NSMenu(title: "Edit")
        editMenuItem.submenu = editMenu
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")

        let windowMenuItem = NSMenuItem()
        mainMenu.addItem(windowMenuItem)
        let windowMenu = NSMenu(title: "Window")
        windowMenuItem.submenu = windowMenu
        windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "Zoom", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
        windowMenu.addItem(.separator())
        windowMenu.addItem(withTitle: "Bring All to Front", action: #selector(NSApplication.arrangeInFront(_:)), keyEquivalent: "")
        NSApp.windowsMenu = windowMenu

        return mainMenu
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        if let cue = urls.first(where: { $0.pathExtension.lowercased() == "cue" }) {
            session.openCue(cue)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}

@MainActor
final class MainViewController: NSViewController {
    private let session: SessionModel
    private let titleLabel = NSTextField(labelWithString: "ctdbfix")
    private let subtitleLabel = NSTextField(labelWithString: "Verify and repair CD rips with CTDB. Output is FLAC.")
    private let dropBox = NSBox()
    private let dropLabel = NSTextField(wrappingLabelWithString: "Drop a .cue here, or Open With from Finder")
    private let statusLabel = NSTextField(wrappingLabelWithString: "Waiting for a cue sheet…")
    private let detailLabel = NSTextField(wrappingLabelWithString: "")
    private let spinner = NSProgressIndicator()
    private let openButton = NSButton(title: "Open .cue…", target: nil, action: nil)
    private let verifyButton = NSButton(title: "Verify again", target: nil, action: nil)
    private let repairButton = NSButton(title: "Repair to FLAC", target: nil, action: nil)
    private let submitButton = NSButton(title: "Submit to CTDB", target: nil, action: nil)

    init(session: SessionModel) {
        self.session = session
        super.init(nibName: nil, bundle: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    override func loadView() {
        let root = DropView(frame: NSRect(x: 0, y: 0, width: 580, height: 480)) { [weak self] url in
            self?.session.openCue(url)
        }
        root.wantsLayer = true
        root.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
        view = root
    }

    override func viewDidLoad() {
        super.viewDidLoad()

        titleLabel.font = .systemFont(ofSize: 28, weight: .semibold)
        subtitleLabel.textColor = .secondaryLabelColor
        subtitleLabel.font = .systemFont(ofSize: 13)

        dropBox.boxType = .custom
        dropBox.titlePosition = .noTitle
        dropBox.borderWidth = 2
        dropBox.cornerRadius = 12
        dropBox.borderColor = NSColor.separatorColor
        dropBox.fillColor = NSColor.controlBackgroundColor
        dropBox.contentViewMargins = NSSize(width: 16, height: 16)
        dropBox.translatesAutoresizingMaskIntoConstraints = false

        dropLabel.alignment = .center
        dropLabel.font = .systemFont(ofSize: 14, weight: .medium)
        dropLabel.translatesAutoresizingMaskIntoConstraints = false
        dropBox.addSubview(dropLabel)

        statusLabel.font = .systemFont(ofSize: 16, weight: .semibold)
        detailLabel.textColor = .secondaryLabelColor
        detailLabel.font = .monospacedSystemFont(ofSize: 11, weight: .regular)
        detailLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        spinner.style = .spinning
        spinner.controlSize = .small
        spinner.isDisplayedWhenStopped = false

        openButton.target = self
        openButton.action = #selector(openClicked)
        verifyButton.target = self
        verifyButton.action = #selector(verifyClicked)
        repairButton.target = self
        repairButton.action = #selector(repairClicked)
        submitButton.target = self
        submitButton.action = #selector(submitClicked)
        submitButton.keyEquivalent = "\r"

        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        let buttonRow = NSStackView(views: [openButton, verifyButton, spacer, repairButton, submitButton])
        buttonRow.orientation = .horizontal
        buttonRow.spacing = 10
        buttonRow.distribution = .fill

        let stack = NSStackView(views: [
            titleLabel, subtitleLabel, dropBox, statusLabel, detailLabel, spinner, buttonRow
        ])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 14
        stack.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -24),
            stack.topAnchor.constraint(equalTo: view.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: view.bottomAnchor, constant: -24),

            dropBox.heightAnchor.constraint(equalToConstant: 110),
            dropBox.widthAnchor.constraint(equalTo: stack.widthAnchor),
            dropLabel.centerXAnchor.constraint(equalTo: dropBox.centerXAnchor),
            dropLabel.centerYAnchor.constraint(equalTo: dropBox.centerYAnchor),
            dropLabel.leadingAnchor.constraint(greaterThanOrEqualTo: dropBox.leadingAnchor, constant: 16),
            dropLabel.trailingAnchor.constraint(lessThanOrEqualTo: dropBox.trailingAnchor, constant: -16),

            buttonRow.widthAnchor.constraint(equalTo: stack.widthAnchor),
        ])

        Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh() }
        }
        refresh()
    }

    private func refresh() {
        if let cue = session.cueURL {
            dropLabel.stringValue = "\(cue.lastPathComponent)\n\(cue.deletingLastPathComponent().path)"
        } else {
            dropLabel.stringValue = "Drop a .cue here, or Open With from Finder"
        }

        if session.isBusy {
            spinner.startAnimation(nil)
            statusLabel.stringValue = "Talking to CTDB…"
            detailLabel.stringValue = ""
        } else {
            spinner.stopAnimation(nil)
            if let err = session.errorText {
                statusLabel.stringValue = "Error"
                detailLabel.stringValue = err
            } else if let result = session.result {
                statusLabel.stringValue = statusTitle(result.status)
                var lines: [String] = []
                if let msg = result.message { lines.append(msg) }
                if let toc = result.tocId { lines.append("TOCID \(toc)") }
                if let path = result.outputPath { lines.append("Wrote \(path)") }
                if let sub = result.submit {
                    if sub.eligible {
                        lines.append("Submit: eligible — \(sub.reason ?? "")")
                        if let drive = sub.drive { lines.append("Drive: \(drive)") }
                    } else {
                        lines.append("Submit: disabled — \(sub.reason ?? "not eligible")")
                    }
                }
                detailLabel.stringValue = lines.joined(separator: "\n")
            } else {
                statusLabel.stringValue = "Waiting for a cue sheet…"
                detailLabel.stringValue = ""
            }
        }

        verifyButton.isEnabled = session.cueURL != nil && !session.isBusy
        repairButton.isEnabled = session.canRepair && !session.isBusy
        submitButton.isEnabled = session.canSubmit && !session.isBusy
    }

    private func statusTitle(_ status: String) -> String {
        switch status {
        case "clean": return "Accurate — no repair needed"
        case "repairable": return "Repairable with CTDB"
        case "unrecoverable": return "Errors — not recoverable"
        case "not_found": return "Not in CTDB"
        case "repaired": return "Repaired"
        case "submitted": return "Submitted to CTDB"
        case "dry_run": return "Submit dry-run (not uploaded)"
        case "ineligible": return "Not eligible to submit"
        default: return status
        }
    }

    @objc private func openClicked() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "cue") ?? .data]
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url {
            session.openCue(url)
        }
    }

    @objc private func verifyClicked() {
        Task { await session.verify() }
    }

    @objc private func repairClicked() {
        Task { await session.repair() }
    }

    @objc private func submitClicked() {
        let sub = session.result?.submit
        let drive = sub?.drive ?? "(unknown drive)"
        let alert = NSAlert()
        alert.messageText = "Submit this rip to CTDB?"
        alert.informativeText = """
            This uploads parity for the open cue sheet so others can verify/repair.

            Drive: \(drive)
            \(sub?.reason ?? "")

            Only submit rips you’re confident are correct.
            """
        alert.alertStyle = .informational
        alert.addButton(withTitle: "Submit")
        alert.addButton(withTitle: "Cancel")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        Task { await session.submit(confirm: true) }
    }
}

final class DropView: NSView {
    private let onDrop: (URL) -> Void

    init(frame: NSRect, onDrop: @escaping (URL) -> Void) {
        self.onDrop = onDrop
        super.init(frame: frame)
        registerForDraggedTypes([.fileURL])
        wantsLayer = true
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    override var intrinsicContentSize: NSSize {
        NSSize(width: 580, height: 480)
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        cueURL(from: sender) != nil ? .copy : []
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        guard let url = cueURL(from: sender) else { return false }
        onDrop(url)
        return true
    }

    private func cueURL(from sender: NSDraggingInfo) -> URL? {
        let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self]) as? [URL]
        return urls?.first(where: { $0.pathExtension.lowercased() == "cue" })
    }
}
