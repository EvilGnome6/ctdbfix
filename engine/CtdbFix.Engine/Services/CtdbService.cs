using System.Net;
using CUETools.AccurateRip;
using CUETools.CDImage;
using CUETools.Codecs;
using CUETools.CTDB;
using CUETools.Processor;
using CtdbFix.Engine.Models;

namespace CtdbFix.Engine.Services;

public sealed class CtdbService
{
    private const string UserAgent = "ctdbfix/0.1";
    private readonly CUEConfig _config;
    private readonly TextWriter _log;

    public CtdbService(TextWriter? log = null)
    {
        _log = log ?? Console.Error;
        if (!CUEProcessorPlugins.decs.Exists(d => d is CUETools.Codecs.Flake.DecoderSettings))
            CUEProcessorPlugins.decs.Add(new CUETools.Codecs.Flake.DecoderSettings());
        _config = new CUEConfig();
    }

    public EngineResult Verify(string cuePath)
    {
        _log.WriteLine($"Verifying: {cuePath}");
        try
        {
            var xld = XldLogParser.FindAndParse(cuePath);
            if (xld.Found)
                _log.WriteLine($"XLD log: {xld.Path}");
            else
                _log.WriteLine("XLD log: not found");

            var cueSheet = OpenCue(cuePath);
            var toc = cueSheet.TOC;
            _log.WriteLine($"TOC ID: {toc.TOCID}");

            var ar = new AccurateRipVerify(toc, _config.GetProxy());
            var ctdb = new CUEToolsDB(toc, _config.GetProxy());
            ctdb.Init(ar);

            if (!FeedAudioToAR(ar, cueSheet, out var feedError))
            {
                var fail = Fail("verify", feedError ?? "Failed to read audio", toc.TOCID);
                fail.Submit = SubmitEligibility.Evaluate("error", xld);
                return fail;
            }

            _log.WriteLine("Contacting CTDB...");
            ctdb.ContactDB(null, UserAgent, null, true, false, CTDBMetadataSearch.Default);

            if (ctdb.Metadata == null && ctdb.Entries == null)
            {
                var notFound = ctdb.QueryResponseStatus == HttpStatusCode.NotFound;
                var status = notFound ? "not_found" : "error";
                return new EngineResult
                {
                    Ok = notFound,
                    Command = "verify",
                    Status = status,
                    Message = $"CTDB returned {ctdb.QueryResponseStatus}",
                    TocId = toc.TOCID,
                    Submit = SubmitEligibility.Evaluate(status, xld)
                };
            }

            _log.WriteLine("Verifying...");
            ctdb.DoVerify();

            var entries = MapEntries(ctdb);
            var statusOk = ClassifyVerify(entries);
            // Empty entries after a successful query still means unknown/not useful for classify —
            // ClassifyVerify already returns not_found for empty.

            return new EngineResult
            {
                Ok = true,
                Command = "verify",
                Status = statusOk,
                TocId = toc.TOCID,
                Confidence = ctdb.Confidence,
                Message = ctdb.Status,
                Entries = entries,
                Submit = SubmitEligibility.Evaluate(statusOk, xld)
            };
        }
        catch (Exception ex)
        {
            _log.WriteLine(ex.ToString());
            return Fail("verify", ex.Message);
        }
    }

    /// <param name="confirm">If false, dry-run only (default). If true, actually upload.</param>
    public EngineResult Submit(string cuePath, bool confirm)
    {
        _log.WriteLine($"Submit{(confirm ? "" : " (dry-run)")}: {cuePath}");
        try
        {
            var xld = XldLogParser.FindAndParse(cuePath);
            var cueSheet = OpenCue(cuePath);
            var toc = cueSheet.TOC;
            _log.WriteLine($"TOC ID: {toc.TOCID}");

            var ar = new AccurateRipVerify(toc, _config.GetProxy());
            var ctdb = new CUEToolsDB(toc, _config.GetProxy());
            ctdb.Init(ar);

            if (!FeedAudioToAR(ar, cueSheet, out var feedError))
            {
                var fail = Fail("submit", feedError ?? "Failed to read audio", toc.TOCID);
                fail.Submit = SubmitEligibility.Evaluate("error", xld);
                return fail;
            }

            string drive = xld.DriveForSubmit ?? "";
            _log.WriteLine("Contacting CTDB...");
            ctdb.ContactDB(null, UserAgent, string.IsNullOrEmpty(drive) ? null : drive, true, false, CTDBMetadataSearch.Default);

            string status;
            List<EntryInfo>? entries = null;
            int? confidence = null;
            string? message;

            if (ctdb.Metadata == null && ctdb.Entries == null
                && ctdb.QueryResponseStatus == HttpStatusCode.NotFound)
            {
                status = "not_found";
                message = "Disc not yet in CTDB.";
            }
            else if (ctdb.QueryExceptionStatus != WebExceptionStatus.Success
                     && (ctdb.QueryExceptionStatus != WebExceptionStatus.ProtocolError
                         || ctdb.QueryResponseStatus != HttpStatusCode.NotFound))
            {
                var fail = Fail("submit", $"CTDB access failed: {ctdb.DBStatus}", toc.TOCID);
                fail.Submit = SubmitEligibility.Evaluate("error", xld);
                return fail;
            }
            else
            {
                _log.WriteLine("Verifying...");
                ctdb.DoVerify();
                entries = MapEntries(ctdb);
                status = ClassifyVerify(entries);
                confidence = ctdb.Confidence;
                message = ctdb.Status;
            }

            var submitInfo = SubmitEligibility.Evaluate(status, xld);
            if (!submitInfo.Eligible)
            {
                return new EngineResult
                {
                    Ok = false,
                    Command = "submit",
                    Status = "ineligible",
                    Message = submitInfo.Reason,
                    TocId = toc.TOCID,
                    Confidence = confidence,
                    Entries = entries,
                    Submit = submitInfo
                };
            }

            string artist = cueSheet.Metadata.Artist ?? "";
            string title = cueSheet.Metadata.Title ?? "";
            string barcode = cueSheet.Metadata.Barcode ?? "";
            int quality = submitInfo.SuggestedQuality ?? 100;

            _log.WriteLine("------------------------------------------------------------");
            _log.WriteLine($"Drive   : {submitInfo.Drive}");
            _log.WriteLine($"Quality : {quality}");
            _log.WriteLine($"Artist  : {(string.IsNullOrEmpty(artist) ? "(empty)" : artist)}");
            _log.WriteLine($"Title   : {(string.IsNullOrEmpty(title) ? "(empty)" : title)}");
            _log.WriteLine($"Barcode : {(string.IsNullOrEmpty(barcode) ? "(empty)" : barcode)}");
            _log.WriteLine($"Status  : {status}");
            _log.WriteLine("------------------------------------------------------------");

            if (!confirm)
            {
                return new EngineResult
                {
                    Ok = true,
                    Command = "submit",
                    Status = "dry_run",
                    Message = "Dry-run only. Pass --confirm to upload to CTDB.",
                    TocId = toc.TOCID,
                    Confidence = confidence,
                    Entries = entries,
                    Submit = submitInfo
                };
            }

            // Re-contact with drive name for the actual submit path if we only had not_found earlier.
            if (status == "not_found")
            {
                ctdb.ContactDB(null, UserAgent, submitInfo.Drive, true, false, CTDBMetadataSearch.Default);
            }

            _log.WriteLine("Submitting to CTDB...");
            var resp = ctdb.Submit(1, quality, artist, title, barcode);
            if (resp == null)
            {
                return new EngineResult
                {
                    Ok = false,
                    Command = "submit",
                    Status = "error",
                    Message = $"Submit failed: {ctdb.SubStatus}",
                    TocId = toc.TOCID,
                    Confidence = confidence,
                    Entries = entries,
                    Submit = submitInfo
                };
            }

            _log.WriteLine($"Submit result: {resp.status} — {resp.message}");
            return new EngineResult
            {
                Ok = true,
                Command = "submit",
                Status = "submitted",
                Message = string.IsNullOrEmpty(ctdb.SubStatus) ? resp.message : ctdb.SubStatus,
                TocId = toc.TOCID,
                Confidence = confidence,
                Entries = entries,
                Submit = submitInfo
            };
        }
        catch (Exception ex)
        {
            _log.WriteLine(ex.ToString());
            return Fail("submit", ex.Message);
        }
    }

    public EngineResult Repair(string cuePath, string? outputPath, string? targetCrc, int flacLevel)
    {
        _log.WriteLine($"Repairing: {cuePath}");
        try
        {
            var cueSheet = OpenCue(cuePath);
            var toc = cueSheet.TOC;
            _log.WriteLine($"TOC ID: {toc.TOCID}");

            if (cueSheet.SourcePaths.Count == 0)
                return Fail("repair", "No audio files found in CUE sheet.", toc.TOCID);

            outputPath ??= Path.Combine(
                Path.GetDirectoryName(cuePath) ?? ".",
                Path.GetFileNameWithoutExtension(cuePath) + "_repaired.flac");

            if (!outputPath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
                outputPath = Path.ChangeExtension(outputPath, ".flac");

            if (File.Exists(outputPath))
                return Fail("repair", $"Output already exists: {outputPath}", toc.TOCID);

            var ar = new AccurateRipVerify(toc, _config.GetProxy());
            var ctdb = new CUEToolsDB(toc, _config.GetProxy());
            ctdb.Init(ar);

            if (!FeedAudioToAR(ar, cueSheet, out var feedError))
                return Fail("repair", feedError ?? "Failed to read audio", toc.TOCID);

            _log.WriteLine("Contacting CTDB...");
            ctdb.ContactDB(null, UserAgent, null, true, false, CTDBMetadataSearch.Default);

            if (ctdb.Metadata == null && ctdb.Entries == null)
            {
                var notFound = ctdb.QueryResponseStatus == HttpStatusCode.NotFound;
                return new EngineResult
                {
                    Ok = false,
                    Command = "repair",
                    Status = notFound ? "not_found" : "error",
                    Message = $"CTDB returned {ctdb.QueryResponseStatus}",
                    TocId = toc.TOCID
                };
            }

            _log.WriteLine("Verifying...");
            ctdb.DoVerify();

            var repairable = SelectRepairEntry(ctdb, targetCrc, out var selectError);
            var entries = MapEntries(ctdb, repairable);

            if (repairable == null)
            {
                var status = entries.Any(e => e.HasErrors) ? "unrecoverable" : "clean";
                return new EngineResult
                {
                    Ok = status == "clean",
                    Command = "repair",
                    Status = status,
                    Message = selectError ?? (status == "clean"
                        ? "No errors detected; repair not needed."
                        : "Errors detected but CTDB cannot recover them."),
                    TocId = toc.TOCID,
                    Confidence = ctdb.Confidence,
                    Entries = entries
                };
            }

            _log.WriteLine($"Using entry CRC {repairable.crc:x8} (confidence {repairable.conf})");
            _log.WriteLine($"Writing repaired FLAC to: {outputPath}");

            var pcm = new AudioPCMConfig(16, 2, 44100);
            var encoderSettings = new CUETools.Codecs.Flake.EncoderSettings();
            encoderSettings.Init(pcm);
            encoderSettings.EncoderMode = Math.Clamp(flacLevel, 0, 8).ToString();

            var audioDest = new CUETools.Codecs.Flake.AudioEncoder(encoderSettings, outputPath);
            audioDest.FinalSampleCount = ar.FinalSampleCount;

            AudioBuffer? buffer = null;
            long samplesWritten = 0;

            try
            {
                foreach (string? audioPath in cueSheet.SourcePaths)
                {
                    if (audioPath == null) continue;

                    var audioSource = AudioReadWrite.GetAudioSource(audioPath, null, _config)
                        ?? throw new InvalidOperationException($"Failed to open audio: {audioPath}");

                    try
                    {
                        if (buffer == null)
                            buffer = new AudioBuffer(audioSource, 1024 * 64);
                        else
                            buffer.Prepare(audioSource, 1024 * 64);

                        while (audioSource.Read(buffer, -1) != 0)
                        {
                            repairable.repair.Write(buffer);
                            audioDest.Write(buffer);
                            samplesWritten += buffer.Length;
                        }
                    }
                    finally
                    {
                        audioSource.Close();
                    }
                }

                repairable.repair.Close();
                audioDest.Close();
            }
            catch
            {
                try { audioDest.Close(); } catch { /* ignore */ }
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                throw;
            }

            _log.WriteLine($"Repair completed. Samples written: {samplesWritten}");

            return new EngineResult
            {
                Ok = true,
                Command = "repair",
                Status = "repaired",
                TocId = toc.TOCID,
                Confidence = repairable.conf,
                OutputPath = outputPath,
                SamplesWritten = samplesWritten,
                Entries = entries,
                Message = $"Corrected via CTDB CRC {repairable.crc:x8}"
            };
        }
        catch (Exception ex)
        {
            _log.WriteLine(ex.ToString());
            return Fail("repair", ex.Message);
        }
    }

    private CUESheet OpenCue(string cuePath)
    {
        var cueSheet = new CUESheet(_config);
        cueSheet.Open(cuePath);
        return cueSheet;
    }

    private bool FeedAudioToAR(AccurateRipVerify ar, CUESheet cueSheet, out string? errorMessage)
    {
        errorMessage = null;
        if (cueSheet.SourcePaths.Count == 0)
        {
            errorMessage = "No audio files found in CUE sheet.";
            _log.WriteLine(errorMessage);
            return false;
        }

        AudioBuffer? buffer = null;

        foreach (string? audioPath in cueSheet.SourcePaths)
        {
            if (audioPath == null) continue;
            _log.WriteLine($"Reading: {audioPath}");

            var audioSource = AudioReadWrite.GetAudioSource(audioPath, null, _config);
            if (audioSource == null)
            {
                errorMessage = $"Failed to open audio source: {audioPath}";
                _log.WriteLine(errorMessage);
                return false;
            }

            try
            {
                if (buffer == null)
                    buffer = new AudioBuffer(audioSource, 1024 * 64);
                else
                    buffer.Prepare(audioSource, 1024 * 64);

                while (audioSource.Read(buffer, -1) != 0)
                {
                    if (ar.Position + buffer.Length > ar.FinalSampleCount)
                        buffer.Length = (int)(ar.FinalSampleCount - ar.Position);

                    if (buffer.Length > 0)
                        ar.Write(buffer);

                    if (ar.Position >= ar.FinalSampleCount) break;
                }
            }
            finally
            {
                audioSource.Close();
            }

            if (ar.Position >= ar.FinalSampleCount) break;
        }

        if (ar.Position < ar.FinalSampleCount)
        {
            _log.WriteLine($"Warning: audio shorter than TOC; padding {ar.FinalSampleCount - ar.Position} samples.");
            if (buffer == null)
                buffer = new AudioBuffer(new AudioPCMConfig(16, 2, 44100), 1024 * 64);
            Array.Clear(buffer.Bytes, 0, buffer.Bytes.Length);
            while (ar.Position < ar.FinalSampleCount)
            {
                int remaining = (int)Math.Min(buffer.Bytes.Length / buffer.PCM.BlockAlign, ar.FinalSampleCount - ar.Position);
                buffer.Length = remaining;
                ar.Write(buffer);
            }
        }

        _log.WriteLine($"Audio fed. Position={ar.Position}, expected={ar.FinalSampleCount}");
        return true;
    }

    private static DBEntry? SelectRepairEntry(CUEToolsDB ctdb, string? targetCrc, out string? error)
    {
        error = null;
        if (!string.IsNullOrEmpty(targetCrc))
        {
            foreach (var entry in ctdb.Entries)
            {
                if (!entry.crc.ToString("x8").Equals(targetCrc, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.hasErrors && entry.canRecover && entry.repair != null)
                    return entry;

                error = entry.hasErrors
                    ? $"Entry CRC {targetCrc} is not recoverable."
                    : $"Entry CRC {targetCrc} does not need repair.";
                return null;
            }

            error = $"Target CRC {targetCrc} not found.";
            return null;
        }

        DBEntry? best = null;
        foreach (var entry in ctdb.Entries)
        {
            if (entry.hasErrors && entry.canRecover && entry.repair != null)
            {
                if (best == null || entry.conf > best.conf)
                    best = entry;
            }
        }
        return best;
    }

    private static List<EntryInfo> MapEntries(CUEToolsDB ctdb, DBEntry? used = null)
    {
        var list = new List<EntryInfo>();
        foreach (var entry in ctdb.Entries)
        {
            int? correctable = null;
            string? affected = null;
            if (entry.canRecover && entry.repair != null)
            {
                try
                {
                    correctable = entry.repair.CorrectableErrors;
                    affected = entry.repair.AffectedSectors;
                }
                catch
                {
                    // Some CTDB entries expose a repair object whose sector map isn't ready yet.
                }
            }

            list.Add(new EntryInfo
            {
                Confidence = entry.conf,
                Crc = entry.crc.ToString("x8"),
                Offset = entry.offset,
                Status = entry.Status ?? "",
                HasErrors = entry.hasErrors,
                CanRecover = entry.canRecover,
                UsedForRepair = used != null && ReferenceEquals(entry, used),
                CorrectableErrors = correctable,
                AffectedSectors = affected
            });
        }
        return list;
    }

    private static string ClassifyVerify(List<EntryInfo> entries)
    {
        if (entries.Count == 0) return "not_found";
        if (entries.Any(e => !e.HasErrors)) return "clean";
        if (entries.Any(e => e.HasErrors && e.CanRecover)) return "repairable";
        if (entries.Any(e => e.HasErrors)) return "unrecoverable";
        return "unknown";
    }

    private static EngineResult Fail(string command, string message, string? tocId = null) => new()
    {
        Ok = false,
        Command = command,
        Status = "error",
        Message = message,
        TocId = tocId
    };
}
