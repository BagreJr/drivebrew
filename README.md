# D&D Compendium Offline

Aplicación .NET MAUI para Android e iOS que prioriza una
regla: un documento instalado debe poder abrirse, buscarse y navegarse sin red,
sin Google Drive y sin una sesión de Google activa.

## Estado de la Fase 2

- Catálogo Drive con 42 TXT: 35 clases y 7 suplementos; cada documento se
  descarga por separado y las clases se muestran en orden alfabético inglés.
- Catálogo `spells.json` del mismo Drive con caché offline, ficha completa,
  filtros, ordenamientos y listas persistentes de preparados y favoritos.
- Catálogo `exploit.json` con Exploits, Knacks, Blood Rites y Techniques,
  búsqueda, ficha completa y filtros por clase, tipo y nivel.
- Detección de cambios por fecha/tamaño remoto y actualización conservando la
  copia offline anterior hasta validar la nueva.
- Instalación de TXT y recursos externos (imágenes, fuentes y CSS) con
  manifiesto y estado offline por documento.
- Instalación persistente en `FileSystem.Current.AppDataDirectory`.
- Metadatos e índice local en SQLite.
- Biblioteca, descargas, Spells, Preparados, Exploits, Favoritos y lector local
  con MVVM y Shell.
- Perfiles de personaje con la hoja oficial de tres páginas visible y editable
  directamente en la app, guardado offline, zoom, exportación e importación.
- `HybridWebView` compartido con `marked-hbfm` oficial y los temas Homebrewery
  V3 `Blank`/`5ePHB` empaquetados, sin CDN ni acceso de red durante la lectura.
- Metadata, CSS del documento, bloques HBFM, páginas, columnas, tablas,
  imágenes locales, enlaces internos, modos Libro/Móvil, zoom y tamaño de texto.
- Pruebas del núcleo y de la persistencia independientes del dispositivo.
- APK de depuración Android generado y firmado con la clave local de desarrollo.

La carpeta compartida permite descargas públicas de solo lectura, por lo que no
se necesita Google OAuth. La aplicación sigue las ediciones de los archivos con
los IDs conocidos; agregar o reemplazar un TXT por otro ID requiere actualizar
el catálogo de la aplicación. Abrir un libro instalado nunca solicita red ni
autorización.

## Herramientas

- .NET SDK 10.0.303 y workload `maui-mobile`.
- Android Studio para SDK Manager, Device Manager, AVD y ADB. El proyecto usa
  Android SDK Platform 36.
- Microsoft OpenJDK 21.
- Un Mac con Xcode para compilar, firmar y probar iOS.

Android Studio no compila C#/XAML de MAUI por sí mismo. El SDK y emulador se
administran allí; la solución se compila con .NET, desde la terminal o Visual
Studio.

## Preparación en Windows

```powershell
.\eng\bootstrap.ps1
```

El script instala de forma local y reversible el SDK/workload y el JDK dentro
del workspace. Android Studio debe tener instalado el Android SDK. Si está en
una ubicación no estándar, definí `ANDROID_SDK_ROOT`. Desde **Android Studio →
SDK Manager**, verificá que esté instalada **Android SDK Platform 36** (no solo
36.1).

## Compilar Android

```powershell
.\eng\build-android.ps1 -Configuration Debug
```

El APK firmado de desarrollo queda en:

```text
src/DndCompendium.App/bin/Debug/net10.0-android/com.march.dndcompendium-Signed.apk
```

Para crear un AAB de distribución, primero deben definirse la firma y los
secretos fuera del repositorio.

Las actualizaciones instalables desde la propia aplicación y el workflow manual
de GitHub Releases se describen en [`docs/android-app-updates.md`](docs/android-app-updates.md).

## Compilar para iPhone/iPad

Una APK solamente funciona en Android. La misma aplicación ya incluye el
destino iOS y genera una IPA para iPhone/iPad, pero Apple exige compilarla y
firmarla en un Mac con Xcode y una cuenta de Apple Developer:

```powershell
.\eng\build-ios.ps1 -Configuration Release
```

Las instrucciones de firma e instalación están en
[`docs/ios-installation.md`](docs/ios-installation.md).

## Pruebas

```powershell
.\eng\test.ps1
Push-Location renderer
npm run verify
npm run visual:verify
Pop-Location
```

El estado verificable de cada prueba manual y automatizada está en
[`docs/test-status.md`](docs/test-status.md).

## Estructura

```text
src/
  DndCompendium.Core/            modelos, contratos y coordinador de descargas
  DndCompendium.Infrastructure/  SQLite, archivos persistentes y fuente local
  DndCompendium.App/             páginas XAML, ViewModels y HybridWebView
tests/                            pruebas del núcleo y la infraestructura
renderer/                         procedencia/build reproducible del bundle web
eng/                              preparación local y compilación Android
docs/                             arquitectura, decisiones y estado de pruebas
```

Los archivos sincronizados bajo `sources/` son referencias de solo lectura y
no se modifican.
