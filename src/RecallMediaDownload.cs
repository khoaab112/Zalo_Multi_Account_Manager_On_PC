using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace ZaloManagerPoc
{
    public static partial class CdpMessageWatcher
    {
        private const int MaxRecallRedirects = 5;
        private const int MaxRecallFileBytes = 64 * 1024 * 1024;
        private static readonly RecallDownloadSlots DownloadSlots = new RecallDownloadSlots();
        private static int activeDownloadRequests;

        private sealed class RecallDownloadSlots
        {
            private sealed class Request
            {
                public string Profile;
                public bool Small;
                public readonly TaskCompletionSource<bool> Ready =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            private readonly object gate = new object();
            private readonly List<Request> queued = new List<Request>();
            private readonly Dictionary<string, int> profiles = new Dictionary<string, int>();
            private int active, large;

            public async Task WaitAsync(string profile, bool small, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var request = new Request { Profile = profile, Small = small };
                using (token.Register(() =>
                {
                    lock (gate)
                    {
                        if (queued.Remove(request) || !request.Ready.Task.IsCompleted)
                            request.Ready.TrySetCanceled();
                    }
                }))
                {
                    lock (gate)
                    {
                        if (!request.Ready.Task.IsCanceled)
                        {
                            queued.Add(request);
                            Grant();
                        }
                    }
                    await request.Ready.Task.ConfigureAwait(false);
                }
            }

            public void Release(string profile, bool small)
            {
                lock (gate)
                {
                    if (--profiles[profile] == 0) profiles.Remove(profile);
                    active--;
                    if (!small) large--;
                    Grant();
                }
            }

            private int InUse(string profile)
            {
                int count;
                return profiles.TryGetValue(profile, out count) ? count : 0;
            }

            private void Grant()
            {
                while (active < 4)
                {
                    var eligible = queued.Where(request => InUse(request.Profile) < 2 && (request.Small || large < 3));
                    // Preserve a slot for small transfers, while an older large request can
                    // use the other slots before newer small work from the same profile.
                    var item = eligible.OrderBy(request => InUse(request.Profile)).FirstOrDefault();
                    if (item == null) return;
                    queued.Remove(item);
                    profiles[item.Profile] = InUse(item.Profile) + 1;
                    active++;
                    if (!item.Small) large++;
                    item.Ready.TrySetResult(true);
                }
            }
        }

        private sealed partial class VisibleMessages
        {
            private sealed class RecallDownload
            {
                public CapturedMessage Message;
                public RecallAttachment Attachment;
                public string Failure;
            }

            private readonly ConcurrentQueue<RecallDownload> downloads = new ConcurrentQueue<RecallDownload>();
            private int queuedDownloads;

            private void QueueRecallDownload(Profile profile, CapturedMessage message, string url, bool original,
                string fileSize, string checksum, CancellationToken token)
            {
                Uri uri;
                bool webSource = Uri.TryCreate(url, UriKind.Absolute, out uri) && IsRecallMediaUri(uri);
                long size;
                long? expectedSize = Int64.TryParse(fileSize, out size) && size >= 0 ? (long?)size : null;
                if (checksum != null && !Regex.IsMatch(checksum, @"\A(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{64})\z")) checksum = null;
                string kind = message.Attachment.Kind;
                string name = message.Content;
                bool isSticker = kind == "sticker";
                bool small = expectedSize.HasValue && expectedSize.Value > 0 && expectedSize.Value < 2 * 1024 * 1024;
                bool canReuse = expectedSize.HasValue && !String.IsNullOrEmpty(checksum);
                if (token.IsCancellationRequested || message.Downloading || message.Attachment.FromSource ||
                    (!webSource && !canReuse && !isSticker) || Volatile.Read(ref queuedDownloads) >= 32) return;
                if (Interlocked.Increment(ref activeDownloadRequests) > 128)
                {
                    Interlocked.Decrement(ref activeDownloadRequests);
                    Status = "Hàng đợi tải tệp đã đầy";
                    return;
                }

                message.Downloading = true;
                Interlocked.Increment(ref queuedDownloads);
                Task.Run(async delegate
                {
                    RecallAttachment attachment = null;
                    string failure = "incomplete";
                    bool entered = false;
                    try
                    {
                        await DownloadSlots.WaitAsync(profile.Id, small, token);
                        entered = true;
                        token.ThrowIfCancellationRequested();
                        if (expectedSize > MaxRecallFileBytes) throw new RecallMediaUnavailable("too-large");
                        if (isSticker)
                        {
                            attachment = ReadLocalSticker(fileSize, checksum, url, token);
                        }
                        if (attachment == null && canReuse)
                        {
                            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                            attachment = ReadDownloadedRecallFile(folder, name, kind, expectedSize.Value, checksum, token);
                        }
                        if (attachment == null && webSource)
                            attachment = await DownloadRecallMedia(uri, kind, original, expectedSize, checksum, token);
                        if (attachment == null && !webSource) failure = "no-source";
                    }
                    catch (RecallMediaUnavailable error)
                    {
                        failure = error.Message;
                        if (kind == "file" && failure.StartsWith("redirect-", StringComparison.Ordinal))
                        {
                            string diagnostic = "Recall file download: " + failure;
                            if (!String.IsNullOrEmpty(error.RedirectHost)) diagnostic += " (host " + error.RedirectHost + ")";
                            try { Store.Log(new IOException(diagnostic)); }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }
                    }
                    catch (WebException error)
                    {
                        var response = error.Response as HttpWebResponse;
                        if (response != null)
                        {
                            using (response) failure = "http-" + (int)response.StatusCode;
                        }
                        else if (error.Status == WebExceptionStatus.SecureChannelFailure || error.Status == WebExceptionStatus.TrustFailure)
                            failure = "secure-channel";
                        else if (error.Status == WebExceptionStatus.Timeout || error.Status == WebExceptionStatus.RequestCanceled)
                            failure = token.IsCancellationRequested ? "interrupted" : "network-timeout";
                        else failure = "network";
                    }
                    catch (IOException) { failure = "incomplete"; }
                    catch (OperationCanceledException) { failure = "interrupted"; }
                    catch (UnauthorizedAccessException) { failure = "storage"; }
                    finally
                    {
                        var result = new RecallDownload { Message = message, Attachment = attachment, Failure = failure };
                        bool accepted = false;
                        if (!token.IsCancellationRequested)
                        {
                            if (PostDownload != null) accepted = PostDownload(() => downloads.Enqueue(result));
                            else { downloads.Enqueue(result); accepted = true; }
                        }
                        if (!accepted) DeleteStagedMedia(attachment);
                        if (entered) DownloadSlots.Release(profile.Id, small);
                        Interlocked.Decrement(ref queuedDownloads);
                        Interlocked.Decrement(ref activeDownloadRequests);
                    }
                });
            }

            private void ApplyRecallDownloads(Profile profile)
            {
                if (profile == null) return;
                RecallDownload result;
                while (downloads.TryDequeue(out result))
                {
                    var message = result.Message;
                    message.Downloading = false;
                    try
                    {
                        if (!IsRecallEnabled(profile.Id) || message.Attachment.FromSource ||
                            (message.Cached.GroupExpires.HasValue && message.Cached.GroupExpires.Value <= DateTimeOffset.UtcNow)) continue;
                        var attachment = result.Attachment;
                        if (attachment == null)
                        {
                            if (!String.IsNullOrEmpty(message.Attachment.Data)) continue;
                            var unavailable = message.Attachment.Clone();
                            unavailable.CaptureStatus = result.Failure;
                            ApplyAttachment(profile, message, unavailable);
                            continue;
                        }

                        if (String.IsNullOrEmpty(attachment.PreviewData))
                        {
                            attachment.PreviewData = message.Attachment.PreviewData ?? message.Attachment.Data;
                            attachment.PreviewMime = message.Attachment.PreviewMime ?? message.Attachment.Mime;
                        }
                        if (String.IsNullOrEmpty(attachment.FileName)) attachment.FileName = message.Attachment.FileName;
                        var publishing = new List<string>();
                        try
                        {
                            lock (RecallMediaGate)
                            {
                                PublishStagedMedia(attachment);
                                ProtectPublishedMedia(attachment, publishing);
                            }
                            ApplyAttachment(profile, message, attachment);
                        }
                        finally { ReleasePublishingMedia(publishing); }
                    }
                    catch (Exception error)
                    {
                        if (!IsRecallStorageError(error)) throw;
                        Status = "Chưa lưu được tệp thu hồi";
                    }
                    finally { DeleteStagedMedia(result.Attachment); }
                }
            }
        }

        private static bool IsRecallMediaUri(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
            string host = uri.IdnHost;
            return host.Equals("zadn.vn", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".zadn.vn", StringComparison.OrdinalIgnoreCase)
                || host.Equals("zdn.vn", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".zdn.vn", StringComparison.OrdinalIgnoreCase)
                || host.Equals("zaloapp.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".zaloapp.com", StringComparison.OrdinalIgnoreCase)
                || host.Equals("zalo.me", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".zalo.me", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".vng.com.vn", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(host, @"\A(?:file-stal-[0-9]+\.dlfl\.vn|video-stal-[0-9]+\.dlmd\.me)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private sealed class RecallMediaUnavailable : Exception
        {
            public readonly string RedirectHost;
            public RecallMediaUnavailable(string code, string redirectHost = null) : base(code) { RedirectHost = redirectHost; }
        }

        private static Uri RecallRedirectTarget(Uri source, int status, string location, int redirectsFollowed)
        {
            if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308) return null;
            if (redirectsFollowed >= MaxRecallRedirects) throw new RecallMediaUnavailable("redirect-limit");
            if (String.IsNullOrWhiteSpace(location)) throw new RecallMediaUnavailable("redirect-missing-location");
            Uri target;
            if (!Uri.TryCreate(source, location, out target)) throw new RecallMediaUnavailable("redirect-invalid-location");
            if (!IsRecallMediaUri(target) && !IsObservedFileCdnRedirect(source, target))
                throw new RecallMediaUnavailable("redirect-policy", target.HostNameType == UriHostNameType.Dns ? target.IdnHost : null);
            return target;
        }

        private static bool IsObservedFileCdnRedirect(Uri source, Uri target)
        {
            // Only permit the HTTPS hops verified on the live ZIP, including the transition to its regional edge.
            if (source.Scheme != Uri.UriSchemeHttps || !source.IsDefaultPort || source.UserInfo.Length != 0 ||
                target.Scheme != Uri.UriSchemeHttps || !target.IsDefaultPort || target.UserInfo.Length != 0)
                return false;
            string from = source.IdnHost;
            string to = target.IdnHost;
            return (String.Equals(from, "file-stal-17.dlfl.vn", StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(to, "file-stal-17.flchat.vn", StringComparison.OrdinalIgnoreCase)) ||
                   (String.Equals(from, "file-stal-17.flchat.vn", StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(to, "file-stal-17-te-vnno-ne-6.flchat.vn", StringComparison.OrdinalIgnoreCase));
        }

        private static RecallAttachment ReadDownloadedRecallFile(string folder, string name, string kind,
            long expectedSize, string checksum, CancellationToken token)
        {
            if (expectedSize <= 0 || expectedSize > MaxRecallFileBytes) return null;
            if (String.IsNullOrWhiteSpace(name) || name.Length > 255
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".", StringComparison.Ordinal)
                || name.EndsWith(" ", StringComparison.Ordinal)) return null;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var searchFolders = new System.Collections.Generic.List<string>();
            if (!String.IsNullOrEmpty(folder)) searchFolders.Add(folder);
            var defaultDl = Path.Combine(userProfile, "Downloads");
            if (!searchFolders.Contains(defaultDl)) searchFolders.Add(defaultDl);
            var zaloDocs = Path.Combine(userProfile, "Documents", "Zalo Received Files");
            if (!searchFolders.Contains(zaloDocs)) searchFolders.Add(zaloDocs);
            var zaloDl = Path.Combine(userProfile, "Downloads", "Zalo Received Files");
            if (!searchFolders.Contains(zaloDl)) searchFolders.Add(zaloDl);

            foreach (var dir in searchFolders)
            {
                if (!Directory.Exists(dir)) continue;
                var path = Path.Combine(dir, name);
                try
                {
                    if (!File.Exists(path)) continue;
                    if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) continue;
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (input.Length != expectedSize) continue;
                        if (input.Length <= 0 || input.Length > MaxRecallFileBytes) continue;
                        if (String.IsNullOrEmpty(checksum)) continue;
                        var copy = StageMedia(input, kind, true, input.Length, expectedSize, checksum,
                            kind == "video" ? "video/mp4" : null, token).GetAwaiter().GetResult();
                        copy.FileName = name;
                        copy.Origin = "local";
                        return copy;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (RecallMediaUnavailable) { }
            }
            return null;
        }

        private static RecallAttachment ReadLocalSticker(string stickerId, string catId, string url, CancellationToken token)
        {
            string sId = stickerId;
            if (String.IsNullOrEmpty(sId) && !String.IsNullOrEmpty(url))
            {
                var match = Regex.Match(url, @"[?&]eid=([0-9]+)");
                if (match.Success) sId = match.Groups[1].Value;
            }
            if (String.IsNullOrEmpty(sId) || sId.Length > 32) return null;

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var mediaDir = Path.Combine(appData, "ZaloData", "media");
                if (!Directory.Exists(mediaDir)) return null;

                foreach (var accountDir in Directory.GetDirectories(mediaDir))
                {
                    var stickerRoot = Path.Combine(accountDir, "sticker");
                    if (!Directory.Exists(stickerRoot)) continue;
                    foreach (var catDir in Directory.GetDirectories(stickerRoot))
                    {
                        var sDir = Path.Combine(catDir, sId);
                        if (Directory.Exists(sDir))
                        {
                            var files = Directory.GetFiles(sDir, "*.png");
                            if (files.Length > 0)
                            {
                                var source = files[0];
                                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    var sticker = StageMedia(input, "sticker", true, input.Length, null, null, "image/png",
                                        token).GetAwaiter().GetResult();
                                    sticker.FileName = Path.GetFileName(source);
                                    sticker.Origin = "local";
                                    return sticker;
                                }
                            }
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (RecallMediaUnavailable) { }
            return null;
        }

        private static string RecallPictureMime(byte[] bytes)
        {
            if (bytes.Length < 12) return null;
            if (bytes[0] == 137 && Encoding.ASCII.GetString(bytes, 1, 7) == "PNG\r\n\u001a\n") return "image/png";
            if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
            string header = Encoding.ASCII.GetString(bytes, 0, 6);
            if (header == "GIF87a" || header == "GIF89a") return "image/gif";
            if (header.StartsWith("RIFF", StringComparison.Ordinal) && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return "image/webp";
            return null;
        }

        private static string RecallAudioMime(byte[] bytes)
        {
            if (bytes.Length < 12) return null;
            if (Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp") return "audio/mp4";
            if (Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE") return "audio/wav";
            if (Encoding.ASCII.GetString(bytes, 0, 4) == "OggS") return "audio/ogg";
            if (bytes[0] == 0xff && (bytes[1] & 0xf6) == 0xf0) return "audio/aac";
            if (Encoding.ASCII.GetString(bytes, 0, 3) == "ID3" || (bytes[0] == 0xff && (bytes[1] & 0xe6) == 0xe2)) return "audio/mpeg";
            return null;
        }

        private static async Task<RecallAttachment> DownloadRecallMedia(Uri uri, string kind, bool original,
            long? expectedSize, string checksum, CancellationToken token)
        {
            if (kind == "image" && uri.AbsolutePath.Contains("/jxl/"))
            {
                string newUrl = Regex.Replace(uri.AbsoluteUri, @"/jxl/([^?]+)\.jxl(\?.*)?", "/jpg/$1.jpg");
                Uri jpgUri;
                if (newUrl != uri.AbsoluteUri && Uri.TryCreate(newUrl, UriKind.Absolute, out jpgUri))
                {
                    uri = jpgUri;
                }
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(90000);
                try
                {
                    for (int redirects = 0; ; redirects++)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        var request = (HttpWebRequest)WebRequest.Create(uri);
                        request.AllowAutoRedirect = false;
                        request.UseDefaultCredentials = false;
                        request.Proxy = null;
                        request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36";
                        request.Referer = "https://chat.zalo.me/";
                        using (var headers = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                        using (headers.Token.Register(request.Abort))
                        using (timeout.Token.Register(request.Abort))
                        using (var response = await ReceiveRecallHeaders(request, headers, timeout.Token))
                        {
                            int status = (int)response.StatusCode;
                            var target = RecallRedirectTarget(uri, status, response.Headers[HttpResponseHeader.Location], redirects);
                            if (target != null) { uri = target; continue; }
                            if (status != 200) throw new RecallMediaUnavailable("http-" + status);
                            return await ReadRecallDownload(response, kind, original, expectedSize, checksum, timeout.Token, request.Abort);
                        }
                    }
                }
                catch (Exception error)
                {
                    if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                    if (timeout.IsCancellationRequested &&
                        (error is OperationCanceledException || error is WebException || error is IOException))
                        throw new RecallMediaUnavailable("total-timeout");
                    throw;
                }
            }
        }

        private static async Task<HttpWebResponse> ReceiveRecallHeaders(HttpWebRequest request, CancellationTokenSource headers,
            CancellationToken total)
        {
            headers.CancelAfter(10000);
            try { return (HttpWebResponse)await request.GetResponseAsync(); }
            catch (WebException)
            {
                if (headers.IsCancellationRequested && !total.IsCancellationRequested)
                    throw new RecallMediaUnavailable("headers-timeout");
                throw;
            }
            finally { headers.CancelAfter(Timeout.Infinite); }
        }

        private static async Task<RecallAttachment> ReadRecallDownload(HttpWebResponse response, string kind, bool original,
            long? expectedSize, string checksum, CancellationToken token, Action abort)
        {
            if (response.ContentLength > MaxRecallFileBytes) throw new RecallMediaUnavailable("too-large");
            string mime = (response.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
            if ((kind != "file" || String.IsNullOrEmpty(checksum))
                && (mime == "text/html" || mime == "application/xhtml+xml" || mime == "application/json")) throw new RecallMediaUnavailable("wrong-type");
            if (kind == "video" && !mime.StartsWith("video/", StringComparison.Ordinal) && mime != "application/octet-stream") throw new RecallMediaUnavailable("wrong-type");
            using (var input = response.GetResponseStream())
                return await StageMedia(input, kind, original, response.ContentLength, expectedSize, checksum, mime, token, abort);
        }

        private static async Task<RecallAttachment> StageMedia(Stream input, string kind, bool original,
            long declaredSize, long? expectedSize, string checksum, string mime, CancellationToken token, Action abort = null)
        {
            Directory.CreateDirectory(RecallStagingDir);
            string stage = Path.Combine(RecallStagingDir, Guid.NewGuid().ToString("N") + ".part");
            ActiveStagedMedia.TryAdd(stage, 0);
            bool complete = false;
            try
            {
                var buffer = new byte[64 * 1024];
                var header = new byte[12];
                int headerLength = 0;
                long total = 0;
                using (HashAlgorithm hash = String.IsNullOrEmpty(checksum) ? null :
                    checksum.Length == 64 ? (HashAlgorithm)SHA256.Create() : MD5.Create())
                using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true))
                using (var idle = abort == null ? null : CancellationTokenSource.CreateLinkedTokenSource(token))
                using (idle == null ? default(CancellationTokenRegistration) : idle.Token.Register(abort))
                {
                    int length;
                    while ((length = await ReadMediaChunk(input, buffer, idle, token).ConfigureAwait(false)) != 0)
                    {
                        total += length;
                        if (total > MaxRecallFileBytes) throw new RecallMediaUnavailable("too-large");
                        int copy = Math.Min(header.Length - headerLength, length);
                        Array.Copy(buffer, 0, header, headerLength, copy);
                        headerLength += copy;
                        if (hash != null) hash.TransformBlock(buffer, 0, length, null, 0);
                        await output.WriteAsync(buffer, 0, length, token).ConfigureAwait(false);
                    }
                    if (total == 0 || (declaredSize >= 0 && total != declaredSize))
                        throw new RecallMediaUnavailable("incomplete");
                    if (expectedSize.HasValue && total != expectedSize.Value)
                        throw new RecallMediaUnavailable("size-mismatch");
                    if (hash != null)
                    {
                        hash.TransformFinalBlock(buffer, 0, 0);
                        if (!String.Equals(BitConverter.ToString(hash.Hash).Replace("-", ""), checksum, StringComparison.OrdinalIgnoreCase))
                            throw new RecallMediaUnavailable("checksum-mismatch");
                    }
                    if (kind == "image" || kind == "sticker" || kind == "contact")
                    {
                        mime = headerLength == header.Length ? RecallPictureMime(header) : null;
                        if (mime == null) throw new RecallMediaUnavailable("wrong-type");
                    }
                    if (kind == "audio")
                    {
                        mime = (headerLength == header.Length ? RecallAudioMime(header) : null) ?? mime;
                        if (String.IsNullOrEmpty(mime) || mime == "application/octet-stream") mime = "audio/aac";
                        if (!mime.StartsWith("audio/", StringComparison.Ordinal)) throw new RecallMediaUnavailable("wrong-type");
                    }
                    if (kind == "video" && (String.IsNullOrEmpty(mime) || mime == "application/octet-stream")) mime = "video/mp4";
                    token.ThrowIfCancellationRequested();
                    output.Flush(true);
                }
                complete = true;
                return new RecallAttachment
                {
                    Kind = kind, StagedPath = stage, Mime = mime,
                    FromSource = (kind != "image" && kind != "contact") || original, Origin = "cdn"
                };
            }
            finally
            {
                if (!complete) DeleteStagedFile(stage);
            }
        }

        private static async Task<int> ReadMediaChunk(Stream input, byte[] buffer, CancellationTokenSource idle, CancellationToken token)
        {
            if (idle != null) idle.CancelAfter(10000);
            try
            {
                int bytes = await input.ReadAsync(buffer, 0, buffer.Length, idle == null ? token : idle.Token).ConfigureAwait(false);
                if (idle != null) idle.Token.ThrowIfCancellationRequested();
                return bytes;
            }
            catch (Exception error)
            {
                if (idle != null && idle.IsCancellationRequested && !token.IsCancellationRequested &&
                    (error is OperationCanceledException || error is WebException || error is IOException))
                    throw new RecallMediaUnavailable("idle-timeout");
                throw;
            }
            finally { if (idle != null) idle.CancelAfter(Timeout.Infinite); }
        }
    }
}
