# Actualizaciones de la APK Android

El actualizador de la aplicación es independiente de las actualizaciones de TXT, `spells.json` y `exploit.json`.

## Estado de firma

Las APK verificadas hasta la versión 1.6/código 7 tienen:

- package ID: `com.march.dndcompendium`
- certificado SHA-256: `0393be8524f1adea9c58b3cfccfe2f2f51a2c08049d664d666f87aecbd2b2fe3`
- certificado: Android Debug

Android solo conserva los datos e instala una APK encima de otra cuando coinciden el package ID y la firma, y el `versionCode` aumenta. Para actualizar las instalaciones existentes hay que conservar exactamente el keystore que generó ese certificado. Una clave de producción nueva no puede actualizar esas instalaciones.

## Configuración de publicación

El repositorio oficial es `https://github.com/BagreJr/drivebrew` y la aplicación consulta:

`https://github.com/BagreJr/drivebrew/releases/latest/download/app-update.json`

La publicación automatizada utiliza el environment `android-production` y los secretos `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` y `ANDROID_KEY_PASSWORD`.

`ANDROID_KEYSTORE_BASE64` debe contener el keystore actual codificado en Base64, no uno nuevo. En esta PC se identificó en `%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore`; antes de automatizar la publicación, guardá una copia privada y verificá que su huella SHA-256 sea la indicada arriba.

Las contraseñas y el alias deben corresponder al mismo keystore. Nunca se guarda el archivo ni sus contraseñas en Git. El workflow obtiene además `OWNER/REPOSITORY` del repositorio al compilar, y Settings permite reemplazar la fuente manualmente si alguna vez cambia.

Para copiar el keystore local como Base64 al portapapeles sin crear otro archivo:

```powershell
$keystore = "$env:LOCALAPPDATA\Xamarin\Mono for Android\debug.keystore"
[Convert]::ToBase64String([IO.File]::ReadAllBytes($keystore)) | Set-Clipboard
```

Pegá ese valor en `ANDROID_KEYSTORE_BASE64`. El keystore de Android Debug normalmente usa el alias `androiddebugkey` y la contraseña predeterminada `android`, pero verificá los valores reales de esta instalación antes de crear los otros tres secretos. Aunque sea una clave Debug, el archivo privado debe tratarse como secreto: si se pierde, no se podrán actualizar las instalaciones actuales; si se filtra, otra persona podría firmar una actualización compatible.

## Funcionamiento

La aplicación consulta el manifiesto como máximo cada 12 horas al abrirse y Android agenda una comprobación de red diaria. También puede comprobarse manualmente desde Settings. Si Android informa que no hay Internet, no inicia una petición; la aplicación offline sigue funcionando con normalidad. La consulta del manifiesto tiene un límite de 30 segundos y el canal estable ignora manifiestos preliminares.

Al descargar una actualización se verifica:

1. HTTPS.
2. SHA-256.
3. Package ID.
4. `versionCode` superior al instalado.
5. Certificado compatible con la aplicación instalada, usando `SigningInfo` en Android 9 o superior y la API heredada solo en Android anteriores.
6. API mínima de Android.

Después se abre el instalador oficial. Android puede solicitar habilitar temporalmente “Instalar apps desconocidas” para D&D Compendium y siempre muestra su propia confirmación. No se utiliza root ni instalación silenciosa.

Si se elige **Later**, la misma versión no vuelve a interrumpir durante 24 horas. Una versión distinta sí se anuncia inmediatamente.

Los perfiles, favoritos, preparados, recursos, configuraciones, bases locales y documentos descargados permanecen en el directorio privado de `com.march.dndcompendium`; el actualizador no los modifica.

## Publicar una versión

1. Incrementar `ApplicationVersion` y `ApplicationDisplayVersion`, o proporcionar ambos valores al workflow.
2. Probar la actualización encima de la versión anterior.
3. Ejecutar manualmente el workflow, no al hacer cada commit.
4. El workflow prueba, firma, calcula SHA-256, genera `app-update.json` y publica ambos archivos en un GitHub Release estable.
5. No borrar ni reemplazar el keystore almacenado como secreto.

El workflow crea el tag y el GitHub Release desde el commit seleccionado; no se ejecuta en cada `push`.
