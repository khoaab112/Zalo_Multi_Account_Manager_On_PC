using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;

namespace ZaloManagerPoc
{
    public static partial class CdpMessageWatcher
    {
        [DataContract]
        internal sealed class CachedIncomingMessage
        {
            [DataMember] public string Id;
            [DataMember(EmitDefaultValue = false)] public string CliMsgId;
            [DataMember] public string ProfileId;
            [DataMember] public string ConversationId;
            [DataMember] public string ConversationName;
            [DataMember(EmitDefaultValue = false)] public string AvatarPng;
            [DataMember] public string Content;
            [DataMember(EmitDefaultValue = false)] public RecallAttachment Attachment;
            [DataMember] public bool Outgoing;
            [DataMember] public DateTimeOffset Timestamp;
        }

        internal static class IncomingMessageCache
        {
            private static readonly object CacheGate = new object();
            private static readonly string CachePath = Path.Combine(ZaloManagerPoc.Store.Root, "incoming-cache.dat");
            private static Dictionary<string, CachedIncomingMessage> cache;
            private static Timer pruneTimer;
            private static bool saveScheduled;

            static IncomingMessageCache()
            {
                EnsureLoaded();
                // Run pruning every 1 hour (3600000 ms)
                pruneTimer = new Timer(delegate { PruneExpired(); }, null, 3600000, 3600000);
            }

            public static void Initialize()
            {
                lock (CacheGate)
                {
                    EnsureLoaded();
                    PruneExpired();
                }
            }

            private static void IndexMessage(CachedIncomingMessage msg)
            {
                if (msg == null || String.IsNullOrEmpty(msg.Id)) return;
                cache[msg.Id] = msg;
                if (!String.IsNullOrEmpty(msg.CliMsgId) && msg.CliMsgId != msg.Id)
                    cache[msg.CliMsgId] = msg;
                int idx1 = msg.Id.IndexOf('_');
                if (idx1 > 0) cache[msg.Id.Substring(0, idx1)] = msg;
                if (!String.IsNullOrEmpty(msg.CliMsgId))
                {
                    int idx2 = msg.CliMsgId.IndexOf('_');
                    if (idx2 > 0) cache[msg.CliMsgId.Substring(0, idx2)] = msg;
                }
            }

            private static void EnsureLoaded()
            {
                if (cache != null) return;
                try
                {
                    byte[] encrypted;
                    try { encrypted = File.ReadAllBytes(CachePath); }
                    catch (FileNotFoundException) { encrypted = null; }
                    catch (DirectoryNotFoundException) { encrypted = null; }
                    if (encrypted != null)
                    {
                        byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                        using (var stream = new MemoryStream(plain, false))
                        {
                            try
                            {
                                var listSerializer = new DataContractJsonSerializer(typeof(List<CachedIncomingMessage>));
                                var list = (List<CachedIncomingMessage>)listSerializer.ReadObject(stream);
                                cache = new Dictionary<string, CachedIncomingMessage>(StringComparer.Ordinal);
                                foreach (var m in list) IndexMessage(m);
                            }
                            catch
                            {
                                stream.Position = 0;
                                var dictSerializer = new DataContractJsonSerializer(typeof(Dictionary<string, CachedIncomingMessage>));
                                cache = (Dictionary<string, CachedIncomingMessage>)dictSerializer.ReadObject(stream);
                            }
                        }
                        if (cache != null && !CachePath.EndsWith("archive.dat", StringComparison.OrdinalIgnoreCase) && !CachePath.Contains("fixture"))
                        {
                            MigrateLargeMedia();
                        }
                    }
                }
                catch { }
                if (cache == null) cache = new Dictionary<string, CachedIncomingMessage>(StringComparer.Ordinal);
            }

            private static void MigrateLargeMedia()
            {
                if (cache == null) return;
                bool modified = false;
                try
                {
                    Directory.CreateDirectory(RecallMediaDir);
                    foreach (var m in cache.Values)
                    {
                        var att = m.Attachment;
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
                                else if (att.Kind == "file" && !String.IsNullOrEmpty(m.Content))
                                {
                                    string e = Path.GetExtension(m.Content);
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
                catch { }
                if (modified)
                {
                    Save();
                }
            }

            private static void ScheduleSave()
            {
                if (saveScheduled) return;
                saveScheduled = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(500);
                    lock (CacheGate)
                    {
                        saveScheduled = false;
                        Save();
                    }
                });
            }

            public static void Flush()
            {
                lock (CacheGate)
                {
                    saveScheduled = false;
                    Save();
                }
            }

            private static void Save()
            {
                var temporary = CachePath + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                    var distinctMessages = cache.Values.Distinct().ToList();
                    using (var stream = new MemoryStream())
                    {
                        var serializer = new DataContractJsonSerializer(typeof(List<CachedIncomingMessage>));
                        serializer.WriteObject(stream, distinctMessages);
                        byte[] encrypted = ProtectedData.Protect(stream.ToArray(), null, DataProtectionScope.CurrentUser);
                        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            output.Write(encrypted, 0, encrypted.Length);
                            output.Flush(true);
                        }
                    }
                    if (File.Exists(CachePath)) File.Replace(temporary, CachePath, null);
                    else File.Move(temporary, CachePath);
                }
                catch
                {
                    try { File.Delete(temporary); } catch { }
                }
            }

            private static RecallAttachment SanitizeAttachment(RecallAttachment att, string content)
            {
                if (att == null) return null;
                string data = att.Data;
                string preview = att.PreviewData;
                string previewMime = att.PreviewMime;

                if (!String.IsNullOrEmpty(preview) && !preview.StartsWith("local:", StringComparison.OrdinalIgnoreCase) && preview.Length > 20000)
                {
                    preview = null;
                    previewMime = null;
                }

                if (!String.IsNullOrEmpty(data) && !data.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
                    && (data.Length > 20000 || att.Kind == "video" || att.Kind == "audio" || att.Kind == "file"))
                {
                    try
                    {
                        Directory.CreateDirectory(RecallMediaDir);
                        string ext = ".bin";
                        if (att.Kind == "video") ext = ".mp4";
                        else if (att.Kind == "audio") ext = (att.Mime ?? "").Contains("wav") ? ".wav" : ".m4a";
                        else if (att.Kind == "image") ext = (att.Mime ?? "").Contains("png") ? ".png" : ".jpg";
                        else if (att.Kind == "file" && !String.IsNullOrEmpty(content))
                        {
                            string e = Path.GetExtension(content);
                            if (!String.IsNullOrEmpty(e) && e.Length <= 8) ext = e;
                        }
                        string filePath = Path.Combine(RecallMediaDir, Guid.NewGuid().ToString("N") + ext);
                        File.WriteAllBytes(filePath, Convert.FromBase64String(data));
                        data = "local:" + filePath;
                    }
                    catch { }
                }

                return new RecallAttachment
                {
                    Kind = att.Kind,
                    Mime = att.Mime,
                    CaptureStatus = att.CaptureStatus,
                    Origin = att.Origin,
                    FromSource = att.FromSource,
                    FileName = att.FileName,
                    PreviewData = preview,
                    PreviewMime = previewMime,
                    SourceUrl = att.SourceUrl,
                    FileSize = att.FileSize,
                    Checksum = att.Checksum,
                    Original = att.Original,
                    Data = data
                };
            }

            public static void Store(CachedIncomingMessage msg)
            {
                if (msg == null || String.IsNullOrEmpty(msg.Id)) return;
                var copy = new CachedIncomingMessage
                {
                    Id = msg.Id,
                    CliMsgId = msg.CliMsgId,
                    ProfileId = msg.ProfileId,
                    ConversationId = msg.ConversationId,
                    ConversationName = msg.ConversationName,
                    AvatarPng = msg.AvatarPng,
                    Content = msg.Content,
                    Outgoing = msg.Outgoing,
                    Timestamp = msg.Timestamp,
                    Attachment = SanitizeAttachment(msg.Attachment, msg.Content)
                };
                lock (CacheGate)
                {
                    EnsureLoaded();
                    IndexMessage(copy);
                    ScheduleSave();
                }
            }

            public static bool TryGet(string msgId, out CachedIncomingMessage msg)
            {
                if (String.IsNullOrEmpty(msgId)) { msg = null; return false; }
                lock (CacheGate)
                {
                    EnsureLoaded();
                    if (cache.TryGetValue(msgId, out msg)) return true;
                    int idx = msgId.IndexOf('_');
                    if (idx > 0 && cache.TryGetValue(msgId.Substring(0, idx), out msg)) return true;
                    return false;
                }
            }

            public static void Remove(string msgId)
            {
                if (String.IsNullOrEmpty(msgId)) return;
                lock (CacheGate)
                {
                    EnsureLoaded();
                    CachedIncomingMessage msg;
                    bool removed = cache.TryGetValue(msgId, out msg);
                    if (removed && msg != null)
                    {
                        cache.Remove(msg.Id);
                        if (!String.IsNullOrEmpty(msg.CliMsgId)) cache.Remove(msg.CliMsgId);
                        int idx1 = msg.Id.IndexOf('_');
                        if (idx1 > 0) cache.Remove(msg.Id.Substring(0, idx1));
                        if (!String.IsNullOrEmpty(msg.CliMsgId))
                        {
                            int idx2 = msg.CliMsgId.IndexOf('_');
                            if (idx2 > 0) cache.Remove(msg.CliMsgId.Substring(0, idx2));
                        }
                    }
                    else
                    {
                        removed = cache.Remove(msgId);
                    }
                    if (removed) ScheduleSave();
                }
            }

            public static void UpdateAttachment(string msgId, RecallAttachment attachment)
            {
                if (String.IsNullOrEmpty(msgId)) return;
                lock (CacheGate)
                {
                    EnsureLoaded();
                    CachedIncomingMessage msg;
                    if (cache.TryGetValue(msgId, out msg))
                    {
                        msg.Attachment = SanitizeAttachment(attachment, msg.Content);
                        ScheduleSave();
                    }
                }
            }

            public static void UpdateAvatar(string msgId, string avatarPng)
            {
                if (String.IsNullOrEmpty(msgId) || String.IsNullOrEmpty(avatarPng)) return;
                lock (CacheGate)
                {
                    EnsureLoaded();
                    CachedIncomingMessage msg;
                    if (cache.TryGetValue(msgId, out msg))
                    {
                        msg.AvatarPng = avatarPng;
                        ScheduleSave();
                    }
                }
            }

            public static void PruneExpired()
            {
                lock (CacheGate)
                {
                    EnsureLoaded();
                    var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
                    var toRemove = cache.Where(kvp => kvp.Value.Timestamp < cutoff).Select(kvp => kvp.Key).ToList();
                    if (toRemove.Count > 0)
                    {
                        foreach (var key in toRemove) cache.Remove(key);
                        ScheduleSave();
                    }
                }
            }
        }
    }
}
