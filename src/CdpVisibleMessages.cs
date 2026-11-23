using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ZaloManagerPoc
{
    public sealed class ZaloMessageEventArgs : EventArgs
    {
        public string ProfileId { get; internal set; }
        public string MessageId { get; internal set; }
        public bool IsOutgoing { get; internal set; }
        public string TextPreview { get; internal set; }
        public DateTimeOffset DetectedAt { get; internal set; }
    }

    public static partial class CdpMessageWatcher
    {
        public static event EventHandler<ZaloMessageEventArgs> MessageDetected;

        private sealed partial class VisibleMessages
        {
            private sealed class CapturedMessage
            {
                public string Content;
                public RecallAttachment Attachment;
                public StringBuilder Transfer;
                public int NextChunk;
                public bool Downloading;
                public string ConversationId;
                public string ConversationName;
                public string AvatarPng;
                public bool Outgoing;
                public bool Recalled;
            }

            private readonly Dictionary<string, CapturedMessage> captured = new Dictionary<string, CapturedMessage>();
            public volatile string Status = "Đang kiểm tra hội thoại Zalo";
            public volatile bool ConversationOpen;

            private readonly string observerId = Guid.NewGuid().ToString("N");

            private const string Query = @"(function(key, readMessage) {
                let watcher = window.__zaloManagerVisibleMessages;
                if (!watcher || watcher.key !== key) {
                    if (watcher) watcher.observer.disconnect();
                    watcher = { key: key, pending: new Map(), seen: new Set(), waiting: new Set(), reading: new Set(), sources: new Map() };
                    const scan = function(baseline) {
                        const pane = document.querySelector('#messageViewScroll');
                        watcher.chatOpen = !!(pane && pane.getClientRects().length);
                        if (!watcher.chatOpen) return;
                        const title = document.querySelector('#header .header-title');
                        const selected = document.querySelector('#conversationList .conv-item.selected');
                        const row = selected && selected.closest('.msg-item[data-id]');
                        const conversation = row ? row.getAttribute('data-id').slice(0, 200) : '';
                        const name = (title && title.innerText || '').trim().slice(0, 200);
                        const selectedTitle = selected && selected.querySelector('.conv-item-title__name');
                        const selectedName = (selectedTitle && selectedTitle.innerText || '').trim().slice(0, 200);
                        if (name && selectedName && name !== selectedName) {
                            watcher.chatOpen = false;
                            return;
                        }
                        let avatar;
                        function avatarPng() {
                            if (avatar !== undefined) return avatar;
                            avatar = '';
                            const image = document.querySelector('#chatViewContainer #header img.a-child')
                                || (selected && selected.querySelector('.conv-item__avatar img.a-child'));
                            if (!image || !image.naturalWidth || !image.naturalHeight) return avatar;
                            try {
                                const canvas = document.createElement('canvas');
                                canvas.width = canvas.height = 48;
                                const size = Math.min(image.naturalWidth, image.naturalHeight);
                                canvas.getContext('2d').drawImage(image, (image.naturalWidth - size) / 2,
                                    (image.naturalHeight - size) / 2, size, size, 0, 0, 48, 48);
                                avatar = canvas.toDataURL('image/png').slice('data:image/png;base64,'.length);
                            } catch (error) { /* Keep the initials when the image cannot be reused. */ }
                            return avatar;
                        }
                        const frames = new Map();
                        for (const node of pane.querySelectorAll('[id^=bb_msg_id_], .message-frame[id^=message-frame_]')) {
                            const frame = node.matches('.message-frame') ? node : node.querySelector('.message-frame') || node;
                            const id = (frame.id || node.id).replace(/^(message-frame_|bb_msg_id_)/, '');
                            if (id) frames.set(id, frame);
                        }
                        for (const [id, frame] of frames) {
                            const root = frame.closest('[id^=bb_msg_id_]') || frame;
                            if (!id) continue;
                            if (root.querySelector('.undo-message, .revoked-sticker-message-content')) {
                                watcher.waiting.delete(id);
                                if (!watcher.seen.has('recall:' + id)) {
                                    watcher.seen.add('recall:' + id);
                                    watcher.pending.set('recall:' + id, { id: id, recalled: true, conversation: conversation, avatar: avatarPng() });
                                }
                                continue;
                            }
                            const seen = watcher.seen.has('text:' + id);
                            if (seen && !watcher.waiting.has(id)) continue;
                            const outgoing = frame.classList.contains('me') || root.classList.contains('me');
                            let entry;
                            try { entry = readMessage(root, !outgoing, !watcher.seen.has('preview:' + id)); }
                            catch (error) {
                                // A malformed attachment must not stop scans for other messages.
                                if (!outgoing) {
                                    try { entry = readMessage(root, false); } catch (fallbackError) { }
                                }
                            }
                            if (!entry || !entry.preview) continue;
                            if (!seen) {
                                watcher.seen.add('text:' + id);
                                if (entry.media) watcher.seen.add('preview:' + id);
                                watcher.pending.set('text:' + id, { id: id, outgoing: outgoing,
                                    preview: entry.preview.slice(0, 16000), baseline: baseline, conversation: conversation,
                                    name: name, avatar: avatarPng(), kind: entry.kind, media: entry.media, mime: entry.mime,
                                    captureStatus: entry.source || entry.checksum ? 'pending' : 'no-source' });
                                if (outgoing) continue;
                                if (entry.kind && entry.kind !== 'emoji' && (!entry.media || entry.source)) watcher.waiting.add(id);
                            } else if (entry.media) {
                                watcher.seen.add('preview:' + id);
                                watcher.pending.set('media:' + id, { id: id, mediaUpdate: true, previewOnly: true, media: entry.media, mime: entry.mime });
                                if (!entry.source) watcher.waiting.delete(id);
                            }
                            const webSource = entry.source && entry.source.startsWith('https:');
                            if (watcher.waiting.has(id) && (webSource || (!entry.source && entry.checksum))) {
                                const signature = (entry.source || '') + ' ' + (entry.checksum || '');
                                if (watcher.sources.get(id) !== signature) {
                                    watcher.sources.set(id, signature);
                                    watcher.pending.set('source:' + id, { id: id, source: entry.source || '', original: !!entry.original,
                                        fileSize: entry.fileSize, checksum: entry.checksum });
                                }
                                continue;
                            }
                            if (watcher.waiting.has(id) && entry.source && !watcher.reading.has(id) && watcher.reading.size < 2 && watcher.pending.size < 64) {
                                watcher.reading.add(id);
                                const transfer = async () => {
                                    const limit = 16 * 1024 * 1024;
                                    const control = new AbortController();
                                    const timeout = setTimeout(() => control.abort(), 25000);
                                    try {
                                        const response = await fetch(entry.source, { credentials: 'omit', redirect: 'error', signal: control.signal });
                                        if (response.status !== 200 || !response.body || Number(response.headers.get('content-length')) > limit) return;
                                        const reader = response.body.getReader();
                                        const pieces = [];
                                        let size = 0;
                                        while (true) {
                                            const part = await reader.read();
                                            if (part.done) break;
                                            size += part.value.length;
                                            if (size > limit) { control.abort(); return; }
                                            pieces.push(part.value);
                                        }
                                        if (!size) return;
                                        const mime = response.headers.get('content-type') || '';
                                        if (/^(text\/html|application\/xhtml\+xml)/i.test(mime)) return;
                                        const blob = new Blob(pieces, { type: mime });
                                        const media = await new Promise((resolve, reject) => {
                                            const converter = new FileReader();
                                            converter.onload = () => resolve(String(converter.result).split(',')[1] || '');
                                            converter.onerror = reject;
                                            converter.readAsDataURL(blob);
                                        });
                                        if (window.__zaloManagerVisibleMessages !== watcher) return;
                                        const chunkSize = 512 * 1024;
                                        for (let offset = 0, chunk = 0; offset < media.length; offset += chunkSize, chunk++) {
                                            watcher.pending.set('media:' + id + ':' + chunk, { id: id, mediaUpdate: true,
                                                media: media.slice(offset, offset + chunkSize), mime: mime,
                                                fromSource: entry.kind !== 'image' || !!entry.original,
                                                chunk: chunk, final: offset + chunkSize >= media.length });
                                        }
                                    } finally { clearTimeout(timeout); }
                                };
                                transfer().catch(() => {}).finally(() => {
                                    watcher.reading.delete(id);
                                    watcher.waiting.delete(id);
                                });
                            }
                        }
                    };
                    scan(true);
                    watcher.observer = new MutationObserver(records => {
                        const pane = document.querySelector('#messageViewScroll');
                        if (pane && records.some(record => pane.contains(record.target)
                            || (record.target.nodeType === 1 && record.target.contains(pane)))) scan(false);
                    });
                    watcher.observer.observe(document.documentElement,
                        { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['class', 'id', 'src', 'href'] });
                    watcher.scan = scan;
                    window.__zaloManagerVisibleMessages = watcher;
                }
                clearTimeout(watcher.expiry);
                watcher.expiry = setTimeout(() => {
                    watcher.observer.disconnect();
                    watcher.pending.clear();
                    if (window.__zaloManagerVisibleMessages === watcher) delete window.__zaloManagerVisibleMessages;
                }, 5000);
                watcher.scan(false);
                const result = [{ chatOpen: watcher.chatOpen }];
                let budget = 3000000;
                for (const [id, row] of watcher.pending) {
                    const size = (row.media || '').length + (row.avatar || '').length + (row.preview || '').length + (row.source || '').length + 512;
                    if (size > budget) break;
                    budget -= size;
                    result.push(row);
                    watcher.pending.delete(id);
                }
                return result;
            })";

            public string CreateQuery(JavaScriptSerializer json)
            {
                return json.Serialize(new
                {
                    id = 2,
                    method = "Runtime.evaluate",
                    @params = new { expression = Query + "(" + json.Serialize(observerId) + "," + ReadRenderedMessage + ")", returnByValue = true, silent = true }
                });
            }
            public void AcceptResponse(Profile profile, Dictionary<string, object> response)
            {
                AcceptResponseWithDownloads(profile, response, System.Threading.CancellationToken.None);
            }

            public void AcceptResponseWithDownloads(Profile profile, Dictionary<string, object> response, System.Threading.CancellationToken token)
            {
                ApplyRecallDownloads(profile);
                var outcome = Object(response, "result");
                var value = Object(outcome, "result");
                if (response.ContainsKey("error") || (outcome != null && outcome.ContainsKey("exceptionDetails")))
                {
                    ConversationOpen = false;
                    Status = "Chưa quét được hội thoại Zalo · đang thử lại";
                    return;
                }
                object items;
                var rows = value != null && value.TryGetValue("value", out items) ? items as System.Collections.IEnumerable : null;
                if (rows == null) return;

                var now = DateTimeOffset.UtcNow;
                foreach (var row in rows.OfType<Dictionary<string, object>>())
                {
                    object chatOpen;
                    if (row.TryGetValue("chatOpen", out chatOpen) && chatOpen is bool)
                    {
                        bool ready = (bool)chatOpen;
                        if (ConversationOpen != ready) Status = ready ? "Đang quét hội thoại Zalo" : "Mở hội thoại ở tài khoản nhận để theo dõi thu hồi";
                        ConversationOpen = ready;
                        if (!ready) Status = "Mở hội thoại ở tài khoản nhận để theo dõi thu hồi";
                        continue;
                    }
                    string id = Text(row, "id");
                    if (String.IsNullOrWhiteSpace(id) || id.Length > 120) continue;

                    object mediaUpdate;
                    CapturedMessage previous;
                    string source = Text(row, "source");
                    if (source != null)
                    {
                        if (captured.TryGetValue(id, out previous) && !previous.Outgoing && previous.Attachment != null)
                        {
                            object original;
                            QueueRecallDownload(id, previous, source, row.TryGetValue("original", out original) && true.Equals(original),
                                Text(row, "fileSize"), Text(row, "checksum"), token);
                        }
                        continue;
                    }
                    if (row.TryGetValue("mediaUpdate", out mediaUpdate) && true.Equals(mediaUpdate))
                    {
                        if (captured.TryGetValue(id, out previous) && previous.Attachment != null
                            && !previous.Attachment.FromSource)
                        {
                            var updated = ReadRecallMediaUpdate(previous, row);
                            if (updated != null && updated.Data != null)
                            {
                                object previewOnly;
                                if (row.TryGetValue("previewOnly", out previewOnly) && true.Equals(previewOnly)
                                    && !String.IsNullOrEmpty(previous.Attachment.Data)) continue;
                                if (updated.FromSource && !String.IsNullOrEmpty(previous.Attachment.Data))
                                {
                                    updated.PreviewData = previous.Attachment.Data;
                                    updated.PreviewMime = previous.Attachment.Mime;
                                }
                                previous.Attachment = updated;
                                if (previous.Recalled && !previous.Outgoing)
                                    UpdateRecallAttachment(profile.Id, previous.ConversationId, id, updated);
                            }
                        }
                        continue;
                    }

                    object recalled;
                    if (row.TryGetValue("recalled", out recalled) && true.Equals(recalled))
                    {
                        CapturedMessage capture;
                        if (captured.TryGetValue(id, out capture) && !capture.Recalled)
                        {
                            capture.Recalled = true;
                            if (!capture.Outgoing)
                            {
                                string conversationKey = "conversation:" + Text(row, "conversation");
                                string avatar = capture.AvatarPng;
                                if (String.IsNullOrEmpty(avatar) && conversationKey == capture.ConversationId)
                                    avatar = ReadAvatar(row);
                                RecordRecall(profile, capture.ConversationId, capture.ConversationName, avatar, id, capture.Content, capture.Attachment);
                            }
                        }
                        continue;
                    }

                    string content = Text(row, "preview");
                    object outgoing;
                    if (String.IsNullOrWhiteSpace(content) || content.Length > 16000
                        || !row.TryGetValue("outgoing", out outgoing) || !(outgoing is bool)
                        || captured.ContainsKey(id)) continue;

                    string conversation = Text(row, "conversation");
                    string name = Text(row, "name");
                    captured.Add(id, new CapturedMessage
                    {
                        Content = content,
                        Attachment = ReadRecallAttachment(Text(row, "kind"), row),
                        Outgoing = (bool)outgoing,
                        AvatarPng = ReadAvatar(row),
                        ConversationId = String.IsNullOrWhiteSpace(conversation) || conversation.Length > 200
                            ? "message:" + id : "conversation:" + conversation,
                        ConversationName = String.IsNullOrWhiteSpace(name) || name.Length > 200
                            ? "Người gửi chưa xác định" : name
                    });
                    object baseline;
                    if (row.TryGetValue("baseline", out baseline) && true.Equals(baseline)) continue;
                    string preview = content.Length > 280 ? content.Substring(0, 280) : content;

                    var message = new ZaloMessageEventArgs
                    {
                        ProfileId = profile.Id,
                        MessageId = id,
                        IsOutgoing = (bool)outgoing,
                        TextPreview = preview,
                        DetectedAt = now.ToLocalTime()
                    };
                    Status = (message.IsOutgoing ? "Tin gửi" : "Tin nhận") + " · "
                        + message.DetectedAt.ToString("HH:mm:ss") + Environment.NewLine + preview;
                    var handlers = MessageDetected;
                    if (handlers == null) continue;
                    foreach (EventHandler<ZaloMessageEventArgs> handler in handlers.GetInvocationList())
                        try { handler(null, message); }
                        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error.GetType().Name); }
                }
            }

            private static RecallAttachment ReadRecallMediaUpdate(CapturedMessage message, Dictionary<string, object> row)
            {
                object chunkValue;
                if (!row.TryGetValue("chunk", out chunkValue)) return ReadRecallAttachment(message.Attachment.Kind, row);
                if (!(chunkValue is int)) return null;
                int chunk = (int)chunkValue;
                string text = Text(row, "media");
                if (chunk == 0)
                {
                    message.Transfer = new StringBuilder();
                    message.NextChunk = 0;
                }
                if (message.Transfer == null || chunk != message.NextChunk || String.IsNullOrEmpty(text)
                    || text.Length > 512 * 1024 || message.Transfer.Length + text.Length > MaxRecallMediaBase64)
                {
                    message.Transfer = null;
                    return null;
                }
                message.Transfer.Append(text);
                message.NextChunk++;
                object final;
                if (!row.TryGetValue("final", out final) || !true.Equals(final)) return null;
                row["media"] = message.Transfer.ToString();
                message.Transfer = null;
                return ReadRecallAttachment(message.Attachment.Kind, row);
            }

            private static RecallAttachment ReadRecallAttachment(string kind, Dictionary<string, object> row)
            {
                if (kind != "image" && kind != "sticker" && kind != "audio" && kind != "video" && kind != "file" && kind != "emoji") return null;
                string data = Text(row, "media");
                string mime = Text(row, "mime");
                bool image = kind == "image" || kind == "sticker";
                if (String.IsNullOrEmpty(data) || data.Length > MaxRecallMediaBase64 || data.Length % 4 != 0
                    || kind == "emoji" || (image && mime != "image/jpeg" && mime != "image/png" && mime != "image/webp" && mime != "image/gif"))
                    return new RecallAttachment { Kind = kind, CaptureStatus = Text(row, "captureStatus") };
                object fromSource;
                return new RecallAttachment
                {
                    Kind = kind, Data = data, Mime = (mime ?? "").Length <= 80 ? mime : null,
                    FromSource = row.TryGetValue("fromSource", out fromSource) && true.Equals(fromSource)
                };
            }

            private static string ReadAvatar(Dictionary<string, object> row)
            {
                string png = Text(row, "avatar");
                return !String.IsNullOrEmpty(png) && png.Length <= 30000 ? png : null;
            }

            private static Dictionary<string, object> Object(Dictionary<string, object> data, string key)
            {
                object item;
                return data != null && data.TryGetValue(key, out item) ? item as Dictionary<string, object> : null;
            }
        }
    }
}
