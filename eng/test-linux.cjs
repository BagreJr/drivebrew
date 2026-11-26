'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { setTimeout: delay } = require('node:timers/promises');

const [executable, ...launchArgs] = process.argv.slice(2);
if (!executable) throw new Error('Pass the Drivebrew executable.');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'drivebrew-linux-test-'));
const chrome = process.env.DRIVEBREW_TEST_CHROME ||
  (process.platform === 'win32' ? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe' : '/usr/bin/google-chrome');
const app = spawn(executable, [...launchArgs, '--no-browser'], {
  env: { ...process.env, DRIVEBREW_DATA_DIR: process.env.DRIVEBREW_TEST_DATA_DIR || path.join(root, 'data') },
  stdio: ['ignore', 'pipe', 'inherit']
});
let browser;
let socket;
let output = '';
app.stdout.setEncoding('utf8');
app.stdout.on('data', chunk => { output += chunk; });

async function until(check, description, attempts = 100) {
  for (let attempt = 0; attempt < attempts; attempt++) {
    const result = await check();
    if (result) return result;
    await delay(100);
  }
  throw new Error(`Timed out: ${description}`);
}

async function main() {
  const url = await until(() => {
    if (app.exitCode !== null) throw new Error(`Drivebrew exited with ${app.exitCode}.`);
    return output.match(/http:\/\/127\.0\.0\.1:\d+\/#([A-F0-9]{64})/)?.[0];
  }, 'loopback startup');
  const address = new URL(url);
  const endpoint = address.origin;
  const token = address.hash.slice(1);
  async function api(route, method = 'GET', payload) {
    const response = await fetch(endpoint + '/api' + route, {
      method, headers: { 'X-Drivebrew-Session': token, 'Content-Type': 'application/json' },
      body: payload === undefined ? undefined : JSON.stringify(payload), signal: AbortSignal.timeout(10000)
    });
    assert.ok(response.ok, `${route}: ${response.status} ${await response.clone().text()}`);
    return (response.headers.get('content-type') || '').includes('json') ? response.json() : response.text();
  }

  assert.equal((await fetch(endpoint + '/api/characters')).status, 403);
  assert.equal((await fetch(endpoint + '/api/characters', {
    headers: { Origin: 'https://other.example', 'X-Drivebrew-Session': token }
  })).status, 403);
  const wrongHost = await new Promise((resolve, reject) =>
    http.get(endpoint + '/api/ping', { headers: { Host: 'localhost' } }, response => {
      response.resume();
      resolve(response.statusCode);
    }).on('error', reject));
  assert.equal(wrongHost, 400);
  assert.ok((await api('/library')).documents.length > 30);
  const importTxt = await fetch(endpoint + '/api/library/import?name=Prueba%20offline.txt', {
    method: 'POST', headers: { 'X-Drivebrew-Session': token, 'Content-Type': 'text/plain' },
    body: '# Prueba offline\n\nEste documento se encontró en el archivo local.'
  });
  assert.equal(importTxt.status, 200, await importTxt.clone().text());
  const imported = await importTxt.json();
  assert.match((await api(`/reader/${imported.id}`)).content, /Prueba offline/);
  assert.ok((await api('/search?q=Prueba%20offline')).some(match => match.entry.documentId === imported.id));
  const created = await api('/characters', 'POST', { name: 'Runa Linux' });
  const resource = await api('/resources', 'PUT', { id: '', name: 'Ki', maximum: 4, current: 4 });
  assert.equal((await api(`/resources/${resource.id}/change`, 'POST', { delta: -1 })).current, 3);
  await api('/saved/Spell/Favorite/test-spell', 'PUT', { enabled: true });
  assert.equal((await api('/saved')).favorite[0].itemId, 'test-spell');
  const sheet = await api(`/characters/${created.id}/sheet`);
  assert.equal(sheet.characterName, 'Runa Linux');
  assert.equal(sheet.preparedSpells.length, 0);
  await api(`/characters/${created.id}/sheet`, 'PUT', {
    sheetJson: JSON.stringify({ chars: [{ id: created.id, type: 'martial', info: { name: 'Runa Linux' } }] })
  });
  const transfer = await api('/characters/export');
  assert.equal(transfer.Character.Id, created.id);
  assert.equal(transfer.Saved.Resources[0].Current, 3);
  assert.equal(JSON.parse(transfer.Sheet.HtmlSheetJson).chars[0].id, created.id);
  assert.equal(await api('/characters/import', 'POST', transfer), 'Runa Linux');
  assert.equal((await api('/characters')).items.filter(item => item.id === created.id).length, 1);
  assert.equal((await api('/characters/backup')).Character.Id, created.id);
  assert.equal((await fetch(endpoint + '/render/js/reader.js')).status, 200);
  assert.equal((await fetch(endpoint + '/render/character-sheet.html')).status, 200);
  assert.equal((await fetch(endpoint + '/')).status, 200);
  assert.equal((await fetch(endpoint + '/desktop.js')).status, 200);

  if (fs.existsSync(chrome)) {
    const profile = path.join(root, 'browser');
    browser = spawn(chrome, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
      '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });
    const target = await until(async () => {
      try {
        const port = fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split(/\r?\n/)[0];
        const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
        return list.find(item => item.type === 'page');
      } catch { return null; }
    }, 'browser startup');
    socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    let sequence = 0;
    const requests = new Map();
    socket.onmessage = event => {
      const message = JSON.parse(event.data);
      const request = requests.get(message.id);
      if (!request) return;
      requests.delete(message.id);
      if (message.error) request.reject(new Error(message.error.message));
      else request.resolve(message.result);
    };
    const send = (method, params) => new Promise((resolve, reject) => {
      const id = ++sequence;
      requests.set(id, { resolve, reject });
      socket.send(JSON.stringify({ id, method, params }));
    });
    await send('Network.enable');
    await send('Network.setBlockedURLs', { urls: ['https://*'] });
    async function evaluate(expression) {
      const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
      return result.result.value;
    }
    await send('Page.navigate', { url });
    await until(() => evaluate(`document.querySelector('#panel')?.textContent.includes('documentos')`), 'library rendered');
    await evaluate(`[...document.querySelectorAll('.card')].find(card => card.textContent.includes('Prueba offline')).querySelector('button').click()`);
    await until(() => evaluate(`document.querySelector('iframe')?.contentDocument?.querySelector('article.page')?.textContent.includes('Prueba offline')`), 'offline reader rendered');
    await evaluate(`document.querySelector('[data-section=characters]').click()`);
    await until(() => evaluate(`[...document.querySelectorAll('.card')].find(card => card.textContent.includes('Runa Linux'))?.textContent`), 'character shown');
    await evaluate(`[...document.querySelectorAll('.card')].find(card => card.textContent.includes('Runa Linux')).querySelector('button').click()`);
    await until(() => evaluate(`document.querySelector('iframe')?.contentDocument?.querySelector('[data-bind="info.name"]')?.value === 'Runa Linux'`), 'editable character sheet');
    await evaluate(`(() => {
      const frame = document.querySelector('iframe');
      const input = frame.contentDocument.querySelector('[data-bind="hp.temp"]');
      input.value = '7';
      input.dispatchEvent(new frame.contentWindow.Event('input', { bubbles: true }));
    })()`);
    await until(async () => {
      const current = await api(`/characters/${created.id}/sheet`);
      return JSON.parse(current.htmlSheetJson).chars[0].hp?.temp === 7;
    }, 'edited sheet persisted from browser');
    await send('Browser.close');
  } else {
    throw new Error(`Browser smoke test needs Chrome: ${chrome}`);
  }
  if (process.platform === 'linux' && !process.env.DRIVEBREW_TEST_DATA_DIR) {
    const status = fs.readFileSync(`/proc/${app.pid}/status`, 'utf8');
    const rss = status.match(/^VmRSS:\s+(\d+) kB$/m)?.[1];
    if (rss) console.log(`Drivebrew backend RSS after the smoke test: ${(Number(rss) / 1024).toFixed(1)} MiB (browser excluded).`);
  }
  await api('/exit', 'POST');
  await until(() => app.exitCode !== null, 'graceful shutdown');
  console.log('Linux desktop verified: offline library, session isolation, sheet editing, character transfer and shutdown.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
}).finally(async () => {
  socket?.close();
  if (browser && browser.exitCode === null) browser.kill();
  if (app.exitCode === null) app.kill();
  await delay(350);
  assert.ok(root.startsWith(os.tmpdir() + path.sep));
  fs.rmSync(root, { recursive: true, force: true, maxRetries: 4, retryDelay: 200 });
});
