using System.Globalization;
using System.Text.RegularExpressions;
using CtdbFix.Engine.Models;

namespace CtdbFix.Engine.Services;

public sealed class XldLogInfo
{
    public string? Path { get; init; }
    public bool Found { get; init; }
    public bool IsXld { get; init; }
    public string? DriveRaw { get; init; }
    public string? DriveForSubmit { get; init; }
    public bool NoErrorsOccurred { get; init; }
    public bool HasHardErrors { get; init; }
    public int HardErrorCount { get; init; }
    public bool? AllTracksAccuratelyRipped { get; init; }
    public int? MinArConfidence { get; init; }
    public List<string> Issues { get; init; } = new();

    public bool PassesQualityGate =>
        Found && IsXld && NoErrorsOccurred && !HasHardErrors && Issues.Count == 0;
}

public static class XldLogParser
{
    private static readonly Regex DriveLine = new(
        @"^Used drive\s*:\s*(.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StatLine = new(
        @"^\s*(Read error|Damaged sector count|Skipped \(treated as error\)|Inconsistency in error sectors)\s*:\s*(\d+)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ArTrackOk = new(
        @"Track\s+\d+\s*:\s*OK\s*\([^)]*confidence\s+(\d+)\s*/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ArTrackDetail = new(
        @"->Accurately ripped\s*\([^)]*confidence\s+(?:(\d+)\+(\d+)|(\d+)\s*)/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static XldLogInfo FindAndParse(string cuePath)
    {
        var dir = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return new XldLogInfo
            {
                Found = false,
                Issues = { "Rip folder not found." }
            };
        }

        var candidates = new List<string>();
        var sibling = Path.Combine(dir, Path.GetFileNameWithoutExtension(cuePath) + ".log");
        if (File.Exists(sibling))
            candidates.Add(sibling);

        foreach (var path in Directory.EnumerateFiles(dir, "*.log"))
        {
            if (!candidates.Contains(path))
                candidates.Add(path);
        }

        foreach (var path in candidates)
        {
            var text = File.ReadAllText(path);
            if (!LooksLikeXld(text))
                continue;
            return Parse(path, text);
        }

        return new XldLogInfo
        {
            Found = false,
            Issues = { "No XLD extraction log found next to the CUE." }
        };
    }

    public static bool LooksLikeXld(string text) =>
        text.Contains("X Lossless Decoder", StringComparison.OrdinalIgnoreCase)
        || text.Contains("XLD extraction logfile", StringComparison.OrdinalIgnoreCase);

    public static XldLogInfo Parse(string path, string text)
    {
        var gateIssues = new List<string>();
        bool isXld = LooksLikeXld(text);
        if (!isXld)
            gateIssues.Add("Not an XLD log.");

        string? driveRaw = null;
        var driveMatch = DriveLine.Match(text);
        if (driveMatch.Success)
            driveRaw = driveMatch.Groups[1].Value.Trim();

        string? driveForSubmit = FormatDriveForCtdb(driveRaw);
        if (string.IsNullOrEmpty(driveForSubmit))
            gateIssues.Add("Missing drive name.");

        bool noErrors = text.Contains("No errors occurred", StringComparison.OrdinalIgnoreCase);
        if (!noErrors)
            gateIssues.Add("Missing \"No errors occurred\".");

        int hardErrors = 0;
        foreach (Match m in StatLine.Matches(text))
        {
            if (int.TryParse(m.Groups[2].Value, out int n))
                hardErrors += n;
        }
        bool hasHard = hardErrors > 0;
        if (hasHard)
            gateIssues.Add($"Hard errors present ({hardErrors}).");

        bool? allAr = null;
        if (text.Contains("All tracks accurately ripped", StringComparison.OrdinalIgnoreCase))
            allAr = true;
        else if (Regex.IsMatch(text, @"AccurateRip Summary", RegexOptions.IgnoreCase))
            allAr = false;

        int? minConf = null;
        foreach (Match m in ArTrackOk.Matches(text))
        {
            if (int.TryParse(m.Groups[1].Value, out int c))
                minConf = minConf.HasValue ? Math.Min(minConf.Value, c) : c;
        }
        foreach (Match m in ArTrackDetail.Matches(text))
        {
            int c;
            if (m.Groups[1].Success && m.Groups[2].Success
                && int.TryParse(m.Groups[1].Value, out int a)
                && int.TryParse(m.Groups[2].Value, out int b))
            {
                c = a + b;
            }
            else if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out c))
            {
                // single confidence
            }
            else
            {
                continue;
            }
            minConf = minConf.HasValue ? Math.Min(minConf.Value, c) : c;
        }

        return new XldLogInfo
        {
            Path = path,
            Found = true,
            IsXld = isXld,
            DriveRaw = driveRaw,
            DriveForSubmit = driveForSubmit,
            NoErrorsOccurred = noErrors,
            HasHardErrors = hasHard,
            HardErrorCount = hardErrors,
            AllTracksAccuratelyRipped = allAr,
            MinArConfidence = minConf,
            Issues = gateIssues
        };
    }

    /// <summary>
    /// XLD: "HL-DT-ST DVDRAM AP70NS50 (revision 1.01)" → "HL-DT-ST - DVDRAM AP70NS50".
    /// CTDB matches drivename with regex ^&lt;vendor&gt;.*- so there must be a hyphen
    /// *after* the registered vendor prefix (hyphens inside HL-DT-ST alone are not enough).
    /// </summary>
    public static string? FormatDriveForCtdb(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        var rev = s.IndexOf("(revision", StringComparison.OrdinalIgnoreCase);
        if (rev >= 0)
            s = s[..rev].Trim();

        // Already in CUERipper/ctdb-cli style: "Vendor - Model…"
        if (s.Contains(" - ", StringComparison.Ordinal))
            return s;

        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{parts[0]} - {string.Join(' ', parts.Skip(1))}";

        // Single token with no space — leave as-is (may still fail CTDB if no trailing hyphen pattern).
        return s;
    }
}

public static class SubmitEligibility
{
    public const int MinArConfidenceForNewDisc = 2;

    public static SubmitInfo Evaluate(string ctdbStatus, XldLogInfo xld)
    {
        if (!xld.Found)
        {
            return new SubmitInfo
            {
                Eligible = false,
                Reason = "No XLD log beside the CUE — submit disabled.",
                XldLogPath = xld.Path,
                Drive = xld.DriveForSubmit,
                XldOk = false,
                AllTracksAccuratelyRipped = xld.AllTracksAccuratelyRipped,
                MinArConfidence = xld.MinArConfidence
            };
        }

        if (!xld.PassesQualityGate)
        {
            return new SubmitInfo
            {
                Eligible = false,
                Reason = "XLD log failed quality gate: " + string.Join(" ", xld.Issues),
                XldLogPath = xld.Path,
                Drive = xld.DriveForSubmit,
                XldOk = false,
                AllTracksAccuratelyRipped = xld.AllTracksAccuratelyRipped,
                MinArConfidence = xld.MinArConfidence
            };
        }

        switch (ctdbStatus)
        {
            case "clean":
                return Ok(xld, "XLD log clean and CTDB match — eligible to confirm/submit.");

            case "not_found":
                if (xld.AllTracksAccuratelyRipped != true)
                {
                    return Block(xld, true,
                        "New-to-CTDB discs require AccurateRip \"All tracks accurately ripped\" in the XLD log.");
                }
                if ((xld.MinArConfidence ?? 0) < MinArConfidenceForNewDisc)
                {
                    return Block(xld, true,
                        $"New-to-CTDB discs require AccurateRip confidence ≥ {MinArConfidenceForNewDisc} (got {xld.MinArConfidence?.ToString(CultureInfo.InvariantCulture) ?? "none"}).");
                }
                return Ok(xld, "XLD log clean, AccurateRip OK, disc not in CTDB — eligible to submit.");

            case "repairable":
                return Block(xld, xld.PassesQualityGate, "CTDB says this rip needs repair — do not submit it.");

            case "unrecoverable":
                return Block(xld, xld.PassesQualityGate, "CTDB could not verify this rip — do not submit it.");

            default:
                return Block(xld, xld.PassesQualityGate, $"CTDB status \"{ctdbStatus}\" is not eligible for submit.");
        }
    }

    private static SubmitInfo Ok(XldLogInfo xld, string reason) => new()
    {
        Eligible = true,
        Reason = reason,
        XldLogPath = xld.Path,
        Drive = xld.DriveForSubmit,
        XldOk = true,
        AllTracksAccuratelyRipped = xld.AllTracksAccuratelyRipped,
        MinArConfidence = xld.MinArConfidence,
        SuggestedQuality = 100
    };

    private static SubmitInfo Block(XldLogInfo xld, bool xldOk, string reason) => new()
    {
        Eligible = false,
        Reason = reason,
        XldLogPath = xld.Path,
        Drive = xld.DriveForSubmit,
        XldOk = xldOk,
        AllTracksAccuratelyRipped = xld.AllTracksAccuratelyRipped,
        MinArConfidence = xld.MinArConfidence
    };
}
