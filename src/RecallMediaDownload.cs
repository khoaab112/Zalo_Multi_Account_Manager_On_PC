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
        private const int MaxRecallFileBytes = 16 * 1024 * 1024;

        private sealed partial class VisibleMessages
        {
            private sealed class RecallDownload
            {
                public string Id;
                public RecallAttachment Attachment;
                public string Failure;
            }

            private readonly ConcurrentQueue<RecallDownload> downloads = new ConcurrentQueue<RecallDownload>();
            private readonly SemaphoreSlim downloadSlots = new SemaphoreSlim(2);
            private int queuedDownloads;

            private void QueueRecallDownload(string id, CapturedMessage message, string url, bool original,
                string fileSize, string checksum, CancellationToken token)
            {
                Uri uri;
                bool webSource = Uri.TryCreate(url, UriKind.Absolute, out uri) && IsRecallMediaUri(uri);
                long size;
                long? expectedSize = Int64.TryParse(fileSize, out size) && size >= 0 ? (long?)size : null;
                if (checksum != null && !Regex.IsMatch(checksum, @"\A(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{64})\z")) checksum = null;
                bool canReuse = expectedSize.HasValue && !String.IsNullOrEmpty(checksum);
                if (message.Downloading || message.Attachment.FromSource || (!webSource && !canReuse)
                    || Volatile.Read(ref queuedDownloads) >= 16) return;

                message.Downloading = true;
                Interlocked.Increment(ref queuedDownloads);
                string kind = message.Attachment.Kind;
                string name = message.Content;
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
                        if (canReuse)
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
                    }
                });
            }

            private void ApplyRecallDownloads(Profile profile)
            {
                RecallDownload result;
                while (downloads.TryDequeue(out result))
                {
                    CapturedMessage message;
                    if (!captured.TryGetValue(result.Id, out message)) continue;
                    message.Downloading = false;
                    if (message.Attachment.FromSource) continue;
                    if (result.Attachment == null)
                    {
                        if (!String.IsNullOrEmpty(message.Attachment.Data)) continue;
                        message.Attachment.CaptureStatus = result.Failure;
                        if (message.Recalled) UpdateRecallAttachment(profile.Id, message.ConversationId, result.Id, message.Attachment);
                        continue;
                    }
                    var attachment = result.Attachment;
                    attachment.PreviewData = message.Attachment.Data;
                    attachment.PreviewMime = message.Attachment.Mime;
                    message.Attachment = attachment;
                    if (message.Recalled) UpdateRecallAttachment(profile.Id, message.ConversationId, result.Id, attachment);
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
                || Regex.IsMatch(host, @"\Afile-stal-[0-9]+\.dlfl\.vn\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
            long size, string checksum, CancellationToken token)
        {
            if (size <= 0 || size > MaxRecallFileBytes || String.IsNullOrWhiteSpace(name) || name.Length > 255
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".", StringComparison.Ordinal)
                || name.EndsWith(" ", StringComparison.Ordinal) || String.IsNullOrEmpty(checksum)) return null;

            var path = Path.Combine(folder, name);
            try
            {
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return null;
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length != size) return null;
                    token.ThrowIfCancellationRequested();
                    var bytes = new byte[(int)size];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = input.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) return null;
                        offset += read;
                    }
                    if (!MatchesRecallChecksum(bytes, checksum)) return null;
                    return new RecallAttachment
                    {
                        Kind = kind, FileName = name, Data = Convert.ToBase64String(bytes),
                        FromSource = true, Origin = "local"
                    };
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
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
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(25000);
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
            bool picture = kind == "image" || kind == "sticker";

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
                    if (!mime.StartsWith("audio/", StringComparison.Ordinal)) throw new RecallMediaUnavailable("wrong-type");
                }
                return new RecallAttachment
                {
                    Kind = kind, Data = Convert.ToBase64String(bytes), Mime = mime,
                    FromSource = kind != "image" || original, Origin = "cdn"
                };
            }
        }
    }
}
