'use strict';

const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');

const root = path.resolve(__dirname, '..', 'src', 'DndCompendium.App', 'Resources', 'Raw', 'wwwroot');
const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const server = http.createServer((request, response) => {
  const pathname = new URL(request.url, 'http://localhost').pathname;
  const relative = pathname === '/' ? 'character-sheet.html' : pathname.slice(1);
  const file = path.resolve(root, relative);
  if (!file.startsWith(root) || !fs.existsSync(file)) { response.writeHead(404).end(); return; }
  response.setHeader('Content-Type', path.extname(file) === '.js' ? 'text/javascript' : 'text/html; charset=utf-8');
  fs.createReadStream(file).pipe(response);
});

async function main() {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  const debugPort = 9331;
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'drivebrew-sheet-'));
  const browser = spawn(chromePath, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    `--remote-debugging-port=${debugPort}`, `--user-data-dir=${profile}`,
    `http://127.0.0.1:${port}/character-sheet.html`
  ], { stdio: 'ignore' });
  let socket;
  try {
    let target;
    for (let attempt = 0; attempt < 50; attempt += 1) {
      try {
        const targets = await fetch(`http://127.0.0.1:${debugPort}/json/list`).then(result => result.json());
        target = targets.find(item => item.type === 'page' && item.url.includes(`127.0.0.1:${port}`));
        if (target) break;
      } catch { /* Chrome is starting. */ }
      await delay(100);
    }
    assert.ok(target, 'Chrome did not open the character sheet');
    socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    let id = 0;
    const pending = new Map();
    socket.onmessage = event => {
      const message = JSON.parse(event.data);
      if (!message.id || !pending.has(message.id)) return;
      const request = pending.get(message.id); pending.delete(message.id);
      if (message.error) request.reject(new Error(message.error.message)); else request.resolve(message.result);
    };
    const send = (method, params = {}) => new Promise((resolve, reject) => {
      const commandId = ++id; pending.set(commandId, { resolve, reject });
      socket.send(JSON.stringify({ id: commandId, method, params }));
    });
    const evaluate = async expression => {
      const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
      return result.result.value;
    };
    await send('Runtime.enable');
    await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    let applicationReady = false;
    for (let attempt = 0; attempt < 50; attempt += 1) {
      applicationReady = await evaluate(`typeof Store !== 'undefined' && typeof App !== 'undefined'`);
      if (applicationReady) break;
      await delay(100);
    }
    assert.equal(applicationReady, true, 'The character sheet application did not finish loading');
    await evaluate(`(() => { const ch=Store.create('caster',{name:'Prueba móvil',classId:'wizard'}); App.open(ch.id); return ch.id; })()`);
    await delay(150);
    const first = await evaluate(`(() => ({
      pages:[...document.querySelectorAll('[data-page]')].map(x=>x.dataset.page),
      tabs:[...document.querySelectorAll('[data-tab]')].map(x=>x.textContent.trim()),
      overflow:document.documentElement.scrollWidth > innerWidth + 2,
      editable:!!document.querySelector('[data-bind="info.name"]')
    }))()`);
    assert.deepEqual(first.pages, ['front', 'details', 'spells']);
    assert.deepEqual(first.tabs, ['Hoja', 'Detalles', 'Conjuros']);
    assert.equal(first.overflow, false, 'The mobile sheet must not overflow horizontally');
    assert.equal(first.editable, true);

    await evaluate(`(() => {
      const set=(selector,value)=>{const el=document.querySelector(selector);el.value=value;el.dispatchEvent(new Event('input',{bubbles:true}));};
      set('[data-bind="info.name"]','Ada'); set('[data-bind="abilities.str"]','16'); set('[data-bind="hp.max"]','24');
      document.querySelector('[data-tab="details"]').click();
      set('[data-bind="details.backstory"]','Historia guardada');
      document.querySelector('[data-tab="spells"]').click();
      set('[data-bind="spell.lists.1.0.name"]','Shield');
    })()`);
    await delay(500);
    const saved = await evaluate(`(() => { const ch=Store.all()[0]; return {name:ch.info.name,str:ch.abilities.str,hp:ch.hp.max,story:ch.details.backstory,spell:ch.spell.lists[1][0].name,stored:!!localStorage.getItem('hoja5e.v1')}; })()`);
    assert.deepEqual(saved, { name: 'Ada', str: 16, hp: 24, story: 'Historia guardada', spell: 'Shield', stored: true });

    const bridged = await evaluate(`(async () => {
      window.__nativeMessages=[];
      window.HybridWebView={SendRawMessage:value=>window.__nativeMessages.push(JSON.parse(value))};
      const message={type:'load',payload:{characterId:'native-1',characterName:'Migrado',abilityBaseScores:{STR:18},textFields:{ClassLevel:'Wizard 5',HPMax:'31'},attacks:[{name:'Daga',attackBonus:'+6',damage:'1d4+3'}],preparedSpells:[{id:'shield',name:'Shield',level:1}]}};
      window.dispatchEvent(new CustomEvent('HybridWebViewMessageReceived',{detail:{message:JSON.stringify(message)}}));
      await new Promise(resolve=>setTimeout(resolve,100));
      const name=document.querySelector('[data-bind="info.name"]'); name.value='Migrado editado'; name.dispatchEvent(new Event('input',{bubbles:true}));
      await new Promise(resolve=>setTimeout(resolve,450));
      const ch=Store.all()[0];
      return {id:ch.id,name:ch.info.name,className:ch.info.class,level:ch.info.level,str:ch.abilities.str,hp:ch.hp.max,attack:ch.attacks[0].name,spell:ch.spell.lists[1][0].name,messages:window.__nativeMessages.map(x=>x.type)};
    })()`);
    assert.equal(bridged.id, 'native-1');
    assert.equal(bridged.name, 'Migrado editado');
    assert.equal(bridged.className, 'Wizard');
    assert.equal(bridged.level, 5);
    assert.equal(bridged.str, 18);
    assert.equal(bridged.hp, 31);
    assert.equal(bridged.attack, 'Daga');
    assert.equal(bridged.spell, 'Shield');
    assert.ok(bridged.messages.includes('loaded'));
    assert.ok(bridged.messages.includes('htmlSheetSave'));
    console.log(JSON.stringify({ mobile: first, saved, bridged }, null, 2));
    await send('Browser.close');
    await Promise.race([new Promise(resolve => browser.once('exit', resolve)), delay(1500)]);
  } finally {
    socket?.close();
    if (browser.exitCode === null) {
      browser.kill();
      await Promise.race([new Promise(resolve => browser.once('exit', resolve)), delay(1500)]);
    }
    server.close();
    const safeRoot = `${path.resolve(os.tmpdir())}${path.sep}`;
    if (path.resolve(profile).startsWith(safeRoot)) {
      try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 8, retryDelay: 150 }); }
      catch (error) { console.warn(`Temporary browser profile could not be removed: ${error.code}`); }
    }
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
