import { createServer } from 'node:http';
import { access, mkdir, readFile, writeFile } from 'node:fs/promises';
import { constants } from 'node:fs';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import pixelmatch from 'pixelmatch';
import { PNG } from 'pngjs';
import { chromium } from 'playwright-core';

const here = dirname(fileURLToPath(import.meta.url));
const workspace = resolve(here, '..');
const webRoot = resolve(workspace, 'src', 'DndCompendium.App', 'Resources', 'Raw', 'wwwroot');
const dataRoot = resolve(workspace, 'test-data', 'homebrewery-real');
const baselineRoot = resolve(here, 'visual-baselines');
const actualRoot = resolve(workspace, 'artifacts', 'visual');
const update = process.argv.includes('--update');
const documents = [
  { file: 'Magus.txt', id: 'magus-real', pages: 39, resource: ['https://i.imgur.com/ov0LfAA.jpg', 'resources/covers/magus-cover.jpg', 'image/jpeg'] },
  { file: 'Psion.txt', id: 'psion-real', pages: 40, resource: ['https://i.imgur.com/6gQDRuK.png', 'resources/covers/psion-cover.png', 'image/png'] },
  { file: 'Backgrounds, Feats & Equipment.txt', id: 'backgrounds-feats-equipment-real', pages: 83, resource: ['https://i.imgur.com/IOyNUC1.jpg', 'resources/covers/backgrounds-cover.jpg', 'image/jpeg'] }
];
const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'], ['.png', 'image/png'], ['.jpg', 'image/jpeg'],
  ['.jpeg', 'image/jpeg'], ['.webp', 'image/webp'], ['.svg', 'image/svg+xml'],
  ['.woff', 'font/woff'], ['.woff2', 'font/woff2'], ['.ttf', 'font/ttf'], ['.otf', 'font/otf']
]);

async function browserExecutable() {
  const candidates = [
    process.env.DND_VISUAL_BROWSER,
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    '/usr/bin/google-chrome', '/usr/bin/chromium', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
  ].filter(Boolean);
  for (const candidate of candidates) {
    try { await access(candidate, constants.X_OK); return candidate; } catch { /* Try next candidate. */ }
  }
  throw new Error('No compatible Chromium browser found. Set DND_VISUAL_BROWSER to its executable.');
}

const server = createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
    if (pathname === '/_framework/hybridwebview.js') {
      response.writeHead(200, { 'Content-Type': 'text/javascript; charset=utf-8' });
      response.end('globalThis.HybridWebView ??= { SendRawMessage() {} };');
      return;
    }
    if (pathname === '/favicon.ico') {
      response.writeHead(204);
      response.end();
      return;
    }
    const requested = pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, '');
    const filePath = resolve(webRoot, requested);
    if (!filePath.startsWith(webRoot + sep)) throw new Error('Path traversal rejected.');
    const bytes = await readFile(filePath);
    response.writeHead(200, { 'Content-Type': mime.get(extname(filePath).toLowerCase()) ?? 'application/octet-stream' });
    response.end(bytes);
  } catch {
    response.writeHead(404);
    response.end('Not found');
  }
});

await new Promise((resolvePromise) => server.listen(0, '127.0.0.1', resolvePromise));
const address = server.address();
const origin = `http://127.0.0.1:${address.port}`;
await mkdir(actualRoot, { recursive: true });
await mkdir(baselineRoot, { recursive: true });

const browser = await chromium.launch({ executablePath: await browserExecutable(), headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1240, height: 1200 }, deviceScaleFactor: 1 });
  const externalRequests = new Set();
  page.on('request', (request) => {
    if (!request.url().startsWith(origin) && !request.url().startsWith('blob:')) externalRequests.add(request.url());
  });
  page.on('console', (message) => {
    if (message.type() === 'error') process.stderr.write(`Browser console: ${message.text()}\n`);
  });
  await page.goto(origin, { waitUntil: 'networkidle' });
  await page.waitForFunction(() => Boolean(globalThis.DndReader));

  for (const documentInfo of documents) {
    const content = await readFile(resolve(dataRoot, documentInfo.file), 'utf8');
    const resourceBytes = await readFile(resolve(dataRoot, documentInfo.resource[1]));
    await page.evaluate(({ source, mimeType, base64 }) => globalThis.DndReader.registerResource({ source, mimeType, base64 }), {
      source: documentInfo.resource[0], mimeType: documentInfo.resource[2], base64: resourceBytes.toString('base64')
    });
    const result = await page.evaluate(async ({ id, file, source }) => globalThis.DndReader.renderDocument({
      id, title: file.replace(/\.txt$/iu, ''), content: source, mode: 'book', zoom: 1
    }), { id: documentInfo.id, file: documentInfo.file, source: content });
    if (result.pageCount !== documentInfo.pages) throw new Error(`${documentInfo.file}: expected ${documentInfo.pages} pages, got ${result.pageCount}.`);
    if (result.metadata.theme !== '5ePHB') throw new Error(`${documentInfo.file}: metadata theme was not applied.`);
    const semantics = await page.evaluate(() => ({
      pages: document.querySelectorAll('.page').length,
      hasP2: Boolean(document.getElementById('p2')),
      metadataShownAsCode: Boolean(document.querySelector('code.language-metadata, code.language-css')),
      tables: document.querySelectorAll('.page table').length,
      hbfmBlocks: document.querySelectorAll('.page .block').length,
      customStyle: Boolean(document.getElementById('document-custom-style')),
      coverUsesBlob: [...document.querySelectorAll('#p1 img')].some((image) => image.src.startsWith('blob:')),
      internalLinks: [...document.querySelectorAll('.page a[href^="#p"]')].length,
      validInternalLinks: [...document.querySelectorAll('.page a[href^="#p"]')]
        .filter((anchor) => document.querySelector(anchor.getAttribute('href'))).length
    }));
    if (semantics.pages !== documentInfo.pages || !semantics.hasP2 || semantics.metadataShownAsCode || !semantics.customStyle || !semantics.coverUsesBlob) {
      throw new Error(`${documentInfo.file}: semantic reader verification failed: ${JSON.stringify(semantics)}`);
    }
    if (semantics.tables === 0 || semantics.hbfmBlocks === 0) throw new Error(`${documentInfo.file}: expected rendered HBFM tables and blocks.`);
    if (semantics.internalLinks > 0 && semantics.validInternalLinks !== semantics.internalLinks) throw new Error(`${documentInfo.file}: an internal page link has no target.`);
    await page.evaluate(async () => { await document.fonts.ready; document.getElementById('p1').scrollIntoView(); });
    await page.waitForTimeout(150);
    const safeName = documentInfo.id;
    const actualPath = resolve(actualRoot, `${safeName}-p1.png`);
    await page.locator('#p1').screenshot({ path: actualPath, animations: 'disabled' });
    const baselinePath = resolve(baselineRoot, `${safeName}-p1.png`);
    if (update) {
      await writeFile(baselinePath, await readFile(actualPath));
      continue;
    }
    const [baselineBytes, actualBytes] = await Promise.all([readFile(baselinePath), readFile(actualPath)]);
    const baseline = PNG.sync.read(baselineBytes);
    const actual = PNG.sync.read(actualBytes);
    if (baseline.width !== actual.width || baseline.height !== actual.height) throw new Error(`${documentInfo.file}: screenshot dimensions changed.`);
    const difference = new PNG({ width: actual.width, height: actual.height });
    const changed = pixelmatch(baseline.data, actual.data, difference.data, actual.width, actual.height, { threshold: 0.1 });
    const ratio = changed / (actual.width * actual.height);
    if (ratio > 0.002) {
      await writeFile(resolve(actualRoot, `${safeName}-p1-diff.png`), PNG.sync.write(difference));
      throw new Error(`${documentInfo.file}: visual regression changed ${(ratio * 100).toFixed(3)}% of pixels.`);
    }
    await page.setViewportSize({ width: 390, height: 844 });
    const controls = await page.evaluate(() => {
      const navigated = globalThis.DndReader.scrollTo('p2');
      globalThis.DndReader.setMode('mobile', 1.2, 1.1);
      const mobile = globalThis.DndReader.getState();
      const pages = [...document.querySelectorAll('.page')];
      const positionedDescendants = pages.flatMap((documentPage) =>
        [...documentPage.querySelectorAll('*')].filter((element) => getComputedStyle(element).position !== 'static'));
      const oversizedImages = pages.flatMap((documentPage) =>
        [...documentPage.querySelectorAll('img, svg, video')].filter((element) =>
          element.getBoundingClientRect().width > documentPage.getBoundingClientRect().width + 1));
      const overflowingPages = pages.filter((documentPage) =>
        documentPage.getBoundingClientRect().width > document.documentElement.clientWidth + 1);
      const oversizedHeadings = pages.flatMap((documentPage) =>
        [...documentPage.querySelectorAll('h1,h2,h3')].filter((element) =>
          Number.parseFloat(getComputedStyle(element).fontSize) > 53));
      return {
        navigated,
        currentPage: mobile.currentPage,
        mode: mobile.mode,
        zoom: mobile.zoom,
        fontScale: mobile.fontScale,
        positionedDescendants: positionedDescendants.length,
        oversizedImages: oversizedImages.length,
        overflowingPages: overflowingPages.length,
        oversizedHeadings: oversizedHeadings.length
      };
    });
    if (!controls.navigated || controls.currentPage !== 2 || controls.mode !== 'mobile' || controls.zoom !== 1.2 || controls.fontScale !== 1.1
      || controls.positionedDescendants !== 0 || controls.oversizedImages !== 0 || controls.overflowingPages !== 0
      || controls.oversizedHeadings !== 0) {
      throw new Error(`${documentInfo.file}: navigation or mobile reading controls failed: ${JSON.stringify(controls)}`);
    }
    await page.evaluate(() => globalThis.DndReader.setMode('book', 1, 1));
    await page.setViewportSize({ width: 1240, height: 1200 });
  }
  const styleSource = 'https://93.184.216.34/offline-probe.css';
  await page.evaluate(({ source, base64 }) => globalThis.DndReader.registerResource({ source, mimeType: 'text/css', base64 }), {
    source: styleSource,
    base64: Buffer.from('#offline-style-probe { color: rgb(12, 34, 56); }', 'utf8').toString('base64')
  });
  await page.evaluate(async ({ styleUrl }) => globalThis.DndReader.renderDocument({
    id: 'offline-style-probe', title: 'Offline style probe', content: '<p id="offline-style-probe">Probe</p>',
    css: `@import url("${styleUrl}");`
  }), { styleUrl: styleSource });
  const styleApplied = await page.locator('#offline-style-probe').evaluate((element) => getComputedStyle(element).color);
  if (styleApplied !== 'rgb(12, 34, 56)') throw new Error(`A locally cached external stylesheet was not applied: ${styleApplied}.`);
  if (externalRequests.size > 0) throw new Error(`The offline reader attempted external requests: ${[...externalRequests].join(', ')}`);
  console.log(`Visual verification passed for ${documents.length} real Homebrewery documents${update ? ' and baselines were updated' : ''}.`);
} finally {
  await browser.close();
  await new Promise((resolvePromise) => server.close(resolvePromise));
}
