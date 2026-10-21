# Integración de Homebrewery V3

Investigación e implementación fijadas al repositorio oficial
`naturalcrit/homebrewery` 3.24.1, commit
`6a9d7aff49a1f13375886848c5e2d70e338bd7a3` (14 de octubre de 2026), y a
`marked-hbfm` 1.0.1.

## Adaptación usada

La aplicación web completa de Homebrewery requiere React, Vite, Express, Node
y MongoDB; ese servidor no se ejecuta en el teléfono. El lector reutiliza el
motor oficial `marked-hbfm` en un bundle UMD local y reproduce el flujo de
`brewRenderer` dentro de un `HybridWebView` compartido:

1. Normaliza finales de línea y separa los fences iniciales `metadata` y `css`.
2. Lee metadata con el esquema JSON seguro de `js-yaml`.
3. Aplica la herencia compilada `Blank -> 5ePHB` desde el commit fijado.
4. Divide con la expresión oficial de `\page`/`\pagebreak`, conserva la
   inyección de clases/estilos y crea identificadores `p1`, `p2`, etc.
5. Renderiza las páginas de forma secuencial para preservar variables, slugs e
   índices, cediendo cuadros al WebView para no bloquear documentos extensos.
6. Inserta el resultado sanitizado en `.page > .columnWrapper`.

Esto conserva metadata, CSS seguro, bloques `{{...}}`, columnas, tablas, stat
blocks, notes, tipografías, símbolos, fondos, enlaces internos e índices. No se
usa un parser Markdown genérico ni se evalúa JavaScript de los documentos.

## Modos y recursos offline

El modo Libro mantiene las dimensiones, dos columnas, fondos y decoraciones
del tema oficial, con desplazamiento y zoom. El modo Móvil —predeterminado en
teléfonos— refluye a una sola columna, permite cambiar el tamaño de texto y da
desplazamiento horizontal a estructuras anchas. En este modo se eliminan los
posicionamientos absolutos de los descendientes de página, se limitan las
imágenes al ancho disponible y se quitan manchas puramente decorativas. Así el
arte pasa al flujo del documento y no puede tapar el texto; Libro conserva la
composición original.

## Catálogo y actualizaciones desde Drive

El catálogo contiene las 35 clases TXT presentes en la carpeta compartida y
siete suplementos, para un total de 42 documentos. Las clases se presentan en
orden alfabético inglés. Cada entrada usa el identificador estable del archivo
de Google Drive. Al iniciar o actualizar la biblioteca se
consulta `Last-Modified` y el tamaño; si cambia alguno, la aplicación marca la
copia como actualizable sin borrar la versión offline instalada. La descarga
posterior obtiene el contenido actual y vuelve a calcular su SHA-256 local.

Este mecanismo sigue automáticamente las ediciones hechas sobre esos mismos
archivos. Agregar un TXT nuevo o reemplazarlo por otro archivo con un ID nuevo
requiere incorporar ese nuevo ID al catálogo de la aplicación.

`spells.json` también se obtiene mediante su identificador estable. La copia se
valida antes de reemplazar el archivo local y queda disponible offline. Sus 814
filas actuales alimentan las pestañas Spells y Preparados; si cambia el JSON,
el contador y los filtros se reconstruyen desde el contenido nuevo.

`exploit.json` usa el mismo mecanismo de descarga validada y reemplazo atómico.
Sus 264 entradas actuales se clasifican como Exploit, Knacks, Blood Rites o
Techniques y se filtran por clase, tipo y nivel. Los tipos sin columna propia de
clase se asocian con su clase de origen: Ranger, Blood Hunter y Monk.

Al instalar un TXT, el servicio de recursos extrae imágenes Markdown/HTML,
`url(...)`, fuentes, `@import` y hojas de estilo. Cada URL se descarga con
límites de tamaño, validación de firma/MIME, redirecciones acotadas y bloqueo de
direcciones privadas. SQLite conserva estado, hash, tamaño y ruta relativa; el
archivo se escribe primero en staging. El lector recibe bytes locales por el
puente C# y crea URLs `blob:`. Este mecanismo funciona tanto en Android como en
iOS y evita depender de las diferencias de interceptación de solicitudes HTTP
entre Android WebView y WKWebView.

Un recurso externo inaccesible no aborta la descarga global. Su error queda
registrado por URL y no se vuelve a solicitar durante seis horas, evitando que
una segunda pulsación de “Descargar todo” quede esperando repetidamente al
mismo servidor caído.

Una hoja CSS externa disponible se integra como estilo local sanitizado. Sus
URLs absolutas de imágenes o fuentes también pueden resolverse desde el
manifiesto. Los imports anidados dentro de otra hoja externa no se recorren en
esta fase y quedan registrados como una limitación.

## Seguridad

- CSP sin conexiones, frames, formularios, plugins ni scripts remotos.
- Eliminación de `script`, handlers `on*`, URLs activas y elementos embebibles.
- CSS limitado y reescritura exclusiva a recursos empaquetados, `data:` o
  `blob:` autorizados.
- Parser aritmético restringido en lugar de `expr-eval`; el bundle distribuido
  se comprueba para impedir `eval` y `new Function`.
- URLs de descarga solo HTTP/HTTPS públicas; HTTP se admite únicamente para
  imágenes no ejecutables.

## Compatibilidad y límites comprobados

- Las 162 páginas reales de Magus, Psion y Backgrounds/Feats/Equipment pasan
  pruebas estructurales; sus portadas pasan comparación visual en Chromium.
- Android compila con SDK Platform 36. La instalación del APK autónomo, el
  arranque y el render de un documento real de 83 páginas se verificaron en un
  TECNO SPARK 30 Pro con Android 14 mediante ADB inalámbrico.
- iOS conserva `net10.0-ios`, arm64, iPhone/iPad y el mismo código compartido,
  pero WKWebView, firma y empaquetado deben validarse en un Mac con Xcode.
- La fidelidad pixel a pixel de Homebrewery se define sobre Chrome; Android
  WebView y WKWebView pueden variar en métricas tipográficas y saltos finos.
- Antes de publicar en tiendas hay que completar una revisión legal de las
  fuentes, imágenes y elementos de identidad visual del tema 5ePHB. Los avisos
  MIT/OFL/CC incluidos se conservan dentro del paquete.

## Fuentes primarias

- [Homebrewery](https://github.com/naturalcrit/homebrewery)
- [Implementación oficial de brewRenderer](https://github.com/naturalcrit/homebrewery/blob/6a9d7aff49a1f13375886848c5e2d70e338bd7a3/client/homebrew/brewRenderer/brewRenderer.jsx)
- [Helpers oficiales de páginas](https://github.com/naturalcrit/homebrewery/blob/6a9d7aff49a1f13375886848c5e2d70e338bd7a3/shared/helpers.js)
- [marked-hbfm](https://github.com/naturalcrit/marked-hbfm)
- [HybridWebView de .NET MAUI](https://learn.microsoft.com/dotnet/maui/user-interface/controls/hybridwebview?view=net-maui-10.0)
- [WebResourceRequested](https://learn.microsoft.com/dotnet/api/microsoft.maui.controls.hybridwebview.webresourcerequested?view=net-maui-10.0)
