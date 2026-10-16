# PokéGrass

Juego de escritorio para Windows hecho con WPF y .NET 10. Incluye Pokémon caminando por el escritorio, jardín de encuentros, combates, PC y colección persistente.

## Compilar

Requiere Windows y .NET 10 SDK:

```powershell
dotnet restore src/PokeGrass.App/PokeGrass.App.csproj
dotnet build src/PokeGrass.App/PokeGrass.App.csproj -c Release
```

## Instalar y actualizar

Consulta [la guía de distribución](docs/DISTRIBUCION.md) para generar el instalador, crear Releases, publicar actualizaciones y recuperar copias de seguridad. El informe de publicación y recursos está en [INFORME_TAMANO.md](docs/INFORME_TAMANO.md).

Las partidas y preferencias se guardan en `%LOCALAPPDATA%\PokéGrass`, fuera de la instalación. No publiques archivos de guardado ni el contenido de `publish/` o `Releases/` en el repositorio.
