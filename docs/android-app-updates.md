# Actualizaciones de Android

La app consulta `app-update.json` en la última versión estable. Verifica HTTPS, la suma SHA-256, el ID `com.march.dndcompendium`, la versión, la API mínima y que la firma de la APK coincida con la de la instalación. Android abre su instalador oficial y solicita confirmación. La comprobación de actualizaciones es independiente de la biblioteca offline.

La última APK estable verificada fue v1.32, con esta huella de certificado SHA-256:

```text
0393be8524f1adea9c58b3cfccfe2f2f51a2c08049d664d666f87aecbd2b2fe3
```

Comprobá la huella con una APK archivada antes del incidente que estés investigando o con una instalación de confianza. Las APK nuevas con esta misma identidad necesitan la llave que corresponde a ese certificado para conservar los datos durante una actualización.

## Publicación

1. Compilá un commit verificado en una máquina de confianza. Guardá la llave fuera de GitHub, en almacenamiento privado y con una copia de respaldo cifrada.
2. Ejecutá `eng/sign-android-release.ps1` con el `apksigner` del SDK de Android, la APK sin firma, el keystore, su alias, la huella verificada y una ruta de salida nueva. `apksigner` pide la contraseña en la terminal.
3. Probá la actualización encima de la versión anterior. Calculá el SHA-256 de la APK firmada y actualizá el manifiesto con esa suma, la nueva versión y la URL del archivo. Usá `updates/app-update.sample.json` como referencia.
4. Firmá la lista de sumas para que también pueda verificarse fuera de la app. Publicala junto con la APK y su manifiesto. [Distribución segura](security-distribution.md)

El workflow de GitHub recompila y prueba candidatos sin acceder a llaves de producción. El entorno heredado `android-production` requiere revisión. Una vez que tengas una copia recuperable de la llave, verificá la huella antes de quitar los secretos de GitHub. Si hay indicios de que la llave fue copiada, una llave e identidad nuevas con migración de personajes por exportación e importación evitan seguir confiando en la firma anterior.
