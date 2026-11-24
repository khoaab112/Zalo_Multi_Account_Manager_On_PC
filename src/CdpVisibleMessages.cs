using System;
using System.Collections.Generic;
using System.IO;
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

            private const string Query = @"(function(key, readMessage) {
                let watcher = window.__zaloManagerVisibleMessages;
                if (!watcher || watcher.key !== key) {
                    if (watcher) watcher.observer.disconnect();
                    watcher = { key: key, pending: new Map(), seen: new Set(), waiting: new Set(), reading: new Set(), sources: new Map(), visible: new Set() };
                    const scan = function(baseline) {
                        const pane = document.querySelector('#messageViewScroll');
                        watcher.chatOpen = !!(pane && pane.getClientRects().length);
                        if (!watcher.chatOpen) { watcher.visible.clear(); return; }
const title = document.querySelector('#header .header-title');
                        const selected = document.querySelector('#conversationList .conv-item.selected');
                        const row = selected && selected.closest('.msg-item[data-id]');
                        const conversation = row ? row.getAttribute('data-id').slice(0, 200) : '';
                        const name = (title && title.innerText || '').trim().slice(0, 200);
                        const selectedTitle = selected && selected.querySelector('.conv-item-title__name');
                        const selectedName = (selectedTitle && selectedTitle.innerText || '').trim().slice(0, 200);
                        if (name && selectedName && name !== selectedName) {
                            watcher.chatOpen = false;
                            watcher.visible.clear();
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
                        const screenKey = conversation + '\n' + name;
                        const previouslyVisible = watcher.screenKey === screenKey ? watcher.visible : new Set();
                        watcher.screenKey = screenKey;
                        watcher.visible = new Set();
                        for (const [id, frame] of frames) {
                            const root = frame.closest('[id^=bb_msg_id_]') || frame;
                            if (!id) continue;
                            const outgoing = frame.classList.contains('me') || root.classList.contains('me');
                            if (root.querySelector('.undo-message, .revoked-sticker-message-content')) {
                                watcher.waiting.delete(id);
                                if (!watcher.seen.has('recall:' + id)) {
                                    watcher.seen.add('recall:' + id);
                                    watcher.pending.set('recall:' + id, { id: id, recalled: true, conversation: conversation,
                                        name: name, avatar: avatarPng(), observedTransition: !baseline && !outgoing && previouslyVisible.has(id) });
                                }
                                continue;
                            }
                            watcher.visible.add(id);
                            const seen = watcher.seen.has('text:' + id);
                            if (seen && !watcher.waiting.has(id)) continue;
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

            public string CreateHookScript(JavaScriptSerializer json)
            {
                return @"(function() {
    try {
        if (window.__zaloManagerV4Interval) clearInterval(window.__zaloManagerV4Interval);
        if (window.__zaloManagerWatcher && window.__zaloManagerWatcher.observer) {
            window.__zaloManagerWatcher.observer.disconnect();
        }
        if (window.__zaloManagerVisibleMessages && window.__zaloManagerVisibleMessages.observer) {
            window.__zaloManagerVisibleMessages.observer.disconnect();
        }
        delete window.__zaloManagerWatcher;
        delete window.__zaloManagerVisibleMessages;
    } catch (e) {}

    const avatarCache = window.__zaloAvatarCache || (window.__zaloAvatarCache = new Map());

    function emit(data) {
        if (typeof window.__zaloManagerDispatch === 'function') {
            try { window.__zaloManagerDispatch(JSON.stringify(data)); } catch (e) {}
        }
    }

    function renderImageToPng(url, callback) {
        if (!url) return;
        if (typeof url === 'string' && url.includes('/jxl/')) {
            url = url.replace(/\/jxl\/([^?]+)\.jxl(\?.*)?/, '/jpg/$1.jpg');
        }
        function tryCanvas(imgObj) {
            try {
                let w = imgObj.naturalWidth || imgObj.width;
                let h = imgObj.naturalHeight || imgObj.height;
                if (!w || !h) return false;
                if (w > 800 || h > 800) {
                    const scale = Math.min(800 / w, 800 / h);
                    w = Math.round(w * scale);
                    h = Math.round(h * scale);
                }
                const canvas = document.createElement('canvas');
                canvas.width = w;
                canvas.height = h;
                const ctx = canvas.getContext('2d');
                ctx.drawImage(imgObj, 0, 0, w, h);
                const dataUrl = canvas.toDataURL('image/png');
                const b64 = dataUrl.slice('data:image/png;base64,'.length);
                if (b64 && callback) { callback(b64); return true; }
            } catch (e) {}
            return false;
        }

        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.onload = function() {
            if (!tryCanvas(img)) fallbackFetch();
        };
        img.onerror = fallbackFetch;

        function fallbackFetch() {
            try {
                fetch(url).then(r => r.blob()).then(blob => {
                    const reader = new FileReader();
                    reader.onload = function() {
                        const fallbackImg = new Image();
                        fallbackImg.onload = function() { tryCanvas(fallbackImg); };
                        fallbackImg.src = reader.result;
                    };
                    reader.readAsDataURL(blob);
                }).catch(function() {});
            } catch (e) {}
        }
        img.src = url;
    }

    function loadAvatarPng(url, callback) {
        if (!url || avatarCache.has(url)) return;
        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.onload = function() {
            try {
                const canvas = document.createElement('canvas');
                canvas.width = canvas.height = 48;
                canvas.getContext('2d').drawImage(img, 0, 0, 48, 48);
                const b64 = canvas.toDataURL('image/png').slice('data:image/png;base64,'.length);
                avatarCache.set(url, b64);
                if (callback) callback(b64);
            } catch (e) {}
        };
        img.src = url;
    }

    function getContactInfo(convId) {
        try {
            const list = document.querySelector('#conversationList');
            if (list) {
                const reactKey = Object.keys(list).find(k => k.startsWith('__react'));
                let curr = list[reactKey];
                while (curr) {
                    if (curr.memoizedProps && curr.memoizedProps.state) {
                        const s = curr.memoizedProps.state;
                        if (s.contacts) {
                            const c = s.contacts instanceof Map ? s.contacts.get(convId) : s.contacts[convId];
                            if (c) return {
                                name: (c.displayName || c.zaloName || c.name || '').replace(/\u00a0/g, ' ').trim(),
                                avatar: c.avatar || ''
                            };
                        }
                        if (s.groups) {
                            const g = s.groups instanceof Map ? s.groups.get(convId) : s.groups[convId];
                            if (g) return {
                                name: (g.name || '').replace(/\u00a0/g, ' ').trim(),
                                avatar: g.avatar || ''
                            };
                        }
                    }
                    if (curr.memoizedProps && curr.memoizedProps.uiController) {
                        const uic = curr.memoizedProps.uiController;
                        if (uic.getRecentContactWithId) {
                            const rc = uic.getRecentContactWithId(convId);
                            if (rc && (rc.name || rc.displayName)) {
                                return {
                                    name: (rc.displayName || rc.name || '').replace(/\u00a0/g, ' ').trim(),
                                    avatar: rc.avatar || ''
                                };
                            }
                        }
                    }
                    curr = curr.return;
                }
            }
            if (window.uiController && window.uiController.convDataManager) {
                const cdm = window.uiController.convDataManager;
                if (typeof cdm.getConversationInfo === 'function') {
                    const info = cdm.getConversationInfo(convId);
                    if (info) return {
                        name: (info.name || info.convName || '').replace(/\u00a0/g, ' ').trim(),
                        avatar: info.avatar || ''
                    };
                }
            }
        } catch (e) {}
        return {};
    }

    function getPreviewManager() {
        try {
            if (window.uiController) {
                const cdm = window.uiController.convDataManager;
                if (cdm && cdm._pm && typeof cdm._pm.onReceiveNewMessage === 'function') return cdm._pm;
            }
            const list = document.querySelector('#conversationList');
            if (list) {
                const reactKey = Object.keys(list).find(k => k.startsWith('__react'));
                let curr = list[reactKey];
                while (curr) {
                    if (curr.memoizedProps && curr.memoizedProps.uiController) {
                        const uic = curr.memoizedProps.uiController;
                        if (uic.convDataManager && uic.convDataManager._pm) return uic.convDataManager._pm;
                    }
                    curr = curr.return;
                }
            }
        } catch (e) {}
        return null;
    }

    function handlePmMessageReceived(msg) {
        if (!msg) return;
        const id = String(msg.msgId || msg.id || '');
        const cliMsgId = String(msg.cliMsgId || '');
        if (!id && !cliMsgId) return;
        if (id.startsWith('bg_') || id.startsWith('conv_') || cliMsgId.startsWith('bg_') || cliMsgId.startsWith('conv_')) return;

        const toUid = String(msg.toUid || msg.idTo || msg.convId || '');
        const fromUid = String(msg.fromUid || msg.uidFrom || '');
        const isOut = fromUid === '0';
        const convId = isOut ? toUid : (fromUid && fromUid !== '0' ? fromUid : toUid);
        if (!convId) return;

        let text = typeof msg.message === 'string' ? msg.message : '';
        let kind = null;
        let source = '';
        let fileSize = '';
        let checksum = '';
        const msgObj = (typeof msg.message === 'object' && msg.message) ? msg.message : {};
        const props = (typeof msg.properties === 'object' && msg.properties) ? msg.properties : {};
        let params = msg.params || msgObj.params;
        if (typeof params === 'string') {
            try { params = JSON.parse(params); } catch (e) {}
        }
        params = (params && typeof params === 'object') ? params : {};

        const childs = (msgObj && Array.isArray(msgObj.childMsgs)) ? msgObj.childMsgs
            : (params && Array.isArray(params.childMsgs)) ? params.childMsgs
            : (msgObj && Array.isArray(msgObj.childs)) ? msgObj.childs : null;
        if (childs && childs.length > 0) {
            for (let i = 0; i < childs.length; i++) {
                const child = childs[i];
                if (child && typeof child === 'object') {
                    const subMsg = Object.assign({}, msg, {
                        msgId: child.msgId || child.id || (id ? (id + '_' + i) : ''),
                        cliMsgId: child.cliMsgId || (cliMsgId ? (cliMsgId + '_' + i) : ''),
                        message: child,
                        params: child.params || params
                    });
                    delete subMsg.message.childMsgs;
                    handlePmMessageReceived(subMsg);
                }
            }
        }

        const mType = Number(msg.messageType || msg.msgType || msgObj.messageType || msgObj.msgType || 0);
        const originType = String(msg.originMsgType || msgObj.originMsgType || '');

        const stickerId = msgObj.id || props.stickerId || params.stickerId;
        const potentialFileName = (typeof msgObj.title === 'string' && msgObj.title) ? msgObj.title : (props.fileName || params.fileName || text || '');
        const isVideoFile = /\.(mp4|m4v|webm|mov|avi|mkv|flv|wmv|3gp|ts)$/i.test(potentialFileName);
        const thumbUrl = msgObj.thumbUrl || msgObj.thumb || props.thumbUrl || props.thumb || params.thumbUrl || params.thumb || '';

        const isContact = (mType === 6 || originType === 'chat.recommended' || msgObj.action === 'recommened.user') && msgObj.action !== 'recommened.misscall';
        const isDoodle = mType === 5 || originType === 'chat.doodle' || msgObj.doodleUrl || params.doodleUrl;
        const isVoice = mType === 3 || originType === 'chat.voice' || (params && (params.m4a || params.voiceUrl || params.audioUrl)) || msgObj.m4a || props.m4aUrl || (msgObj.href && /\.(m4a|aac|mp3|wav|ogg|amr)$/i.test(msgObj.href)) || msgObj.voiceUrl;
        const isVideo = mType === 18 || originType === 'chat.video.msg' || msgObj.videoUrl || props.videoUrl || isVideoFile || (thumbUrl && thumbUrl.includes('/video/')) || params.videoUrl;
        const isSticker = mType === 4 || mType === 13 || (mType >= 401 && mType <= 404) || originType === 'chat.sticker' || stickerId || props.stickerUrl;
        const isImage = mType === 2 || mType === 11 || mType === 7 || originType === 'chat.photo' || originType === 'chat.gif' || msgObj.oriUrl || msgObj.hdUrl || msgObj.normalUrl || props.hdUrl || params.hdUrl || params.oriUrl;
        const isFile = mType === 19 || mType === 23 || originType === 'share.file' || msgObj.href || props.fileUrl || params.fileUrl;

        if (isSticker) {
            kind = 'sticker';
            text = text || 'Sticker';
            const sId = String(stickerId || '');
            const cId = String(msgObj.catId || params.catId || '');
            fileSize = sId;
            checksum = cId;
            if (sId) {
                source = 'https://api.zalo.me/api/emoticon/sticker/webpc?size=130&eid=' + encodeURIComponent(sId);
            } else {
                source = props.stickerUrl || props.url || params.stickerUrl || params.url || '';
            }
        } else if (isContact) {
            kind = 'contact';
            const contactName = (msgObj.title && msgObj.title !== 'sendBubbleMessage') ? msgObj.title : (msgObj.dName || msgObj.name || (params && (params.name || params.dName)) || 'Liên hệ');
            text = 'Danh thiếp: ' + contactName;
            source = msgObj.thumb || msgObj.avatar || (params && (params.thumb || params.avatar)) || '';
        } else if (isDoodle) {
            kind = 'image';
            text = 'Hình vẽ';
            source = msgObj.oriUrl || msgObj.normalUrl || msgObj.thumbUrl || msgObj.thumb || props.oriUrl || props.url || (params && (params.oriUrl || params.thumbUrl || params.doodleUrl || params.url)) || msgObj.doodleUrl || msgObj.url || '';
        } else if (isVoice) {
            kind = 'audio';
            const dur = (params && params.duration) ? Math.round(params.duration / 1000) : (msgObj.duration ? Math.round(msgObj.duration / 1000) : 0);
            text = dur > 0 ? ('Tin nhắn thoại (' + dur + 's)') : 'Tin nhắn thoại';
            source = (params && (params.m4a || params.voiceUrl || params.audioUrl || params.href || params.url)) || msgObj.href || msgObj.m4a || msgObj.voiceUrl || props.m4aUrl || props.audioUrl || props.url || msgObj.url || msg.href || '';
            fileSize = String((params && params.fileSize) || msgObj.fileSize || '');
        } else if (isVideo) {
            kind = 'video';
            text = potentialFileName || (typeof msgObj.title === 'string' && msgObj.title) ? msgObj.title : (text || 'Video');
            source = msgObj.videoUrl || msgObj.oriUrl || props.videoUrl || props.oriUrl || props.fileUrl || props.url || params.videoUrl || params.url || params.oriUrl || msgObj.href || params.fileUrl || msgObj.url || msg.videoUrl || msg.href || '';
            fileSize = String(msgObj.fileSize || params.fileSize || props.fileSize || '');
            checksum = String(msgObj.checksum || params.checksum || props.checksum || '');
        } else if (isImage) {
            kind = 'image';
            text = (typeof msgObj.title === 'string' && msgObj.title) ? msgObj.title : (text || (mType === 7 ? 'GIF' : 'Ảnh'));
            source = msgObj.oriUrl || msgObj.hdUrl || msgObj.normalUrl || msgObj.thumbUrl || props.hdUrl || props.normalUrl || props.thumbUrl || props.url || params.hdUrl || params.oriUrl || params.normalUrl || params.thumbUrl || params.url || msgObj.url || msgObj.thumb || '';
        } else if (isFile) {
            kind = 'file';
            text = (typeof msgObj.title === 'string' && msgObj.title) ? msgObj.title : (text || props.fileName || params.fileName || 'Tệp đính kèm');
            source = msgObj.href || props.fileUrl || params.fileUrl || props.url || params.url || msgObj.url || msg.href || '';
            fileSize = String(msgObj.fileSize || params.fileSize || props.fileSize || '');
            checksum = String(msgObj.checksum || params.checksum || props.checksum || '');
        }

        if (source && (kind === 'image' || kind === 'sticker' || isDoodle || isImage)) {
            if (typeof source === 'string' && source.includes('/jxl/')) {
                source = source.replace(/\/jxl\/([^?]+)\.jxl(\?.*)?/, '/jpg/$1.jpg');
            }
        }

        const info = getContactInfo(convId) || {};
        const convName = info.name || '';
        const ava = avatarCache.get(info.avatar) || '';
        const primaryId = id || cliMsgId;

        emit([{
            id: primaryId,
            cliMsgId: cliMsgId,
            outgoing: isOut,
            preview: (text || 'Tin nhắn').slice(0, 16000),
            conversation: convId,
            name: convName,
            kind: kind,
            source: source,
            fileSize: fileSize,
            checksum: checksum,
            avatar: ava,
            captureStatus: source ? 'pending' : 'no-source',
            timestamp: msg.ts || Date.now()
        }]);

        if (info.avatar && !avatarCache.has(info.avatar)) {
            loadAvatarPng(info.avatar, function(b64) {
                emit([{ id: primaryId, cliMsgId: cliMsgId, avatarUpdate: true, avatar: b64 }]);
            });
        }

        if ((kind === 'image' || kind === 'sticker' || kind === 'contact') && source) {
            renderImageToPng(source, function(pngB64) {
                emit([{
                    id: primaryId,
                    cliMsgId: cliMsgId,
                    kind: kind,
                    mediaUpdate: true,
                    media: pngB64,
                    mime: 'image/png',
                    fromSource: true
                }]);
            });
        } else if (kind === 'video' && thumbUrl) {
            renderImageToPng(thumbUrl, function(pngB64) {
                emit([{
                    id: primaryId,
                    cliMsgId: cliMsgId,
                    kind: 'video',
                    mediaUpdate: true,
                    previewOnly: true,
                    media: pngB64,
                    mime: 'image/png'
                }]);
            });
        }
    }

    const seenRecalls = new Set();
    function reportUndo(id, cliMsgId, convId) {
        if (!id && !cliMsgId) return;
        if (id && (id.startsWith('bg_') || id.startsWith('conv_'))) return;
        if (cliMsgId && (cliMsgId.startsWith('bg_') || cliMsgId.startsWith('conv_'))) return;

        const recallKey = (id || '') + ':' + (cliMsgId || '');
        if (seenRecalls.has(recallKey)) return;
        seenRecalls.add(recallKey);
        if (seenRecalls.size > 2000) {
            const first = seenRecalls.values().next().value;
            seenRecalls.delete(first);
        }

        emit([{
            id: id || cliMsgId,
            cliMsgId: cliMsgId || '',
            recalled: true,
            observedTransition: true,
            conversation: convId || ''
        }]);
    }

    function handlePmMessageUndo(msg) {
        if (!msg) return;
        const id = String(msg.msgId || msg.id || '');
        const cliMsgId = String(msg.cliMsgId || '');
        const toUid = String(msg.toUid || msg.idTo || msg.convId || '');
        const fromUid = String(msg.fromUid || msg.uidFrom || '');
        const convId = toUid || fromUid || '';
        reportUndo(id, cliMsgId, convId);
    }

    function setupPmHooks() {
        const pm = getPreviewManager();
        if (!pm) return false;
        pm.__zaloManagerHandleReceived = handlePmMessageReceived;
        pm.__zaloManagerHandleUndo = handlePmMessageUndo;
        if (pm.__zaloManagerHookedDispatch) return true;
        pm.__zaloManagerHookedDispatch = true;

        const origOnReceive = pm.onReceiveNewMessage;
        pm.onReceiveNewMessage = function(src, msg) {
            try {
                if (msg && typeof pm.__zaloManagerHandleReceived === 'function')
                    pm.__zaloManagerHandleReceived(msg);
            } catch (e) {}
            return origOnReceive.apply(this, arguments);
        };

        if (typeof pm.onReceiveNewMessages === 'function') {
            const origBatch = pm.onReceiveNewMessages;
            pm.onReceiveNewMessages = function(src, list) {
                try {
                    if (Array.isArray(list) && typeof pm.__zaloManagerHandleReceived === 'function')
                        list.forEach(m => pm.__zaloManagerHandleReceived(m));
                } catch (e) {}
                return origBatch.apply(this, arguments);
            };
        }

        const origOnUndo = pm.onUndoMessage;
        pm.onUndoMessage = function(src, msg) {
            try {
                if (msg && typeof pm.__zaloManagerHandleUndo === 'function')
                    pm.__zaloManagerHandleUndo(msg);
            } catch (e) {}
            return origOnUndo.apply(this, arguments);
        };

        if (typeof pm.onDeleteMessage === 'function') {
            const origOnDelete = pm.onDeleteMessage;
            pm.onDeleteMessage = function(src, msg) {
                try {
                    if (msg && typeof pm.__zaloManagerHandleUndo === 'function')
                        pm.__zaloManagerHandleUndo(msg);
                } catch (e) {}
                return origOnDelete.apply(this, arguments);
            };
        }

        if (typeof pm.onDeleteMessages === 'function') {
            const origOnDeleteMsgs = pm.onDeleteMessages;
            pm.onDeleteMessages = function(src, list) {
                try {
                    if (Array.isArray(list) && typeof pm.__zaloManagerHandleUndo === 'function') {
                        list.forEach(m => pm.__zaloManagerHandleUndo(m));
                    }
                } catch (e) {}
                return origOnDeleteMsgs.apply(this, arguments);
            };
        }

        try {
            const list = document.querySelector('#conversationList');
            let uic = window.uiController;
            if (!uic && list) {
                const rKey = Object.keys(list).find(k => k.startsWith('__react'));
                let curr = list[rKey];
                while (curr) {
                    if (curr.memoizedProps && curr.memoizedProps.uiController) {
                        uic = curr.memoizedProps.uiController;
                        break;
                    }
                    curr = curr.return;
                }
            }
            const cdm = uic && uic.convDataManager;
            if (cdm) {
                if (typeof cdm.onDeleteMessages === 'function' && !cdm.__zaloManagerHookedDelete) {
                    cdm.__zaloManagerHookedDelete = true;
                    const origDelete = cdm.onDeleteMessages;
                    cdm.onDeleteMessages = function(convId, msgs) {
                        try {
                            if (Array.isArray(msgs)) {
                                msgs.forEach(m => {
                                    if (m) {
                                        const id = String(m.msgId || m.id || '');
                                        const cliId = String(m.cliMsgId || '');
                                        reportUndo(id, cliId, String(convId || ''));
                                    }
                                });
                            }
                        } catch (e) {}
                        return origDelete.apply(this, arguments);
                    };
                }

                if (typeof cdm.onReceiveNewMessages === 'function' && !cdm.__zaloManagerHooked) {
                    cdm.__zaloManagerHooked = true;
                    const origCdmBatch = cdm.onReceiveNewMessages;
                    cdm.onReceiveNewMessages = function(convId, msgs, opt) {
                        try {
                            if (Array.isArray(msgs) && typeof pm.__zaloManagerHandleReceived === 'function') {
                                msgs.forEach(m => pm.__zaloManagerHandleReceived(m));
                            }
                        } catch (e) {}
                        return origCdmBatch.apply(this, arguments);
                    };
                }
            }

            const chat = document.querySelector('#chatViewContainer');
            if (chat) {
                const cKey = Object.keys(chat).find(k => k.startsWith('__react'));
                let curr = chat[cKey];
                let store = null;
                while (curr) {
                    if (curr.memoizedProps && curr.memoizedProps.store && typeof curr.memoizedProps.store.dispatch === 'function') {
                        store = curr.memoizedProps.store;
                        break;
                    }
                    curr = curr.return;
                }
                if (store && !store.__zaloManagerHooked) {
                    store.__zaloManagerHooked = true;
                    const origStoreDispatch = store.dispatch;
                    store.dispatch = function(action) {
                        try {
                            if (action && typeof action === 'object') {
                                const actType = String(action.type || '');
                                if (/undo|delete|remove|revoke/i.test(actType)) {
                                    const payload = action.payload || action.data || action;
                                    const id = String(payload.msgId || payload.id || action.msgId || action.id || '');
                                    const cliId = String(payload.cliMsgId || action.cliMsgId || '');
                                    if (id || cliId) {
                                        reportUndo(id, cliId, String(payload.convId || action.convId || ''));
                                    }
                                    if (Array.isArray(payload.ids || payload.msgIds || action.msgIds)) {
                                        (payload.ids || payload.msgIds || action.msgIds).forEach(mId => reportUndo(String(mId), '', ''));
                                    }
                                }
                            }
                        } catch (e) {}
                        return origStoreDispatch.apply(this, arguments);
                    };
                }
            }
        } catch (e) {}
        return true;
    }

    if (!window.__zaloManagerPmInterval) {
        window.__zaloManagerPmInterval = setInterval(setupPmHooks, 1000);
    }
    setupPmHooks();
})();";
            }

            public void AcceptResponse(Profile profile, Dictionary<string, object> response)
            {
                AcceptResponseWithDownloads(profile, response, System.Threading.CancellationToken.None);
            }

            public void AcceptBindingPayload(Profile profile, string payload, System.Threading.CancellationToken token)
            {
                if (profile != null && !IsRecallEnabled(profile.Id)) return;
                ApplyRecallDownloads(profile);
                if (String.IsNullOrEmpty(payload)) return;
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                    object parsed = serializer.DeserializeObject(payload);
                    var rows = parsed as System.Collections.IEnumerable;
                    if (rows == null && parsed is Dictionary<string, object>)
                        rows = new[] { (Dictionary<string, object>)parsed };
                    if (rows != null)
                    {
                        AcceptRows(profile, rows.OfType<Dictionary<string, object>>(), token);
                    }
                }
                catch (Exception error) { System.Diagnostics.Debug.WriteLine("AcceptBindingPayload: " + error.Message); }
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
                AcceptRows(profile, rows.OfType<Dictionary<string, object>>(), token);
            }

            private void AcceptRows(Profile profile, IEnumerable<Dictionary<string, object>> rows, System.Threading.CancellationToken token)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var row in rows)
                {
                    object chatOpen;
                    if (row.TryGetValue("chatOpen", out chatOpen) && chatOpen is bool)
                    {
                        bool ready = (bool)chatOpen;
                        if (ConversationOpen != ready) Status = ready ? "Đang quét hội thoại Zalo (Endpoint)" : "Mở hội thoại ở tài khoản nhận để theo dõi thu hồi";
                        ConversationOpen = ready;
                        if (!ready) Status = "Mở hội thoại ở tài khoản nhận để theo dõi thu hồi";
                        continue;
                    }
                    string id = Text(row, "id");
                    if (String.IsNullOrWhiteSpace(id) || id.Length > 120 || id.StartsWith("bg_") || id.StartsWith("conv_")) continue;
                    string rowCliId = Text(row, "cliMsgId");
                    if (!String.IsNullOrEmpty(rowCliId) && (rowCliId.StartsWith("bg_") || rowCliId.StartsWith("conv_"))) continue;

                    object mediaUpdate;
                    CapturedMessage previous;
                    string source = Text(row, "source");
                    if (source != null && !row.ContainsKey("outgoing") && !row.ContainsKey("preview"))
                    {
                        lock (syncRoot) { captured.TryGetValue(id, out previous); }
                        if (previous != null && previous.Attachment != null)
                        {
                            object original;
                            QueueRecallDownload(profile, id, previous, source, row.TryGetValue("original", out original) && true.Equals(original),
                                Text(row, "fileSize"), Text(row, "checksum"), token);
                        }
                        continue;
                    }
                    if (row.TryGetValue("mediaUpdate", out mediaUpdate) && true.Equals(mediaUpdate))
                    {
                        string cliId = Text(row, "cliMsgId");
                        lock (syncRoot)
                        {
                            if (!captured.TryGetValue(id, out previous) && !String.IsNullOrEmpty(cliId))
                                captured.TryGetValue(cliId, out previous);
                        }

                        var updated = previous != null ? ReadRecallMediaUpdate(previous, row) : ReadRecallAttachment(Text(row, "kind") ?? "image", row);
                        if (updated != null && updated.Data != null)
                        {
                            if (!updated.Data.StartsWith("local:", StringComparison.OrdinalIgnoreCase) && updated.Data.Length > 20000
                                && !RecallArchivePath.EndsWith("archive.dat", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    Directory.CreateDirectory(RecallMediaDir);
                                    string ext = updated.Mime == "image/jpeg" ? ".jpg" : ".png";
                                    string filePath = Path.Combine(RecallMediaDir, Guid.NewGuid().ToString("N") + ext);
                                    File.WriteAllBytes(filePath, Convert.FromBase64String(updated.Data));
                                    updated.Data = "local:" + filePath;
                                }
                                catch { }
                            }
                            object previewOnly;
                            if (row.TryGetValue("previewOnly", out previewOnly) && true.Equals(previewOnly)
                                && previous != null && !String.IsNullOrEmpty(previous.Attachment != null ? previous.Attachment.Data : null)) continue;
                            if (previous != null)
                            {
                                if (updated.FromSource && previous.Attachment != null && !String.IsNullOrEmpty(previous.Attachment.Data)
                                    && previous.Attachment.Data.Length <= 20000)
                                {
                                    updated.PreviewData = previous.Attachment.Data;
                                    updated.PreviewMime = previous.Attachment.Mime;
                                }
                                previous.Attachment = updated;
                                if (previous.Recalled)
                                    UpdateRecallAttachment(profile.Id, previous.ConversationId, id, updated);
                            }
                            else
                            {
                                CachedIncomingMessage cached;
                                if (IncomingMessageCache.TryGet(id, out cached) || (!String.IsNullOrEmpty(cliId) && IncomingMessageCache.TryGet(cliId, out cached)))
                                {
                                    if (cached.Attachment == null) cached.Attachment = updated;
                                    else
                                    {
                                        cached.Attachment.Data = updated.Data;
                                        cached.Attachment.Mime = updated.Mime;
                                    }
                                }
                            }
                            IncomingMessageCache.UpdateAttachment(id, updated);
                            if (!String.IsNullOrEmpty(cliId)) IncomingMessageCache.UpdateAttachment(cliId, updated);
                        }
                        continue;
                    }

                    object avatarUpdate;
                    if (row.TryGetValue("avatarUpdate", out avatarUpdate) && true.Equals(avatarUpdate))
                    {
                        string ava = ReadAvatar(row);
                        if (!String.IsNullOrEmpty(ava))
                        {
                            CapturedMessage prev;
                            if (captured.TryGetValue(id, out prev)) prev.AvatarPng = ava;
                            IncomingMessageCache.UpdateAvatar(id, ava);
                        }
                        continue;
                    }

                    object recalled;
                    if (row.TryGetValue("recalled", out recalled) && true.Equals(recalled))
                    {
                        string cliId = Text(row, "cliMsgId");
                        CapturedMessage capture = null;
                        CachedIncomingMessage cachedMsg = null;

                        lock (syncRoot)
                        {
                            if (!captured.TryGetValue(id, out capture) && !String.IsNullOrEmpty(cliId))
                                captured.TryGetValue(cliId, out capture);
                        }

                        if (capture != null)
                        {
                            if (capture.Recalled) continue;
                            capture.Recalled = true;
                            if (!capture.Outgoing)
                            {
                                string conversationKey = capture.ConversationId;
                                string convName = !String.IsNullOrWhiteSpace(capture.ConversationName) && capture.ConversationName != "Người gửi chưa xác định"
                                    ? capture.ConversationName : Text(row, "name");
                                if (String.IsNullOrWhiteSpace(convName)) convName = "Người gửi chưa xác định";
                                string avatar = capture.AvatarPng;
                                if (String.IsNullOrEmpty(avatar))
                                    avatar = ReadAvatar(row);
                                RecordRecall(profile, conversationKey, convName, avatar, id, cliId, capture.Content, capture.Attachment, capture.Outgoing);

                                if (capture.Attachment != null && String.IsNullOrEmpty(capture.Attachment.Data) && !capture.Downloading && !String.IsNullOrEmpty(capture.Attachment.SourceUrl))
                                {
                                    QueueRecallDownload(profile, id, capture, capture.Attachment.SourceUrl, capture.Attachment.Original, capture.Attachment.FileSize, capture.Attachment.Checksum, token);
                                }
                            }

                            IncomingMessageCache.Remove(id);
                            if (!String.IsNullOrEmpty(cliId)) IncomingMessageCache.Remove(cliId);
                        }
                        else if (IncomingMessageCache.TryGet(id, out cachedMsg) || (!String.IsNullOrEmpty(cliId) && IncomingMessageCache.TryGet(cliId, out cachedMsg)))
                        {
                            if (!cachedMsg.Outgoing)
                            {
                                string convId = cachedMsg.ConversationId;
                                string conversationKey = String.IsNullOrWhiteSpace(convId) || convId.Length > 200
                                    ? "message:" + id : (convId.StartsWith("conversation:") || convId.StartsWith("message:") ? convId : "conversation:" + convId);
                                string convName = !String.IsNullOrWhiteSpace(cachedMsg.ConversationName)
                                    ? cachedMsg.ConversationName : "Người gửi chưa xác định";
                                string recallCliId = !String.IsNullOrEmpty(cliId) ? cliId : cachedMsg.CliMsgId;
                                RecordRecall(profile, conversationKey, convName, cachedMsg.AvatarPng, id, recallCliId, cachedMsg.Content, cachedMsg.Attachment, cachedMsg.Outgoing);

                                if (cachedMsg.Attachment != null && String.IsNullOrEmpty(cachedMsg.Attachment.Data) && !String.IsNullOrEmpty(cachedMsg.Attachment.SourceUrl))
                                {
                                    var tempCap = new CapturedMessage
                                    {
                                        Content = cachedMsg.Content,
                                        Attachment = cachedMsg.Attachment,
                                        ConversationId = conversationKey,
                                        ConversationName = convName,
                                        AvatarPng = cachedMsg.AvatarPng,
                                        Outgoing = cachedMsg.Outgoing,
                                        Recalled = true
                                    };
                                    lock (syncRoot) { captured[id] = tempCap; }
                                    QueueRecallDownload(profile, id, tempCap, cachedMsg.Attachment.SourceUrl, cachedMsg.Attachment.Original, cachedMsg.Attachment.FileSize, cachedMsg.Attachment.Checksum, token);
                                }
                            }

                            IncomingMessageCache.Remove(id);
                            if (!String.IsNullOrEmpty(cliId)) IncomingMessageCache.Remove(cliId);
                        }
                        else
                        {
                            object transition;
                            if (row.TryGetValue("observedTransition", out transition) && true.Equals(transition))
                            {
                                string observedConversation = Text(row, "conversation");
                                string conversationKey = String.IsNullOrWhiteSpace(observedConversation) || observedConversation.Length > 200
                                    ? "message:" + id : "conversation:" + observedConversation;
                                string conversationName = Text(row, "name");
                                if (String.IsNullOrWhiteSpace(conversationName) || conversationName.Length > 200)
                                    conversationName = "Người gửi chưa xác định";
                                string previewText = Text(row, "preview");
                                if (String.IsNullOrWhiteSpace(previewText))
                                    previewText = "Tin đã thu hồi trước khi Manager đọc được nội dung";
                                RecordRecall(profile, conversationKey, conversationName, ReadAvatar(row), id, cliId,
                                    previewText, null, false);
                            }
                        }
                        continue;
                    }

                    string content = Text(row, "preview");
                    object outgoing;
                    if (String.IsNullOrWhiteSpace(content) || content.Length > 16000
                        || !row.TryGetValue("outgoing", out outgoing) || !(outgoing is bool)) continue;
                    bool alreadyCaptured;
                    lock (syncRoot) { alreadyCaptured = captured.ContainsKey(id); }
                    if (alreadyCaptured) continue;

                    string conversation = Text(row, "conversation");
                    string name = Text(row, "name");
                    string msgAvatar = ReadAvatar(row);
                    var attachment = ReadRecallAttachment(Text(row, "kind"), row);

                    var cachedIncoming = new CachedIncomingMessage
                    {
                        Id = id,
                        CliMsgId = Text(row, "cliMsgId"),
                        ProfileId = profile.Id,
                        ConversationId = conversation,
                        ConversationName = String.IsNullOrWhiteSpace(name) || name.Length > 200 ? "Người gửi chưa xác định" : name,
                        AvatarPng = msgAvatar,
                        Content = content,
                        Attachment = attachment,
                        Outgoing = (bool)outgoing,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    IncomingMessageCache.Store(cachedIncoming);

                    var capturedMsg = new CapturedMessage
                    {
                        Content = content,
                        Attachment = attachment,
                        Outgoing = (bool)outgoing,
                        AvatarPng = msgAvatar,
                        ConversationId = String.IsNullOrWhiteSpace(conversation) || conversation.Length > 200
                            ? "message:" + id : (conversation.StartsWith("conversation:") ? conversation : "conversation:" + conversation),
                        ConversationName = cachedIncoming.ConversationName
                    };
                    lock (syncRoot)
                    {
                        captured[id] = capturedMsg;
                        string incomingCliId = Text(row, "cliMsgId");
                        if (!String.IsNullOrEmpty(incomingCliId))
                        {
                            captured[incomingCliId] = capturedMsg;
                        }
                    }
                    if (!String.IsNullOrEmpty(source) && capturedMsg.Attachment != null)
                    {
                        object original;
                        QueueRecallDownload(profile, id, capturedMsg, source, row.TryGetValue("original", out original) && true.Equals(original),
                            Text(row, "fileSize"), Text(row, "checksum"), token);
                    }
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
                if (!row.TryGetValue("chunk", out chunkValue))
                {
                    var att = ReadRecallAttachment(message.Attachment != null ? message.Attachment.Kind : "image", row);
                    if (att != null && message.Attachment != null)
                    {
                        if (String.IsNullOrEmpty(att.SourceUrl)) att.SourceUrl = message.Attachment.SourceUrl;
                        if (String.IsNullOrEmpty(att.FileSize)) att.FileSize = message.Attachment.FileSize;
                        if (String.IsNullOrEmpty(att.Checksum)) att.Checksum = message.Attachment.Checksum;
                    }
                    return att;
                }
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
                var result = ReadRecallAttachment(message.Attachment != null ? message.Attachment.Kind : "image", row);
                if (result != null && message.Attachment != null)
                {
                    if (String.IsNullOrEmpty(result.SourceUrl)) result.SourceUrl = message.Attachment.SourceUrl;
                    if (String.IsNullOrEmpty(result.FileSize)) result.FileSize = message.Attachment.FileSize;
                    if (String.IsNullOrEmpty(result.Checksum)) result.Checksum = message.Attachment.Checksum;
                }
                return result;
            }

            private static RecallAttachment ReadRecallAttachment(string kind, Dictionary<string, object> row)
            {
                if (kind != "image" && kind != "sticker" && kind != "audio" && kind != "video" && kind != "file" && kind != "emoji" && kind != "contact") return null;
                string data = Text(row, "media");
                string mime = Text(row, "mime");
                string source = Text(row, "source");
                string fileSize = Text(row, "fileSize");
                string checksum = Text(row, "checksum");
                object origVal;
                bool original = row.TryGetValue("original", out origVal) && true.Equals(origVal);

                bool image = kind == "image" || kind == "sticker" || kind == "contact";
                bool isLocal = !String.IsNullOrEmpty(data) && data.StartsWith("local:", StringComparison.OrdinalIgnoreCase);
                if (String.IsNullOrEmpty(data) || (!isLocal && (data.Length > MaxRecallMediaBase64 || data.Length % 4 != 0))
                    || kind == "emoji" || (!isLocal && image && mime != "image/jpeg" && mime != "image/png" && mime != "image/webp" && mime != "image/gif" && mime != "image/jxl"))
                    return new RecallAttachment
                    {
                        Kind = kind, CaptureStatus = Text(row, "captureStatus"),
                        SourceUrl = source, FileSize = fileSize, Checksum = checksum, Original = original
                    };
                object fromSource;
                return new RecallAttachment
                {
                    Kind = kind, Data = data, Mime = (mime ?? "").Length <= 80 ? mime : null,
                    FromSource = row.TryGetValue("fromSource", out fromSource) && true.Equals(fromSource),
                    SourceUrl = source, FileSize = fileSize, Checksum = checksum, Original = original
                };
            }

            private static string ReadAvatar(Dictionary<string, object> row)
            {
                string png = Text(row, "avatar");
                return !String.IsNullOrEmpty(png) && png.Length <= 30000 ? png : null;
            }

            private static string NormalizeConversationKey(string convId, string convName)
            {
                if (!String.IsNullOrWhiteSpace(convId) && convId != "0" && !convId.StartsWith("message:"))
                {
                    return convId.StartsWith("conversation:") ? convId : "conversation:" + convId;
                }
                if (!String.IsNullOrWhiteSpace(convName) && convName != "Người gửi chưa xác định")
                {
                    return "conversation:name:" + convName.Trim();
                }
                return "conversation:unknown";
            }

            private static Dictionary<string, object> Object(Dictionary<string, object> data, string key)
            {
                object item;
                return data != null && data.TryGetValue(key, out item) ? item as Dictionary<string, object> : null;
            }
        }
    }
}
