#!/usr/bin/env node
// Reusable Chrome DevTools Protocol helper for verifying the Umbraco backoffice dashboards in a
// REAL browser (unit tests cannot see render-timing and Shadow DOM bugs; see docs/EXTENDING.md).
// Needs Node 22+ (native WebSocket + fetch). No dependencies.
//
// Launch Chrome once (a throw-away profile keeps your own sessions out of it):
//   "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --remote-debugging-port=9222 \
//     --user-data-dir=/tmp/chrome-rm --no-first-run https://localhost:44346/umbraco
//
// Usage: node scripts/dev/cdp.mjs <command> [args]
//   login [email] [password]       fill and submit the Umbraco login form
//   goto <url>                     navigate and wait a few seconds
//   reload                         hard reload with the cache disabled (needed to pick up changed JS)
//   viewport <width> <height>      set a CSS-pixel viewport at device pixel ratio 1
//   wait-for <selector> [text] [ms]  poll until an element (optionally containing <text>) exists; the
//                                  backoffice SPA needs a while after a site restart (default 60000 ms)
//   click-text <selector> <text>   real mouse click on the most specific element matching selector
//                                  whose text contains <text> (pierces shadow roots)
//   eval <js>                      evaluate JS in the page; the result is printed as JSON.
//                                  Wrap code that uses await as (async () => { ... })()
//   shot <file.png>                screenshot of the current viewport
//
// In `eval` code, window.__rm provides: findAll(root, selector) / byText(selector, text) that pierce
// shadow roots, and dashboard() which returns the <redirect-manager-dashboard> element.
//
// Environment: CDP_PORT (default 9222), CDP_ORIGIN (default localhost:44346) pick the page target.
//
// Why real mouse events: a synthetic el.click() on SPA router links is unreliable, while
// Input.dispatchMouseEvent at the element's centre behaves like a person clicking.

import fs from 'node:fs';

const PORT = process.env.CDP_PORT ?? '9222';
const ORIGIN = process.env.CDP_ORIGIN ?? 'localhost:44346';

const HELPERS = `
window.__rm = window.__rm || {
  findAll(root, selector) {
    let found = Array.from(root.querySelectorAll(selector));
    for (const el of root.querySelectorAll('*')) {
      if (el.shadowRoot) found = found.concat(this.findAll(el.shadowRoot, selector));
    }
    return found;
  },
  byText(selector, text) {
    const wanted = text.toLowerCase();
    return this.findAll(document, selector)
      .filter(e => (e.textContent || '').trim().toLowerCase().includes(wanted))
      .sort((a, b) => a.textContent.length - b.textContent.length)[0];
  },
  dashboard() { return this.findAll(document, 'redirect-manager-dashboard')[0]; }
};
true`;

async function connect() {
  const targets = await (await fetch(`http://localhost:${PORT}/json/list`)).json();
  const page = targets.find(t => t.type === 'page' && t.url.includes(ORIGIN)) ?? targets.find(t => t.type === 'page');
  if (!page) throw new Error(`No page target on port ${PORT}; is Chrome running with --remote-debugging-port?`);

  const ws = new WebSocket(page.webSocketDebuggerUrl);
  let nextId = 0;
  const pending = new Map();
  ws.addEventListener('message', (event) => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      pending.get(message.id)(message);
      pending.delete(message.id);
    }
  });
  await new Promise((resolve, reject) => {
    ws.addEventListener('open', resolve);
    ws.addEventListener('error', reject);
  });

  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++nextId;
    pending.set(id, (m) => (m.error ? reject(new Error(JSON.stringify(m.error))) : resolve(m.result)));
    ws.send(JSON.stringify({ id, method, params }));
  });

  return { send, close: () => ws.close() };
}

async function evaluate(send, expression) {
  await send('Runtime.evaluate', { expression: HELPERS, returnByValue: true });
  const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description ?? JSON.stringify(result.exceptionDetails));
  }
  return result.result.value;
}

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

const commands = {
  async login(send, [email = 'admin@example.com', password = 'TestAdmin123!']) {
    const script = `(() => {
      const emails = window.__rm.findAll(document, 'input[type=email], input[name=email], input[autocomplete=username]');
      const passwords = window.__rm.findAll(document, 'input[type=password]');
      if (!emails.length || !passwords.length) return 'login form not found';
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
      setter.call(emails[0], ${JSON.stringify(email)});
      emails[0].dispatchEvent(new Event('input', { bubbles: true }));
      setter.call(passwords[0], ${JSON.stringify(password)});
      passwords[0].dispatchEvent(new Event('input', { bubbles: true }));
      const form = passwords[0].closest('form');
      if (form) { form.requestSubmit ? form.requestSubmit() : form.submit(); return 'submitted'; }
      const button = window.__rm.byText('button', 'log');
      if (button) { button.click(); return 'clicked'; }
      return 'no submit path';
    })()`;
    console.log(await evaluate(send, script));
    await sleep(4000);
  },

  async goto(send, [url]) {
    await send('Page.enable');
    await send('Page.navigate', { url });
    await sleep(4000);
    console.log('navigated');
  },

  async reload(send) {
    await send('Network.enable');
    await send('Network.setCacheDisabled', { cacheDisabled: true });
    await send('Page.enable');
    await send('Page.reload', { ignoreCache: true });
    await sleep(4000);
    console.log('reloaded');
  },

  async viewport(send, [width, height]) {
    await send('Emulation.setDeviceMetricsOverride', {
      width: Number(width), height: Number(height), deviceScaleFactor: 1, mobile: false
    });
    console.log(`viewport ${width}x${height}`);
  },

  async 'wait-for'(send, [selector, text = '', ms = '60000']) {
    const deadline = Date.now() + Number(ms);
    while (Date.now() < deadline) {
      const found = await evaluate(send, `!!(${JSON.stringify(text)}
        ? window.__rm.byText(${JSON.stringify(selector)}, ${JSON.stringify(text)})
        : window.__rm.findAll(document, ${JSON.stringify(selector)})[0])`);
      if (found) {
        console.log(`found "${selector}"${text ? ` containing "${text}"` : ''}`);
        return;
      }
      await sleep(500);
    }
    throw new Error(`Timed out waiting for "${selector}"${text ? ` containing "${text}"` : ''}`);
  },

  async 'click-text'(send, [selector, text]) {
    const rect = await evaluate(send, `(() => {
      const el = window.__rm.byText(${JSON.stringify(selector)}, ${JSON.stringify(text)});
      if (!el) return null;
      el.scrollIntoView({ block: 'center', inline: 'center' });
      const r = el.getBoundingClientRect();
      return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
    })()`);
    if (!rect) throw new Error(`No element matching "${selector}" with text "${text}"`);
    await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: rect.x, y: rect.y });
    await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: rect.x, y: rect.y, button: 'left', clickCount: 1 });
    await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: rect.x, y: rect.y, button: 'left', clickCount: 1 });
    console.log(`clicked "${text}" at ${Math.round(rect.x)},${Math.round(rect.y)}`);
    await sleep(1200);
  },

  async eval(send, [js]) {
    console.log(JSON.stringify(await evaluate(send, js), null, 2));
  },

  async shot(send, [file]) {
    await send('Page.enable');
    const { data } = await send('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(file, Buffer.from(data, 'base64'));
    console.log(`saved ${file}`);
  }
};

const [command, ...args] = process.argv.slice(2);
if (!commands[command]) {
  console.error(`Unknown command "${command ?? ''}". Commands: ${Object.keys(commands).join(', ')}`);
  process.exit(2);
}

const cdp = await connect();
try {
  await commands[command](cdp.send, args);
} finally {
  cdp.close();
}
