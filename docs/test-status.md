# Estado de pruebas — Fase 2

Este archivo distingue lo ejecutado de lo pendiente. Compilar no equivale a
aprobar una prueba de dispositivo.

## Automatización ejecutada

| Verificación | Resultado |
|---|---|
| `DndCompendium.Core.Tests` | 39/39 aprobadas |
| `DndCompendium.Infrastructure.Tests` | 64/64 aprobadas |
| Smart Search: mayúsculas, tildes, puntuación y aliases | Aprobada |
| Smart Search: full-text real (`Dexterity Saving Throw` → Acid Splash) | Aprobada con `artifacts/spells.json` (814 spells) |
| Smart Search: Class Feature/Subclass Feature | Aprobada con Wizard, Alchemist, Magus y Psion |
| Smart Search: Exploit real (`Defensive Stance`) | Aprobada con `artifacts/exploit.json` (264 entradas) |
| Smart Search: frase, todas las palabras, parcial, typo y ranking | Aprobada; consultas múltiples no aceptan una sola palabra aislada |
| Smart Search: filtros combinados y deduplicación | Aprobada |
| Smart Search: actualización incremental y perfiles | Aprobada; altas/bajas no cambian personaje ni favoritos |
| Actualizador APK: detección, versión superior y canal estable | Aprobada; compara `versionCode`, no texto |
| Actualizador APK: descarga, progreso, SHA-256 y cancelación | Aprobada; elimina archivos parciales |
| Actualizador APK: paquete, firma, API mínima y downgrade | Aprobada; rechaza candidatos incompatibles |
| Actualizador APK: servidor caído/offline | Aprobada; no altera la copia local |
| Actualizador APK: postergación del aviso | Aprobada; no repite la misma versión antes de 24 h y avisa de inmediato una versión distinta |
| Actualizador APK: transporte y manifiesto hostil | Aprobada; rechaza HTTP inicial/final y paquetes ajenos |
| Workflow de GitHub Releases | YAML validado; ejecución remota pendiente hasta crear/conectar el repositorio |
| Catálogo Drive | 35 clases + 7 suplementos; 42/42 TXT descargados en el dispositivo |
| `spells.json` de Drive | 814/814 filas cargadas; caché y preparado persistentes aprobados |
| `exploit.json` de Drive | 264/264 filas cargadas; caché, cuatro tipos y clases inferidas aprobados |
| Favoritos de Spells | Escritura, reapertura offline y listado persistente aprobados |
| Detección de actualización por versión remota | Aprobada; conserva la copia local validada |
| Persistencia/reapertura de TXT y SQLite | Aprobada |
| Staging, checksum, reemplazo atómico y rutas hostiles | Aprobada |
| Búsqueda local sin fuente remota | Aprobada |
| Pausa, reanudación, cancelación y reintento | Aprobada |
| Descargar todo: continúa tras fallo y prioriza todos los TXT | Aprobada |
| Recursos externos fallidos: aislamiento y espera antes del reintento | Aprobada; una segunda descarga termina sin bucle |
| Extracción de imágenes, fuentes y estilos | Aprobada |
| Manifiesto y recursos persistentes tras reinicio | Aprobada |
| Fallo parcial: conserva recursos válidos y marca faltantes | Aprobada |
| Bloqueo de red privada/redirección SSRF y validación MIME | Aprobada |
| Verificador HBFM | 162 páginas reales aprobadas |
| Regresión visual Chromium | 3/3 documentos aprobados |
| Vista móvil de 390 px: ancho, imágenes y posicionamiento | Aprobada en 162 páginas reales |
| Metadata, CSS, tema 5ePHB, tablas y bloques | Aprobada |
| Tipografía móvil de tablas, espaciado y números tabulares | Aprobada |
| Portadas desde recursos blob offline | Aprobada |
| Enlaces internos, navegación, zoom y modo móvil | Aprobada |
| Hoja CSS externa almacenada y aplicada sin red | Aprobada |
| Solicitudes de red durante render visual | 0 |
| Compilación Android Debug | Aprobada; 0 errores, 0 advertencias |
| Compilación compartida iOS Simulator | Aprobada; 0 errores, 0 advertencias |
| APK autónomo (sin Fast Deployment) | Instalación y arranque aprobados |
| Mensajes HybridWebView en hilo principal | Lector estable; sin crash nativo/administrado |

Resultado .NET total: **103 aprobadas, 0 fallidas, 0 omitidas**.

El corpus es el descargado del Drive suministrado por el propietario. Los hashes
y los IDs de archivo se documentan en
[`test-data/homebrewery-real/PROVENANCE.md`](../test-data/homebrewery-real/PROVENANCE.md).

## Matriz de dispositivo

| Caso | Android | iOS | Observación |
|---|---|---|---|
| Instalar y abrir biblioteca/Backgrounds | Aprobado | Pendiente | TECNO SPARK 30 Pro, Android 14; iOS exige Mac |
| Abrir sin Wi-Fi ni datos móviles | Aprobado | Pendiente | Wizard abrió 56 páginas con recursos offline |
| Reemplazar APK, reabrir y conservar la copia local | Aprobado | Pendiente | Wizard permaneció disponible tras varias reinstalaciones `-r` |
| Actualizar APK 1.1/código 2 a 1.3/código 4 | Aprobado | No aplica | Misma fecha de primera instalación; 42 TXT, 484 recursos, perfiles, favoritos y preparados presentes |
| Comprobación periódica de nueva APK | Aprobado | No aplica | `JobScheduler` registrado y en estado `waiting` con requisito de red |
| Comparar páginas, tablas, imágenes y TOC | WebView y Chromium aprobados | Pendiente | Backgrounds renderizó 83 páginas en Android |
| Navegación entre páginas | Aprobado | Pendiente | Wizard: cambio comprobado de 1/56 a 2/56 |
| Tamaño de texto y controles compactos | Aprobado | Pendiente | 100 % → 110 %; todos los controles visibles a 360 px |
| Gestos, zoom y rotación | Parcial | Pendiente | Zoom y navegación aprobados; rotación manual pendiente |
| Descargar recursos remotos desde red real | Aprobado | Pendiente | Wizard quedó “Offline completo” |
| Descargar todo el catálogo | Aprobado | Pendiente | 42/42 TXT, `spells.json` y `exploit.json`; repetición completada sin quedar bloqueada por enlaces caídos |
| Filtros, orden y preparados de spells | Aprobado | Pendiente | Nivel, concentración, ritual, Z–A, detalle y persistencia comprobados por ADB |
| Favoritos de spells | Aprobado | Pendiente | Abduct marcado, persistido y mostrado en Favoritos por ADB |
| Filtros y detalle de Exploits | Aprobado | Pendiente | Clase, tipo, nivel, Z–A y ficha completa comprobados por ADB |
| Instalar Smart Search 1.4/código 5 preservando datos | Aprobado | No aplica | Primera instalación conservada; 42 TXT y JSON siguen presentes |
| Actualizar 1.4/código 5 a 1.5/código 6 | Aprobado | No aplica | `adb install -r` aceptó la misma firma y package ID; `firstInstallTime` permaneció en 18/10/2026 y la app abrió sin excepción fatal |
| Actualizar 1.5/código 6 a 1.6/código 7 | Aprobado | No aplica | La APK pública de GitHub se instaló con `adb install -r`; conservó `firstInstallTime`, datos y certificado |
| Smart Search visual y búsqueda Spells/Restrained | Aprobado | Pendiente | 29 resultados reales en TECNO KL7; proceso activo y 0 excepciones fatales después del renderizado |
| Estado compacto de contenido e historial de lectura 1.7 | Build y pruebas aprobados | Pendiente | 83 pruebas aprobadas; Release firmado y hash público verificado. El dispositivo se desconectó antes de la instalación USB |
| Filtro Subclases y jerarquías Homebrewery 1.8 | Build, corpus y 89 pruebas aprobados | Pendiente | El filtro incluye subclases y sus features; 35 TXT reales validados. Release firmado y hash público verificado; no había dispositivo ADB conectado |
| Filtros de Spells y búsqueda manual resaltada 1.9 | Build, catálogo y 98 pruebas aprobados | Pendiente | 814 spells clasificados por casteo y componentes; Release firmado y hash público verificado. No había dispositivo ADB conectado |
| Filtros y orden persistente de Preparados 1.10 | Build Android, 100 pruebas y PWA E2E aprobados | Aprobada | Filtros por contenido, nivel, clase, casteo y componente; orden manual por personaje; se conserva el scroll al marcar Preparado/Favorito |
| Orden táctil de Preparados y Resources 1.12 | Build Android, 101 pruebas y PWA E2E aprobados | Aprobada | Tirador ⋮ con gesto táctil directo para mover Spells, Exploits y Resources; orden persistente por personaje y disponible también dentro de Resources |
| Navegación visual Android 1.13 | Build Android, 101 pruebas y comprobación táctil en TECNO KL7 | Aprobada | Íconos visibles sobre Biblioteca, Buscar, Spells y Exploits; acceso de más opciones representado por tres puntos grandes |
| Arrastre libre 1.14 | Build Android y 101 pruebas automáticas aprobadas | Aprobada con prueba USB pendiente | Pulsación mantenida sobre ⋮, tarjeta elevada que sigue el dedo, desplazamiento en bordes y una sola persistencia al soltar; el teléfono no estaba visible en ADB |
| Reordenamiento visual 1.15 | Build Android y 101 pruebas automáticas | Falló al abrir Preparados | La prueba USB detectó que Android agregaba el controlador de arrastre durante el dibujo de la lista |
| Preparados y Favoritos 1.16 | Build Android, 103 pruebas y comprobación táctil en TECNO KL7 | Aprobada | Preparados abre sin excepción; ambas listas permiten arrastrar con ⋮, apartan las tarjetas, desplazan en los bordes y conservan el orden por personaje |
| Filtros y orden de Resources 1.17 | Build Android y 103 pruebas automáticas | Prueba USB pendiente | Búsqueda; filtros por tipo, recuperación y estado; orden Manual, A–Z, Z–A y por cantidad/nivel sin sobrescribir el orden manual |
| Hoja de personaje PDF 1.18 | Build Android, 103 pruebas y verificación PDF | Prueba USB pendiente | Copia independiente por personaje; 3 páginas, 334 campos AcroForm y 336 widgets preservados; abrir con escritura en Android, importar y guardar/compartir copia |
| Compilar/firmar paquete | APK Debug aprobado | Pendiente | El código iOS y `ios-arm64` compilan; la IPA se detiene por ausencia de firma Apple/Xcode |

La APK más reciente, con actualizador instalable, catálogo Drive, spells,
descarga masiva en dos fases, tablas adaptadas y reflujo móvil, compiló sin
errores y se instaló por USB el 21 de octubre de 2026. La variante Release
conservó la fecha de instalación original, abrió con los 42 documentos y no
produjo ninguna excepción fatal. En Wizard completo (56 páginas),
la inspección del DOM en Android obtuvo 0 superposiciones, 0 medios fuera del
ancho, 0 elementos posicionados y ancho de documento igual al viewport.
El APK final 1.6/código 7 se compiló y publicó automáticamente en GitHub,
se instaló por USB sobre 1.5/código 6 y tiene SHA-256
`5C608493FC6E2C3A634B914B905C2121CED84D48372E34837940C06B43DDDC10`.
El APK 1.7/código 8 se publicó con SHA-256
`91197FE45B318C94EF12B4A15EAB5AA5E02F5A0AABFEFB86D6572E379E5C9063`;
la prueba USB quedó pendiente porque el teléfono dejó de aparecer en ADB.
El APK 1.8/código 9 se publicó con SHA-256
`6E3B9824E86ED761EB39FEC5320B6802DAB1ECE26612CC86787CAD0DEEC0490F`;
el manifiesto público, la versión, el package ID y el certificado fueron verificados.
El APK 1.9/código 10 se publicó con SHA-256
`EF84A58B3708A6C7C4EC6C0923BA0344D706A6AD5D2D7F3E986D5B038C09463E`;
el catálogo real, el manifiesto público, la versión y la firma fueron verificados.
El APK 1.10/código 11 se publicó con SHA-256
`5491D731446BBD466AA15AB643EA10D71C1F1C0F0F24420ED930963E75831955`;
se instaló sobre 1.9 en el TECNO KL7 conservando los datos. En el dispositivo
se verificaron filtros, scroll estable, orden manual persistente y ausencia de
excepciones fatales. La PWA pública versión 14 superó también su prueba E2E.
El APK 1.12/código 13 se publicó con SHA-256
`A5E355A650AB8182767274CCDF1B5CDEA1C257275F3554E35C541DFF5C7FD0EC`;
se instaló en el TECNO KL7 conservando los datos y la fecha original de
instalación. En el dispositivo se verificó el arrastre táctil directo con ⋮,
su persistencia tras reiniciar y la gestión tanto de Preparados como de
Resources. La PWA pública versión 15 superó su prueba E2E equivalente.
El APK 1.13/código 14 se publicó con SHA-256
`FEC920EAA5D3D8AA459A97BBB6751FCF69D4C333B5108A50BBA1C38F755DD2EE`;
se instaló en el TECNO KL7 conservando los 42 documentos y la fecha original
de instalación. Se verificaron visualmente los cinco íconos de la navegación
inferior y el acceso táctil a Biblioteca, Buscar, Spells, Exploits y más opciones.
El APK 1.14/código 15 se publicó con SHA-256
`2A2B308E170633F77394DDEE1D41D236F95BC0055569A48D46F0E58FFF275696`;
el manifiesto, la versión, el package ID y el certificado se verificaron. La
prueba táctil USB quedó pendiente porque el teléfono no apareció conectado en ADB.
El APK 1.15/código 16 se publicó con SHA-256
`4A1F33A8255811DE1C7DE5A61989D25861D7FD873C7316FCF5F6F98238755CE9`;
el manifiesto, la versión, el package ID y el certificado se verificaron. La
prueba USB posterior detectó y reprodujo el cierre al abrir Preparados.
El APK 1.16/código 17 se publicó con SHA-256
`0A31EEC34E30EE16AC5381D856ABE41E53603677F75808387E447EAE10157955`;
el manifiesto, el package ID y el certificado se verificaron. Se instaló sobre
1.15 conservando la fecha de instalación original y todos los datos. Antes de
publicar se comprobaron por USB la apertura de Preparados y Favoritos, el hueco
visual entre tarjetas, el desplazamiento en bordes y la persistencia del orden.
El APK 1.17/código 18 se publicó con SHA-256
`2E0DB7A14BDE6310DA1489827966F989201FF704BC2B3A7A437CAD0A599B7BAE`;
el workflow, las 103 pruebas, el manifiesto público, el package ID y el hash del
asset de GitHub se verificaron. La prueba USB queda pendiente porque el teléfono
no aparece conectado en ADB.
El APK 1.18/código 19 se publicó con SHA-256
`916558CF9DD7B752125D61BD09D339266432ABACE29322CB86DB61DEEF1B1165`;
el workflow, las 103 pruebas, el manifiesto público, el package ID, la firma y
el hash del asset de GitHub se verificaron. La hoja empaquetada conserva sus 3
páginas, 334 campos AcroForm y 336 widgets. La prueba USB queda pendiente porque
el teléfono no aparece conectado en ADB.
El editor interno de Character Sheet 1.19 se verificó mediante build Android,
105 pruebas y revisión visual/PDF. Funciona offline por personaje, cubre 210
campos de texto y 122 casillas, y conserva los 334 campos y 336 widgets AcroForm.
La prueba táctil USB queda pendiente si el teléfono no aparece conectado en ADB.
El APK 1.19/código 20 se publicó con SHA-256
`D9B9E42ADE598558129E924B9DDB89380445E7DCC80EC837595D426F70E2C863`;
el manifiesto, el package ID, la firma, la plantilla incluida y el hash del
asset de GitHub se verificaron. La prueba táctil USB queda pendiente porque el
teléfono no aparece conectado en ADB.
La Character Sheet visual 1.20 se verificó en sus tres páginas a tamaño móvil,
con 210 campos de texto y 122 casillas superpuestos sobre la plantilla original.
La compilación Android y las 109 pruebas finalizaron sin errores. La prueba
táctil USB queda pendiente porque el teléfono no aparece conectado en ADB.

## Limitaciones del equipo

- Android Studio aporta SDK Platform 36 y ADB.
- El dispositivo Android se conectó y probó mediante depuración USB.
- La CPU soporta virtualización, pero el firmware informa
  `VirtualizationFirmwareEnabled=False`; no se puede ejecutar un emulador
  acelerado hasta habilitar AMD-V/SVM en BIOS/UEFI.
- iOS conserva configuración arm64 para iPhone/iPad y el mismo código MAUI,
  pero necesita un Mac con Xcode, simulador/dispositivo y firma de Apple.
