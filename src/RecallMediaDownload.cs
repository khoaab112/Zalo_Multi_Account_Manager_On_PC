using System;
using System.Collections.Concurrent;
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

        private sealed partial class VisibleMessages
        {
            private sealed class RecallDownload
            {
                public string Id;
                public RecallAttachment Attachment;
                public string Failure;
            }

            private readonly object syncRoot = new object();
            private readonly ConcurrentQueue<RecallDownload> downloads = new ConcurrentQueue<RecallDownload>();
            private readonly SemaphoreSlim downloadSlots = new SemaphoreSlim(8);
            private int queuedDownloads;

            private void QueueRecallDownload(Profile profile, string id, CapturedMessage message, string url, bool original,
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
                bool canReuse = expectedSize.HasValue || !String.IsNullOrEmpty(checksum) || !String.IsNullOrEmpty(name) || isSticker;
                if (message.Downloading || message.Attachment.FromSource || (!webSource && !canReuse)
                    || Volatile.Read(ref queuedDownloads) >= 128) return;

                message.Downloading = true;
                Interlocked.Increment(ref queuedDownloads);
                Task.Run(async delegate
                {
                    RecallAttachment attachment = null;
                    string failure = "incomplete";
                    bool entered = false;
                    try
                    {
                        await downloadSlots.WaitAsync(token);
                        entered = true;
                        if (expectedSize > MaxRecallFileBytes) throw new RecallMediaUnavailable("too-large");
                        if (isSticker)
                        {
                            attachment = ReadLocalSticker(fileSize, checksum, url, token);
                        }
                        if (attachment == null && canReuse && expectedSize.HasValue)
                        {
                            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                            attachment = ReadDownloadedRecallFile(folder, name, kind, expectedSize.Value, checksum, token);
                        }
                        if (attachment == null && webSource)
                            attachment = await DownloadRecallMedia(uri, kind, original, expectedSize, checksum, token);
                        if (attachment == null && !webSource) failure = "no-source";
                    }
                    catch (RecallMediaUnavailable error) { failure = error.Message; }
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
                    finally
                    {
                        downloads.Enqueue(new RecallDownload { Id = id, Attachment = attachment, Failure = failure });
                        if (entered) downloadSlots.Release();
                        Interlocked.Decrement(ref queuedDownloads);
                        if (profile != null)
                        {
                            try { ApplyRecallDownloads(profile); }
                            catch { }
                        }
                    }
                });
            }

            private void ApplyRecallDownloads(Profile profile)
            {
                if (profile == null) return;
                RecallDownload result;
                bool anyChanged = false;
                while (downloads.TryDequeue(out result))
                {
                    CapturedMessage message;
                    lock (syncRoot)
                    {
                        if (!captured.TryGetValue(result.Id, out message)) continue;
                    }
                    message.Downloading = false;
                    if (message.Attachment.FromSource) continue;
                    if (result.Attachment == null)
                    {
                        if (!String.IsNullOrEmpty(message.Attachment.Data)) continue;
                        message.Attachment.CaptureStatus = result.Failure;
                        IncomingMessageCache.UpdateAttachment(result.Id, message.Attachment);
                        if (message.Recalled && UpdateRecallAttachmentCore(profile.Id, message.ConversationId, result.Id, message.Attachment))
                            anyChanged = true;
                        continue;
                    }
                    var attachment = result.Attachment;
                    if (String.IsNullOrEmpty(attachment.PreviewData))
                    {
                        attachment.PreviewData = message.Attachment.Data;
                        attachment.PreviewMime = message.Attachment.Mime;
                    }
                    if (String.IsNullOrEmpty(attachment.FileName))
                    {
                        attachment.FileName = message.Attachment.FileName;
                    }
                    message.Attachment = attachment;
                    IncomingMessageCache.UpdateAttachment(result.Id, attachment);
                    if (message.Recalled && UpdateRecallAttachmentCore(profile.Id, message.ConversationId, result.Id, attachment))
                        anyChanged = true;
                }
                if (anyChanged)
                {
                    CommitRecallChanges();
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
            public RecallMediaUnavailable(string code) : base(code) { }
        }

        private static Uri RecallRedirectTarget(Uri source, int status, string location, int redirectsFollowed)
        {
            if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308) return null;
            if (redirectsFollowed >= MaxRecallRedirects) throw new RecallMediaUnavailable("redirect-limit");
            Uri target;
            if (String.IsNullOrWhiteSpace(location) || !Uri.TryCreate(source, location, out target) || !IsRecallMediaUri(target))
                throw new RecallMediaUnavailable("redirect-blocked");
            return target;
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
                        token.ThrowIfCancellationRequested();
                        var bytes = new byte[(int)input.Length];
                        int offset = 0;
                        while (offset < bytes.Length)
                        {
                            int read = input.Read(bytes, offset, bytes.Length - offset);
                            if (read == 0) break;
                            offset += read;
                        }
                        if (offset != bytes.Length) continue;
                        if (!String.IsNullOrEmpty(checksum) && !MatchesRecallChecksum(bytes, checksum)) continue;
                        return new RecallAttachment
                        {
                            Kind = kind, FileName = name, Data = Convert.ToBase64String(bytes),
                            Mime = kind == "video" ? "video/mp4" : null,
                            FromSource = true, Origin = "local"
                        };
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
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
                                return new RecallAttachment
                                {
                                    Kind = "sticker",
                                    FileName = Path.GetFileName(files[0]),
                                    Data = "local:" + files[0],
                                    Mime = "image/png",
                                    FromSource = true,
                                    Origin = "local"
                                };
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool MatchesRecallChecksum(byte[] bytes, string checksum)
        {
            if (String.IsNullOrEmpty(checksum)) return true;
            using (HashAlgorithm digest = checksum.Length == 64 ? (HashAlgorithm)SHA256.Create() : MD5.Create())
                return String.Equals(BitConverter.ToString(digest.ComputeHash(bytes)).Replace("-", ""), checksum, StringComparison.OrdinalIgnoreCase);
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
                timeout.CancelAfter(8000);
                for (int redirects = 0; ; redirects++)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    var request = (HttpWebRequest)WebRequest.Create(uri);
                    request.AllowAutoRedirect = false;
                    request.UseDefaultCredentials = false;
                    request.Proxy = null;
                    request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36";
                    request.Referer = "https://chat.zalo.me/";
                    using (timeout.Token.Register(request.Abort))
                    using (var response = (HttpWebResponse)await request.GetResponseAsync())
                    {
                        int status = (int)response.StatusCode;
                        var target = RecallRedirectTarget(uri, status, response.Headers[HttpResponseHeader.Location], redirects);
                        if (target != null) { uri = target; continue; }
                        if (status != 200) throw new RecallMediaUnavailable("http-" + status);
                        return await ReadRecallDownload(response, kind, original, expectedSize, checksum, timeout.Token);
                    }
                }
            }
        }

        private static async Task<RecallAttachment> ReadRecallDownload(HttpWebResponse response, string kind, bool original,
            long? expectedSize, string checksum, CancellationToken token)
        {
            if (response.ContentLength > MaxRecallFileBytes) throw new RecallMediaUnavailable("too-large");
            string mime = (response.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
            if ((kind != "file" || String.IsNullOrEmpty(checksum))
                && (mime == "text/html" || mime == "application/xhtml+xml" || mime == "application/json")) throw new RecallMediaUnavailable("wrong-type");
            if (kind == "video" && !mime.StartsWith("video/", StringComparison.Ordinal) && mime != "application/octet-stream") throw new RecallMediaUnavailable("wrong-type");
            bool picture = kind == "image" || kind == "sticker" || kind == "contact";

            using (var input = response.GetResponseStream())
            using (var body = new MemoryStream())
            {
                var buffer = new byte[64 * 1024];
                int length;
                while ((length = await input.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                {
                    if (body.Length + length > MaxRecallFileBytes) throw new RecallMediaUnavailable("too-large");
                    body.Write(buffer, 0, length);
                }
                if (body.Length == 0 || (response.ContentLength >= 0 && body.Length != response.ContentLength)) return null;
                var bytes = body.ToArray();
                if ((expectedSize.HasValue && bytes.LongLength != expectedSize.Value) || !MatchesRecallChecksum(bytes, checksum))
                    throw new RecallMediaUnavailable("checksum-mismatch");
                if (picture)
                {
                    mime = RecallPictureMime(bytes);
                    if (mime == null) throw new RecallMediaUnavailable("wrong-type");
                }
                if (kind == "audio")
                {
                    mime = RecallAudioMime(bytes) ?? mime;
                    if (String.IsNullOrEmpty(mime) || mime == "application/octet-stream") mime = "audio/aac";
                    if (!mime.StartsWith("audio/", StringComparison.Ordinal)) throw new RecallMediaUnavailable("wrong-type");
                }
                if (kind == "video" && (String.IsNullOrEmpty(mime) || mime == "application/octet-stream"))
                {
                    mime = "video/mp4";
                }
                string mediaData;
                bool saveToDisk = kind == "video" || kind == "audio" || kind == "file" || bytes.Length > 200 * 1024;
                if (saveToDisk)
                {
                    try
                    {
                        Directory.CreateDirectory(RecallMediaDir);
                        string ext = ".bin";
                        if (kind == "video") ext = ".mp4";
                        else if (kind == "audio") ext = (mime ?? "").Contains("wav") ? ".wav" : ((mime ?? "").Contains("ogg") ? ".ogg" : ".m4a");
                        else if (kind == "image") ext = (mime ?? "").Contains("png") ? ".png" : ((mime ?? "").Contains("gif") ? ".gif" : ((mime ?? "").Contains("webp") ? ".webp" : ".jpg"));
                        string filePath = Path.Combine(RecallMediaDir, Guid.NewGuid().ToString("N") + ext);
                        File.WriteAllBytes(filePath, bytes);
                        mediaData = "local:" + filePath;
                    }
                    catch
                    {
                        mediaData = Convert.ToBase64String(bytes);
                    }
                }
                else
                {
                    mediaData = Convert.ToBase64String(bytes);
                }
                return new RecallAttachment
                {
                    Kind = kind, Data = mediaData, Mime = mime,
                    FromSource = (kind != "image" && kind != "contact") || original, Origin = "cdn"
                };
            }
        }
    }
}
