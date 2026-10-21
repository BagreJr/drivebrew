# Firma e instalación en iPhone y iPad

La aplicación ya tiene un destino iOS (`net10.0-ios`) y usa el identificador
`com.march.dndcompendium`. Android e iOS no comparten el paquete instalable:
Android usa `.apk`; iOS exige una app firmada y normalmente se entrega como
`.ipa`.

## Lo que necesitás

- Una Mac compatible con la versión vigente de Xcode. Puede ser propia,
  prestada o un equipo Mac remoto al que tengas acceso.
- Una cuenta Apple con autenticación de dos factores.
- La membresía Apple Developer Program para distribuir por TestFlight/App
  Store o para generar perfiles de distribución. Apple informa actualmente un
  costo de USD 99 por año, sujeto al precio regional.
- .NET 10 y el workload `maui-ios` instalados en esa Mac.

La inscripción puede hacerse como persona o como organización. Como persona,
Apple solicita nombre legal y datos de contacto. Como organización también
solicita autoridad legal, entidad jurídica, sitio público y número D‑U‑N‑S.

Inicio oficial: https://developer.apple.com/programs/enroll/

## Crear la firma Apple

### 1. Registrar el identificador de la app

1. Entrá en **Certificates, Identifiers & Profiles**.
2. Abrí **Identifiers**, presioná `+` y elegí **App IDs** → **App**.
3. Elegí **Explicit App ID**.
4. Usá exactamente este Bundle ID: `com.march.dndcompendium`.
5. Confirmá con **Register**.

Guía oficial: https://developer.apple.com/help/account/identifiers/register-an-app-id

### 2. Crear e instalar el certificado

En la Mac:

1. Abrí **Keychain Access**.
2. Elegí **Certificate Assistant** → **Request a Certificate from a
   Certificate Authority**.
3. Indicá tu correo y nombre, dejá vacío el correo de la autoridad y elegí
   **Saved to disk**. Esto crea el archivo `.certSigningRequest` y conserva la
   clave privada en esa Mac.
4. En el portal Apple, abrí **Certificates**, presioná `+` y creá:
   - **Apple Development** para probar directamente en dispositivos; o
   - **Apple Distribution** para TestFlight/App Store.
5. Subí el CSR, descargá el `.cer` y abrilo en la misma Mac para instalarlo en
   Keychain.

CSR oficial: https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request

Podés comprobar el nombre exacto de la identidad instalada con:

```bash
security find-identity -v -p codesigning
```

No compartas ni borres la clave privada asociada al certificado. Sin ella, el
archivo `.cer` por sí solo no permite firmar.

### 3A. Perfil para instalar en un iPhone concreto

1. Obtené el UDID del iPhone desde Finder/Xcode y registralo en **Devices**.
2. En **Profiles**, presioná `+` y elegí **iOS App Development** (pruebas desde
   desarrollo) o **Ad Hoc** (IPA para dispositivos registrados).
3. Seleccioná el App ID `com.march.dndcompendium`, el certificado y el iPhone.
4. Poné un nombre claro al perfil, descargalo y abrilo en la Mac.

Documentación oficial:

- https://developer.apple.com/help/account/devices/register-a-single-device
- https://developer.apple.com/help/account/provisioning-profiles/create-a-development-provisioning-profile
- https://developer.apple.com/help/account/provisioning-profiles/create-an-ad-hoc-provisioning-profile

### 3B. Perfil para TestFlight o App Store

1. En **Profiles**, presioná `+`.
2. Elegí **App Store Connect**.
3. Seleccioná el App ID `com.march.dndcompendium` y el certificado **Apple
   Distribution**.
4. Nombrá, generá, descargá e instalá el perfil en la Mac.

Guía oficial: https://developer.apple.com/help/account/provisioning-profiles/create-an-app-store-provisioning-profile

## Generar la IPA firmada

Copiá este proyecto completo a la Mac. Desde la raíz, abrí PowerShell y
ejecutá:

```powershell
dotnet workload install maui-ios
.\eng\build-ios.ps1 -Configuration Release
```

Si .NET no elige la firma correcta automáticamente, pasá los nombres exactos:

```powershell
.\eng\build-ios.ps1 -Configuration Release `
  -CodesignKey "Apple Distribution: Nombre Legal (TEAMID)" `
  -CodesignProvision "DndCompendium App Store"
```

Para una compilación de desarrollo, reemplazá la identidad y el perfil por los
de **Apple Development**. El resultado se busca en:

```text
src/DndCompendium.App/bin/Release/net10.0-ios/ios-arm64/publish/
```

La referencia oficial de .NET MAUI para publicar por línea de comandos es:
https://learn.microsoft.com/dotnet/maui/ios/deployment/publish-cli?view=net-maui-10.0

## Cómo instalar o distribuir

- **Prueba en tu iPhone:** usá un perfil Development o Ad Hoc que incluya el
  UDID de ese teléfono; instalá desde Xcode o Apple Configurator.
- **TestFlight:** usá firma Apple Distribution y perfil App Store Connect;
  subí la compilación a App Store Connect y agregá testers.
- **App Store:** completá ficha, privacidad, capturas y revisión en App Store
  Connect después de subir la compilación.

## Por qué no se puede finalizar la IPA sólo en Windows

Windows permite editar y validar el código compartido, pero la firma final usa
Xcode, el SDK de iOS, Keychain y las claves privadas instaladas en macOS. Por
eso este equipo Windows no puede producir una IPA válida por sí solo. Una vez
que tengas acceso a una Mac y la membresía activa, el script incluido deja el
proceso reducido a un comando.
