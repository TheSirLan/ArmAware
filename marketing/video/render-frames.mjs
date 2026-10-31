// Renders promo.html frame by frame with headless Edge over the DevTools protocol.
// Usage: node render-frames.mjs <framesDir> [fps]
import { spawn, execSync } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';

const framesDir = resolve(process.argv[2] ?? 'frames');
const fps = Number(process.argv[3] ?? 30);
const page = 'file:///' + resolve(import.meta.dirname, 'promo.html').replaceAll('\\', '/') + '#render';
const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
// A fresh port and profile each run, so a leftover browser from an earlier run can never be reused.
const port = 9400 + Math.floor(Math.random() * 400);
const profile = join(tmpdir(), `armaware-promo-edge-${process.pid}`);

rmSync(framesDir, { recursive: true, force: true });
mkdirSync(framesDir, { recursive: true });
const browser = spawn(edge, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--remote-debugging-port=${port}`, '--remote-allow-origins=*',
  `--user-data-dir=${profile}`, '--window-size=1920,1080', '--force-device-scale-factor=1', 'about:blank'], { stdio: 'ignore' });

const sleep = ms => new Promise(r => setTimeout(r, ms));
let target;
for (let i = 0; i < 50 && !target; i++) {
  try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch { }
  if (!target) await sleep(200);
}
if (!target) { browser.kill(); throw new Error('Edge DevTools did not start'); }

const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = fail; });
let nextId = 1; const pending = new Map(); const events = [];
ws.onmessage = m => { const msg = JSON.parse(m.data); if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); } else events.push(msg); };
const send = (method, params = {}, ms = 15000) => new Promise((ok, fail) => {
  const id = nextId++;
  const timer = setTimeout(() => { pending.delete(id); fail(new Error(`${method} timed out`)); }, ms);
  pending.set(id, msg => { clearTimeout(timer); ok(msg); });
  ws.send(JSON.stringify({ id, method, params }));
});

await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
await send('Page.navigate', { url: page });
for (let i = 0; i < 100 && !events.some(e => e.method === 'Page.loadEventFired'); i++) await sleep(100);
await sleep(800); // let images decode

const duration = (await send('Runtime.evaluate', { expression: 'window.DURATION', returnByValue: true })).result.result.value;
const total = Math.round(duration * fps);
const started = Date.now();
for (let f = 0; f < total; f++) {
  let shot;
  for (let attempt = 1; !shot; attempt++) {
    try {
      await send('Runtime.evaluate', { expression: `renderAt(${(f / fps).toFixed(5)})` });
      shot = await send('Page.captureScreenshot', { format: 'jpeg', quality: 94, captureBeyondViewport: false });
    } catch (err) { if (attempt >= 3) throw err; console.log(`frame ${f}: ${err.message}, retrying`); }
  }
  writeFileSync(join(framesDir, `frame_${String(f).padStart(5, '0')}.jpg`), Buffer.from(shot.result.data, 'base64'));
  if (f % 60 === 0) console.log(`frame ${f}/${total} (${((Date.now() - started) / 1000).toFixed(0)}s)`);
}
console.log(`done: ${total} frames in ${((Date.now() - started) / 1000).toFixed(0)}s`);
ws.close();
// Kill the whole Edge process tree, not just the launcher.
try { execSync(`taskkill /PID ${browser.pid} /T /F`, { stdio: 'ignore' }); } catch { }
// Edge can hold its profile briefly after exiting; a leftover temp folder is harmless.
try { rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 400 }); } catch { }
