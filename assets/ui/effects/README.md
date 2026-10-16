# PokéGrass encounter effects

These PNG sheets were extracted from `assets/reference/pokegrass_encounter_effects.png`. The original reference was not modified. No scaling, resampling, blur, or color interpolation was applied.

## Cleanup and transparency

Pixels with source alpha below 51/255 were cleared to remove diffuse green/orange sheet haze. Retained pixels preserve their original RGB and alpha. Component separation was performed on the retained alpha mask; no color key was used. The yellow glow around the final exclamation states remains in the sheet because it is part of those effects.

## Sheets

| File | Grid | Cell size | Contents |
|---|---:|---:|---|
| `leaves.png` | 8 columns x 7 rows | 128 x 128 | 52 separated leaf particles/clusters; cells ordered by source position; final 4 cells in the last row are transparent. |
| `exclamation.png` | 9 columns x 1 row | 160 x 160 | 9 exclamation states, centered horizontally and aligned to a shared baseline. |
| `grass_rustle.png` | 10 columns x 1 row | 160 x 160 | 10 clean grass motion frames, centered and aligned to a shared baseline. |
| `stars.png` | single overlay | 416 x 276 | Shiny encounter star cluster isolated from the lower-right source region; alpha below 51/255 was cleared. |

`previews/` contains opaque composites over light, dark, and checkerboard backgrounds for visual validation. The checkerboard is only in the previews; the resource PNGs contain real transparency. `stars.png` was extracted separately for shiny encounters with `tools/ExtractShinyStars.ps1`; the original reference remains unchanged.
