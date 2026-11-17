const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');

const root = path.resolve(__dirname, '..', 'src', 'DndCompendium.App', 'Resources', 'Raw', 'wwwroot');
const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.jpg': 'image/jpeg', '.woff2': 'font/woff2' };
const server = http.createServer((request, response) => {
  const pathname = new URL(request.url, 'http://localhost').pathname === '/' ? '/index.html' : new URL(request.url, 'http://localhost').pathname;
  const file = path.resolve(root, `.${pathname}`);
  if (!file.startsWith(root) || !fs.existsSync(file)) { response.writeHead(404).end(); return; }
  response.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
  fs.createReadStream(file).pipe(response);
});

const delay = (milliseconds) => new Promise(resolve => setTimeout(resolve, milliseconds));

async function main() {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const appPort = server.address().port;
  const debugPort = 9327;
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'drivebrew-reader-'));
  const browser = spawn(chromePath, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    `--remote-debugging-port=${debugPort}`, `--user-data-dir=${profile}`,
    `http://127.0.0.1:${appPort}/index.html`
  ], { stdio: 'ignore' });
  try {
    let target;
    for (let attempt = 0; attempt < 40; attempt += 1) {
      try {
        const targets = await fetch(`http://127.0.0.1:${debugPort}/json/list`).then(result => result.json());
        target = targets.find(item => item.type === 'page');
        if (target) break;
      } catch { /* Chrome is still starting. */ }
      await delay(100);
    }
    if (!target) throw new Error('Chrome did not expose a reader page.');
    const socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    let commandId = 0;
    const pending = new Map();
    socket.onmessage = event => {
      const message = JSON.parse(event.data);
      if (!message.id || !pending.has(message.id)) return;
      const { resolve, reject } = pending.get(message.id);
      pending.delete(message.id);
      if (message.error) reject(new Error(message.error.message)); else resolve(message.result);
    };
    const send = (method, params = {}) => new Promise((resolve, reject) => {
      const id = ++commandId;
      pending.set(id, { resolve, reject });
      socket.send(JSON.stringify({ id, method, params }));
    });
    await send('Runtime.enable');
    await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    await delay(350);

    const pixel = 'iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAYAAAB/qH1jAAAAFUlEQVR42mNkYPj/n4GBgYGJAQoAHg4CAZnfB5QAAAAASUVORK5CYII=';
    const paragraphs = Array.from({ length: 150 }, (_, index) => `Paragraph ${index + 1}. This deliberately long line verifies that readable prose is never clipped outside the book page.`).join('\n\n');
    const source = `# Reader regression\n\n${paragraphs}\n\n\\page\n# Mobile artwork\n\nBefore the illustration.\n\n![Wide illustration](data:image/png;base64,${pixel}){position:absolute,bottom:0,left:0,width:900px,height:420px}\n\nAfter the illustration.`;
    const encoded = Buffer.from(source, 'utf8').toString('base64');
    const renderExpression = `DndReader.renderDocument({title:'Reader regression',mode:'book',content:new TextDecoder().decode(Uint8Array.from(atob('${encoded}'),c=>c.charCodeAt(0)))})`;
    const rendered = await send('Runtime.evaluate', { expression: renderExpression, awaitPromise: true, returnByValue: true });
    if (rendered.exceptionDetails) throw new Error(rendered.exceptionDetails.text);
    const book = await send('Runtime.evaluate', { expression: `(() => { const p=document.querySelector('.page'); const r=p.getBoundingClientRect(); return {repaired:p.classList.contains('readerTextOverflow'), height:r.height, textOutside:[...p.querySelectorAll('p')].some(x=>{const q=x.getBoundingClientRect();return q.right>r.right+1||q.bottom>r.bottom+1;})}; })()`, returnByValue: true });
    await send('Runtime.evaluate', { expression: `DndReader.setMode('mobile')`, returnByValue: true });
    await delay(250);
    const mobile = await send('Runtime.evaluate', { expression: `(() => { const p=document.querySelectorAll('.page')[1]; const i=p.querySelector('img'); const pr=p.getBoundingClientRect(),ir=i.getBoundingClientRect(); return {artwork:i.classList.contains('readerArtwork'), width:ir.width, height:ir.height, inside:ir.left>=pr.left-1&&ir.right<=pr.right+1, pageWidth:pr.width}; })()`, returnByValue: true });
    const fixture = fs.readFileSync(path.resolve(__dirname, '..', 'test-data', 'homebrewery-real', 'Magus.txt'), 'utf8');
    const fixtureEncoded = Buffer.from(fixture, 'utf8').toString('base64');
    const fixtureRender = await send('Runtime.evaluate', {
      expression: `DndReader.renderDocument({title:'Magus',mode:'book',content:new TextDecoder().decode(Uint8Array.from(atob('${fixtureEncoded}'),c=>c.charCodeAt(0)))})`,
      awaitPromise: true,
      returnByValue: true
    });
    const fixtureBook = await send('Runtime.evaluate', { expression: `(() => [...document.querySelectorAll('.page')].filter(p=>p.classList.contains('readerTextOverflow')).map(p=>({page:Number(p.dataset.pageNumber),height:Math.round(p.getBoundingClientRect().height)})))()`, returnByValue: true });
    await send('Runtime.evaluate', { expression: `DndReader.setMode('mobile')`, returnByValue: true });
    await delay(300);
    const fixtureMobile = await send('Runtime.evaluate', { expression: `(() => { const images=[...document.querySelectorAll('.page img')]; return {images:images.length,artwork:images.filter(i=>i.classList.contains('readerArtwork')).length,outside:images.filter(i=>{const p=i.closest('.page').getBoundingClientRect(),r=i.getBoundingClientRect();return r.width>0&&(r.left<p.left-1||r.right>p.right+1);}).length}; })()`, returnByValue: true });
    const result = {
      render: rendered.result.value,
      book: book.result.value,
      mobile: mobile.result.value,
      fixture: { render: fixtureRender.result.value, repaired: fixtureBook.result.value, mobile: fixtureMobile.result.value }
    };
    if (!result.book.repaired || result.book.textOutside) throw new Error(`Book overflow repair failed: ${JSON.stringify(result.book)}`);
    if (!result.mobile.artwork || !result.mobile.inside || result.mobile.width <= 0 || result.mobile.height <= 0) throw new Error(`Mobile artwork repair failed: ${JSON.stringify(result.mobile)}`);
    if (result.fixture.render.pageCount < 20 || result.fixture.render.pageErrors.length > 0 || result.fixture.mobile.outside > 0) throw new Error(`Real Homebrewery fixture failed: ${JSON.stringify(result.fixture)}`);
    console.log(JSON.stringify(result, null, 2));
    await send('Browser.close');
    await Promise.race([new Promise(resolve => browser.once('exit', resolve)), delay(1500)]);
    socket.close();
  } finally {
    if (browser.exitCode === null) browser.kill();
    server.close();
    const resolvedProfile = path.resolve(profile);
    const temporaryRoot = `${path.resolve(os.tmpdir())}${path.sep}`;
    if (resolvedProfile.startsWith(temporaryRoot)) fs.rmSync(resolvedProfile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
