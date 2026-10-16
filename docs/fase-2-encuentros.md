# Fase 2: encuentros salvajes

## Ejecución

Desde la carpeta `PokéGrass`:

```powershell
dotnet run --project src/PokeGrass.App/PokeGrass.App.csproj -c Debug
```

El jardín agita una zona de hierba cada 3–8 minutos. Al empezar la agitación, hay 10 segundos para hacer clic en esa misma zona. Si se deja pasar el aviso, los efectos se quitan y se agenda otro intervalo. Ocultar el jardín pausa el aviso o el tiempo restante del intervalo.

Después del clic aparece el Pokémon, reproduce su grito local si existe y queda esperando hasta que se cierre desde el menú de bandeja. Un shiny usa el sprite shiny, el sonido local `shiny_sparkle.mp3` y el efecto de estrellas. No se inicia música de fondo.

## Rarezas

Los pesos acordados están en `assets/data/encounter-rarity.json`: común 55, poco común 28, raro 12, épico 4 y legendario 1. La clasificación inicial contiene las 1.340 formas válidas, una sola vez cada una. Los criterios y casos dudosos están en `assets/data/encounter-rarity-report.md`. Las listas son editables; si falta una asignación, hay duplicados o aparece un ID inválido, el juego falla de forma segura y no modifica los pesos.

## Catálogo y exclusiones

`pokemon-catalog.json` contiene solo IDs exactos presentes en las seis carpetas de encuentro, caminata e iconos, normales y shiny. `pokemon-excluded.csv` y `pokemon-excluded.json` dan el motivo de cada exclusión. `mega-form-exclusions.json` conserva la lista explícita de variantes Mega; los sufijos por sí solos nunca se usan para excluir. Vuelve a ejecutar `tools/GeneratePokemonCatalog.ps1` al cambiar el conjunto de recursos.

## Pruebas de desarrollo

Las opciones de bandeja **Desarrollo (solo Debug)** provocan una agitación, fuerzan un Pokémon normal o shiny y cierran el estado actual. Los encuentros forzados eligen una especie válida al azar y omiten la tabla de rareza y la tirada shiny real. También se pueden probar por línea de comandos:

```powershell
dotnet run --project src/PokeGrass.App/PokeGrass.App.csproj -c Debug -- --dev-shake
dotnet run --project src/PokeGrass.App/PokeGrass.App.csproj -c Debug -- --dev-normal
dotnet run --project src/PokeGrass.App/PokeGrass.App.csproj -c Debug -- --dev-shiny
```

Para validar una especie conocida, `--dev-normal-id=PIKACHU` y `--dev-shiny-id=PIKACHU` fuerzan la forma exacta si está en el catálogo. `--dev-shake-zone=0` fija la primera zona para automatizar clics.

La comprobación integrada se ejecuta con `dotnet run --project src/PokeGrass.App/PokeGrass.App.csproj -c Debug -- --self-test`.

## Posición y profundidad del Pokémon

Las capas originales `OverlayCesped 3.png`, `OverlayCesped2.png` y `OverlayCesped1.png` se mantienen separadas. Sus filas visibles son, respectivamente, y=6–21, y=26–41 e y=46–61 dentro de la cuadrícula de 128×80. Todas se dibujan detrás de la sombra y el Pokémon. Los encuentros usan el lienzo completo de 192×192 y el mismo factor lineal 35/92 (≈0,3804 por píxel fuente), elegido para que los 92 píxeles visibles de Pikachu ocupen 35 unidades de jardín como en la referencia aprobada. Las áreas transparentes del lienzo no se recortan ni influyen en el tamaño. El punto de apoyo se obtiene de los píxeles opacos de los tres últimos renglones visibles y se ancla dentro del césped (x=18–112, y=4–64). La fila de apoyo se elige según la altura visible, sin cambiar la escala: las formas grandes pueden sobresalir por arriba.

Las sombras se cargan sin transformación desde `pokemon/pokemons_shadow/1.png` (72×16), `2.png` (100×20) y `3.png` (136×24), todas RGBA con alfa real. Se selecciona la anchura nativa más cercana al área opaca de los 25 píxeles inferiores del sprite; en empate gana el índice menor. Así, Joltik y Pikachu usan la 1, mientras Wailord usa la 3. La sombra se centra horizontalmente bajo el centro del cuerpo visible y se alinea verticalmente con su borde alfa inferior. Conserva proporción y escala común, y permanece dentro del área verde.

La prueba integrada verifica lienzos completos de especies pequeñas, medianas y grandes, el mismo factor para Joltik/Pikachu/Wailord, el punto de apoyo, las tres sombras, su escala y su contención en el área verde, además de transparencia y alineación de las tres filas. La matriz de referencia de Pokémon sin overlays delanteros y sus sombras está en `outputs/pokemon-shadow-matrix.png`; cada columna usa una escala del jardín y mantiene x=56 para comparar tamaños.

Al hacer clic, desaparece la exclamación; las hojas se dispersan mientras el Pokémon se funde desde opacidad 0 hasta 1 en cinco fotogramas (500 ms nominales). Su rectángulo de dibujo permanece fijo durante el fundido, sin desplazamiento vertical. Las capturas de Joltik, Pikachu y Wailord a lo largo de la transición están en `outputs/encounter-fade-contact-sheet.png`, con los fotogramas y metadatos individuales en `outputs/encounter-fade-sequence/`.

## Límites actuales

Los encuentros aleatorios usan la clasificación inicial descrita arriba. Las capturas, Poké Balls, mochila, PC y recompensas permanecen fuera de esta fase. La reproducción del audio se probó decodificando un OGG local a WAV; la salida acústica depende del dispositivo de Windows.
