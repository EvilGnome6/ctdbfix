using System.Text.Json.Serialization;

namespace CtdbFix.Engine.Models;

public sealed class EngineResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("tocId")]
    public string? TocId { get; set; }

    [JsonPropertyName("confidence")]
    public int? Confidence { get; set; }

    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }

    [JsonPropertyName("samplesWritten")]
    public long? SamplesWritten { get; set; }

    [JsonPropertyName("entries")]
    public List<EntryInfo>? Entries { get; set; }

    [JsonPropertyName("submit")]
    public SubmitInfo? Submit { get; set; }
}

public sealed class EntryInfo
{
    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }

    [JsonPropertyName("crc")]
    public string Crc { get; set; } = "";

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("hasErrors")]
    public bool HasErrors { get; set; }

    [JsonPropertyName("canRecover")]
    public bool CanRecover { get; set; }

    [JsonPropertyName("usedForRepair")]
    public bool UsedForRepair { get; set; }

    [JsonPropertyName("correctableErrors")]
    public int? CorrectableErrors { get; set; }

    [JsonPropertyName("affectedSectors")]
    public string? AffectedSectors { get; set; }
}

public sealed class SubmitInfo
{
    [JsonPropertyName("eligible")]
    public bool Eligible { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("xldLogPath")]
    public string? XldLogPath { get; set; }

    [JsonPropertyName("drive")]
    public string? Drive { get; set; }

    [JsonPropertyName("xldOk")]
    public bool XldOk { get; set; }

    [JsonPropertyName("allTracksAccuratelyRipped")]
    public bool? AllTracksAccuratelyRipped { get; set; }

    [JsonPropertyName("minArConfidence")]
    public int? MinArConfidence { get; set; }

    [JsonPropertyName("suggestedQuality")]
    public int? SuggestedQuality { get; set; }
}
