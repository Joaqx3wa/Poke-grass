# Clasificación inicial de rarezas de encuentros

Fecha de la revisión: 12 de octubre de 2026. La tabla cubre los 1.340 IDs válidos de `pokemon-catalog.json`; las variantes heredan la categoría de su especie de origen para que cambiar de forma no altere arbitrariamente su rareza.

## Datos y límites

La base de referencia consultada fue `pokemon_species.csv` del proyecto comunitario [PokéAPI](https://github.com/PokeAPI/pokeapi/blob/master/data/v2/csv/pokemon_species.csv), junto con la [documentación del recurso Pokémon Species](https://pokeapi.github.io/pokeapi.co/v2/). Se utilizaron las relaciones de evolución, los indicadores `is_legendary` / `is_mythical`, el estado de bebé y el hábitat. No se usó el número de Pokédex ni la tasa de captura como medida de rareza. La rareza aquí es una regla de aparición de PokéGrass; no representa dificultad de captura.

PokéAPI es una base de datos mantenida por la comunidad, no una tabla oficial de rarezas de los videojuegos. Por eso, la clasificación es una propuesta de diseño editable y documenta los criterios donde los datos no determinan una respuesta única.

## Criterios

- **Legendario (1 %)**: especies marcadas como legendarias o singulares por los campos de PokéAPI. También se elevan explícitamente Type: Null y Silvally, por su origen artificial singular y su obtención restringida en la historia.
- **Épico (4 %)**: líneas pseudo-legendarias: etapa final épica, etapa intermedia rara y primera etapa poco común. Los Ultraentes y los Pokémon Paradoja se colocan aquí como grupos excepcionales, pero separados de la categoría Legendario. La comunicación oficial también nombra por separado a los Ultraentes y a los Pokémon legendarios ([Pokémon.com](https://www.pokemon.com/us/features/ten-tips-for-catching-legendary-pokemon)).
- **Raro (12 %)**: hábitat marcado como raro en PokéAPI, o forma evolucionada terminal; la etapa final de una línea pseudo-legendaria también es épica.
- **Poco común (28 %)**: etapas intermedias, especies con hábitat desconocido o menos accesible, y especies iniciales de líneas de evolución especiales, incluidas las líneas de los iniciales.
- **Común (55 %)**: especies bebé o primeras etapas con hábitats corrientes (cueva, bosque, pradera, mar o borde del agua), y especies sin evolución con esos mismos hábitats.
- Las variantes regionales, de sexo, temporada, postura, patrón y demás formas conservan la categoría de su especie de origen. No se eliminan formas Mega por sufijo: el catálogo se contrasta con `mega-form-exclusions.json`, que es una lista explícita.

Los pesos permanecen exactamente en 55/28/12/4/1. Dentro de la categoría elegida, el juego elige uniformemente entre sus IDs; por ello una especie con más variantes compatibles puede tener más entradas dentro de su grupo. La tabla no redistribuye los pesos según el número de especies o formas.

## Resumen de la tabla

| Categoría | Peso | Formas (IDs) | Especies de origen |
|---|---:|---:|---:|
| Común | 55 % | 134 | 118 |
| Poco común | 28 % | 479 | 409 |
| Raro | 12 % | 520 | 362 |
| Épico | 4 % | 42 | 41 |
| Legendario | 1 % | 165 | 94 |
| **Total** | **100 %** | **1.340** | **1.024** |

El conteo de especies de origen deduplica variantes. El total de formas puede superar al de especies porque cada forma compatible ocupa un ID propio en el catálogo.

## Casos dudosos y decisiones explícitas

- **Ultraentes** como Nihilego, Buzzwole y Poipole: son excepcionales y poco frecuentes, pero se dejan en Épico porque Pokémon los presenta como grupo propio, distinto de los legendarios. Así el 1 % queda para las especies formalmente legendarias/singulares y las dos excepciones narrativas indicadas abajo.
- **Pokémon Paradoja** como Bramaluna/Roaring Moon y Ferropaladín/Iron Valiant: se dejan en Épico por su carácter excepcional y grupo propio; no se reclasifican como legendarios únicamente por ser difíciles de encontrar.
- **Pseudo-legendarios**: el término y su extensión no constituyen una categoría oficial de rareza de encuentro. Se fijó una lista explícita de las diez líneas ampliamente reconocidas; sus finales son Épicos y las etapas previas se distribuyen por evolución. Si se prefiere otro criterio editorial, esta lista puede revisarse sin tocar probabilidades.
- **Type: Null y Silvally**: los datos no los marcan como legendarios, pero se elevan explícitamente a Legendario por ser especies artificiales singulares con obtención narrativa restringida. Es una excepción de diseño, no una afirmación sobre su estatus oficial.
- **Especies de una sola fase y hábitat desconocido**: se colocan en Poco común, ya que el dato no justifica llamarlas comunes ni raras. Las de hábitats conocidos y corrientes quedan en Común.
- **Formas regionales**: heredan la rareza de la especie aunque su distribución geográfica sea distinta. Es una simplificación deliberada para que las variantes estén disponibles bajo una misma clasificación estable.

Las listas por ID, incluidas todas las formas que usan los encuentros, están en `assets/data/encounter-rarity.json`. Las formas Mega y cualquier recurso excluido siguen documentados por separado y no aparecen en la tabla.
