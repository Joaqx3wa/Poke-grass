# 🌿 PokéGrass

**¡Convierte tu escritorio en un pequeño mundo Pokémon!**

PokéGrass es una aplicación interactiva para Windows que permite tener Pokémon animados caminando libremente por tu escritorio mientras estudias, trabajas, juegas o navegas por internet.

Cada Pokémon tiene sus propias animaciones, comportamientos y reacciones, haciendo que tu escritorio se sienta más vivo.

## ✨ Características

- 🌱 **Pokémon en tu escritorio:** observa hasta 6 Pokémon caminando, descansando e interactuando.
- 🔴 **Captura y colección:** encuentra Pokémon salvajes, captúralos con Poké Balls y organízalos en tu PC.
- 🍓 **Cuidados e interacciones:** alimenta a tus Pokémon con bayas, juega con ellos y descubre sus reacciones.
- ⚔️ **Combates espontáneos:** tus Pokémon pueden enfrentarse en combates amistosos con ataques y efectos de diferentes tipos.
- ✨ **Pokémon especiales:** descubre variantes shiny, Pokémon Alfa, formas regionales y diferencias de género.
- 🔮 **Megaevolución:** presencia transformaciones especiales acompañadas de animaciones y efectos.
- 🧬 **Ditto:** observa cómo se transforma en otros Pokémon durante combates e interacciones.
- 🌟 **Evolución:** tus Pokémon pueden evolucionar después de ganar combates, conservando sus características individuales.
- 💕 **Interacciones sociales:** los Pokémon pueden formar parejas, pasear juntos, bailar y mostrar afecto.
- 🖥️ **Escala adaptable:** elige entre x1, x1.5 y x2 para ajustar el tamaño de tus Pokémon a tu monitor.
- 💾 **Progreso persistente:** conserva tu colección y tus Pokémon favoritos entre sesiones.

## 💚 Tu escritorio, tu aventura

En PokéGrass, los Pokémon no son simples mascotas decorativas. Pueden sorprenderte con encuentros, regalos, combates, evoluciones y pequeñas interacciones mientras continúas utilizando tu computadora.

**¡Atrápalos, cuídalos y deja que te acompañen cada día!**

---

*PokéGrass es un proyecto independiente creado por fans y no está afiliado, respaldado ni patrocinado por Nintendo, Game Freak o The Pokémon Company. Pokémon y sus personajes pertenecen a sus respectivos titulares.*

## Compilar

Requiere Windows y .NET 10 SDK:

```powershell
dotnet restore src/PokeGrass.App/PokeGrass.App.csproj
dotnet build src/PokeGrass.App/PokeGrass.App.csproj -c Release
```

## Instalar y actualizar

Consulta [la guía de distribución](docs/DISTRIBUCION.md) para generar el instalador, crear Releases, publicar actualizaciones y recuperar copias de seguridad. El informe de publicación y recursos está en [INFORME_TAMANO.md](docs/INFORME_TAMANO.md).

Las partidas y preferencias se guardan en `%LOCALAPPDATA%\\PokéGrass`, fuera de la instalación. No publiques archivos de guardado ni el contenido de `publish/` o `Releases/` en el repositorio.
