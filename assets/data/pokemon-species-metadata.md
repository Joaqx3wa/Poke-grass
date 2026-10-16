# PokéGrass species metadata for capture

`pokemon-species-metadata.json` enriches each valid catalogue form with its base species, Pokédex number, display name, form suffix, and base capture rate. It contains one row for every catalogue ID (1,446 total).

## Source and mapping

The factual fields `id`, `identifier`, and `capture_rate` come from the PokeAPI `pokemon_species.csv` dataset as retrieved on 2026-10-12: <https://github.com/PokeAPI/pokeapi/blob/master/data/v2/csv/pokemon_species.csv>. PokeAPI defines `capture_rate` as the base capture rate, up to 255: <https://pokeapi.co/docs/v2#pokemon-species>.

Catalogue IDs were matched to the longest normalized species identifier prefix (case-insensitive, ignoring separators). This mapped all 1,446 entries. Form IDs remain unchanged in `id`; `form` records the remaining suffix when present. `sex` records curated sex metadata for gender-restricted forms. Ditto is explicitly `genderless`; Arceus, Mew and Mewtwo, including their forms, are male. Gardevoir, Froslass, Miltank, Wormadam, Vespiquen, Illumise, Latias, Happiny, Chansey, Blissey, Vullaby, Mandibuzz, Kangaskhan, Salazzle, Cresselia, Smoochum, Jynx, Bounsweet, Steenee, Tsareena, Flabebe, Floette, Florges, Petilil, Lilligant, Nidoran-female, Nidorina, Nidoqueen, Hatenna, Hattrem, and Hatterene are female. Gallade, Nidoran-male, Nidorino, Nidoking, Tauros, Volbeat, Mothim, Tyrogue, Hitmonlee, Hitmonchan, Hitmontop, Latios, Throh, Sawk, Rufflet, Braviary, Tornadus, Thundurus, Landorus, Impidimp, Morgrem, Grimmsnarl, Okidogi, Munkidori, and Fezandipiti are male. Ursaluna Crimson (`URSALUNA_1`) is specifically male. Pikachu costume IDs 3–7 are female-only, while 8–15 are male-only. Variants use their base species' capture rate and Pokédex number.

The same rows include `types` (one or two type IDs) and `typeSourcePokemon`. Type data was joined from PokeAPI's `pokemon.csv` / `pokemon_types.csv` and, for form-specific typings, `pokemon_forms.csv` / `pokemon_form_types.csv`. Type-changing regional forms and multi-form species such as Rotom, Wormadam, Oricorio, Calyrex, Necrozma, Arceus, and Silvally have explicit PokeAPI source identifiers. Ogerpon's numbered mask sprites map in catalog order to Wellspring, Hearthflame, and Cornerstone masks; its additional Terastallized sprites use its Grass Tera typing. Cosmetic variants inherit the species' type list. Where PokeAPI publishes no type entry for a local placeholder form, the base species types are used; the chosen source identifier remains in the JSON for audit.

The type datasets were retrieved on 2026-10-12: <https://github.com/PokeAPI/pokeapi/blob/master/data/v2/csv/pokemon.csv>, <https://github.com/PokeAPI/pokeapi/blob/master/data/v2/csv/pokemon_types.csv>, <https://github.com/PokeAPI/pokeapi/blob/master/data/v2/csv/pokemon_forms.csv>, and <https://github.com/PokeAPI/pokeapi/blob/master/data/v2/csv/pokemon_form_types.csv>. PokeAPI documents that Pokémon variants may differ in typing and that forms can publish their own types: <https://pokeapi.co/docs/v2#pokemon>, <https://pokeapi.co/docs/v2#pokemonform>.

## PokéGrass probability model

The base capture chance is `0.08 + capture_rate / 255 × 0.72`. Ball multipliers are Poké Ball `1.0`, Super Ball `1.5`, Ultra Ball `2.0`; Master Ball and shiny encounters guarantee capture. Frambu multiplies chance by `1.5`; Frambu Dorada by `2.5`, capped at `0.99` for non-Master Balls. These are PokéGrass tuning values, not claimed to reproduce any particular Pokémon game's full capture formula.

Species flee chance is `0.05 + (255 - capture_rate) / 255 × 0.5`, capped at `0.55`. Latano multiplies flee chance by `0.35`. Shiny Pokémon never flee. These mechanics are independent of the encounter rarity category. Adjust these constants only as explicit game-balance changes.

The four Genesect drive IDs reuse Genesect capture metadata; Ogerpon IDs 8–10, Terapagos_2, and Poltchageist were added when their complete six-sprite sets appeared. Ogerpon 8–10 inherit base Ogerpon types pending any form-specific type mapping.

CAMERUPT_1_female usa el encounter shiny normal de CAMERUPT; su encounter normal femenino se mantiene propio.
