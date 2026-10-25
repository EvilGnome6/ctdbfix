import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct ContentView: View {
    @EnvironmentObject private var session: SessionModel
    @State private var isDropTargeted = false

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            header
            dropZone
            statusBlock
            actions
            if !session.logTail.isEmpty {
                DisclosureGroup("Engine log") {
                    ScrollView {
                        Text(session.logTail)
                            .font(.system(.caption, design: .monospaced))
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .textSelection(.enabled)
                    }
                    .frame(maxHeight: 160)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(20)
        .background(Color(nsColor: .windowBackgroundColor))
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("ctdbfix")
                .font(.system(size: 28, weight: .semibold, design: .rounded))
            Text("Verify and repair CD rips with CTDB. Output is FLAC.")
                .foregroundStyle(.secondary)
        }
    }

    private var dropZone: some View {
        RoundedRectangle(cornerRadius: 12)
            .strokeBorder(style: StrokeStyle(lineWidth: 2, dash: [6]))
            .foregroundStyle(isDropTargeted ? Color.accentColor : Color.secondary.opacity(0.4))
            .background(
                RoundedRectangle(cornerRadius: 12)
                    .fill(isDropTargeted ? Color.accentColor.opacity(0.08) : Color.clear)
            )
            .frame(height: 110)
            .overlay {
                VStack(spacing: 8) {
                    Image(systemName: "opticaldisc")
                        .font(.system(size: 28))
                        .foregroundStyle(.secondary)
                    if let cue = session.cueURL {
                        Text(cue.lastPathComponent)
                            .fontWeight(.medium)
                        Text(cue.deletingLastPathComponent().path)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .truncationMode(.middle)
                    } else {
                        Text("Drop a .cue here, or Open With from Finder")
                            .foregroundStyle(.secondary)
                    }
                }
                .padding()
            }
            .onDrop(of: [.fileURL], isTargeted: $isDropTargeted) { providers in
                handleDrop(providers)
            }
    }

    @ViewBuilder
    private var statusBlock: some View {
        if session.isBusy {
            HStack(spacing: 10) {
                ProgressView()
                Text("Talking to CTDB…")
            }
        } else if let err = session.errorText {
            Label(err, systemImage: "exclamationmark.triangle.fill")
                .foregroundStyle(.red)
                .textSelection(.enabled)
        } else if let result = session.result {
            VStack(alignment: .leading, spacing: 8) {
                Label(statusTitle(result.status), systemImage: statusIcon(result.status))
                    .font(.headline)
                    .foregroundStyle(statusColor(result.status))
                if let msg = result.message {
                    Text(msg)
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                }
                if let toc = result.tocId {
                    Text("TOCID \(toc)")
                        .font(.system(.caption, design: .monospaced))
                        .textSelection(.enabled)
                }
                if let path = result.outputPath {
                    Text("Wrote \(path)")
                        .font(.caption)
                        .textSelection(.enabled)
                }
            }
        }
    }

    private var actions: some View {
        HStack {
            Button("Open .cue…") { openPanel() }
            Button("Verify again") {
                Task { await session.verify() }
            }
            .disabled(session.cueURL == nil || session.isBusy)

            Spacer()

            Button("Repair to FLAC") {
                Task { await session.repair() }
            }
            .buttonStyle(.borderedProminent)
            .disabled(!session.canRepair || session.isBusy)
        }
    }

    private func openPanel() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "cue") ?? .data]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        if panel.runModal() == .OK, let url = panel.url {
            session.openCue(url)
        }
    }

    private func handleDrop(_ providers: [NSItemProvider]) -> Bool {
        guard let provider = providers.first else { return false }
        _ = provider.loadObject(ofClass: URL.self) { url, _ in
            guard let url, url.pathExtension.lowercased() == "cue" else { return }
            DispatchQueue.main.async { session.openCue(url) }
        }
        return true
    }

    private func statusTitle(_ status: String) -> String {
        switch status {
        case "clean": return "Accurate — no repair needed"
        case "repairable": return "Repairable with CTDB"
        case "unrecoverable": return "Errors — not recoverable"
        case "not_found": return "Not in CTDB"
        case "repaired": return "Repaired"
        default: return status
        }
    }

    private func statusIcon(_ status: String) -> String {
        switch status {
        case "clean", "repaired": return "checkmark.seal.fill"
        case "repairable": return "wrench.and.screwdriver.fill"
        case "unrecoverable", "error": return "xmark.octagon.fill"
        default: return "questionmark.circle"
        }
    }

    private func statusColor(_ status: String) -> Color {
        switch status {
        case "clean", "repaired": return .green
        case "repairable": return .orange
        case "unrecoverable", "error": return .red
        default: return .secondary
        }
    }
}
