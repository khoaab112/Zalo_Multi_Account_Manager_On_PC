using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ZaloManagerPoc
{
    public static partial class CdpMessageWatcher
    {
        [DataContract]
        public sealed class RecalledMessage
        {
            [DataMember] internal string Id;
            [DataMember(EmitDefaultValue = false)] internal string CliMsgId;
            [DataMember] public DateTimeOffset DetectedAt { get; internal set; }
            [DataMember] public string Content { get; internal set; }
            [DataMember] internal bool Unread = true;
            [DataMember(EmitDefaultValue = false)] internal RecallAttachment Attachment;
            [DataMember(EmitDefaultValue = false)] public bool Outgoing { get; internal set; }
        }

        [DataContract]
        internal sealed class RecallAttachment
        {
            [DataMember] internal string Kind;
            [DataMember(EmitDefaultValue = false)] internal string Data;
            [DataMember(EmitDefaultValue = false)] internal string Mime;
            [DataMember(EmitDefaultValue = false)] internal string CaptureStatus;
            [DataMember(EmitDefaultValue = false)] internal string Origin;
            [DataMember(EmitDefaultValue = false)] internal bool FromSource;
            [DataMember(EmitDefaultValue = false)] internal string FileName { get; set; }
            [DataMember(EmitDefaultValue = false)] internal string PreviewData;
            [DataMember(EmitDefaultValue = false)] internal string PreviewMime;
            [DataMember(EmitDefaultValue = false)] internal string SourceUrl;
            [DataMember(EmitDefaultValue = false)] internal string FileSize;
            [DataMember(EmitDefaultValue = false)] internal string Checksum;
            [DataMember(EmitDefaultValue = false)] internal bool Original;
        }

        internal sealed class RecallConversation
        {
            public string Id { get; internal set; }
            public string Name { get; internal set; }
            public string AvatarPng { get; internal set; }
            public int Unread { get; internal set; }
            public DateTimeOffset Latest { get; internal set; }
        }

        [DataContract]
        private sealed class RecallThread
        {
            [DataMember] public string Name;
            [DataMember] public string AvatarPng;
            [DataMember] public readonly List<RecalledMessage> Messages = new List<RecalledMessage>();
        }

        private static readonly object RecallGate = new object();
        private static readonly string RecallArchivePath = Path.Combine(Store.Root, "recalled-messages.dat");
        private static Dictionary<string, Dictionary<string, RecallThread>> recalled;
        private static bool archiveLoadFailed;
        private static string recallStorageError;
        private static int recallRevision;
        public static event Action RecalledMessagesChanged;

        internal static readonly string RecallMediaDir = Path.Combine(Store.Root, "media");

        public sealed class RecallStorageInfo
        {
            public long TextBytes;
            public long MediaBytes;
            public int TextCount;
            public int MediaCount;
            public long TotalBytes { get { return TextBytes + MediaBytes; } }

            public string FormattedTotal { get { return StorageMeasurement.Format(TotalBytes); } }
            public string FormattedText { get { return StorageMeasurement.Format(TextBytes); } }
            public string FormattedMedia { get { return StorageMeasurement.Format(MediaBytes); } }
        }

        private static readonly HashSet<string> DisabledRecallProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string RecallSettingsPath = Path.Combine(Store.Root, "recall-disabled.txt");
        private static bool recallSettingsLoaded;

        private static void EnsureRecallSettingsLoaded()
        {
            if (recallSettingsLoaded) return;
            recallSettingsLoaded = true;
            try
            {
                if (File.Exists(RecallSettingsPath))
                {
                    string[] lines = File.ReadAllLines(RecallSettingsPath);
                    foreach (var line in lines)
                    {
                        string trimmed = (line ?? "").Trim();
                        if (!String.IsNullOrEmpty(trimmed)) DisabledRecallProfiles.Add(trimmed);
                    }
                }
            }
            catch { }
        }

        public static bool IsRecallEnabled(string profileId)
        {
            lock (RecallGate)
            {
                EnsureRecallSettingsLoaded();
                return !DisabledRecallProfiles.Contains(profileId ?? "");
            }
        }

        public static void SetRecallEnabled(string profileId, bool enabled)
        {
            lock (RecallGate)
            {
                EnsureRecallSettingsLoaded();
                if (enabled) DisabledRecallProfiles.Remove(profileId ?? "");
                else DisabledRecallProfiles.Add(profileId ?? "");

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RecallSettingsPath));
                    File.WriteAllLines(RecallSettingsPath, DisabledRecallProfiles.ToArray());
                }
                catch { }
            }
            NotifyRecallChanged();
        }

        public static RecallStorageInfo GetRecallStorageInfo(string profileId)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                var info = new RecallStorageInfo();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return info;

                foreach (var thread in threads.Values)
                {
                    foreach (var message in thread.Messages)
                    {
                        if (!String.IsNullOrEmpty(message.Content))
                        {
                            info.TextBytes += Encoding.UTF8.GetByteCount(message.Content);
                            info.TextCount++;
                        }
                        var attachment = message.Attachment;
                        if (attachment != null)
                        {
                            long m = RecallMediaBytes(attachment.Data) + RecallMediaBytes(attachment.PreviewData);
                            if (m > 0)
                            {
                                info.MediaBytes += m;
                                info.MediaCount++;
                            }
                        }
                    }
                }
                return info;
            }
        }

        public static RecallStorageInfo GetRecallConversationStorageInfo(string profileId, string conversationId)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                var info = new RecallStorageInfo();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return info;

                RecallThread target;
                if (threads.TryGetValue(conversationId, out target) && target != null)
                {
                    foreach (var message in target.Messages)
                    {
                        if (!String.IsNullOrEmpty(message.Content))
                        {
                            info.TextBytes += Encoding.UTF8.GetByteCount(message.Content);
                            info.TextCount++;
                        }
                        var attachment = message.Attachment;
                        if (attachment != null)
                        {
                            long m = RecallMediaBytes(attachment.Data) + RecallMediaBytes(attachment.PreviewData);
                            if (m > 0)
                            {
                                info.MediaBytes += m;
                                info.MediaCount++;
                            }
                        }
                    }
                }
                return info;
            }
        }

        internal static int RecallRevision
        {
            get { return Volatile.Read(ref recallRevision); }
        }

        internal static string RecallStorageError
        {
            get { lock (RecallGate) { EnsureRecallLoaded(); return recallStorageError; } }
        }

        public static int GetRecalledCount(string profileId)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                return recalled.TryGetValue(profileId, out threads)
                    ? threads.Values.Sum(thread => thread.Messages.Count(message => message.Unread))
                    : 0;
            }
        }

        internal static long? GetRecallContentBytes(string profileId)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                if (recallStorageError != null) return null;
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return 0;

                long bytes = 0;
                foreach (var thread in threads.Values)
                foreach (var message in thread.Messages)
                {
                    if (message.Content != null) bytes += Encoding.UTF8.GetByteCount(message.Content);
                    var attachment = message.Attachment;
                    if (attachment != null)
                        bytes += RecallMediaBytes(attachment.Data) + RecallMediaBytes(attachment.PreviewData);
                }
                return bytes;
            }
        }

        internal static long RecallMediaBytes(string data)
        {
            if (String.IsNullOrEmpty(data)) return 0;
            if (data.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string localPath = data.Substring("local:".Length);
                    if (File.Exists(localPath)) return new FileInfo(localPath).Length;
                }
                catch { }
                return 0;
            }
            int padding = data.EndsWith("==", StringComparison.Ordinal) ? 2 : data.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;
            return data.Length / 4L * 3 - padding;
        }

        internal static RecallConversation[] GetRecallConversations(string profileId)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return new RecallConversation[0];

                var grouped = new Dictionary<string, RecallConversation>();
                foreach (var pair in threads.Where(p => p.Value.Messages.Count > 0))
                {
                    string name = String.IsNullOrWhiteSpace(pair.Value.Name) ? "Người gửi chưa xác định" : pair.Value.Name;
                    string key = name != "Người gửi chưa xác định" ? "name:" + name : pair.Key;

                    RecallConversation conv;
                    if (grouped.TryGetValue(key, out conv))
                    {
                        conv.Unread += pair.Value.Messages.Count(m => m.Unread);
                        var latestMsg = pair.Value.Messages[pair.Value.Messages.Count - 1].DetectedAt;
                        if (latestMsg > conv.Latest) conv.Latest = latestMsg;
                        if (String.IsNullOrEmpty(conv.AvatarPng) && !String.IsNullOrEmpty(pair.Value.AvatarPng))
                            conv.AvatarPng = pair.Value.AvatarPng;
                    }
                    else
                    {
                        grouped[key] = new RecallConversation
                        {
                            Id = pair.Key,
                            Name = name,
                            AvatarPng = pair.Value.AvatarPng,
                            Unread = pair.Value.Messages.Count(m => m.Unread),
                            Latest = pair.Value.Messages[pair.Value.Messages.Count - 1].DetectedAt
                        };
                    }
                }
                return grouped.Values.OrderByDescending(item => item.Latest).ToArray();
            }
        }

        internal static RecalledMessage[] ReadRecallConversation(string profileId, string conversationId)
        {
            RecalledMessage[] messages;
            bool changed = false;
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return new RecalledMessage[0];

                RecallThread primaryThread;
                threads.TryGetValue(conversationId, out primaryThread);
                string name = primaryThread != null ? primaryThread.Name : null;

                var allMatchingThreads = threads.Values.Where(t => t == primaryThread || (!String.IsNullOrWhiteSpace(name) && name != "Người gửi chưa xác định" && t.Name == name)).ToList();

                var messageList = new List<RecalledMessage>();
                foreach (var t in allMatchingThreads)
                {
                    foreach (var m in t.Messages)
                    {
                        if (!messageList.Any(existing => existing.Id == m.Id))
                        {
                            messageList.Add(m);
                            if (m.Unread)
                            {
                                m.Unread = false;
                                changed = true;
                            }
                        }
                    }
                }
                if (changed)
                {
                    SaveRecallArchive();
                    recallRevision++;
                }
                messages = messageList.OrderBy(m => m.DetectedAt).ToArray();
            }
            if (changed) NotifyRecallChanged();
            return messages;
        }

        private static void TryDeleteLocalMedia(string data)
        {
            if (String.IsNullOrEmpty(data) || !data.StartsWith("local:", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                string localPath = data.Substring("local:".Length);
                if (localPath.StartsWith(RecallMediaDir, StringComparison.OrdinalIgnoreCase) && File.Exists(localPath))
                {
                    File.Delete(localPath);
                }
            }
            catch { }
        }

        internal static bool DeleteAllRecalls(string profileId)
        {
            bool saved;
            var attachmentsToClean = new List<RecallAttachment>();
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return true;
                foreach (var t in threads.Values)
                foreach (var m in t.Messages)
                    if (m.Attachment != null) attachmentsToClean.Add(m.Attachment);
                recalled.Remove(profileId);
                saved = SaveRecallArchive();
                if (!saved) recalled.Add(profileId, threads);
                recallRevision++;
            }
            if (saved)
            {
                foreach (var att in attachmentsToClean)
                {
                    TryDeleteLocalMedia(att.Data);
                    TryDeleteLocalMedia(att.PreviewData);
                }
            }
            NotifyRecallChanged();
            return saved;
        }

        internal static bool DeleteRecallConversation(string profileId, string conversationId)
        {
            bool saved;
            var attachmentsToClean = new List<RecallAttachment>();
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return true;

                RecallThread targetThread;
                threads.TryGetValue(conversationId, out targetThread);
                string name = targetThread != null ? targetThread.Name : null;

                var keysToRemove = threads.Where(pair => pair.Key == conversationId || (!String.IsNullOrWhiteSpace(name) && name != "Người gửi chưa xác định" && pair.Value.Name == name)).Select(pair => pair.Key).ToList();
                foreach (var k in keysToRemove)
                {
                    RecallThread remThread;
                    if (threads.TryGetValue(k, out remThread))
                    {
                        foreach (var m in remThread.Messages)
                            if (m.Attachment != null) attachmentsToClean.Add(m.Attachment);
                    }
                    threads.Remove(k);
                }

                saved = SaveRecallArchive();
                recallRevision++;
            }
            if (saved)
            {
                foreach (var att in attachmentsToClean)
                {
                    TryDeleteLocalMedia(att.Data);
                    TryDeleteLocalMedia(att.PreviewData);
                }
            }
            NotifyRecallChanged();
            return saved;
        }

        private static RecallThread FindRecallThread(string profileId, string conversationId)
        {
            EnsureRecallLoaded();
            Dictionary<string, RecallThread> threads;
            RecallThread thread;
            return recalled.TryGetValue(profileId, out threads) && threads.TryGetValue(conversationId, out thread)
                ? thread : null;
        }

        private static void RecordRecall(Profile profile, string conversationId, string conversationName, string avatarPng, string messageId, string cliMsgId, string content, RecallAttachment attachment, bool outgoing = false)
        {
            if (profile != null && !IsRecallEnabled(profile.Id)) return;
            if (String.IsNullOrWhiteSpace(messageId) || messageId.StartsWith("bg_") || messageId.StartsWith("conv_")) return;
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profile.Id, out threads))
                {
                    threads = new Dictionary<string, RecallThread>();
                    recalled.Add(profile.Id, threads);
                }
                foreach (var existingThread in threads.Values)
                {
                    if (existingThread.Messages.Any(message =>
                        message.Id == messageId
                        || (!String.IsNullOrEmpty(cliMsgId) && (message.Id == cliMsgId || message.CliMsgId == cliMsgId))
                        || (!String.IsNullOrEmpty(message.CliMsgId) && message.CliMsgId == messageId)))
                        return;
                }
                RecallThread thread = null;
                if (!String.IsNullOrWhiteSpace(conversationName) && conversationName != "Người gửi chưa xác định")
                {
                    thread = threads.Values.FirstOrDefault(t => t.Name == conversationName);
                }
                if (thread == null && !String.IsNullOrWhiteSpace(conversationId))
                {
                    threads.TryGetValue(conversationId, out thread);
                }
                if (thread == null)
                {
                    thread = new RecallThread();
                    string key = !String.IsNullOrWhiteSpace(conversationId) ? conversationId : ("conversation:" + messageId);
                    threads.Add(key, thread);
                }
                if (!String.IsNullOrWhiteSpace(conversationName) && conversationName != "Người gửi chưa xác định")
                    thread.Name = conversationName;
                if (!String.IsNullOrEmpty(avatarPng) && String.IsNullOrEmpty(thread.AvatarPng)) thread.AvatarPng = avatarPng;
                thread.Messages.Add(new RecalledMessage { Id = messageId, CliMsgId = cliMsgId, DetectedAt = DateTimeOffset.Now, Content = content, Attachment = attachment, Outgoing = outgoing });
                SaveRecallArchive();
                recallRevision++;
            }
            NotifyRecallChanged();
        }

        private static bool UpdateRecallAttachmentCore(string profileId, string conversationId, string messageId, RecallAttachment attachment)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return false;

                RecallThread thread;
                if (!threads.TryGetValue(conversationId, out thread))
                {
                    thread = threads.Values.FirstOrDefault(t => t.Messages.Any(m => m.Id == messageId || (!String.IsNullOrEmpty(m.CliMsgId) && m.CliMsgId == messageId)));
                }
                if (thread == null) return false;
                int index = thread.Messages.FindIndex(m => m.Id == messageId || (!String.IsNullOrEmpty(m.CliMsgId) && m.CliMsgId == messageId));
                if (index < 0) return false;
                var messageToUpdate = thread.Messages[index];
                thread.Messages[index] = new RecalledMessage
                {
                    Id = messageToUpdate.Id, CliMsgId = messageToUpdate.CliMsgId, Content = messageToUpdate.Content, DetectedAt = messageToUpdate.DetectedAt,
                    Unread = messageToUpdate.Unread, Attachment = attachment, Outgoing = messageToUpdate.Outgoing
                };
                return true;
            }
        }

        private static void CommitRecallChanges()
        {
            lock (RecallGate)
            {
                SaveRecallArchive();
                recallRevision++;
            }
            NotifyRecallChanged();
        }

        private static void UpdateRecallAttachment(string profileId, string conversationId, string messageId, RecallAttachment attachment)
        {
            if (UpdateRecallAttachmentCore(profileId, conversationId, messageId, attachment))
            {
                CommitRecallChanges();
            }
        }

        private static void EnsureRecallLoaded()
        {
            if (recalled != null) return;
            try
            {
                byte[] encrypted;
                try { encrypted = File.ReadAllBytes(RecallArchivePath); }
                catch (FileNotFoundException) { encrypted = null; }
                catch (DirectoryNotFoundException) { encrypted = null; }
                if (encrypted != null)
                {
                    byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    using (var stream = new MemoryStream(plain, false))
                        recalled = (Dictionary<string, Dictionary<string, RecallThread>>)CreateRecallSerializer().ReadObject(stream);
                    if (recalled == null) throw new SerializationException();
                    if (!RecallArchivePath.EndsWith("archive.dat", StringComparison.OrdinalIgnoreCase) && !RecallArchivePath.Contains("fixture"))
                    {
                        MigrateLargeMediaToDisk();
                    }
                    return;
                }
            }
            catch (Exception error)
            {
                if (!IsRecallStorageError(error)) throw;
                archiveLoadFailed = true;
                recallStorageError = "Không đọc được kho tin thu hồi. File đã lưu được giữ nguyên.";
            }
            recalled = new Dictionary<string, Dictionary<string, RecallThread>>();
        }

        private static void MigrateLargeMediaToDisk()
        {
            if (recalled == null) return;
            bool modified = false;
            try
            {
                Directory.CreateDirectory(RecallMediaDir);
                foreach (var profileThreads in recalled.Values)
                {
                    foreach (var thread in profileThreads.Values)
                    {
                        foreach (var message in thread.Messages)
                        {
                            var att = message.Attachment;
                            if (att == null) continue;

                            if (!String.IsNullOrEmpty(att.Data) && !att.Data.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
                                && (att.Data.Length > 20000 || att.Kind == "video" || att.Kind == "audio" || att.Kind == "file"))
                            {
                                try
                                {
                                    byte[] bytes = Convert.FromBase64String(att.Data);
                                    string ext = ".bin";
                                    if (att.Kind == "video") ext = ".mp4";
                                    else if (att.Kind == "audio") ext = (att.Mime ?? "").Contains("wav") ? ".wav" : ".m4a";
                                    else if (att.Kind == "image") ext = (att.Mime ?? "").Contains("png") ? ".png" : ".jpg";
                                    else if (att.Kind == "file" && !String.IsNullOrEmpty(message.Content))
                                    {
                                        string e = Path.GetExtension(message.Content);
                                        if (!String.IsNullOrEmpty(e) && e.Length <= 8) ext = e;
                                    }
                                    string filePath = Path.Combine(RecallMediaDir, Guid.NewGuid().ToString("N") + ext);
                                    File.WriteAllBytes(filePath, bytes);
                                    att.Data = "local:" + filePath;
                                    modified = true;
                                }
                                catch { }
                            }

                            if (!String.IsNullOrEmpty(att.PreviewData) && !att.PreviewData.StartsWith("local:", StringComparison.OrdinalIgnoreCase) && att.PreviewData.Length > 20000)
                            {
                                att.PreviewData = null;
                                att.PreviewMime = null;
                                modified = true;
                            }
                        }
                    }
                }
            }
            catch { }
            if (modified)
            {
                SaveRecallArchive();
            }
        }

        private static bool SaveRecallArchive()
        {
            if (archiveLoadFailed) return false;
            var temporary = RecallArchivePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecallArchivePath));
                using (var stream = new MemoryStream())
                {
                    CreateRecallSerializer().WriteObject(stream, recalled);
                    byte[] encrypted = ProtectedData.Protect(stream.ToArray(), null, DataProtectionScope.CurrentUser);
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        output.Write(encrypted, 0, encrypted.Length);
                        output.Flush(true);
                    }
                }
                if (File.Exists(RecallArchivePath)) File.Replace(temporary, RecallArchivePath, null);
                else File.Move(temporary, RecallArchivePath);
                recallStorageError = null;
                return true;
            }
            catch (Exception error)
            {
                if (!IsRecallStorageError(error)) throw;
                recallStorageError = "Không lưu được kho tin thu hồi. Thay đổi trong phiên này có thể chưa được lưu.";
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return false;
            }
        }

        private static DataContractJsonSerializer CreateRecallSerializer()
        {
            return new DataContractJsonSerializer(typeof(Dictionary<string, Dictionary<string, RecallThread>>));
        }

        private static bool IsRecallStorageError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException
                || error is CryptographicException || error is SerializationException;
        }

        private static void NotifyRecallChanged()
        {
            var changed = RecalledMessagesChanged;
            if (changed != null) changed();
        }
    }
}
