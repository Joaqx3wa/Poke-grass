# Fase 1: prototipo técnico del jardín

## Alcance

- Ventana WPF sin bordes, transparente, siempre encima y oculta de la barra de tareas.
- Imagen original `assets/Garden/Jardín.png`, cargada desde disco y copiada al directorio de salida sin transformar el original.
- Menú con cuatro tamaños: 1× (128 × 80 px), 2× (256 × 160 px), 3× (384 × 240 px) y 4× (512 × 320 px). 3× conserva el tamaño de referencia aprobado. Renderizado con vecino más cercano y proporciones originales; los PNG no se modifican.
- Arrastrar el jardín por sus píxeles visibles cuando la posición está desbloqueada.
- Los píxeles de transparencia total dejan pasar el ratón; los píxeles visibles conservan la interacción y permiten arrastrar hierba/jardín.
- Menú de bandeja para mostrar u ocultar, elegir una de las cuatro escalas, bloquear o desbloquear la posición y salir. Las instalaciones nuevas empiezan en 3×; se guarda la escala elegida y se restaura al iniciar.
- Guardado automático de la posición física, escala y bloqueo en `%LOCALAPPDATA%\PokéGrass\garden-settings.json`.
- El jardín siempre se muestra al iniciar, aunque se hubiera ocultado antes. Al cambiar la escala, restaurar el jardín o cambiar de DPI, la ventana conserva su tamaño en píxeles físicos. El movimiento mantiene al menos una zona de 24 × 24 píxeles visible y arrastrable; las escalas mayores que el monitor pueden extenderse fuera de pantalla.
- Sin Pokémon, encuentros, catálogo, almacenamiento de partida ni sonidos en esta fase.

## Recursos

La ventana muestra `assets/Garden/Jardín.png` (128 × 80). Para los encuentros, la aplicación compone en memoria las tres capas originales `OverlayCesped 3.png`, `OverlayCesped2.png` y `OverlayCesped1.png`, conservándolas intactas.

## Diagnóstico y corrección del tamaño visible

La ventana nativa ya tenía el tamaño físico pedido, pero el elemento `Image` usaba `Stretch=None`. WPF dibujaba entonces el bitmap a su tamaño natural, determinado por su DPI integrado (95.9866), dejando el jardín pequeño dentro de la ventana transparente. A 120 DPI, por ejemplo, ese tamaño natural era aproximadamente 160 × 100 px aunque el HWND midiera 384 × 240 px. Mover o reducir la ventana no resolvía esa diferencia entre el área de la ventana y el contenido visible.

Ahora el `Image` usa `Stretch=Uniform` para ocupar el área calculada sin cambiar la proporción, y queda anclado explícitamente arriba a la izquierda. Al probar 1×, WPF ubicaba el contenido 4 px a la derecha y recortaba ese borde; la alineación explícita eliminó el desplazamiento. La imagen decodificada se normaliza solo en memoria a 96 DPI; así cada píxel de origen tiene una correspondencia estable con la escala física seleccionada. `NearestNeighbor` sigue controlando el muestreo. El PNG original no se reescribe.

El preset 3× genera un jardín visible de 384 × 240 px, equivalente al rectángulo de referencia del usuario. En este preset, la ventana y los límites alfa visibles del PNG miden ambos 384 × 240 px. Los otros tamaños son fracciones lineales exactas de ese tamaño: 1× es un tercio, 2× dos tercios y 4× cuatro tercios. Los tamaños se fijan en píxeles físicos y al cambiar de monitor WPF recalcula sus DIPs con el DPI de esa ventana; no se reduce el tamaño en los bordes.

Las configuraciones anteriores con escalas 1×, 2×, 3× o 4× conservan su tamaño equivalente. Los valores anteriores mayores que 4× no tienen equivalente exacto entre las cuatro opciones nuevas y se migran a 3×.

## Compilación

El proyecto apunta a `net10.0-windows` y no declara paquetes NuGet. Se compiló en Debug y Release con .NET SDK 10.0.401: ambos builds terminaron con cero advertencias y cero errores.

## Comprobaciones realizadas

- Debug y Release compilaron con cero advertencias y cero errores.
- Se midieron las cuatro dimensiones físicas y el tamaño visual del elemento WPF en el centro y las cuatro esquinas: 1× 128 × 80, 2× 256 × 160, 3× 384 × 240 y 4× 512 × 320 px. Ningún tamaño cambió al mover la ventana.
- Las capturas reales de las cuatro escalas coincidieron en sus píxeles opacos con el PNG ampliado por el factor elegido, sin interpolación. A 3×, además, las capturas del centro y las cuatro esquinas del monitor de 96 DPI mostraron 384 × 240 px y bloques uniformes de 3 × 3. En el segundo monitor, a 120 DPI, la ventana también midió 384 × 240 px y el contenido opaco coincidió.
- Se comprobó la migración de 3× y 4× antiguos a sus tamaños equivalentes, y de 6×, 8× y 32× a 3×. Una configuración ausente inicia en 3×; se guardó y restauró la posición (1200, 650) y la escala 3×.
- `WindowFromPoint` pasó un punto completamente transparente a la ventana de debajo y asignó un punto opaco de la hierba al jardín. Un arrastre real movió la ventana y guardó su posición.
- No hay un proyecto de pruebas automatizadas. No se eligió la escala mediante el menú de bandeja durante esta comprobación; se verificó el menú a partir de sus cuatro entradas configuradas y la restauración de cada escala desde los ajustes.

## Abrir PokéGrass

Con .NET Desktop Runtime 10 instalado, abre con doble clic:

`src/PokeGrass.App/bin/Release/net10.0-windows/PokéGrass.exe`

El jardín aparece al iniciar. Haz clic izquierdo en su hierba visible y arrastra para moverlo. Haz clic en el icono de la bandeja para mostrar u ocultar el jardín; haz clic derecho para elegir escala, bloquear la posición o salir. Las preferencias quedan en `%LOCALAPPDATA%\PokéGrass\garden-settings.json`.
