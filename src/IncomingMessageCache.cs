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
            [DataMember(EmitDefaultValue = false)] public string PendingRecall;
            [DataMember(EmitDefaultValue = false)] public bool PendingPlaceholder;
            [DataMember(EmitDefaultValue = false)] public bool ProvisionalConversation;
            [DataMember(EmitDefaultValue = false)] public DateTimeOffset? GroupExpires;
        }

        private static string ConversationScope(string id)
        {
            const string prefix = "conversation:";
            if (String.IsNullOrWhiteSpace(id) || id.Length > 200 || id == "0" || id.StartsWith("message:", StringComparison.Ordinal))
                return "";
            return id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : id;
        }

        private static bool MatchesMessage(string messageId, string messageAlias, string id, string alias)
        {
            return (!String.IsNullOrEmpty(id) && (messageId == id || messageAlias == id)) ||
                   (!String.IsNullOrEmpty(alias) && (messageId == alias || messageAlias == alias));
        }

        private static readonly TimeSpan GroupRetention = TimeSpan.FromHours(5);

        private static DateTimeOffset? EarlierExpiry(DateTimeOffset? first, DateTimeOffset? second)
        {
            return !first.HasValue || (second.HasValue && second.Value < first.Value) ? second : first;
        }

        internal static class IncomingMessageCache
        {
            [DataContract]
            private sealed class CacheSnapshot
            {
                [DataMember] public List<CachedIncomingMessage> Messages;
                [DataMember] public List<ConversationAvatar> Avatars;
                [DataMember(EmitDefaultValue = false)] public List<string> PendingMediaCleanup;
            }

            [DataContract]
            private sealed class ConversationAvatar
            {
                [DataMember] public string Profile;
                [DataMember] public string Conversation;
                [DataMember] public string Png;
                [DataMember] public DateTimeOffset Updated;
            }

            private static readonly object CacheGate = new object();
            private static readonly string CachePath = Path.Combine(ZaloManagerPoc.Store.Root, "incoming-cache.dat");
            private static Dictionary<Tuple<string, string, string>, CachedIncomingMessage> cache;
            private static readonly Dictionary<Tuple<string, string>, HashSet<Tuple<string, string, string>>> MessageScopes =
                new Dictionary<Tuple<string, string>, HashSet<Tuple<string, string, string>>>();
            private static bool loadFailed;
            private static bool dirty;
            private static bool keepLegacyBackup;
            private static HashSet<string> committedMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private static long cachedBytes;
            private static long avatarBytes;
            private static readonly Dictionary<Tuple<string, string>, ConversationAvatar> Avatars =
                new Dictionary<Tuple<string, string>, ConversationAvatar>();
            private static readonly Dictionary<string, long> EarlyEvictions = new Dictionary<string, long>();
            private static readonly Dictionary<string, long> ExpiredEvictions = new Dictionary<string, long>();
            private const int MaximumMessages = 20000;
            private const long MaximumMetadataBytes = 64L * 1024 * 1024;
            private static Timer pruneTimer;
            private static bool saveScheduled;
            private static int retentionRunning;
            private static DateTimeOffset nextDirectPrune;
            private static readonly HashSet<string> PendingMediaCleanup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static IncomingMessageCache()
            {
                pruneTimer = new Timer(delegate { RunRetention(); }, null, 60000, 60000);
            }

            private static void RunRetention()
            {
                if (Interlocked.Exchange(ref retentionRunning, 1) != 0) return;
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    bool full = now >= nextDirectPrune;
                    PruneExpired(now, full);
                    var expiredRecalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    PruneGroupRecalls(now, expiredRecalls);
                    QueueMediaCleanup(expiredRecalls);
                    HashSet<string> candidates;
                    lock (CacheGate) candidates = new HashSet<string>(PendingMediaCleanup, StringComparer.OrdinalIgnoreCase);
                    DeleteUnreferencedMedia(candidates);
                    if (full)
                    {
                        nextDirectPrune = now.AddHours(1);
                        ScheduleRecallMediaScan();
                    }
                }
                catch (Exception error) { ZaloManagerPoc.Store.Log(error); }
                finally { Volatile.Write(ref retentionRunning, 0); }
            }

            public static void Initialize()
            {
                RunRetention();
            }

            private static Tuple<string, string, string> Key(CachedIncomingMessage message)
            {
                return Tuple.Create(message.ProfileId ?? "", ConversationScope(message.ConversationId), message.Id);
            }

            private static void IndexMessage(CachedIncomingMessage message)
            {
                if (message == null || String.IsNullOrEmpty(message.Id)) return;
                var key = Key(message);
                CachedIncomingMessage previous;
                if (cache.TryGetValue(key, out previous)) cachedBytes -= EstimateBytes(previous);
                cache[key] = message;
                var identity = Tuple.Create(key.Item1, key.Item3);
                HashSet<Tuple<string, string, string>> scopes;
                if (!MessageScopes.TryGetValue(identity, out scopes))
                    MessageScopes.Add(identity, scopes = new HashSet<Tuple<string, string, string>>());
                scopes.Add(key);
                cachedBytes += EstimateBytes(message);
            }

            private static long EstimateBytes(CachedIncomingMessage message)
            {
                long chars = Length(message.Id) + Length(message.CliMsgId) + Length(message.ProfileId) +
                    Length(message.ConversationId) + Length(message.ConversationName) + Length(message.AvatarPng) +
                    Length(message.Content);
                var media = message.Attachment;
                if (media != null)
                    chars += Length(media.Data) + Length(media.PreviewData) + Length(media.SourceUrl) + Length(media.FileName);
                return 512 + chars * sizeof(char);
            }

            private static int Length(string value)
            {
                return value == null ? 0 : value.Length;
            }

            private static void PutAvatar(string profile, string conversation, string png, DateTimeOffset updated)
            {
                if (String.IsNullOrEmpty(png) || png.Length > 30000 || String.IsNullOrEmpty(conversation)) return;
                var key = Tuple.Create(profile ?? "", conversation);
                ConversationAvatar old;
                if (Avatars.TryGetValue(key, out old))
                {
                    if (old.Updated > updated) return;
                    if (old.Png == png) { old.Updated = updated; return; }
                    avatarBytes -= old.Png.Length * sizeof(char);
                }
                Avatars[key] = new ConversationAvatar { Profile = key.Item1, Conversation = conversation, Png = png, Updated = updated };
                avatarBytes += png.Length * sizeof(char);
                while (avatarBytes > MaximumMetadataBytes / 8 || Avatars.Count > 4096)
                {
                    var oldest = Avatars.OrderBy(pair => pair.Value.Updated).First();
                    avatarBytes -= oldest.Value.Png.Length * sizeof(char);
                    Avatars.Remove(oldest.Key);
                }
            }

            public static string GetAvatar(CachedIncomingMessage message)
            {
                lock (CacheGate)
                {
                    ConversationAvatar avatar;
                    return message.AvatarPng ?? (Avatars.TryGetValue(Tuple.Create(message.ProfileId ?? "",
                        ConversationScope(message.ConversationId)), out avatar) ? avatar.Png : null);
                }
            }

            private static void Unindex(Tuple<string, string, string> key)
            {
                cachedBytes -= EstimateBytes(cache[key]);
                cache.Remove(key);
                var identity = Tuple.Create(key.Item1, key.Item3);
                var scopes = MessageScopes[identity];
                scopes.Remove(key);
                if (scopes.Count == 0) MessageScopes.Remove(identity);
            }

            private static void PruneBounds()
            {
                if (cache.Count <= MaximumMessages && cachedBytes + avatarBytes <= MaximumMetadataBytes) return;
                var groups = cache.GroupBy(pair => pair.Key.Item1)
                    .Select(group => new
                    {
                        Profile = group.Key,
                        Count = group.Count(),
                        Bytes = group.Sum(pair => EstimateBytes(pair.Value)),
                        Evictable = new Queue<KeyValuePair<Tuple<string, string, string>, CachedIncomingMessage>>(
                            group.Where(pair => pair.Value.PendingRecall == null).OrderBy(pair => pair.Value.Timestamp))
                    }).ToArray();
                int fairCount = MaximumMessages / groups.Length;
                long fairBytes = (MaximumMetadataBytes - avatarBytes) / groups.Length;
                var removed = groups.ToDictionary(group => group.Profile, group => Tuple.Create(0, 0L));
                while (cache.Count > MaximumMessages || cachedBytes + avatarBytes > MaximumMetadataBytes)
                {
                    bool countPressure = cache.Count > MaximumMessages;
                    bool bytePressure = cachedBytes + avatarBytes > MaximumMetadataBytes;
                    var owner = groups.Where(group => group.Evictable.Count != 0)
                        .OrderByDescending(group => Math.Max(
                            countPressure ? (group.Count - removed[group.Profile].Item1) / (double)Math.Max(1, fairCount) : 0,
                            bytePressure ? (group.Bytes - removed[group.Profile].Item2) / (double)Math.Max(1L, fairBytes) : 0))
                        .ThenBy(group => group.Evictable.Peek().Value.Timestamp).FirstOrDefault();
                    if (owner == null) break;
                    var item = owner.Evictable.Dequeue();
                    var counts = removed[owner.Profile];
                    removed[owner.Profile] = Tuple.Create(counts.Item1 + 1, counts.Item2 + EstimateBytes(item.Value));
                    AddMediaReferences(PendingMediaCleanup, item.Value.Attachment);
                    Unindex(item.Key);
                    CountEviction(item.Value.Timestamp < DateTimeOffset.UtcNow.AddHours(-24) ? ExpiredEvictions : EarlyEvictions,
                        owner.Profile);
                }
            }

            internal static long GetEarlyEvictions(string profile)
            {
                lock (CacheGate)
                {
                    long count;
                    return EarlyEvictions.TryGetValue(profile, out count) ? count : 0;
                }
            }

            internal static long GetExpiredEvictions(string profile)
            {
                lock (CacheGate)
                {
                    long count;
                    return ExpiredEvictions.TryGetValue(profile, out count) ? count : 0;
                }
            }

            private static void CountEviction(Dictionary<string, long> counts, string profile)
            {
                long count;
                counts.TryGetValue(profile, out count);
                counts[profile] = count + 1;
            }

            private static void EnsureLoaded()
            {
                if (cache != null) return;
                cache = new Dictionary<Tuple<string, string, string>, CachedIncomingMessage>();
                try
                {
                    byte[] encrypted;
                    try { encrypted = File.ReadAllBytes(CachePath); }
                    catch (FileNotFoundException) { return; }
                    catch (DirectoryNotFoundException) { return; }
                    byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    using (var stream = new MemoryStream(plain, false))
                    {
                        IEnumerable<CachedIncomingMessage> messages;
                        bool oldFormat;
                        var snapshotSerializer = new DataContractJsonSerializer(typeof(CacheSnapshot));
                        var snapshot = snapshotSerializer.ReadObject(stream) as CacheSnapshot;
                        if (snapshot != null && snapshot.Messages != null)
                        {
                            messages = snapshot.Messages;
                            oldFormat = false;
                            foreach (var avatar in snapshot.Avatars ?? new List<ConversationAvatar>())
                                PutAvatar(avatar.Profile, avatar.Conversation, avatar.Png, avatar.Updated);
                            if (snapshot.PendingMediaCleanup != null) PendingMediaCleanup.UnionWith(snapshot.PendingMediaCleanup);
                        }
                        else
                        {
                            stream.Position = 0;
                            oldFormat = true;
                            try
                            {
                                var serializer = new DataContractJsonSerializer(typeof(List<CachedIncomingMessage>));
                                messages = (List<CachedIncomingMessage>)serializer.ReadObject(stream);
                                if (messages == null || messages.Any(message => message == null || String.IsNullOrEmpty(message.Id)))
                                    throw new SerializationException("Invalid cache list.");
                            }
                            catch (SerializationException)
                            {
                                stream.Position = 0;
                                var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, CachedIncomingMessage>));
                                messages = ((Dictionary<string, CachedIncomingMessage>)serializer.ReadObject(stream)).Values;
                            }
                        }
                        foreach (var message in messages.OrderBy(item => item.Timestamp))
                        {
                            var scope = ConversationScope(message.ConversationId);
                            PutAvatar(message.ProfileId, scope, message.AvatarPng, message.Timestamp);
                            if (scope.Length != 0) message.AvatarPng = null;
                            IndexMessage(message);
                            AddMediaReferences(committedMedia, message.Attachment);
                        }
                        if (oldFormat)
                        {
                            keepLegacyBackup = true;
                            ScheduleSave();
                        }
                    }
                    if (!CachePath.EndsWith("archive.dat", StringComparison.OrdinalIgnoreCase) && !CachePath.Contains("fixture"))
                        MigrateLargeMedia();
                    cachedBytes = cache.Values.Sum(message => EstimateBytes(message));
                    int before = cache.Count;
                    PruneBounds();
                    if (cache.Count != before) ScheduleSave();
                }
                catch (Exception error)
                {
                    if (!IsRecallStorageError(error) && !(error is InvalidCastException) && !(error is System.Xml.XmlException)) throw;
                    loadFailed = true;
                    System.Diagnostics.Debug.WriteLine(error.GetType().Name);
                }
            }

            private static void MigrateLargeMedia()
            {
                foreach (var message in cache.Values)
                {
                    var updated = OffloadInlineMedia(message.Attachment);
                    if (updated == message.Attachment) continue;
                    message.Attachment = updated;
                    dirty = true;
                }
                if (dirty) Save();
            }

            private static void ScheduleSave()
            {
                dirty = true;
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

            public static bool Flush()
            {
                lock (CacheGate) return Save();
            }

            private static bool Save()
            {
                if (loadFailed) return false;
                if (!dirty) return true;
                var temporary = CachePath + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                    var snapshot = new CacheSnapshot
                    {
                        Messages = cache.Values.ToList(), Avatars = Avatars.Values.ToList(),
                        PendingMediaCleanup = PendingMediaCleanup.Count == 0 ? null : PendingMediaCleanup.ToList()
                    };
                    var media = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var message in snapshot.Messages) AddMediaReferences(media, message.Attachment);
                    using (var stream = new MemoryStream())
                    {
                        var serializer = new DataContractJsonSerializer(typeof(CacheSnapshot));
                        serializer.WriteObject(stream, snapshot);
                        byte[] encrypted = ProtectedData.Protect(stream.ToArray(), null, DataProtectionScope.CurrentUser);
                        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            output.Write(encrypted, 0, encrypted.Length);
                            output.Flush(true);
                        }
                    }
                    var backup = CachePath + ".v1.bak";
                    if (File.Exists(CachePath))
                        File.Replace(temporary, CachePath, keepLegacyBackup && !File.Exists(backup) ? backup : null);
                    else File.Move(temporary, CachePath);
                    keepLegacyBackup = false;
                    committedMedia = media;
                    dirty = false;
                    return true;
                }
                catch
                {
                    try { File.Delete(temporary); } catch { }
                    return false;
                }
            }

            public static CachedIncomingMessage Store(CachedIncomingMessage msg)
            {
                bool duplicate;
                return StoreCaptured(msg, out duplicate);
            }

            public static CachedIncomingMessage StoreCaptured(CachedIncomingMessage msg, out bool duplicate)
            {
                duplicate = false;
                if (msg == null || String.IsNullOrEmpty(msg.Id)) return null;
                if (!String.IsNullOrEmpty(msg.ProfileId) && !IsRecallEnabled(msg.ProfileId)) return null;
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
                    PendingRecall = msg.PendingRecall,
                    PendingPlaceholder = msg.PendingPlaceholder,
                    ProvisionalConversation = msg.ProvisionalConversation,
                    GroupExpires = msg.GroupExpires,
                    Attachment = msg.Attachment
                };
                var publishing = new List<string>();
                try
                {
                    copy.Attachment = OffloadInlineMedia(copy.Attachment, publishing);
                    lock (CacheGate)
                    {
                        EnsureLoaded();
                        CachedIncomingMessage existing;
                        var key = Key(copy);
                        PutAvatar(copy.ProfileId, key.Item2, copy.AvatarPng, copy.Timestamp);
                        if (key.Item2.Length != 0) copy.AvatarPng = null;
                        if (cache.TryGetValue(key, out existing) && !existing.PendingPlaceholder)
                        {
                            duplicate = true;
                            var expiry = EarlierExpiry(existing.GroupExpires, copy.GroupExpires);
                            if (expiry != existing.GroupExpires)
                            {
                                existing.GroupExpires = expiry;
                                ScheduleSave();
                            }
                            if (existing.ProvisionalConversation && !copy.ProvisionalConversation)
                            {
                                existing.ProvisionalConversation = false;
                                existing.ConversationName = copy.ConversationName;
                                ScheduleSave();
                            }
                            return existing;
                        }
                        if (existing == null)
                        {
                            HashSet<Tuple<string, string, string>> scopes;
                            var candidates = !MessageScopes.TryGetValue(Tuple.Create(key.Item1, key.Item3), out scopes) ? null :
                                scopes.Select(scope => new KeyValuePair<Tuple<string, string, string>, CachedIncomingMessage>(scope, cache[scope]))
                                .Where(pair =>
                                pair.Value.ProvisionalConversation != copy.ProvisionalConversation &&
                                pair.Value.Outgoing == copy.Outgoing && !pair.Value.PendingPlaceholder &&
                                (String.IsNullOrEmpty(pair.Value.CliMsgId) || String.IsNullOrEmpty(copy.CliMsgId) ||
                                 pair.Value.CliMsgId == copy.CliMsgId)).Take(2).ToArray();
                            if (candidates != null && candidates.Length == 1)
                            {
                                existing = candidates[0].Value;
                                duplicate = true;
                                if (copy.ProvisionalConversation) return existing;
                                Unindex(candidates[0].Key);
                                existing.ConversationId = copy.ConversationId;
                                existing.ConversationName = copy.ConversationName;
                                existing.ProvisionalConversation = false;
                                existing.GroupExpires = EarlierExpiry(existing.GroupExpires, copy.GroupExpires);
                                if (!String.IsNullOrEmpty(copy.CliMsgId)) existing.CliMsgId = copy.CliMsgId;
                                IndexMessage(existing);
                                PruneBounds();
                                ScheduleSave();
                                return cache.ContainsKey(key) ? existing : null;
                            }
                        }
                        if (cache.TryGetValue(Key(copy), out existing) && copy.PendingRecall == null)
                            copy.PendingRecall = existing.PendingRecall;
                        IndexMessage(copy);
                        PruneBounds();
                        ScheduleSave();
                        return cache.ContainsKey(Key(copy)) ? copy : null;
                    }
                }
                finally { ReleasePublishingMedia(publishing); }

            }

            public static bool TryGet(string profileId, string conversationId, string id, string cliId, out CachedIncomingMessage message)
            {
                lock (CacheGate)
                {
                    EnsureLoaded();
                    var scope = ConversationScope(conversationId);
                    if (scope.Length != 0 && !String.IsNullOrEmpty(id) &&
                        cache.TryGetValue(Tuple.Create(profileId, scope, id), out message)) return !IsExpiredGroup(message);
                    HashSet<Tuple<string, string, string>> exact;
                    if (scope.Length == 0 && !String.IsNullOrEmpty(id) &&
                        MessageScopes.TryGetValue(Tuple.Create(profileId, id), out exact))
                    {
                        message = exact.Count == 1 ? cache[exact.First()] : null;
                        return message != null && !IsExpiredGroup(message);
                    }
                    var matches = cache.Values.Where(candidate => candidate.ProfileId == profileId &&
                        (scope.Length == 0 || scope == ConversationScope(candidate.ConversationId)) &&
                        !IsExpiredGroup(candidate) && MatchesMessage(candidate.Id, candidate.CliMsgId, id, cliId)).Take(2).ToArray();
                    message = matches.Length == 1 ? matches[0] : null;
                    return message != null;
                }
            }

            private static bool IsExpiredGroup(CachedIncomingMessage message)
            {
                return message.GroupExpires.HasValue && message.GroupExpires.Value <= DateTimeOffset.UtcNow;
            }

            public static void Remove(CachedIncomingMessage message)
            {
                if (message == null) return;
                lock (CacheGate)
                {
                    CachedIncomingMessage current;
                    if (cache.TryGetValue(Key(message), out current) && ReferenceEquals(current, message))
                    {
                        Unindex(Key(message));
                        ScheduleSave();
                    }
                }
            }

            public static void MarkPending(CachedIncomingMessage message, string recallId)
            {
                if (message == null) return;
                lock (CacheGate)
                {
                    if (message.PendingRecall == recallId) return;
                    message.PendingRecall = recallId;
                    ScheduleSave();
                }
            }

            public static CachedIncomingMessage[] Pending(string profileId)
            {
                lock (CacheGate)
                {
                    EnsureLoaded();
                    return cache.Values.Where(message => message.ProfileId == profileId && message.PendingRecall != null).ToArray();
                }
            }

            public static void UpdateAttachment(CachedIncomingMessage message, RecallAttachment attachment)
            {
                if (message == null) return;
                lock (CacheGate)
                {
                    CachedIncomingMessage current;
                    if (!cache.TryGetValue(Key(message), out current) || !ReferenceEquals(current, message)) return;
                    cachedBytes -= EstimateBytes(message);
                    message.Attachment = attachment;
                    cachedBytes += EstimateBytes(message);
                    PruneBounds();
                    ScheduleSave();
                }
            }

            public static void UpdateAvatar(CachedIncomingMessage message, string avatarPng)
            {
                if (message == null || String.IsNullOrEmpty(avatarPng)) return;
                lock (CacheGate)
                {
                    CachedIncomingMessage current;
                    if (!cache.TryGetValue(Key(message), out current) || !ReferenceEquals(current, message)) return;
                    cachedBytes -= EstimateBytes(message);
                    var scope = ConversationScope(message.ConversationId);
                    if (scope.Length == 0) message.AvatarPng = avatarPng;
                    else PutAvatar(message.ProfileId, scope, avatarPng, DateTimeOffset.UtcNow);
                    cachedBytes += EstimateBytes(message);
                    PruneBounds();
                    ScheduleSave();
                }
            }

            internal static bool WithMediaReferences(HashSet<string> references, Action<Func<string, bool>> evaluate = null)
            {
                lock (CacheGate)
                {
                    EnsureLoaded();
                    if (loadFailed) return false;
                    foreach (var message in cache.Values) AddMediaReferences(references, message.Attachment);
                    if (evaluate == null) references.UnionWith(committedMedia);
                    else evaluate(committedMedia.Contains);
                    return true;
                }
            }

            internal static void QueueMediaCleanup(HashSet<string> paths)
            {
                if (paths.Count == 0) return;
                lock (CacheGate)
                {
                    EnsureLoaded();
                    int before = PendingMediaCleanup.Count;
                    PendingMediaCleanup.UnionWith(paths);
                    if (PendingMediaCleanup.Count != before) ScheduleSave();
                }
            }

            internal static void CompleteMediaCleanup(string path)
            {
                lock (CacheGate)
                    if (PendingMediaCleanup.Remove(path)) ScheduleSave();
            }

            public static void PruneExpired()
            {
                PruneExpired(DateTimeOffset.UtcNow, true);
            }

            private static void PruneExpired(DateTimeOffset now, bool pruneDirect)
            {
                lock (CacheGate)
                {
                    EnsureLoaded();
                    var cutoff = now.AddHours(-24);
                    var toRemove = cache.Where(pair => pair.Value.GroupExpires.HasValue ? pair.Value.GroupExpires.Value <= now :
                        pruneDirect && pair.Value.Timestamp < cutoff && pair.Value.PendingRecall == null).Select(pair => pair.Key).ToList();
                    if (toRemove.Count > 0)
                    {
                        var expiredGroups = new HashSet<Tuple<string, string>>();
                        foreach (var key in toRemove)
                        {
                            var message = cache[key];
                            if (message.GroupExpires.HasValue)
                                expiredGroups.Add(Tuple.Create(key.Item1, key.Item2));
                            AddMediaReferences(PendingMediaCleanup, message.Attachment);
                            Unindex(key);
                            CountEviction(ExpiredEvictions, key.Item1);
                        }
                        if (expiredGroups.Count != 0)
                        {
                            expiredGroups.ExceptWith(cache.Keys.Select(key => Tuple.Create(key.Item1, key.Item2)));
                            foreach (var key in expiredGroups)
                            {
                                ConversationAvatar avatar;
                                if (!Avatars.TryGetValue(key, out avatar)) continue;
                                avatarBytes -= avatar.Png.Length * sizeof(char);
                                Avatars.Remove(key);
                            }
                        }
                        ScheduleSave();
                    }
                }
            }
        }
    }
}
