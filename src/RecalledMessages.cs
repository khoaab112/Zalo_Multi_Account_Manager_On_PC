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
            [DataMember(EmitDefaultValue = false)] internal bool Placeholder;
            [DataMember(EmitDefaultValue = false)] internal DateTimeOffset? GroupExpires;
            [DataMember] public DateTimeOffset DetectedAt { get; internal set; }
            [DataMember] public string Content { get; internal set; }
            [DataMember] internal bool Unread = true;
            [DataMember(EmitDefaultValue = false)] internal RecallAttachment Attachment;
            [DataMember(EmitDefaultValue = false)] public bool Outgoing { get; internal set; }
        }

        [DataContract]
        internal sealed class RecallAttachment
        {
            [IgnoreDataMember] internal string StagedPath;
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

            internal RecallAttachment Clone()
            {
                return (RecallAttachment)MemberwiseClone();
            }
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
            if (!enabled) StopRecallProfile(profileId);
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

                return threads.Where(pair => pair.Value.Messages.Count != 0)
                    .Select(pair => new RecallConversation
                    {
                        Id = pair.Key, Name = pair.Value.Name ?? "Người gửi chưa xác định",
                        AvatarPng = pair.Value.AvatarPng, Unread = pair.Value.Messages.Count(message => message.Unread),
                        Latest = pair.Value.Messages.Max(message => message.DetectedAt)
                    }).OrderByDescending(item => item.Latest).ToArray();
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

                RecallThread thread;
                if (!threads.TryGetValue(conversationId, out thread)) return new RecalledMessage[0];

                var unread = thread.Messages.Where(message => message.Unread).ToArray();
                if (unread.Length != 0)
                {
                    foreach (var message in unread) message.Unread = false;
                    changed = SaveRecallArchive();
                    if (!changed)
                        foreach (var message in unread) message.Unread = true;
                    recallRevision++;
                }
                messages = thread.Messages.OrderBy(m => m.DetectedAt).ToArray();
            }
            if (changed) NotifyRecallChanged();
            return messages;
        }

        internal static bool DeleteAllRecalls(string profileId)
        {
            return DeleteRecalls(profileId, null).Saved;
        }

        internal static bool DeleteRecallConversation(string profileId, string conversationId)
        {
            return DeleteRecalls(profileId, conversationId).Saved;
        }

        internal static RecallDeletion DeleteRecalls(string profileId, string conversationId)
        {
            var result = new RecallDeletion { Saved = true };
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                if (archiveLoadFailed) { result.Saved = false; return result; }
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return result;
                RecallThread thread = null;
                if (conversationId == null) recalled.Remove(profileId);
                else
                {
                    if (!threads.TryGetValue(conversationId, out thread)) return result;
                    threads.Remove(conversationId);
                }
                result.Saved = SaveRecallArchive();
                if (!result.Saved)
                {
                    if (conversationId == null) recalled.Add(profileId, threads);
                    else threads.Add(conversationId, thread);
                }
                else
                {
                    foreach (var removed in thread == null ? (IEnumerable<RecallThread>)threads.Values : new[] { thread })
                    foreach (var message in removed.Messages)
                        AddMediaReferences(candidates, message.Attachment);
                }
                recallRevision++;
            }
            if (result.Saved)
            {
                try
                {
                    IncomingMessageCache.QueueMediaCleanup(candidates);
                    result.ReleasedBytes = DeleteUnreferencedMedia(candidates);
                }
                catch (Exception error)
                {
                    if (!IsRecallStorageError(error)) throw;
                    Store.Log(error);
                }
            }
            NotifyRecallChanged();
            return result;
        }

        private static void PruneGroupRecalls(DateTimeOffset now, HashSet<string> candidates)
        {
            bool changed = false;
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                if (archiveLoadFailed) return;
                var expired = new List<Tuple<RecallThread, int, RecalledMessage>>();
                var empty = new List<Tuple<Dictionary<string, RecallThread>, string, RecallThread>>();
                foreach (var profile in recalled.Values)
                foreach (var entry in profile)
                {
                    var thread = entry.Value;
                    int before = thread.Messages.Count;
                    for (int i = before - 1; i >= 0; i--)
                    {
                        var message = thread.Messages[i];
                        if (!message.GroupExpires.HasValue || message.GroupExpires.Value > now) continue;
                        expired.Add(Tuple.Create(thread, i, message));
                        thread.Messages.RemoveAt(i);
                    }
                    if (before > 0 && thread.Messages.Count == 0)
                        empty.Add(Tuple.Create(profile, entry.Key, thread));
                }
                if (expired.Count == 0) return;
                foreach (var entry in empty) entry.Item1.Remove(entry.Item2);
                changed = SaveRecallArchive();
                if (changed)
                    foreach (var item in expired) AddMediaReferences(candidates, item.Item3.Attachment);
                else
                {
                    foreach (var entry in empty) entry.Item1.Add(entry.Item2, entry.Item3);
                    for (int i = expired.Count - 1; i >= 0; i--)
                        expired[i].Item1.Messages.Insert(expired[i].Item2, expired[i].Item3);
                }
                recallRevision++;
            }
            if (changed) NotifyRecallChanged();
        }

        private static bool MoveRecallToConversation(string profileId, string from, string to, string recallId, string name,
            DateTimeOffset? groupExpires)
        {
            if (from == to && !groupExpires.HasValue) return true;
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                RecallThread previous, target;
                if (!recalled.TryGetValue(profileId, out threads) || !threads.TryGetValue(from, out previous)) return false;
                int index = previous.Messages.FindIndex(item => item.Id == recallId);
                if (index < 0) return false;
                bool created = !threads.TryGetValue(to, out target);
                if (created) threads.Add(to, target = new RecallThread { Name = name });
                var message = previous.Messages[index];
                var previousExpiry = message.GroupExpires;
                message.GroupExpires = EarlierExpiry(previousExpiry, groupExpires);
                if (from == to)
                {
                    if (!SaveRecallArchive()) { message.GroupExpires = previousExpiry; return false; }
                    recallRevision++;
                    return true;
                }
                bool added = !target.Messages.Any(item => MatchesMessage(item.Id, item.CliMsgId, message.Id, message.CliMsgId));
                if (added) target.Messages.Add(message);
                previous.Messages.RemoveAt(index);
                if (!SaveRecallArchive())
                {
                    previous.Messages.Insert(index, message);
                    message.GroupExpires = previousExpiry;
                    if (added) target.Messages.Remove(message);
                    if (created) threads.Remove(to);
                    return false;
                }
                recallRevision++;
            }
            NotifyRecallChanged();
            return true;
        }

        private static bool HasRecordedRecall(string profileId, string conversation, string id, string alias)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return false;
                string knownKey = "conversation:" + conversation;
                return threads.Any(pair => (conversation.Length == 0 || pair.Key == knownKey || pair.Key == "message:" + id) &&
                    pair.Value.Messages.Any(message => MatchesMessage(message.Id, message.CliMsgId, id, alias)));
            }
        }

        private static bool RecordRecall(Profile profile, string conversationId, string conversationName, string avatarPng,
            string messageId, string cliMsgId, string content, RecallAttachment attachment, bool outgoing = false,
            bool placeholder = false, DateTimeOffset? groupExpires = null)
        {
            if (String.IsNullOrWhiteSpace(messageId) || messageId.StartsWith("bg_") || messageId.StartsWith("conv_")) return false;
            bool saved;
            lock (RecallGate)
            {
                if (!IsRecallEnabled(profile.Id)) return false;
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                bool newProfile = !recalled.TryGetValue(profile.Id, out threads);
                if (newProfile) recalled.Add(profile.Id, threads = new Dictionary<string, RecallThread>());
                RecallThread thread;
                bool newThread = !threads.TryGetValue(conversationId, out thread);
                if (newThread) threads.Add(conversationId, thread = new RecallThread());

                int index = thread.Messages.FindIndex(message => MatchesMessage(message.Id, message.CliMsgId, messageId, cliMsgId));
                var existing = index < 0 ? null : thread.Messages[index];
                if (existing != null && (!existing.Placeholder || placeholder)) return true;

                string oldName = thread.Name, oldAvatar = thread.AvatarPng;
                if (!String.IsNullOrWhiteSpace(conversationName) && conversationName != "Người gửi chưa xác định")
                    thread.Name = conversationName;
                if (!String.IsNullOrEmpty(avatarPng) && String.IsNullOrEmpty(thread.AvatarPng)) thread.AvatarPng = avatarPng;

                var updated = new RecalledMessage
                {
                    Id = messageId, CliMsgId = cliMsgId, DetectedAt = existing == null ? DateTimeOffset.Now : existing.DetectedAt,
                    Unread = existing == null || existing.Unread, Content = content, Attachment = WithoutMissingLocalMedia(attachment),
                    Outgoing = outgoing, Placeholder = placeholder, GroupExpires = groupExpires
                };
                if (index < 0) thread.Messages.Add(updated);
                else thread.Messages[index] = updated;
                saved = SaveRecallArchive();
                if (!saved)
                {
                    if (index < 0) thread.Messages.Remove(updated);
                    else thread.Messages[index] = existing;
                    thread.Name = oldName;
                    thread.AvatarPng = oldAvatar;
                    if (newThread) threads.Remove(conversationId);
                    if (newProfile) recalled.Remove(profile.Id);
                }
                recallRevision++;
            }
            NotifyRecallChanged();
            return saved;
        }

        private static string ResolveRecallPlaceholder(string profileId, string conversationId, CachedIncomingMessage incoming,
            out bool saveFailed)
        {
            saveFailed = false;
            string recallId;
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return null;
                var matches = threads.Where(pair => pair.Key == conversationId || pair.Key.StartsWith("message:", StringComparison.Ordinal))
                    .SelectMany(pair => pair.Value.Messages.Where(message => message.Placeholder &&
                        MatchesMessage(message.Id, message.CliMsgId, incoming.Id, incoming.CliMsgId))
                        .Select(message => Tuple.Create(pair.Key, pair.Value, message))).Take(2).ToArray();
                if (matches.Length != 1) return null;
                var match = matches[0];
                var prior = match.Item3;
                recallId = prior.Id;
                var oldThread = match.Item2;
                int oldIndex = oldThread.Messages.IndexOf(prior);
                oldThread.Messages.RemoveAt(oldIndex);
                bool removedThread = oldThread.Messages.Count == 0;
                if (removedThread) threads.Remove(match.Item1);
                RecallThread thread = null;
                bool newThread = false;
                RecalledMessage replacement = null;
                if (!incoming.Outgoing)
                {
                    newThread = !threads.TryGetValue(conversationId, out thread);
                    if (newThread) threads.Add(conversationId, thread = new RecallThread
                    {
                        Name = incoming.ConversationName, AvatarPng = IncomingMessageCache.GetAvatar(incoming)
                    });
                    if (!thread.Messages.Any(message => MatchesMessage(message.Id, message.CliMsgId, prior.Id, incoming.CliMsgId)))
                    {
                        replacement = new RecalledMessage
                        {
                            Id = prior.Id, CliMsgId = incoming.CliMsgId ?? prior.CliMsgId, DetectedAt = prior.DetectedAt,
                            Unread = prior.Unread, Content = incoming.Content, Attachment = WithoutMissingLocalMedia(incoming.Attachment),
                            GroupExpires = EarlierExpiry(prior.GroupExpires, incoming.GroupExpires)
                        };
                        thread.Messages.Add(replacement);
                    }
                }
                saveFailed = !SaveRecallArchive();
                if (saveFailed)
                {
                    if (replacement != null) thread.Messages.Remove(replacement);
                    if (newThread) threads.Remove(conversationId);
                    if (removedThread) threads.Add(match.Item1, oldThread);
                    oldThread.Messages.Insert(oldIndex, prior);
                }
                recallRevision++;
            }
            NotifyRecallChanged();
            return recallId;
        }

        private static bool UpdateRecallAttachment(string profileId, string conversationId, string messageId, RecallAttachment attachment)
        {
            bool saved;
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                RecallThread thread;
                if (!recalled.TryGetValue(profileId, out threads) || !threads.TryGetValue(conversationId, out thread)) return true;
                int index = thread.Messages.FindIndex(message => MatchesMessage(message.Id, message.CliMsgId, messageId, null));
                if (index < 0) return true;
                var previous = thread.Messages[index];
                thread.Messages[index] = new RecalledMessage
                {
                    Id = previous.Id, CliMsgId = previous.CliMsgId, DetectedAt = previous.DetectedAt,
                    Unread = previous.Unread, Attachment = WithoutMissingLocalMedia(attachment), Outgoing = previous.Outgoing,
                    Content = previous.Content, Placeholder = previous.Placeholder, GroupExpires = previous.GroupExpires
                };
                saved = SaveRecallArchive();
                if (!saved) thread.Messages[index] = previous;
                recallRevision++;
            }
            NotifyRecallChanged();
            return saved;
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
            bool modified = false;
            foreach (var profile in recalled.Values)
            foreach (var thread in profile.Values)
            foreach (var message in thread.Messages)
            {
                var updated = OffloadInlineMedia(message.Attachment);
                if (updated == message.Attachment) continue;
                message.Attachment = updated;
                modified = true;
            }
            if (modified) SaveRecallArchive();
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
