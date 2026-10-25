import Foundation

enum EngineClientError: LocalizedError {
    case engineMissing(String)
    case badJSON(String)
    case nonZeroExit(Int, String)

    var errorDescription: String? {
        switch self {
        case .engineMissing(let path):
            return "ctdbfix-engine not found at \(path)"
        case .badJSON(let detail):
            return "Could not parse engine JSON: \(detail)"
        case .nonZeroExit(let code, let stderr):
            return "Engine exited \(code). \(stderr)"
        }
    }
}

struct EngineClient {
    func run(command: String, cueURL: URL, extraArgs: [String] = []) async throws -> (EngineResult, String) {
        let engineURL = try resolveEngine()
        let process = Process()
        process.executableURL = engineURL
        process.arguments = [command, cueURL.path] + extraArgs
        process.currentDirectoryURL = engineURL.deletingLastPathComponent()

        let stdout = Pipe()
        let stderr = Pipe()
        process.standardOutput = stdout
        process.standardError = stderr

        try process.run()
        process.waitUntilExit()

        let outData = stdout.fileHandleForReading.readDataToEndOfFile()
        let errData = stderr.fileHandleForReading.readDataToEndOfFile()
        let errText = String(data: errData, encoding: .utf8) ?? ""

        do {
            let result = try JSONDecoder().decode(EngineResult.self, from: outData)
            return (result, errText)
        } catch {
            if process.terminationStatus != 0 {
                throw EngineClientError.nonZeroExit(Int(process.terminationStatus), errText)
            }
            let preview = String(data: outData, encoding: .utf8) ?? "<binary>"
            throw EngineClientError.badJSON("\(error.localizedDescription) — stdout: \(preview.prefix(400))")
        }
    }

    private func resolveEngine() throws -> URL {
        if let resources = Bundle.main.resourceURL {
            let bundled = resources.appendingPathComponent("engine/ctdbfix-engine")
            if FileManager.default.isExecutableFile(atPath: bundled.path) {
                return bundled
            }
        }

        // Dev fallback: published binary in the repo
        let dev = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent() // CtdbFix/
            .deletingLastPathComponent() // app/
            .deletingLastPathComponent() // repo root
            .appendingPathComponent("engine/dist/osx-arm64/ctdbfix-engine")
        if FileManager.default.isExecutableFile(atPath: dev.path) {
            return dev
        }

        throw EngineClientError.engineMissing(dev.path)
    }
}
