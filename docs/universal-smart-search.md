# Universal Smart Search — implementación

## Resultado

Se mejoró el buscador existente; no se creó otro flujo ni se modificaron
perfiles, favoritos, preparados o el Resource Manager.

- Búsqueda full-text en nombre, descripción, efecto, requisitos y metadata.
- Normalización case-insensitive, sin tildes, puntuación ni espacios repetidos.
- Frase literal, todas las palabras en el mismo elemento, términos parciales,
  typo pequeño en nombres y sinónimos mecánicos controlados.
- Ranking determinista: nombre exacto, prefijo/parte del nombre, frase en
  descripción, todas las palabras, parcial y fuzzy.
- Tipos separados para Class, Class Feature, Subclass, Subclass Feature, Spell,
  Exploit, Feat, Race, Racial Feature, Background, Equipment y Other.
- Jerarquía explícita `ParentClass`/`ParentSubclass`, incluso con encabezados
  Homebrewery hermanos como `Arcane Traditions` y `Arcane Commander`.
- Fragmento localizado sin usar offsets incorrectos después de normalizar.
- Badge de tipo, jerarquía, nivel/requisitos y palabras resaltadas en cada card.
- Los filtros existentes de categoría, clase/origen y nivel siguen combinándose
  de forma simultánea y conservan estado al volver desde un resultado.
- Índice offline incremental, persistente, atómico y tolerante a catálogos
  temporalmente inaccesibles.

## Correcciones principales

El ranking anterior aceptaba cualquier palabra de una consulta múltiple. Eso
producía falsos positivos entre elementos que sólo contenían `fire`, pero no
`damage reaction`. Ahora todas las palabras deben existir en el mismo elemento.

El clasificador anterior convertía casi todos los encabezados de clases en
features y perdía el contexto cuando el contenedor y las subclases tenían igual
nivel Markdown. Ahora mantiene un contexto estructural de subclase, deja como
Other encabezados narrativos o ambiguos y evita convertir opciones internas de
una disciplina en subclases.

Spells ahora indexa también escuela, casteo, rango, duración, componentes,
materiales, concentración, ritual, clases y origen. Exploits incluye tipo,
nivel, prerrequisito y clases. Esos campos también forman parte de la huella de
actualización.

## Verificación

- 23 pruebas Core y 52 Infrastructure: 75/75 aprobadas.
- Corpus Homebrewery real: Magus, Psion y Backgrounds/Feats/Equipment.
- Catálogos reales: 814 spells y 264 exploits.
- `DEXTERITY SAVING THROW`, `dexterity saving throw` y combinación irregular de
  mayúsculas producen el mismo conjunto.
- `Dexterity Saving Throw` encuentra Acid Splash por descripción.
- `Magnetic Personality` conserva `Alchemist → Amorist`.
- `Arcane Recovery` permanece Class Feature de Wizard.
- `Defensive Stance` permanece Exploit.
- Android Release e iOS Simulator compilan con 0 errores y 0 advertencias.

## Limitaciones conocidas

La biblioteca local disponible usa TXT Homebrewery más `spells.json` y
`exploit.json`; no hay en el repositorio actual un catálogo de clases con
`pagesByCategory` para integrar o probar. El indexador no inventa esa metadata:
si se incorpora ese formato en una actualización futura, necesitará un adaptador
explícito al mismo modelo de índice.

La prueba táctil final en el dispositivo quedó condicionada a desbloquear su
pantalla con huella/patrón. La APK se instaló preservando datos y no registró un
crash; navegación por referencia y fallback de página permanecen implementados.
