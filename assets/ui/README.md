# PokéGrass UI asset extraction

Prepared from `assets/reference/pokegrass_ui.png`. The source image is unchanged.

## Extraction and alpha handling

The source is a 1536 × 1024 RGBA PNG with a real alpha channel. Empty canvas and gaps are transparent; illustrated pixels are semi-opaque. The checker-like green and blue appearance in the reference is transparent canvas color, not a background layer.

The image was segmented into 8-connected regions at alpha ≥ 128. Each PNG retains original RGB and alpha values only for pixels in its own region; all other pixels are transparent. This removes faint outer halos while preserving white and colored panel interiors. No blur, color-key deletion, resampling, or source edits were used. Exported cutouts are at native pixel dimensions. Use Nearest Neighbor for runtime resizing.

There are 100 main regions at this threshold. Ninety-seven UI assets were extracted. Three Pokémon examples were excluded: normal Pikachu slot, shiny Pikachu slot, and the sample Pikachu profile card. Do not use those illustrations as catalog Pokémon graphics.

## Files and organization

- `inventory.csv` lists each exported PNG, dimensions, source coordinates, and component ID.
- `previews/pokegrass-ui-light.png` and `previews/pokegrass-ui-dark.png` show all assets on contrasting backgrounds.
- Folders group assets by purpose: arrows, buttons, controls, frames, icons, panels, and slots. Palette buttons are under `buttons/palette`; the five action buttons are under `buttons/actions`.
- Three-digit suffixes preserve the source component ID.

Some connected regions are inseparable and remain composites: the slider/progress controls, tabbed window with its field, search field with its magnifier, paired layout toggle, and page selector. Use these as complete assets unless a future source provides separate parts.

## Button states

Palette buttons are separated by color and state: `buttons/palette/{green,red,blue,yellow}_{normal,hover,pressed,disabled}_NNN.png`. The reference does not show four states for every control, so missing states were not invented.

## 9-slice guidance

Decorative frames and panel borders can use nine-slice resizing with Nearest Neighbor:

1. Keep corner blocks at native pixel size. For ornate frames, start with 18–20 source pixels per corner and inspect rivets/rounded corners before choosing final insets.
2. Stretch only straight horizontal and vertical edge strips.
3. Stretch or tile the center while preserving its fill and transparency.
4. Prefer integer destination scaling. Do not stretch an entire frame if the destination aspect ratio differs.

The tabbed container and attached field controls have overlapping decoration; use them at native size or as complete backgrounds instead of splitting the attached tabs and fields.

## Recovery limits

All 97 exported regions were visually checked in both preview sheets. No neighboring thresholded region appears in a cutout, and interiors retain their original color. The three Pikachu examples were intentionally omitted. No substitute graphics or unsupported button states were created.