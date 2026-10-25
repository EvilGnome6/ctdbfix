import AppKit
import Combine
import Foundation

struct EngineResult: Codable, Equatable {
    var ok: Bool
    var command: String
    var status: String
    var message: String?
    var tocId: String?
    var confidence: Int?
    var outputPath: String?
    var samplesWritten: Int64?
    var entries: [EntryInfo]?
    var submit: SubmitInfo?
}

struct EntryInfo: Codable, Equatable, Identifiable {
    var id: String { crc }
    var confidence: Int
    var crc: String
    var offset: Int
    var status: String
    var hasErrors: Bool
    var canRecover: Bool
    var usedForRepair: Bool
    var correctableErrors: Int?
    var affectedSectors: String?
}

struct SubmitInfo: Codable, Equatable {
    var eligible: Bool
    var reason: String?
    var xldLogPath: String?
    var drive: String?
    var xldOk: Bool
    var allTracksAccuratelyRipped: Bool?
    var minArConfidence: Int?
    var suggestedQuality: Int?
}

@MainActor
final class SessionModel: ObservableObject {
    @Published var cueURL: URL?
    @Published var result: EngineResult?
    @Published var isBusy = false
    @Published var logTail = ""
    @Published var errorText: String?

    private let engine = EngineClient()

    var canRepair: Bool {
        result?.status == "repairable"
    }

    var canSubmit: Bool {
        result?.submit?.eligible == true && result?.status != "submitted"
    }

    func openCue(_ url: URL) {
        cueURL = url
        result = nil
        errorText = nil
        logTail = ""
        Task { await verify() }
    }

    func verify() async {
        guard let cueURL else { return }
        isBusy = true
        errorText = nil
        defer { isBusy = false }
        do {
            let (res, log) = try await engine.run(command: "verify", cueURL: cueURL)
            result = res
            logTail = log
        } catch {
            errorText = error.localizedDescription
        }
    }

    func repair() async {
        guard let cueURL else { return }
        isBusy = true
        errorText = nil
        defer { isBusy = false }
        do {
            let (res, log) = try await engine.run(command: "repair", cueURL: cueURL)
            result = res
            logTail = log
            if let out = res.outputPath {
                NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: out)])
            }
        } catch {
            errorText = error.localizedDescription
        }
    }

    func submit(confirm: Bool) async {
        guard let cueURL else { return }
        isBusy = true
        errorText = nil
        defer { isBusy = false }
        do {
            var args: [String] = []
            if confirm { args.append("--confirm") }
            let (res, log) = try await engine.run(command: "submit", cueURL: cueURL, extraArgs: args)
            result = res
            logTail = log
            if !res.ok, let msg = res.message {
                errorText = msg
            }
        } catch {
            errorText = error.localizedDescription
        }
    }
}
