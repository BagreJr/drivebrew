import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { createContext, runInContext, Script } from 'node:vm';
import { fileURLToPath } from 'node:url';
import { Parser as SafeMathParser } from './safe-expr-eval.js';

const rendererDirectory = dirname(fileURLToPath(import.meta.url));
const webRoot = resolve(
  rendererDirectory,
  '..',
  'src',
  'DndCompendium.App',
  'Resources',
  'Raw',
  'wwwroot'
);
const fixtureDirectory = resolve(
  rendererDirectory,
  '..',
  'test-data',
  'homebrewery-real'
);
const expectedHbfmBundleSha256 = '5ab3d0a6b8125ac4d33b5edc72e41424b05b4013fd6ecdc2f9eaa2c783575bdb';

const paths = {
  html: resolve(webRoot, 'index.html'),
  reader: resolve(webRoot, 'js', 'reader.js'),
  css: resolve(webRoot, 'css', 'reader.css'),
  dependencies: resolve(webRoot, 'vendor', 'hbfm-dependencies.min.js'),
  hbfm: resolve(webRoot, 'vendor', 'marked-hbfm.umd.js')
};

const [html, reader, css, dependencies, hbfmBundle] = await Promise.all(
  Object.values(paths).map((path) => readFile(path, 'utf8'))
);
const fixtureNames = ['Magus.txt', 'Psion.txt', 'Backgrounds, Feats & Equipment.txt'];
const expectedPageCounts = [39, 40, 83];
const fixtureSources = await Promise.all(
  fixtureNames.map((name) => readFile(resolve(fixtureDirectory, name), 'utf8'))
);

new Script(reader, { filename: paths.reader });
new Script(dependencies, { filename: paths.dependencies });
new Script(hbfmBundle, { filename: paths.hbfm });

const requiredHtmlFragments = [
  'Content-Security-Policy',
  '_framework/hybridwebview.js',
  'vendor/hbfm-dependencies.min.js',
  'vendor/marked-hbfm.umd.js',
  'js/reader.js'
];
for (const fragment of requiredHtmlFragments) {
  if (!html.includes(fragment)) {
    throw new Error(`index.html is missing required fragment: ${fragment}`);
  }
}

if (/<(?:script|link)[^>]+(?:src|href)=["']https?:\/\//iu.test(html)) {
  throw new Error('index.html must not load remote scripts or styles.');
}
if (!html.includes("connect-src 'none'")) {
  throw new Error("The offline CSP must include connect-src 'none'.");
}
if (!css.includes('.page') || !css.includes('.columnWrapper')) {
  throw new Error('reader.css must style both .page and .columnWrapper.');
}
if (!css.includes('.mode-mobile table')
    || !css.includes('.mode-mobile .classTable')
    || !css.includes('hyphens: none !important')
    || !css.includes('-webkit-hyphens: none !important')) {
  throw new Error('Mobile tables must disable automatic hyphenation in Android and iOS webviews.');
}
if (/\bnew Function\b|\beval\s*\(/u.test(dependencies)) {
  throw new Error('The browser dependency bundle must not contain dynamic JavaScript evaluation.');
}

const safeMath = new SafeMathParser();
safeMath.functions.double = (value) => Number(value) * 2;
if (safeMath.evaluate('double(3) + round(1.6) + 2^3') !== 16) {
  throw new Error('The restricted arithmetic parser failed its compatibility smoke test.');
}
for (const hostileExpression of [
  'constructor.constructor(1)',
  'x = 1',
  '[1, 2]',
  'unknown(1)'
]) {
  try {
    safeMath.evaluate(hostileExpression);
    throw new Error(`Unsafe expression was accepted: ${hostileExpression}`);
  } catch (error) {
    if (String(error).includes('Unsafe expression was accepted')) throw error;
  }
}

const hbfmHash = createHash('sha256').update(hbfmBundle).digest('hex');
if (hbfmHash !== expectedHbfmBundleSha256) {
  throw new Error(`Vendored marked-hbfm UMD hash mismatch: ${hbfmHash}`);
}

const browserContext = createContext({
  console,
  setTimeout,
  clearTimeout,
  URL,
  TextEncoder,
  TextDecoder
});
runInContext(dependencies, browserContext, { filename: paths.dependencies });
runInContext(hbfmBundle, browserContext, { filename: paths.hbfm });

const renderer = browserContext.hbfm?.hbfm;
if (typeof renderer?.render !== 'function' || typeof renderer?.validate !== 'function') {
  throw new Error('The marked-hbfm browser global does not expose render and validate.');
}

const smokeInput = [
  '# Aventurero',
  '',
  '{{note',
  'Una nota **offline**.',
  '}}',
  '',
  '\\column',
  '',
  '| Dado | Resultado |',
  '|:----:|-----------|',
  '| 1d20 | Éxito |'
].join('\n');
const smokeHtml = renderer.render(smokeInput, 0);
if (!smokeHtml.includes('class="block note"') || !smokeHtml.includes('columnSplit') || !smokeHtml.includes('<table>')) {
  throw new Error(`marked-hbfm smoke render did not produce the expected HBFM structures:\n${smokeHtml}`);
}
if (renderer.validate(smokeInput).length !== 0) {
  throw new Error('marked-hbfm rejected the valid smoke document.');
}

let magusHtml = '';
fixtureSources.forEach((source, fixtureIndex) => {
  let body = source.replace(/^\uFEFF/u, '').replaceAll('\r\n', '\n').replaceAll('\r', '\n');
  for (const fence of ['metadata', 'css']) {
    const match = body.match(new RegExp(`^\`\`\`${fence}[^\\n]*\\n[\\s\\S]*?\\n\`\`\`[ \\t]*(?:\\n{1,2}|$)`, 'u'));
    if (match) body = body.slice(match[0].length);
  }
  const pages = body.split(/^(?=\\page(?:break)?(?: *\{[^\n{}]*\})?$)/mu);
  if (pages.length !== expectedPageCounts[fixtureIndex]) {
    throw new Error(`${fixtureNames[fixtureIndex]} expected ${expectedPageCounts[fixtureIndex]} pages, got ${pages.length}.`);
  }

  pages.forEach((page, pageIndex) => {
    const content = /^\\page(?:break)?/u.test(page)
      ? page.slice(page.indexOf('\n') + 1)
      : page;
    const rendered = renderer.render(`${content}\n\n&nbsp;\n\\column\n&nbsp;`, pageIndex);
    if (!rendered.trim()) {
      throw new Error(`${fixtureNames[fixtureIndex]} page ${pageIndex + 1} rendered empty.`);
    }
    if (fixtureIndex === 0) magusHtml += rendered;
  });
});

if (!magusHtml.includes('class="block note"') || !magusHtml.includes('<table>')) {
  throw new Error('The packaged Magus fixture did not render its HBFM note and tables.');
}

console.log('Renderer verification passed: syntax, offline policy, UMD integrity, security checks, and 162 real HBFM pages.');
