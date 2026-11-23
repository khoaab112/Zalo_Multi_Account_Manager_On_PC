using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace ZaloManagerPoc
{
    public static partial class CdpMessageWatcher
    {
        [DataContract]
        public sealed class RecalledMessage
        {
            [DataMember] internal string Id;
            [DataMember] public DateTimeOffset DetectedAt { get; internal set; }
            [DataMember] public string Content { get; internal set; }
            [DataMember] internal bool Unread = true;
            [DataMember(EmitDefaultValue = false)] internal RecallAttachment Attachment;
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

        internal static int RecallRevision
        {
            get { lock (RecallGate) return recallRevision; }
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
                return threads.Where(pair => pair.Value.Messages.Count > 0).Select(pair => new RecallConversation
                {
                    Id = pair.Key,
                    Name = pair.Value.Name,
                    AvatarPng = pair.Value.AvatarPng,
                    Unread = pair.Value.Messages.Count(message => message.Unread),
                    Latest = pair.Value.Messages[pair.Value.Messages.Count - 1].DetectedAt
                }).OrderByDescending(item => item.Latest).ToArray();
            }
        }

        internal static RecalledMessage[] ReadRecallConversation(string profileId, string conversationId)
        {
            RecalledMessage[] messages;
            bool changed;
            lock (RecallGate)
            {
                var thread = FindRecallThread(profileId, conversationId);
                if (thread == null) return new RecalledMessage[0];
                messages = thread.Messages.ToArray();
                changed = messages.Any(message => message.Unread);
                if (changed)
                {
                    foreach (var message in messages) message.Unread = false;
                    SaveRecallArchive();
                    recallRevision++;
                }
            }
            if (changed) NotifyRecallChanged();
            return messages;
        }

        internal static bool DeleteAllRecalls(string profileId)
        {
            bool saved;
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profileId, out threads)) return true;
                recalled.Remove(profileId);
                saved = SaveRecallArchive();
                if (!saved) recalled.Add(profileId, threads);
                recallRevision++;
            }
            NotifyRecallChanged();
            return saved;
        }

        internal static bool DeleteRecallConversation(string profileId, string conversationId)
        {
            bool saved;
            lock (RecallGate)
            {
                var thread = FindRecallThread(profileId, conversationId);
                if (thread == null) return true;
                var threads = recalled[profileId];
                threads.Remove(conversationId);
                saved = SaveRecallArchive();
                if (!saved) threads.Add(conversationId, thread);
                recallRevision++;
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

        private static void RecordRecall(Profile profile, string conversationId, string conversationName, string avatarPng, string messageId, string content, RecallAttachment attachment)
        {
            lock (RecallGate)
            {
                EnsureRecallLoaded();
                Dictionary<string, RecallThread> threads;
                if (!recalled.TryGetValue(profile.Id, out threads))
                {
                    threads = new Dictionary<string, RecallThread>();
                    recalled.Add(profile.Id, threads);
                }
                RecallThread thread;
                if (!threads.TryGetValue(conversationId, out thread))
                {
                    thread = new RecallThread();
                    threads.Add(conversationId, thread);
                }
                if (thread.Messages.Any(message => message.Id == messageId)) return;
                thread.Name = conversationName;
                if (!String.IsNullOrEmpty(avatarPng)) thread.AvatarPng = avatarPng;
                thread.Messages.Add(new RecalledMessage { Id = messageId, DetectedAt = DateTimeOffset.Now, Content = content, Attachment = attachment });
                SaveRecallArchive();
                recallRevision++;
            }
            NotifyRecallChanged();
        }

        private static void UpdateRecallAttachment(string profileId, string conversationId, string messageId, RecallAttachment attachment)
        {
            lock (RecallGate)
            {
                var thread = FindRecallThread(profileId, conversationId);
                if (thread == null) return;
                int index = thread.Messages.FindIndex(message => message.Id == messageId);
                if (index < 0) return;
                var messageToUpdate = thread.Messages[index];
                thread.Messages[index] = new RecalledMessage
                {
                    Id = messageToUpdate.Id, Content = messageToUpdate.Content, DetectedAt = messageToUpdate.DetectedAt,
                    Unread = messageToUpdate.Unread, Attachment = attachment
                };
                SaveRecallArchive();
                recallRevision++;
            }
            NotifyRecallChanged();
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
