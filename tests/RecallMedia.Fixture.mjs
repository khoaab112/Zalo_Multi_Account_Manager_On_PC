import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

function expression(file, name) {
    const source = readFileSync(new URL('../src/' + file, import.meta.url), 'utf8');
    const match = source.match(new RegExp('private const string ' + name + ' = @"([\\s\\S]*?)";'));
    assert.ok(match, 'Could not read production expression ' + name);
    return match[1].replaceAll('""', '"');
}

const image = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aUe8AAAAASUVORK5CYII=', 'base64');
const wave = Buffer.alloc(2044);
wave.write('RIFF'); wave.writeUInt32LE(wave.length - 8, 4); wave.write('WAVEfmt ', 8);
wave.writeUInt32LE(16, 16); wave.writeUInt16LE(1, 20); wave.writeUInt16LE(1, 22);
wave.writeUInt32LE(8000, 24); wave.writeUInt32LE(16000, 28); wave.writeUInt16LE(2, 32);
wave.writeUInt16LE(16, 34); wave.write('data', 36); wave.writeUInt32LE(wave.length - 44, 40);
const cases = [
    { id: 'photo', kind: 'image', selector: '.photo-message-v2', mime: 'image/png', bytes: image },
    { id: 'sticker', kind: 'sticker', selector: '.sticker-message', mime: 'image/png', bytes: image },
    { id: 'file', kind: 'file', selector: '.file-message', name: 'fixture.txt', mime: 'text/plain', bytes: Buffer.from('Zalo Manager fixture\n') },
    { id: 'audio', kind: 'audio', selector: '.voice-message', mime: 'audio/wav', bytes: wave },
    // A chunked byte-transport fixture, not a playable video.
    { id: 'video', kind: 'video', selector: '.video-message', mime: 'video/mp4', bytes: Buffer.alloc(1536 * 1024, 42) },
    { id: 'no-source', kind: 'file', selector: '.file-message', name: 'unavailable.txt' },
    { id: 'outgoing', kind: 'file', selector: '.file-message', name: 'outgoing.txt', mime: 'text/plain', bytes: Buffer.from('sender'), outgoing: true },
];

function frame(sample) {
    const source = sample.bytes ? URL.createObjectURL(new Blob([sample.bytes], { type: sample.mime })) : '';
    const part = {
        matches: selector => (sample.kind === 'audio' && selector === 'audio') || (sample.kind === 'video' && selector === 'video'),
        currentSrc: source,
        getAttribute: key => key === (sample.kind === 'image' ? 'data-original' : 'data-download-url') ? source : null,
        querySelector: selector => selector === '.file-message__content-title' ? { title: sample.name } : null,
        querySelectorAll: () => [],
    };
    const node = {
        id: 'message-frame_' + sample.id,
        classList: { contains: name => name === 'me' && !!sample.outgoing },
        matches: name => name === '.message-frame',
        closest: () => null,
        querySelector: selector => selector.includes('.undo-message')
            ? (node.recalled ? {} : null)
            : (selector.includes(sample.selector) ? part : null),
    };
    return { node, source, part };
}

// Zalo's file card can render a download action without an href on any DOM element.
const readRendered = runInNewContext(expression('CdpRecallMedia.cs', 'ReadRenderedMessage'), { URL });
const reactKey = '__reactFiber$fixture';
const directFile = frame({ id: '12345', kind: 'file', selector: '.file-message', name: 'direct.txt' });
const fileUrl = 'https://file.zadn.vn/fixture/direct.txt';
const otherUrl = 'https://file.zadn.vn/fixture/other.txt';
const owner = { memoizedProps: { message: { cliMsgId: 12345, message: { href: fileUrl } } } };
const outer = { memoizedProps: { message: { cliMsgId: 12346, message: { fileUrl: otherUrl } } } };
directFile.node[reactKey] = { return: owner };
directFile.part[reactKey] = { return: directFile.node[reactKey] };
owner.return = outer;
assert.equal(readRendered(directFile.node, true).source, fileUrl, 'File card owner should supply the attachment link');
owner.memoizedProps.message.cliMsgId = 12347;
assert.equal(readRendered(directFile.node, true).source, '', 'Never capture links from another message');
owner.memoizedProps.message.cliMsgId = 12345;
owner.memoizedProps.message.message.href = 'https://file.zadn.vn/fixture/direct.txt?e2esession=fixture';
assert.equal(readRendered(directFile.node, true).source, '', 'Do not capture a link requiring an encryption session');
directFile.part[reactKey] = { memoizedProps: { message: { message: { href: fileUrl } } }, return: directFile.node[reactKey] };
assert.equal(readRendered(directFile.node, true).source, fileUrl, 'File card should expose its own attachment URL');

const updatedFile = frame({ id: '12345', kind: 'file', selector: '.file-message', name: 'updated.txt' });
updatedFile.node.nodeType = updatedFile.part.nodeType = 1;
updatedFile.node.contains = node => node === updatedFile.part;
updatedFile.node[reactKey] = {};
const updatedRoot = { stateNode: updatedFile.node, alternate: updatedFile.node[reactKey], return: owner };
updatedFile.part[reactKey] = { stateNode: updatedFile.part, return: updatedRoot };
owner.memoizedProps.message.message.href = fileUrl;
assert.equal(readRendered(updatedFile.node, true).source, fileUrl, 'File card should find its own payload after a React render');
delete updatedFile.node[reactKey];
assert.equal(readRendered(updatedFile.node, true).source, fileUrl, 'File card should recognize its own DOM boundary without a React key');
owner.return = { stateNode: { nodeType: 1 }, memoizedProps: { message: { cliMsgId: 12345, fileUrl: otherUrl } } };
delete owner.memoizedProps.message.message.href;
assert.equal(readRendered(updatedFile.node, true).source, '', 'File card should stop at the first outer host node');

const nestedFile = frame({ id: '12345', kind: 'file', selector: '.file-message', name: 'deep.txt' });
const nestedRoot = { id: 'bb_msg_id_12345', matches: () => false, contains: node => node === nestedFile.node,
    querySelector: nestedFile.node.querySelector };
nestedFile.part.closest = () => nestedFile.node;
const nestedBoundary = { stateNode: nestedFile.node };
nestedFile.node.nodeType = 1;
nestedFile.node[reactKey] = nestedBoundary;
let nestedOwner = nestedBoundary;
for (let index = 0; index < 18; index++) nestedOwner = { return: nestedOwner };
const fileCdn = 'https://file-stal-18.dlfl.vn/fixture/deep.txt';
const fileChecksum = 'abcdef0123456789abcdef0123456789';
nestedFile.part[reactKey] = { return: nestedOwner, memoizedProps: { message: { message: {
    href: fileCdn, params: JSON.stringify({ fileSize: '55', checksum: fileChecksum, checksumSha: 'null' }),
} } } };
const nestedResult = readRendered(nestedRoot, true);
assert.equal(nestedResult.source, fileCdn, 'Recognize the CDN on a deeply nested file card');
assert.equal(nestedResult.fileSize, '55');
assert.equal(nestedResult.checksum, fileChecksum);
console.log('PASS file card without DOM href and message scope');

const voice = frame({ id: 'voice-params', kind: 'audio', selector: '.voice-message' });
const voiceUrl = 'https://voice.zadn.vn/fixture/recording.m4a';
const voiceFallback = 'https://voice.zadn.vn/fixture/recording.aac';
voice.node[reactKey] = {};
const voiceMessage = { params: JSON.stringify({ m4a: voiceUrl }), href: voiceFallback };
voice.part[reactKey] = { memoizedProps: { message: { message: voiceMessage } }, return: voice.node[reactKey] };
assert.equal(readRendered(voice.node, true).source, voiceUrl, 'Voice card should use the M4A in message.params');
voiceMessage.params = '{';
assert.equal(readRendered(voice.node, true).source, voiceFallback, 'Invalid voice parameters should preserve the direct source');
const phoneVoice = 'https://f2-voice-aac-dl.zdn.vn/fixture/phone.aac';
for (const prop of ['data', 'transformedData']) {
    voice.part[reactKey].memoizedProps = { [prop]: { message: { params: JSON.stringify({ m4a: phoneVoice }) } } };
    assert.equal(readRendered(voice.node, true).source, phoneVoice, `Phone voice should read ${prop}`);
    assert.ok(!readRendered(voice.node, false).source, 'Outgoing voice must not expose a download source');
}

const sticker = frame({ id: 'sticker-formats', kind: 'sticker', selector: '.sticker-message' });
const stickerGif = 'https://sticker.zadn.vn/fixture/sticker.gif';
const stickerWebp = 'https://sticker.zadn.vn/fixture/sticker.webp';
sticker.node[reactKey] = {};
const stickerModel = { stickerGifUrl: stickerGif, stickerWebpUrl: stickerWebp };
sticker.part[reactKey] = { memoizedProps: { sticker: stickerModel }, return: sticker.node[reactKey] };
assert.equal(readRendered(sticker.node, true).source, stickerGif, 'Sticker card should recognize its GIF');
delete stickerModel.stickerGifUrl;
assert.equal(readRendered(sticker.node, true).source, stickerWebp, 'Sticker card should recognize its WebP');
console.log('PASS Zalo voice M4A parameters and sticker asset fields');

const frames = cases.map(frame);
const broken = frame({ id: 'broken-card', kind: 'file', selector: '.file-message' });
broken.part.querySelector = () => { throw new Error('Attachment component failed while rendering'); };
frames.unshift(broken);
const pane = { getClientRects: () => [1], querySelectorAll: () => frames.map(item => item.node) };
const watched = {};
let paneOpen = true;
const fetches = [];
const delayedSource = frames.find(item => item.node.id === 'message-frame_file').source;
let finishDownload;
const afterRecall = new Promise(resolve => { finishDownload = resolve; });
class FixtureFileReader {
    readAsDataURL(blob) {
        blob.arrayBuffer().then(bytes => {
            this.result = 'data:' + blob.type + ';base64,' + Buffer.from(bytes).toString('base64');
            this.onload();
        }, error => this.onerror(error));
    }
}
const context = {
    window: watched, URL, Blob, FileReader: FixtureFileReader, AbortController, setTimeout, clearTimeout,
    getComputedStyle: () => ({ backgroundImage: 'none' }),
    fetch: async (...args) => {
        fetches.push(args[0]);
        if (args[0] === delayedSource) await afterRecall;
        return fetch(...args);
    },
    document: { documentElement: {}, querySelector: selector => paneOpen && selector === '#messageViewScroll' ? pane : null },
    MutationObserver: class { observe() {} disconnect() {} },
};
const evaluate = () => runInNewContext(
    expression('CdpVisibleMessages.cs', 'Query') + '("fixture",' + expression('CdpRecallMedia.cs', 'ReadRenderedMessage') + ')', context,
);
const batches = [];
const started = new Set();
try {
    paneOpen = false;
    const closed = evaluate();
    assert.equal(closed[0].chatOpen, false, 'A connected window without a conversation must report it');
    paneOpen = true;
    const opened = evaluate();
    assert.equal(opened[0].chatOpen, true, 'Opening the conversation enables scanning');
    batches.push(...opened);
    for (const row of opened) if (row.kind) started.add(row.id);
    // Hold one response until after recall while the others complete normally.
    const deadline = Date.now() + 5000;
    do {
        const rows = evaluate();
        batches.push(...rows);
        for (const row of rows) if (row.kind) started.add(row.id);
        if (fetches.length === 5 && started.size === cases.length) break;
        await new Promise(resolve => setTimeout(resolve, 20));
    } while (Date.now() < deadline);
    assert.equal(started.size, cases.length, 'One or more attachment types were not recognized');
    assert.equal(fetches.length, 5, 'Only received attachments with a source should be downloaded');
    for (const item of frames) item.node.recalled = true;
    batches.push(...evaluate());
    assert.ok(batches.some(row => row.id === 'file' && row.recalled), 'Broken attachment card prevented other recalls');
    assert.ok(!batches.some(row => row.id === 'file' && row.mediaUpdate), 'File response was not held until after recall');
    finishDownload();

    const hashes = new Map();
    const transferDeadline = Date.now() + 8000;
    do {
        await new Promise(resolve => setTimeout(resolve, 20));
        batches.push(...evaluate());
        for (const sample of cases.filter(item => item.bytes && !item.outgoing)) {
            const chunks = batches.filter(row => row.id === sample.id && row.mediaUpdate);
            if (!chunks.some(row => row.final)) continue;
            const received = Buffer.from(chunks.map(row => row.media).join(''), 'base64');
            const sha256 = createHash('sha256').update(received).digest('hex');
            assert.equal(sha256, createHash('sha256').update(sample.bytes).digest('hex'), sample.id + ' bytes changed');
            hashes.set(sample.id, sha256);
        }
        if (hashes.size === 5) break;
    } while (Date.now() < transferDeadline);
    assert.equal(hashes.size, 5, 'Not all attachments finished downloading');

    // Canvas pixels can arrive after the CDN URL. Keep them even if that URL does not change.
    const late = frame({ id: 'late', kind: 'image', selector: '.photo-message-v2' });
    let previewReady = false;
    const fallbackContext = { ...context, window: {}, document: { documentElement: {}, querySelector:
        selector => selector === '#messageViewScroll' ? { ...pane, querySelectorAll: () => [late.node] } : null },
        readPhoto: (_, needMedia, needPreview) => ({ kind: 'image', preview: 'Photo', source: 'https://file.zadn.vn/photo',
            media: needMedia && needPreview && previewReady ? image.toString('base64') : undefined, mime: 'image/png' }) };
    const fallbackQuery = expression('CdpVisibleMessages.cs', 'Query') + '("late",readPhoto)';
    runInNewContext(fallbackQuery, fallbackContext);
    try {
        previewReady = true;
        assert.ok(runInNewContext(fallbackQuery, fallbackContext).some(row => row.id === 'late' && row.previewOnly && row.media),
            'Archive a late preview when the CDN URL stays the same');
        assert.ok(!runInNewContext(fallbackQuery, fallbackContext).some(row => row.previewOnly), 'Send the preview only once');
    } finally { clearTimeout(fallbackContext.window.__zaloManagerVisibleMessages.expiry); }
    const expected = cases.filter(sample => !sample.outgoing).map(sample => ({
        id: sample.id, kind: sample.kind, sha256: hashes.get(sample.id) ?? null,
    }));
    writeFileSync(process.argv[2], JSON.stringify({ rows: batches, expected }));
    console.log('PASS DOM: 5 attachment types, exact bytes, recall, missing source and sender exclusion');
} finally {
    finishDownload();
    clearTimeout(watched.__zaloManagerVisibleMessages?.expiry);
    for (const item of frames) if (item.source) URL.revokeObjectURL(item.source);
}
