# Offline HBFM renderer

This folder makes the static reader under
`src/DndCompendium.App/Resources/Raw/wwwroot` reproducible. The shipped parser is
the official browser UMD file from `marked-hbfm@1.0.1`; it is copied byte for
byte from npm after a SHA-256 check. `hbfm-dependencies.min.js` only supplies the
browser globals that the upstream UMD expects.

The official Homebrewery `Blank` and `5ePHB` Less themes, fonts, backgrounds,
and icon fonts are pinned under `upstream/homebrewery` at commit
`6a9d7aff49a1f13375886848c5e2d70e338bd7a3`. `build.mjs` compiles and copies
them into the app together with their available MIT/OFL/CC notices. A legal and
trade-dress review is still required before public store distribution.

## Rebuild and verify

From this directory:

```text
npm ci
npm run rebuild
npm run visual:verify
```

`package-lock.json` pins the complete dependency graph. Do not commit
`node_modules`; only commit the generated files under `wwwroot/vendor`.

The build checks the upstream UMD against this SHA-256:

```text
5ab3d0a6b8125ac4d33b5edc72e41424b05b4013fd6ecdc2f9eaa2c783575bdb
```

The verifier checks JavaScript syntax, local-only script/style references, CSP,
the vendored UMD hash, a focused smoke render, and all 162 pages in the three
real TXT files. `visual:verify` renders the documents with a system Chromium,
checks metadata, CSS, page counts, tables, HBFM blocks, local image blobs,
internal navigation, mobile controls and golden screenshots.

`marked-variables` declares the unmaintained `expr-eval@2.0.2`, which currently
has unresolved code-execution advisories. The build therefore aliases that
import to `safe-expr-eval.js`, a bounded arithmetic-only compatibility parser.
The verifier rejects `eval`/`new Function` in the shipped dependency bundle and
tests both valid arithmetic and hostile expressions. `npm audit` will still
report the upstream package because it remains in npm's installation graph,
but its implementation is not included in the generated mobile bundle.

## .NET MAUI integration

Use the default HybridWebView root (`wwwroot`) and file (`index.html`). Listen
to `RawMessageReceived`; the reader sends UTF-8 JSON strings through
`window.HybridWebView.SendRawMessage`.

Wait for this message before sending a document:

```json
{
  "type": "ready",
  "protocolVersion": 2,
  "renderer": "marked-hbfm",
  "rendererVersion": "1.0.1"
}
```

Render a downloaded document with `HybridWebView.SendRawMessage`:

```json
{
  "type": "document",
  "payload": {
    "id": "magus",
    "title": "Magus",
    "content": "# Magus\n\nContenido HBFM...",
    "mode": "book",
    "zoom": 0.8,
    "fontScale": 1,
    "fontScale": 1
  }
}
```

Before the document message, C# sends one `resource` message per available
file, containing its original source URL, validated MIME type and base64 bytes.
The reader creates a local `blob:` URL and never exposes a sandbox path.

`content` may also be named `text`, `markdown`, or `rawText`. PascalCase names
from the default .NET JSON serializer are accepted too. Pages are split at a
line containing `\page`, rendered sequentially so HBFM page variables and
heading IDs work, then placed in `.page > .columnWrapper` containers.

Change presentation without reparsing:

```json
{
  "type": "mode",
  "payload": {
    "mode": "mobile",
    "zoom": 1,
    "fontScale": 1.1
  }
}
```

Supported modes are `book` (fixed pages, two columns, zoom) and `mobile`
(single-column vertical reflow). The global API `DndReader.renderDocument`,
`DndReader.setMode`, `DndReader.setZoom`, `DndReader.scrollTo`, and
`DndReader.clear` is also available for `InvokeJavaScriptAsync` integration.

After a successful render the host receives `rendered`; failures are reported
as `error`. Taps on non-local links are cancelled and reported as `link` so the
native layer can decide whether to open them.

## Offline and security contract

- `index.html` loads only packaged files, including
  `_framework/hybridwebview.js` supplied internally by .NET MAUI.
- CSP disables network connections, remote scripts/styles, frames, forms, and
  plug-ins. Images, fonts, and media are limited to app-local, `data:`, and
  `blob:` sources where appropriate.
- Persistent resource files are read by C# and transferred through the bridge;
  this is shared by Android WebView and iOS WKWebView.
- Generated HTML is sanitized: executable/embedding/form elements and event
  handlers are removed, URL-bearing attributes are restricted, and unsafe CSS
  constructs are stripped. This is defense in depth for user-controlled HBFM,
  not a replacement for validating downloaded files and resource mappings.
- Custom document CSS is retained when it uses safe, local resources. Because
  it is authored content, it can still affect the document's appearance.

Third-party versions and license notices ship in
`wwwroot/THIRD_PARTY_NOTICES.txt`.
