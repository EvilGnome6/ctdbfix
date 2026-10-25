using System.Text.Json;
using CtdbFix.Engine.Models;
using CtdbFix.Engine.Services;

namespace CtdbFix.Engine;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 2;
        }

        string command = args[0].ToLowerInvariant();
        string cuePath = args[1];

        if (!File.Exists(cuePath))
        {
            WriteResult(new EngineResult
            {
                Ok = false,
                Command = command,
                Status = "error",
                Message = $"CUE file not found: {cuePath}"
            });
            return 1;
        }

        cuePath = Path.GetFullPath(cuePath);
        var service = new CtdbService(Console.Error);
        EngineResult result;

        switch (command)
        {
            case "verify":
                result = service.Verify(cuePath);
                break;
            case "repair":
            {
                string? output = GetArg(args, "--output") ?? GetArg(args, "-o");
                string? target = GetArg(args, "--target");
                int level = 5;
                var levelStr = GetArg(args, "--flac-level");
                if (levelStr != null && int.TryParse(levelStr, out var parsed))
                    level = parsed;
                result = service.Repair(cuePath, output, target, level);
                break;
            }
            case "submit":
            {
                bool confirm = HasFlag(args, "--confirm");
                result = service.Submit(cuePath, confirm);
                break;
            }
            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                PrintUsage();
                return 2;
        }

        WriteResult(result);
        return result.Ok ? 0 : 1;
    }

    static void WriteResult(EngineResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        Console.Out.WriteLine(json);
    }

    static string? GetArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    static void PrintUsage()
    {
        Console.Error.WriteLine("""
            ctdbfix-engine <command> <cue_file> [options]

            Commands:
              verify              Check rip against CTDB (+ XLD submit eligibility)
              repair              Repair via CTDB parity; write FLAC
              submit              Submit to CTDB if XLD log + CTDB status allow it

            Options (repair):
              -o, --output PATH   Output .flac path (default: <cue>_repaired.flac)
              --target CRC        Prefer a specific CTDB entry CRC
              --flac-level N      Flake compression 0-8 (default: 5)

            Options (submit):
              --confirm           Actually upload (otherwise dry-run)

            JSON result is written to stdout. Progress/logs go to stderr.
            """);
    }
}
