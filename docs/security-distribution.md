# Aislamiento y distribución de Drivebrew

Los paquetes de prueba de GitHub son candidatos. Para publicar una versión, revisá el código y compilalo desde una copia de confianza. Conservá la llave privada y la copia cifrada de respaldo fuera de GitHub. Un atacante que controle GitHub puede cambiar etiquetas, archivos, sumas y workflows. La verificación necesita una firma y una clave pública o huella confirmada por otro canal.

| Plataforma | Paquete y verificación |
|---|---|
| Android | APK firmada localmente. Android exige una firma compatible para actualizar la instalación. Verificá la llave existente antes de retirar los secretos heredados. [Proceso de firma](android-app-updates.md) |
| iOS | Distribuí por App Store o TestFlight con las credenciales fuera de GitHub. El build de simulador sirve para pruebas. Los datos residen en el contenedor de la app y las conexiones usan ATS. |
| Windows | `eng/build-msix.ps1` genera el MSIX x64 en AppContainer. Para distribuirlo, usá Microsoft Store o un certificado de firma de código confiable cuyo sujeto coincida con `Identity.Publisher` en el manifiesto. Mantené esa identidad en las actualizaciones. Verificá la firma y el editor con `signtool verify /pa /v Drivebrew-AppContainer-x64.msix`. El paquete usa Windows App Runtime y WebView2 compartidos. [AppContainer](https://learn.microsoft.com/en-us/windows/msix/msix-container) |
| Linux | `eng/build-linux.sh` y `eng/build-flatpak.sh` generan el Flatpak x86-64. Publicá desde un repositorio OSTree firmado con una llave GPG independiente y configurá los clientes con su clave pública verificada. Para un archivo `.flatpak` suelto, verificá también la lista de sumas firmada antes de instalar. [Firma y distribución de Flatpak](https://docs.flatpak.org/en/latest/hosting-a-repository.html) |

## Verificación independiente de archivos

Generá `SHA256SUMS` para los archivos revisados usando `sha256sum` y firmalo con `gpg --local-user HUELLA --armor --detach-sign SHA256SUMS`. Entregá la huella completa y la clave pública por un canal independiente de GitHub. El usuario compara la huella, importa la clave validada y corre `gpg --verify SHA256SUMS.asc SHA256SUMS` y `sha256sum -c SHA256SUMS`. Comprobá que GPG identifica al firmante esperado. Una suma descargada en la misma publicación solamente permite detectar daños de transferencia si carece de una firma confiable.

## Datos locales y red

- Android conserva los datos en el espacio privado de la app. El respaldo y la transferencia automáticos de esos archivos están desactivados en el paquete nuevo. Exportá los personajes desde la app antes de cambiar de dispositivo.
- El MSIX y el Flatpak usan un espacio privado nuevo. Para migrar desde los ejecutables anteriores, exportá e importá tus personajes desde la app y descargá la biblioteca en la instalación nueva.
- Flatpak comparte la red para que tu navegador pueda comunicarse con Drivebrew mediante `127.0.0.1` y para sincronizar Drive cuando lo pedís. El servidor se limita a loopback y las operaciones de datos exigen un token de sesión. El paquete evita el acceso general a tu carpeta personal. El navegador que ya usás muestra la interfaz.
- Los documentos locales, catálogos disponibles, recursos y hojas se pueden usar con Internet desconectado. Agregar contenido desde Drive requiere Internet. El lector descarga imágenes por HTTPS y conserva la imagen original cuando está disponible por ese medio.
