# Arquitectura offline

## Corte vertical

```text
IDocumentSource -> DownloadManager -> staging + SHA-256 -> TXT persistente
                         |
                         v
              extractor + downloader seguro
                         |
                         v
          manifiesto SQLite + recursos con hash
                         |
                         v
 Biblioteca -> ReaderPage -> HybridWebView local -> marked-hbfm + 5ePHB
```

`IDocumentSource` sigue aislando el origen. `GoogleDriveDocumentSource` publica
las 35 clases y el suplemento compartido mediante IDs estables, consulta
metadatos para detectar cambios y descarga el TXT seleccionado. Tres documentos
empaquetados actúan como respaldo sin red. Una vez instalado, el recorrido de
lectura no invoca la fuente ni solicita autenticación.

## Persistencia

Todos los datos esenciales viven bajo `FileSystem.Current.AppDataDirectory`.
SQLite conserva únicamente rutas relativas. Documentos y recursos se escriben
en staging, se validan y luego se mueven a su ubicación definitiva.

```text
AppDataDirectory/
  metadata/dnd-compendium.db3
  documents/{id-hash}/{sha256-guid-nombre}.txt
  resources/{id-hash}/{sha256}.{ext}
  .staging/*.part
```

El manifiesto de cada documento distingue `Pending`, `Downloading`,
`Available` y `Unavailable`. La Biblioteca y el lector no llaman “offline
completo” a un documento que tenga un recurso requerido ausente. Eliminar una
copia local elimina también su manifiesto y sus recursos.

## Responsabilidades

- `DndCompendium.Core`: modelos, contratos, descargas y sincronización de
  recursos, sin MAUI ni SQLite.
- `DndCompendium.Infrastructure`: SQLite, archivos, hashes, búsqueda, corpus
  local y descarga HTTP endurecida.
- `DndCompendium.App`: Shell, XAML/MVVM, navegación, controles y puente con
  `HybridWebView`.
- `wwwroot`: shell, parser, temas, fuentes y recursos de Homebrewery.
- `renderer`: build reproducible, pruebas estructurales y regresión visual.

## Búsqueda universal offline

`UniversalSearchService` conserva un índice JSON versionado bajo
`AppDataDirectory/search/universal-index.json`. Cada fuente tiene una huella:
los TXT usan su SHA-256 y los catálogos estructurados usan una huella de todos
los campos buscables. Sólo se vuelve a indexar una fuente cuando cambia su
huella; el archivo se reemplaza atómicamente y un fallo de lectura conserva la
última versión válida.

Los TXT se dividen en documento, clase, subclase, feature de clase, feature de
subclase, raza, feature racial, background, feat, equipo u otra sección. El
índice preserva clase y subclase padre, línea, página y ancla. Spells y exploits
usan sus JSON estructurados y se vinculan a sus fichas por identificador.

La consulta se normaliza sin modificar lo mostrado: ignora mayúsculas, tildes,
puntuación y espacios repetidos. El ranking prioriza nombre exacto, nombre
parcial, frase descriptiva, todas las palabras dentro del mismo elemento y,
por último, un error tipográfico pequeño. Los sinónimos mecánicos son una lista
cerrada y los resultados se deduplican por referencia o identidad semántica.

## Puente multiplataforma

C# envía primero cada recurso como base64, el JavaScript crea una URL `blob:`
y luego renderiza el TXT. Se eligió este protocolo porque Android puede
interceptar HTTP/HTTPS mediante `WebResourceRequested`, mientras que iOS usa
esquemas personalizados; el mensaje C# → JavaScript da el mismo comportamiento
en ambas plataformas. El WebView bloquea cualquier solicitud HTTP restante.

Mensajes principales: `ready`, `resource`, `document`, `mode`, `zoom`,
`fontScale`, `navigate`, `rendered`, `pageChanged`, `resourceMissing` y `error`.
