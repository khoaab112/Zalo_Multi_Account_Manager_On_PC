using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZaloManagerPoc
{
    public static partial class CdpMessageWatcher
    {
        private static readonly object RecallMediaGate = new object();
        private const int MediaCleanupBatchSize = 32;
        private static readonly string RecallStagingDir = Path.Combine(Store.Root, "media-staging");
        private static readonly HashSet<string> UncommittedMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static int mediaScanScheduled;
        private static readonly ConcurrentDictionary<string, byte> ActiveStagedMedia = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        internal sealed class RecallMediaReport
        {
            public int CandidateCount;
            public long CandidateBytes;
        }

        internal sealed class RecallDeletion
        {
            public bool Saved;
            public long ReleasedBytes;
        }

        private static void PublishStagedMedia(RecallAttachment attachment)
        {
            if (attachment.StagedPath == null) return;
            string path = NewMediaPath(attachment.Mime);
            File.Move(attachment.StagedPath, path);
            byte ignored;
            ActiveStagedMedia.TryRemove(attachment.StagedPath, out ignored);
            attachment.Data = "local:" + path;
            attachment.StagedPath = null;
        }

        private static string NewMediaPath(string mime)
        {
            string extension;
            switch (mime)
            {
                case "image/png": extension = ".png"; break;
                case "image/jpeg": extension = ".jpg"; break;
                case "image/webp": extension = ".webp"; break;
                case "image/gif": extension = ".gif"; break;
                case "video/mp4": extension = ".mp4"; break;
                case "video/quicktime": extension = ".mov"; break;
                case "video/webm": extension = ".webm"; break;
                case "audio/wav": extension = ".wav"; break;
                case "audio/ogg": extension = ".ogg"; break;
                case "audio/aac": extension = ".aac"; break;
                case "audio/mp4": extension = ".m4a"; break;
                case "audio/mpeg": extension = ".mp3"; break;
                default: extension = ".bin"; break;
            }
            Directory.CreateDirectory(RecallMediaDir);
            return Path.Combine(RecallMediaDir, Guid.NewGuid().ToString("N") + extension);
        }

        private static RecallAttachment OffloadInlineMedia(RecallAttachment attachment, List<string> publishing = null)
        {
            if (attachment == null) return null;
            string data = OffloadInlineData(attachment.Data, attachment.Mime,
                attachment.Kind == "audio" || attachment.Kind == "video" || attachment.Kind == "file", publishing);
            string preview = OffloadInlineData(attachment.PreviewData, attachment.PreviewMime, false, publishing);
            if (data == attachment.Data && preview == attachment.PreviewData) return attachment;
            var updated = attachment.Clone();
            updated.Data = data;
            updated.PreviewData = preview;
            return updated;
        }

        private static string OffloadInlineData(string data, string mime, bool offloadSmall, List<string> publishing)
        {
            if (String.IsNullOrEmpty(data) || data.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ||
                (!offloadSmall && data.Length <= 20000)) return data;
            try
            {
                string path = NewMediaPath(mime);
                if (publishing != null)
                {
                    Directory.CreateDirectory(RecallStagingDir);
                    string temporary = Path.Combine(RecallStagingDir, Guid.NewGuid().ToString("N") + ".part");
                    ActiveStagedMedia.TryAdd(temporary, 0);
                    try
                    {
                        File.WriteAllBytes(temporary, Convert.FromBase64String(data));
                        ActiveStagedMedia.TryAdd(path, 0);
                        publishing.Add(path);
                        File.Move(temporary, path);
                        return "local:" + path;
                    }
                    finally { DeleteStagedFile(temporary); }
                }
                File.WriteAllBytes(path, Convert.FromBase64String(data));
                return "local:" + path;
            }
            catch (Exception error)
            {
                if (!IsRecallStorageError(error) && !(error is FormatException)) throw;
                System.Diagnostics.Debug.WriteLine(error.GetType().Name);
                return data;
            }
        }

        private static void ProtectPublishedMedia(RecallAttachment attachment, List<string> publishing)
        {
            if (attachment.Data == null || !attachment.Data.StartsWith("local:", StringComparison.Ordinal)) return;
            string path = attachment.Data.Substring("local:".Length);
            if (ActiveStagedMedia.TryAdd(path, 0)) publishing.Add(path);
        }

        private static void ReleasePublishingMedia(List<string> publishing)
        {
            byte ignored;
            foreach (string path in publishing) ActiveStagedMedia.TryRemove(path, out ignored);
        }

        private static void DeleteStagedMedia(RecallAttachment attachment)
        {
            if (attachment != null && attachment.StagedPath != null)
                DeleteStagedFile(attachment.StagedPath);
        }

        private static void DeleteStagedFile(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                byte ignored;
                ActiveStagedMedia.TryRemove(path, out ignored);
            }
        }

        private static void AddMediaReferences(HashSet<string> references, RecallAttachment attachment)
        {
            if (attachment == null) return;
            foreach (string value in new[] { attachment.Data, attachment.PreviewData })
            {
                if (value == null || !value.StartsWith("local:", StringComparison.OrdinalIgnoreCase)) continue;
                try { references.Add(Path.GetFullPath(value.Substring("local:".Length))); }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
        }

        internal static void ScheduleRecallMediaScan()
        {
            Task.Run(() => ScanAndReport()).ContinueWith(task =>
            {
                if (task.Exception != null) Store.Log(task.Exception.GetBaseException());
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        internal static Task<RecallMediaReport> ScanRecallMediaNow()
        {
            return Task.Run(() => ScanAndReport());
        }

        private static RecallMediaReport ScanAndReport()
        {
            if (Interlocked.Exchange(ref mediaScanScheduled, 1) != 0) return null;
            try
            {
                var report = ScanRecallMedia(DateTime.UtcNow.AddHours(-1));
                if (report != null && report.CandidateCount > 0)
                    System.Diagnostics.Debug.WriteLine("Ứng viên dọn media: " + report.CandidateCount +
                        " tệp, " + report.CandidateBytes + " byte.");
                return report;
            }
            finally { Volatile.Write(ref mediaScanScheduled, 0); }
        }

        private static RecallMediaReport ScanRecallMedia(DateTime cutoff)
        {
            var references = GetMediaReferences();
            if (references == null) return null;
            var report = new RecallMediaReport();
            ScanMediaDirectory(RecallMediaDir, references, cutoff, report);
            ScanMediaDirectory(RecallStagingDir, references, cutoff, report);
            return report;
        }

        private static HashSet<string> GetMediaReferences()
        {
            lock (RecallMediaGate)
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                if (archiveLoadFailed || recallStorageError != null) return null;
                var references = ArchiveMediaReferences();

                bool scanned = IncomingMessageCache.WithMediaReferences(references);
                return scanned ? references : null;
            }
        }

        private static HashSet<string> ArchiveMediaReferences()
        {
            var references = new HashSet<string>(UncommittedMedia, StringComparer.OrdinalIgnoreCase);
            foreach (var profile in recalled.Values)
            foreach (var thread in profile.Values)
            foreach (var message in thread.Messages)
                AddMediaReferences(references, message.Attachment);
            return references;
        }

        // Called under RecallGate so a cached reference cannot be published after cleanup unlinks it.
        private static RecallAttachment WithoutMissingLocalMedia(RecallAttachment attachment)
        {
            if (attachment == null) return null;
            bool missingData = IsMissingLocalMedia(attachment.Data);
            bool missingPreview = IsMissingLocalMedia(attachment.PreviewData);
            if (!missingData && !missingPreview) return attachment;
            var updated = attachment.Clone();
            if (missingData)
            {
                updated.Data = null;
                updated.CaptureStatus = "local-missing";
            }
            if (missingPreview) updated.PreviewData = null;
            return updated;
        }

        private static bool IsMissingLocalMedia(string data)
        {
            return data != null && data.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(data.Substring("local:".Length));
        }

        private static long DeleteUnreferencedMedia(HashSet<string> candidates)
        {
            if (candidates.Count == 0) return 0;
            if (!IncomingMessageCache.Flush()) return 0;
            string root = Path.GetFullPath(RecallMediaDir);
            var pending = candidates.ToArray();
            long released = 0;
            for (int start = 0; start < pending.Length; start += MediaCleanupBatchSize)
            {
                lock (RecallMediaGate)
                lock (RecallGate)
                {
                    EnsureRecallLoaded();
                    if (archiveLoadFailed || recallStorageError != null || !Directory.Exists(root) ||
                        (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return released;
                    var references = ArchiveMediaReferences();
                    int end = Math.Min(start + MediaCleanupBatchSize, pending.Length);
                    bool eligible = IncomingMessageCache.WithMediaReferences(references, committedContains =>
                    {
                        for (int index = start; index < end; index++)
                        {
                            string path = pending[index];
                            if (ActiveStagedMedia.ContainsKey(path) || UncommittedMedia.Contains(path)) continue;
                            if (!references.Contains(path) &&
                                (committedContains(path) || !TryDeleteManagedMedia(path, root, ref released))) continue;
                            candidates.Remove(path);
                            IncomingMessageCache.CompleteMediaCleanup(path);
                        }
                    });
                    if (!eligible) return released;
                }
            }
            return released;
        }

        private static bool TryDeleteManagedMedia(string path, string root, ref long released)
        {
            Guid managedName;
            if (!String.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out managedName)) return true;
            try
            {
                if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return true;
                long size = StorageUsage.GetFileStorageBytes(path, new StorageMeasurement());
                File.Delete(path);
                released += size;
                return true;
            }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void ScanMediaDirectory(string directory, HashSet<string> references, DateTime cutoff, RecallMediaReport report)
        {
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                Guid managedName;
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out managedName) ||
                    (references.Contains(Path.GetFullPath(path)) || ActiveStagedMedia.ContainsKey(path))) continue;
                try
                {
                    var file = new FileInfo(path);
                    if (file.LastWriteTimeUtc >= cutoff || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    report.CandidateCount++;
                    report.CandidateBytes += StorageUsage.GetFileStorageBytes(path, new StorageMeasurement());
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
