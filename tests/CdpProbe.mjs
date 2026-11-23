// Run with Node 22+ against Zalo instances started with separate CDP ports.
// Example: node tests/CdpProbe.mjs 19220 19221 --seconds=60
import { writeFileSync, mkdirSync } from 'node:fs';

const ports = process.argv.slice(2).filter(value => /^\d+$/.test(value)).map(Number);
const seconds = Number(process.argv.find(value => value.startsWith('--seconds='))?.split('=')[1] ?? 60);
if (!ports.length || ports.some(port => port < 1 || port > 65535) || !Number.isFinite(seconds) || seconds < 1) {
  throw new Error('Supply valid CDP ports and --seconds greater than zero');
}
const sessions = [];
const startedUtc = new Date().toISOString();
const sockets = [];
for (const port of ports) {
  const targets = await fetch(`http://127.0.0.1:${port}/json/list`, { signal: AbortSignal.timeout(5000) }).then(response => response.json());
  for (const target of targets.filter(target => target.webSocketDebuggerUrl)) {
    const url = new URL(target.webSocketDebuggerUrl);
    if (url.hostname !== '127.0.0.1' || Number(url.port) !== port || url.protocol !== 'ws:') {
      throw new Error('Discovery returned an unexpected endpoint');
    }
    const stats = { port, targetId: target.id, type: target.type, page: new URL(target.url).pathname.split('/').pop(), enabled: false, methods: {}, frames: [], errors: [] };
    sessions.push(stats);
    const socket = new WebSocket(url);
    sockets.push(socket);
    socket.addEventListener('message', event => {
      try {
        const message = JSON.parse(event.data);
        if (message.id === 1) {
          stats.enabled = !message.error;
          if (message.error) stats.errors.push(message.error);
        }
        if (message.method) stats.methods[message.method] = (stats.methods[message.method] ?? 0) + 1;
        if (['Network.webSocketFrameReceived', 'Network.webSocketFrameSent'].includes(message.method)) {
          const frame = message.params.response;
          // Retain metadata only. Payload may contain personal messages or credentials.
          stats.frames.push({ utc: new Date().toISOString(), direction: message.method.endsWith('Sent') ? 'sent' : 'received', opcode: frame.opcode, length: frame.payloadData.length });
        }
      } catch (error) { stats.errors.push(String(error)); }
    });
    socket.addEventListener('error', () => stats.errors.push('WebSocket connection error'));
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Connection timeout')), 5000);
      socket.addEventListener('open', () => { clearTimeout(timer); resolve(); }, { once: true });
      socket.addEventListener('error', () => { clearTimeout(timer); reject(new Error('Connection failed')); }, { once: true });
    });
    socket.send(JSON.stringify({ id: 1, method: 'Network.enable' }));
  }
}
console.log(`Attached to ${sessions.length} targets for ${seconds} seconds`);
await new Promise(resolve => setTimeout(resolve, seconds * 1000));
for (const socket of sockets) socket.close();
const report = { startedUtc, finishedUtc: new Date().toISOString(), sessions };
mkdirSync('phase1-lab', { recursive: true });
const reportPath = `phase1-lab/cdp-probe-${startedUtc.replace(/[:.]/g, '-')}.json`;
writeFileSync(reportPath, JSON.stringify(report, null, 2));
console.log(`Report saved to ${reportPath}`);
console.log(JSON.stringify(report, null, 2));
if (sessions.some(session => !session.enabled || session.errors.length)) process.exitCode = 1;
