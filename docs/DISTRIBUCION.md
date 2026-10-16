# Distribuir y actualizar PokéGrass

## Datos del usuario

La partida activa y las preferencias ya se guardan fuera de la instalación, en `%LOCALAPPDATA%\PokéGrass`. La partida está en `save.json`; las preferencias de jardín, ventanas, volumen y escalado están en archivos JSON en esa misma carpeta. La ruta `POKEGRASS_SAVE_PATH` sigue permitiendo usar un guardado aislado para desarrollo.

La instalación o actualización no copia archivos encima de esa carpeta. No se introdujo una migración de ruta porque la versión actual ya usa AppData. Si un guardado antiguo junto al ejecutable aparece en otra versión, conservarlo y usar la importación de partida; no borrarlo manualmente hasta confirmar que el guardado nuevo contiene la colección completa.

## Copias y recuperación

- Las copias automáticas están en `%LOCALAPPDATA%\PokéGrass\Backups`.
- Se genera una copia después de un guardado confirmado como máximo cada cinco minutos, al iniciar la aplicación y cada 30 minutos durante una sesión larga.
- Se conservan las 10 automáticas más recientes. Las manuales y las previas a una migración no se incluyen en esa rotación.
- Cada carpeta contiene `save.json` y `backup.json`, con versión, fecha, formato e integridad SHA-256.
- En Configuración > General > Copias se puede crear, ver y restaurar una copia. La restauración pide confirmación y conserva el archivo que estaba activo.
- Si la partida no se puede leer, PokéGrass no crea una partida nueva encima: ofrece recuperar la copia válida más reciente. Si no hay una copia válida, deja el archivo original intacto y se cierra.

## Requisitos de empaquetado

PokéGrass es WPF para `net10.0-windows` y carga recursos del directorio `assets` en tiempo de ejecución. La publicación elegida es self-contained `win-x64`, en carpeta y sin trimming ni single-file. Esto incluye .NET para que el amigo no tenga que instalar Visual Studio o el runtime; mantiene rutas dinámicas a sprites, audio y datos.

Se usa Velopack 1.2.161, compatible con el TFM .NET 10 del proyecto. Genera un `Setup.exe`, paquete completo, deltas cuando existe una versión anterior y el feed que Velopack usa para las actualizaciones. La actualización se aplica con el mecanismo de Velopack, no reemplazando ejecutables en uso. El paquete apunta explícitamente a `win-x64`.

## Crear un instalador local

1. Instala .NET 10 SDK en el equipo de desarrollo.
2. Instala la misma versión fijada del CLI: `dotnet tool install --global vpk --version 1.2.161`.
3. Desde la raíz del repositorio ejecuta `./tools/publish-windows.ps1 -Version 0.1.0-beta`.
4. El instalador y paquetes salen en `Releases/`; la publicación de archivos está en `publish/`. Ambos directorios están ignorados por Git.

La primera vez también se puede compilar y comprobar con:

```powershell
dotnet restore src/PokeGrass.App/PokeGrass.App.csproj
dotnet publish src/PokeGrass.App/PokeGrass.App.csproj -c Release -f net10.0-windows -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o publish
& .\publish\PokéGrass.exe --self-test
```

## Crear y publicar una versión

1. Actualiza `<Version>` en `src/PokeGrass.App/PokeGrass.App.csproj`.
2. Crea las notas en `docs/releases/<versión>.md`.
3. Crea una etiqueta con la misma versión, por ejemplo `v0.1.0-beta`.
4. Publica la etiqueta en GitHub. El workflow `.github/workflows/release.yml` compila en Windows, ejecuta los self-tests y crea/sube el instalador y los paquetes Velopack a GitHub Releases. Ese workflow es el único procedimiento de publicación; no crees una Release manual en paralelo.

El workflow rechaza etiquetas que no coincidan con la versión del proyecto y no publica si falla el build o el self-test. No se suben `bin`, `obj`, partidas ni datos privados. Los binarios de distribución solo se adjuntan a GitHub Releases.

Los usuarios deben instalar el `PokeGrass-Setup.exe` inicial desde Releases. Una copia portable o un `dotnet publish` directo no dispone de la integración de instalación necesaria para aplicar actualizaciones.

## Actualizaciones desde la aplicación

La app consulta Releases públicos de `https://github.com/Joaqx3wa/Poke-grass`, incluidas las versiones beta. Al abrir, avisa si encuentra una versión nueva; el usuario puede posponerla. En Configuración > General > Buscar actualizaciones se consulta manualmente y se muestra el estado de descarga. Antes de instalar, crea una copia automática y luego Velopack aplica el paquete y reinicia.

GitHub Releases debe ser público para que las aplicaciones distribuidas puedan consultar y descargar el feed sin credenciales. No se incrustan tokens en el ejecutable. Para un repositorio privado haría falta otro servidor de distribución autenticado o entregar credenciales al cliente, lo cual no es seguro.

## Backups, desinstalación y límites de verificación

La desinstalación de Velopack elimina la aplicación; los datos de `%LOCALAPPDATA%\PokéGrass` no forman parte del paquete de instalación, por lo que se conservan. El usuario puede eliminar esa carpeta manualmente si desea borrar el progreso.

El workflow prueba compilación, assets empaquetados y validaciones integradas en `--self-test`. Una instalación y actualización reales entre dos versiones en una máquina Windows limpia todavía requieren ejecutarse después de publicar dos Releases; no se puede afirmar esa prueba antes de disponer de los paquetes instalados y de una segunda versión.
