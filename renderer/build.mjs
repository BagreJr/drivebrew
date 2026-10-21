import { createHash } from 'node:crypto';
import { copyFile, cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import less from 'less';

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
const vendorDirectory = resolve(webRoot, 'vendor');
const officialBundleSource = resolve(
  rendererDirectory,
  'node_modules',
  'marked-hbfm',
  'lib',
  'index.umd.js'
);
const officialBundleDestination = resolve(vendorDirectory, 'marked-hbfm.umd.js');
const dependencyBundleDestination = resolve(vendorDirectory, 'hbfm-dependencies.min.js');
const homebreweryRoot = resolve(rendererDirectory, 'upstream', 'homebrewery');
const themeOutputRoot = resolve(webRoot, 'themes', 'V3');

// SHA-256 of lib/index.umd.js from the marked-hbfm@1.0.1 npm package.
const expectedHbfmBundleSha256 = '5ab3d0a6b8125ac4d33b5edc72e41424b05b4013fd6ecdc2f9eaa2c783575bdb';

await mkdir(vendorDirectory, { recursive: true });
await mkdir(resolve(themeOutputRoot, 'Blank'), { recursive: true });
await mkdir(resolve(themeOutputRoot, '5ePHB'), { recursive: true });

const officialBundle = await readFile(officialBundleSource);
const actualHash = createHash('sha256').update(officialBundle).digest('hex');
if (actualHash !== expectedHbfmBundleSha256) {
  throw new Error(
    `marked-hbfm UMD integrity check failed. Expected ${expectedHbfmBundleSha256}, received ${actualHash}.`
  );
}

await build({
  entryPoints: [resolve(rendererDirectory, 'dependency-globals.js')],
  outfile: dependencyBundleDestination,
  bundle: true,
  // Several marked extensions publish a browser UMD file without ESM exports
  // alongside a proper `module` entry. Prefer the ESM source so esbuild can
  // compose one deterministic dependency bundle for the WebView.
  mainFields: ['module', 'main'],
  alias: {
    // marked-variables only needs Parser#evaluate for small arithmetic
    // expressions. The upstream expr-eval package has unresolved code-execution
    // advisories, so the mobile bundle uses a deliberately restricted parser.
    'expr-eval': resolve(rendererDirectory, 'safe-expr-eval.js')
  },
  format: 'iife',
  minify: true,
  target: ['chrome88', 'safari15'],
  legalComments: 'inline',
  charset: 'utf8',
  logLevel: 'info',
  banner: {
    js: '/* Generated from renderer/package-lock.json. Do not edit by hand. */'
  }
});

// Keep the upstream UMD byte-for-byte identical to the npm artifact.
await copyFile(officialBundleSource, officialBundleDestination);

for (const themeName of ['Blank', '5ePHB']) {
  const sourcePath = resolve(homebreweryRoot, 'themes', 'V3', themeName, 'style.less');
  const source = await readFile(sourcePath, 'utf8');
  const compiled = await less.render(source, {
    filename: sourcePath,
    paths: [homebreweryRoot],
    math: 'always',
    javascriptEnabled: false
  });
  const notice = `/* Homebrewery ${themeName} theme, pinned at 6a9d7aff49a1f13375886848c5e2d70e338bd7a3. */\n`;
  await writeFile(resolve(themeOutputRoot, themeName, 'style.css'), notice + compiled.css, 'utf8');
}

await cp(resolve(homebreweryRoot, 'themes', 'assets'), resolve(webRoot, 'assets'), {
  recursive: true,
  force: true
});
await cp(resolve(homebreweryRoot, 'themes', 'fonts'), resolve(webRoot, 'fonts'), {
  recursive: true,
  force: true
});
await copyFile(resolve(homebreweryRoot, 'LICENSE'), resolve(webRoot, 'vendor', 'HOMEBREWERY_LICENSE.txt'));

console.log(`Copied official marked-hbfm@1.0.1 UMD (${actualHash}) and compiled pinned Homebrewery themes.`);
