# Catálogo de evoluciones de PokéGrass

`pokemon-evolutions.json` es la tabla central de transiciones permanentes. Cada relación guarda un ID de PokéGrass de origen y destino; `requiredSex`, cuando aparece, restringe el sexo que puede evolucionar. Las formas regionales se guardan como relaciones separadas y no se infieren a partir de nombres.

La tabla se generó a partir de los datos públicos de PokeAPI (`pokemon_species`, `pokemon_evolution`, `pokemon` y `pokemon_forms`, consultados el 15 de octubre de 2026), enlazando las formas por sus IDs estables. Se conservaron las ramas compatibles con el catálogo de PokéGrass y se añadieron las relaciones de forma que requieren un ID local explícito, incluida Slowpoke de Galar → Slowbro de Galar.

La elegibilidad omite condiciones de nivel, piedra, intercambio o amistad porque PokéGrass usa la victoria como disparador. Sí conserva restricciones de sexo y forma, y descarta especies que no están en el catálogo, Ditto y formas Mega. Para formas con diferencia visual femenina, el destino femenino se resuelve mediante la variante femenina del catálogo; su falta de sprite propio usa los fallbacks de recursos que ya aplica PokéGrass.

Al crear nuevas formas o evoluciones, actualiza el JSON con IDs existentes, revisa la forma y el sexo en `pokemon-species-metadata.json`, y ejecuta `--evolution-self-test` para comprobar ramas, restricciones y persistencia.
