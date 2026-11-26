const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');

const root = process.env.READER_ASSET_ROOT
  ? path.resolve(process.env.READER_ASSET_ROOT)
  : path.resolve(__dirname, '..', 'src', 'DndCompendium.App', 'Resources', 'Raw', 'wwwroot');
const chromePath = process.env.READER_CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
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
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'drivebrew-reader-'));
  const browser = spawn(chromePath, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--remote-debugging-port=0', `--user-data-dir=${profile}`,
    'about:blank'
  ], { stdio: 'ignore' });
  try {
    let target;
    for (let attempt = 0; attempt < 40; attempt += 1) {
      try {
        const debugPort = Number(fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split(/\r?\n/, 1)[0]);
        const targets = await fetch(`http://127.0.0.1:${debugPort}/json/list`).then(result => result.json());
        target = targets.find(item => item.type === 'page' && item.url === 'about:blank');
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
    await send('Performance.enable');
    await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    const navigation = await send('Page.navigate', { url: `http://127.0.0.1:${appPort}/index.html` });
    if (navigation.errorText) throw new Error(navigation.errorText);
    let readerReady = false;
    for (let attempt = 0; attempt < 50; attempt += 1) {
      const readiness = await send('Runtime.evaluate', {
        expression: `document.readyState === 'complete' && typeof DndReader !== 'undefined' &&
          typeof DndReader.renderDocument === 'function' &&
          [...document.styleSheets].some(sheet => sheet.href?.endsWith('/css/reader.css'))`,
        returnByValue: true
      });
      readerReady = readiness.result.value === true;
      if (readerReady) break;
      await delay(100);
    }
    if (!readerReady) {
      const readerState = await send('Runtime.evaluate', { returnByValue: true,
        expression: `({ url: location.href, ready: document.readyState, api: typeof DndReader,
          styles: [...document.styleSheets].map(sheet => sheet.href), title: document.title })`
      });
      throw new Error(`The offline reader did not finish loading: ${JSON.stringify(readerState.result.value)}`);
    }

    const pixel = 'iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAYAAAB/qH1jAAAAFUlEQVR42mNkYPj/n4GBgYGJAQoAHg4CAZnfB5QAAAAASUVORK5CYII=';
    const paragraphs = Array.from({ length: 150 }, (_, index) => `Paragraph ${index + 1}. This deliberately long line verifies that readable prose is never clipped outside the book page.`).join('\n\n');
    const source = `# Reader regression\n\n![Book background](data:image/png;base64,${pixel}){position:absolute,top:0,left:0,height:100%}\n\n${paragraphs}\n\n\\page\n# Mobile artwork\n\nBefore the illustration.\n\n![Wide illustration](data:image/png;base64,${pixel}){position:absolute,bottom:0,left:0,width:900px,height:420px}\n\nAfter the illustration.`;
    const encoded = Buffer.from(source, 'utf8').toString('base64');
    const renderExpression = `DndReader.renderDocument({title:'Reader regression',mode:'book',content:new TextDecoder().decode(Uint8Array.from(atob('${encoded}'),c=>c.charCodeAt(0)))})`;
    const rendered = await send('Runtime.evaluate', { expression: renderExpression, awaitPromise: true, returnByValue: true });
    if (rendered.exceptionDetails) throw new Error(rendered.exceptionDetails.text);
    const book = await send('Runtime.evaluate', { expression: `(() => { const p=document.querySelector('.page'); const r=p.getBoundingClientRect(),i=p.querySelector('img'),ir=i.getBoundingClientRect(),s=getComputedStyle(i); return {repaired:p.classList.contains('readerTextOverflow'), height:r.height, pageWidth:r.width, imageWidth:ir.width, imageHeight:ir.height,imageClass:i.className,parent:i.parentElement.outerHTML.slice(0,240),frame:i.closest('.readerArtworkFrame')?.className||'',bookWidth:i.style.getPropertyValue('--reader-book-width'),computedWidth:s.width,computedHeight:s.height,position:s.position, textOutside:[...p.querySelectorAll('p')].some(x=>{const q=x.getBoundingClientRect();return q.right>r.right+1||q.bottom>r.bottom+1;})}; })()`, returnByValue: true });
    await send('Runtime.evaluate', { expression: `DndReader.setMode('mobile')`, returnByValue: true });
    await delay(250);
    const mobile = await send('Runtime.evaluate', { expression: `(() => { const p=document.querySelectorAll('.page')[1]; const i=p.querySelector('img'); const pr=p.getBoundingClientRect(),ir=i.getBoundingClientRect(); return {artwork:i.classList.contains('readerArtwork'), width:ir.width, height:ir.height, inside:ir.left>=pr.left-1&&ir.right<=pr.right+1, pageWidth:pr.width}; })()`, returnByValue: true });
    const fixture = fs.readFileSync(path.resolve(__dirname, '..', 'test-data', 'homebrewery-real', 'Magus.txt'), 'utf8');
    const fixtureEncoded = Buffer.from(fixture, 'utf8').toString('base64');
    // Give the public URLs in this fixture local test images so the regression
    // measures downloaded artwork rather than broken-image placeholders.
    const fixtureImages = Object.fromEntries(
      Array.from(fixture.matchAll(/!\[[^\]]*\]\((https?:\/\/[^)\s]+)\)/g), ([, url]) =>
        [url, `data:image/png;base64,${pixel}`])
    );
    const metrics = async () => Object.fromEntries((await send('Performance.getMetrics')).metrics.map(({ name, value }) => [name, value]));
    const beforeFixture = await metrics();
    const fixtureRender = await send('Runtime.evaluate', {
      expression: `DndReader.renderDocument({title:'Magus',mode:'book',resourceMap:${JSON.stringify(fixtureImages)},content:new TextDecoder().decode(Uint8Array.from(atob('${fixtureEncoded}'),c=>c.charCodeAt(0)))})`,
      awaitPromise: true,
      returnByValue: true
    });
    const afterFixture = await metrics();
    const fixtureBook = await send('Runtime.evaluate', { expression: `(() => [...document.querySelectorAll('.page')].filter(p=>p.classList.contains('readerTextOverflow')).map(p=>({page:Number(p.dataset.pageNumber),height:Math.round(p.getBoundingClientRect().height),oversize:[...p.querySelectorAll('.readerBookGeometry')].filter(x=>{const r=x.getBoundingClientRect();return r.width>816.5||r.height>1056.5;}).length})))()`, returnByValue: true });
    await send('Runtime.evaluate', { expression: `DndReader.setMode('mobile')`, returnByValue: true });
    const fixtureMediaReady = await send('Runtime.evaluate', { expression: `(async () => {
      const images = [...document.querySelectorAll('.page img')];
      for (const image of images) image.loading = 'eager';
      await Promise.all(images.map(image => image.decode()));
      await document.fonts.ready;
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      return images.every(image => image.naturalWidth > 0);
    })()`, awaitPromise: true, returnByValue: true });
    if (fixtureMediaReady.exceptionDetails || !fixtureMediaReady.result.value) throw new Error('The local fixture images did not decode.');
    const mobileGeometry = `(() => { const images=[...document.querySelectorAll('.page img')]; const metrics=images.filter(i=>i.classList.contains('readerArtwork')).map(i=>{const page=i.closest('.page'),p=page.getBoundingClientRect(),r=i.getBoundingClientRect(),f=i.closest('.readerArtworkFrame'),fr=f?.getBoundingClientRect();return {ratio:r.width/p.width,frameRatio:fr?fr.width/p.width:0,page:page.dataset.pageNumber,parent:i.parentElement.className||i.parentElement.tagName,frame:f?.className||''};}).sort((a,b)=>a.ratio-b.ratio); const ratios=metrics.map(x=>x.ratio); return {images:images.length,artwork:ratios.length,outside:images.filter(i=>{const p=i.closest('.page').getBoundingClientRect(),r=i.getBoundingClientRect();return r.width>0&&(r.left<p.left-1||r.right>p.right+1);}).length,metrics,medianArtworkWidth:ratios.length?ratios[Math.floor(ratios.length/2)]:0}; })()`;
    let fixtureMobile;
    for (let attempt = 0; attempt < 20; attempt += 1) {
      const measured = await send('Runtime.evaluate', { expression: mobileGeometry, returnByValue: true });
      fixtureMobile = measured.result.value;
      if (fixtureMobile.outside === 0 && fixtureMobile.medianArtworkWidth >= .9) break;
      await delay(100);
    }
    const result = {
      render: rendered.result.value,
      book: book.result.value,
      mobile: mobile.result.value,
      fixture: { render: fixtureRender.result.value, repaired: fixtureBook.result.value, mobile: fixtureMobile },
      fixtureRenderMetrics: Object.fromEntries(
        ['LayoutCount', 'RecalcStyleCount', 'LayoutDuration', 'TaskDuration'].map(name => [name, afterFixture[name] - beforeFixture[name]])
      )
    };
    if (!result.book.repaired || result.book.textOutside) throw new Error(`Book overflow repair failed: ${JSON.stringify(result.book)}`);
    if (result.book.imageWidth > result.book.pageWidth + 1 || result.book.imageHeight > 1057) throw new Error(`Book artwork escaped its original paper: ${JSON.stringify(result.book)}`);
    if (!result.mobile.artwork || !result.mobile.inside || result.mobile.width < result.mobile.pageWidth * .9 || result.mobile.height <= 0) throw new Error(`Mobile artwork repair failed: ${JSON.stringify(result.mobile)}`);
    if (result.fixture.render.pageCount < 20 || result.fixture.render.pageErrors.length > 0 || result.fixture.mobile.outside > 0
      || result.fixture.repaired.some(page => page.oversize > 0) || result.fixture.mobile.medianArtworkWidth < .9) {
      const diagnostic = await send('Runtime.evaluate', { returnByValue: true, expression: `(() => {
        const image = document.querySelector('article[data-page-number="3"] img');
        const rule = (element) => {
          const style = getComputedStyle(element);
          return { class: element.className, width: element.getBoundingClientRect().width, cssWidth: style.width,
            columnCount: style.columnCount, columnWidth: style.columnWidth, position: style.position, zoom: style.zoom };
        };
        const chain = [];
        for (let node = image; node; node = node.parentElement) chain.push(rule(node));
        return { viewport: innerWidth, visualViewport: visualViewport.width, chain };
      })()` });
      throw new Error(`Real Homebrewery fixture failed: ${JSON.stringify({ fixture: result.fixture, diagnostic: diagnostic.result.value })}`);
    }
    console.log(JSON.stringify(result, null, 2));
    await send('Browser.close');
    await Promise.race([new Promise(resolve => browser.once('exit', resolve)), delay(1500)]);
    socket.close();
  } finally {
    if (browser.exitCode === null) {
      browser.kill();
      await Promise.race([new Promise(resolve => browser.once('exit', resolve)), delay(1500)]);
    }
    server.close();
    const resolvedProfile = path.resolve(profile);
    const temporaryRoot = `${path.resolve(os.tmpdir())}${path.sep}`;
    if (resolvedProfile.startsWith(temporaryRoot)) {
      try { fs.rmSync(resolvedProfile, { recursive: true, force: true, maxRetries: 8, retryDelay: 150 }); }
      catch (error) { console.warn(`Temporary browser profile could not be removed: ${error.code}`); }
    }
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
