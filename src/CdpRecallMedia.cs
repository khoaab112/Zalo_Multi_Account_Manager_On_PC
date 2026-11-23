namespace ZaloManagerPoc
{
    public static partial class CdpMessageWatcher
    {
        private const int MaxRecallMediaBase64 = 22369624; // 16 MiB before base64.
        private const string ReadRenderedMessage = @"(function(root, needMedia, needPreview = true) {
            const find = selector => root.matches(selector) ? root : root.querySelector(selector);
            const sticker = find('.sticker-message, .sticker-message-selectable, .default-sticker-message-content, .normal-sticker-message-content, .photo-sticker-message');
            const voice = find('.voice-message, .voice-message-old, .audio-message, audio');
            const file = find('.file-message, .file-message-v2');
            const photo = find('.photo-message-v2, .image-message, .card--picture');
            const video = find('.video-msg-container, .video-message, .video-message-v2, .video-message__non-caption-wrapper, .video-message__video-player, .file-video-message, .videoContainer__video--video-message, .videoContainer__video--file-message, .card--video, .videoContainer, video');
            const text = find('.text-message__container .text');
            const safeSource = value => {
                if (typeof value !== 'string' || !value || value.length > 8192) return '';
                if (value.startsWith('blob:')) return value;
                try {
                    const url = new URL(value);
                    if (url.protocol !== 'https:' || url.username || url.password || url.port) return '';
                    if (url.searchParams.has('e2esession')) return '';
                    if (/(^|\.)(zadn\.vn|zdn\.vn|zaloapp\.com)$/i.test(url.hostname) || /^file-stal-[0-9]+\.dlfl\.vn$/i.test(url.hostname)
                        || /^video-stal-[0-9]+\.dlmd\.me$/i.test(url.hostname)) return url.href;
                } catch (error) { }
                return '';
            };
            const renderedPayloads = (node, mediaCard = false) => {
                if (!needMedia) return [];
                const key = Object.keys(node).find(name => /^__react(Fiber|InternalInstance)\$/.test(name));
                if (!key) return [];
                // Zalo wraps the message frame in another host several levels above the card.
                const frame = typeof node.closest === 'function' && node.closest('.message-frame[id^=message-frame_]');
                const scope = frame && (frame === root || root.contains(frame)) ? frame : root;
                const boundaryKey = Object.keys(scope).find(name => /^__react(Fiber|InternalInstance)\$/.test(name));
                const boundary = boundaryKey && scope[boundaryKey];
                const payloads = [];
                const messageId = scope.id.replace(/^(message-frame_|bb_msg_id_)/, '');
                let reachedRoot = false;
                for (let fiber = node[key], depth = 0; fiber && depth < 32; fiber = fiber.return, depth++) {
                    const host = fiber.stateNode;
                    if (host && host.nodeType === 1 && (reachedRoot || (host !== scope && !scope.contains(host)))) break;
                    const props = fiber.memoizedProps;
                    if (props) {
                        const models = [props.message, props.messageInfo, props.sticker];
                        if (mediaCard) models.push(props.data, props.transformedData);
                        for (const model of models) {
                            if (!model || typeof model !== 'object') continue;
                            // An enclosing component can supply media only for this exact message.
                            if (reachedRoot && ![model.msgId, model.cliMsgId].some(id => id != null && String(id) === messageId)) continue;
                            payloads.push(model);
                            for (const value of [model.message, model.content, model.UIContent]) {
                                if (value && typeof value === 'object') payloads.push(value);
                            }
                        }
                    }
                    if (host === scope || fiber === boundary || (boundary && fiber.alternate === boundary)) reachedRoot = true;
                }
                return reachedRoot ? payloads : [];
            };
            const fileMetadata = payloads => {
                for (const payload of payloads) {
                    if (typeof payload.params !== 'string' || payload.params.length > 32768) continue;
                    let params;
                    try { params = JSON.parse(payload.params); } catch (error) { continue; }
                    if (!params || typeof params !== 'object') continue;
                    const size = Number(params.fileSize);
                    if (!Number.isSafeInteger(size) || size < 0) continue;
                    const checksum = typeof params.checksumSha === 'string' && /^[\da-f]{64}$/i.test(params.checksumSha) ? params.checksumSha
                        : typeof params.checksum === 'string' && /^[\da-f]{32}$/i.test(params.checksum) ? params.checksum : '';
                    return { fileSize: String(size), checksum: checksum };
                }
                return {};
            };
            const payloadSource = (payloads, fields) => {
                for (const field of fields) {
                    for (const payload of payloads) {
                        const url = safeSource(payload[field]);
                        if (url) return url;
                    }
                }
                return '';
            };
            const voiceSource = payloads => {
                for (const payload of payloads) {
                    let params = payload.params;
                    if (typeof params === 'string' && params.length <= 32768) {
                        try { params = JSON.parse(params); } catch (error) { continue; }
                    }
                    const source = safeSource(params && params.m4a);
                    if (source) return source;
                }
                return '';
            };
            const attributes = ['data-original', 'data-download-url', 'data-file-url', 'data-video-url', 'data-audio-url', 'data-src', 'data-url', 'href', 'src'];
            const regionSource = (region, includeBackground, names = attributes) => {
                const selector = names.map(name => '[' + name + ']').join(',');
                const nodes = [region, ...region.querySelectorAll(selector)];
                for (const name of names) {
                    for (const node of nodes) {
                        const url = safeSource(node.getAttribute(name));
                        if (url) return url;
                    }
                }
                if (!includeBackground) return '';
                const graphics = [region, ...region.querySelectorAll('[style], svg image')];
                for (const node of graphics) {
                    const image = getComputedStyle(node).backgroundImage.match(/^url\(['""]?(.*?)['""]?\)$/);
                    const url = safeSource(image && image[1]);
                    if (url) return url;
                }
                return '';
            };
            if (video) {
                const player = video.matches('video') ? video : video.querySelector('video');
                const src = player && (player.currentSrc || player.getAttribute('src') || player.querySelector('source')?.getAttribute('src'));
                return { kind: 'video', preview: 'Video', source: payloadSource(renderedPayloads(video, true), ['oriUrl', 'videoUrl', 'fileUrl', 'href']) || safeSource(src) ||
                    regionSource(video, false, ['data-download-url', 'data-video-url', 'data-file-url', 'data-url', 'href']) };
            }
            if (sticker || photo) {
                const part = sticker || photo;
                const caption = text && text.innerText.trim();
                const result = { kind: sticker ? 'sticker' : 'image', preview: caption || (sticker ? 'Sticker' : 'Ảnh') };
                if (!needMedia) return result;
                const candidates = part.matches('img, canvas') ? [part] : Array.from(part.querySelectorAll('img, canvas'));
                const declared = [part, ...part.querySelectorAll('[data-original], [srcset]')];
                const payloads = renderedPayloads(part);
                result.source = payloadSource(payloads, ['oriUrl']) ||
                    declared.map(node => safeSource(node.getAttribute('data-original'))).find(Boolean) || '';
                result.original = !!result.source;
                result.source = result.source || payloadSource(payloads, sticker
                    ? ['stickerGifUrl', 'stickerWebpUrl', 'stickerUrl', 'animationUrl', 'hdUrl', 'normalUrl', 'url'] : ['hdUrl', 'normalUrl']);
                if (!result.source) {
                    const sizes = declared.flatMap(node => (node.getAttribute('srcset') || '').split(',').map(candidate => candidate.trim().split(/\s+/)));
                    sizes.sort((a, b) => (parseFloat(b[1]) || 1) - (parseFloat(a[1]) || 1));
                    result.source = sizes.map(candidate => safeSource(candidate[0])).find(Boolean) || '';
                }
                const image = candidates.find(node => node.tagName === 'CANVAS' ? node.width > 0 : node.naturalWidth > 0);
                result.source = result.source || safeSource(image && (image.currentSrc || image.src)) || regionSource(part, true);
                if (image && needPreview) {
                    try {
                        const width = image.naturalWidth || image.width;
                        const height = image.naturalHeight || image.height;
                        const canvas = document.createElement('canvas');
                        for (const maxDimension of [2048, 1024, 512]) {
                            const scale = Math.min(1, maxDimension / Math.max(width, height));
                            canvas.width = Math.max(1, Math.round(width * scale));
                            canvas.height = Math.max(1, Math.round(height * scale));
                            canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
                            const preview = canvas.toDataURL('image/png').split(',')[1];
                            if (preview && preview.length <= 1398104) {
                                result.media = preview;
                                result.mime = 'image/png';
                                break;
                            }
                        }
                    } catch (error) { /* Keep the message entry if pixels are unavailable. */ }
                }
                return result;
            }
            if (voice || file) {
                const title = file && file.querySelector('.file-message__content-title');
                const duration = voice && voice.querySelector('.duration-wrapper, .voice-message-normal__meta-duration, .voice-message-normal-old__duration-wrapper');
                const player = voice && (voice.matches('audio') ? voice : voice.querySelector('audio'));
                const region = file || voice;
                const src = player && (player.currentSrc || player.src || player.querySelector('source')?.src);
                const fields = file ? ['fileUrl', 'href', 'oriUrl'] : ['m4aUrl', 'aacUrl', 'voiceUrl', 'href', 'fileUrl'];
                const payloads = renderedPayloads(region, !!voice && !file);
                const source = (voice && !file ? voiceSource(payloads) : '') || payloadSource(payloads, fields) || safeSource(src) || regionSource(region, false,
                    ['data-download-url', 'data-file-url', 'data-video-url', 'data-audio-url', 'href', 'data-url', 'data-src']);
                const preview = file ? (title && (title.title || title.innerText.trim()) || 'Tệp đính kèm')
                    : 'Tin nhắn thoại' + (duration && duration.innerText.trim() ? ' · ' + duration.innerText.trim() : '');
                const kind = file ? (/\.(mp4|m4v|webm|mov|avi|mkv)$/i.test(preview) ? 'video' : 'file') : 'audio';
                return { kind: kind, preview: preview, source: source, ...(file && needMedia ? fileMetadata(payloads) : {}) };
            }
            if (!text || !text.getClientRects().length) return null;
            const icons = text.querySelectorAll('[data-emoji], img[alt]');
            if (!icons.length) {
                const preview = text.innerText.trim();
                return preview ? { preview: preview } : null;
            }
            const clone = text.cloneNode(true);
            for (const icon of clone.querySelectorAll('[data-emoji], img[alt]')) {
                const label = icon.getAttribute('alt') || icon.getAttribute('data-emoji') || icon.textContent || '';
                const code = label.match(/^(?:U\+|0x)([0-9a-f]{4,6})$/i);
                const value = code ? parseInt(code[1], 16) : 0;
                icon.replaceWith(value > 0 && value <= 0x10ffff ? String.fromCodePoint(value) : label);
            }
            const preview = clone.textContent.trim();
            return { kind: text.innerText.trim() ? '' : 'emoji', preview: preview || 'Emoji' };
        })";
    }
}
