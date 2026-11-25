import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const sent = [];
const intervals = new Map();
let sequence = 0;
const pm = {
    onReceiveNewMessage(_source, message) { return message.msgId; },
    onDeleteMessage() { return 'deleted'; },
};
const originalReceive = pm.onReceiveNewMessage;
const window = {
    uiController: { currUser: { userId: 'self' }, convDataManager: { _pm: pm } },
    __zaloManagerDispatch: payload => sent.push(payload),
};
window.top = window;
const browser = {
    window,
    document: { querySelector: () => null },
    setInterval(callback) { intervals.set(++sequence, callback); return sequence; },
    clearInterval(id) { intervals.delete(id); },
};

runInNewContext(readFileSync(process.argv[2], 'utf8'), browser);
assert.deepEqual(sent, [], 'the listener waits for an undo hook');
pm.onUndoMessage = () => 'undo';
window.uiController.convDataManager.getConvByIdSync = id => id === 'group-1' ? { isGroup: true } : null;
window.uiController.convDataManager.onReceiveNewMessages = (_conversation, messages) =>
    messages.forEach(item => pm.onReceiveNewMessage('batch', item));
for (const tick of intervals.values()) tick();
assert.deepEqual(sent, ['fixture:ready']);

const message = { msgId: 'msg-1', fromUid: 'member-1', message: 'fixture' };
window.uiController.convDataManager.onReceiveNewMessages('group-1', [message]);
assert.equal(sent.length, 2);
const body = JSON.parse(sent[1].slice('fixture:'.length));
assert.equal(body[0].conversation, 'group-1');
assert.equal(body[0].isGroup, true);
assert.equal(body[0].preview, 'fixture');
assert.equal(pm.onDeleteMessage('local', message), 'deleted');
assert.equal(sent.length, 2, 'local deletion should not create a recall');
assert.equal(pm.onUndoMessage('remote', message), 'undo');
const recall = JSON.parse(sent[2].slice('fixture:'.length));
assert.equal(recall[0].recalled, true);
assert.equal(recall[0].conversation, 'group-1');

pm.onUndoMessage('local', { msgId: 'own', fromUid: 'self' });
pm.onUndoMessage('local', { msgId: 'own-numeric', fromUid: 0 });
assert.equal(sent.length, 3, 'known outgoing recalls must not be emitted');
pm.onReceiveNewMessage('local', { msgId: 'own', fromUid: 'self', toUid: 'peer', message: 'own message' });
assert.equal(JSON.parse(sent[3].slice('fixture:'.length))[0].outgoing, true);
assert.equal(JSON.parse(sent[3].slice('fixture:'.length))[0].isGroup, false);
pm.onUndoMessage('unknown', { msgId: 'no-direction' });
assert.equal(JSON.parse(sent[4].slice('fixture:'.length))[0].recalled, true);

window.__zaloRecallHook.stop();
assert.equal(intervals.size, 0);
assert.equal(pm.onReceiveNewMessage, originalReceive);
pm.onUndoMessage('remote', message);
assert.equal(sent.length, 5, 'stopping the hook restores handlers');
console.log('PASS hook readiness, late hook attachment, group scope, local deletion and cleanup');
