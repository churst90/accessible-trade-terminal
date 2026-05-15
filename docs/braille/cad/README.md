# CAD source files

All parts are described in **OpenSCAD** (text-based parametric CAD) and **DXF** (2D fabrication). OpenSCAD is open-source, scriptable, and version-controllable; DXF is the universal format for laser/CNC services.

## Why OpenSCAD over Fusion 360 / SolidWorks

- **Open-source, no license.** Anyone can clone the repo and modify.
- **Text-based.** Diffable in git; reviewable in pull requests.
- **Parametric by default.** Every dimension has a name; changing one parameter regenerates downstream geometry.
- **Scales across tiers.** Single-cell, 40-cell, and full-array share the same source files; tier is a parameter at the top.

## Installing OpenSCAD

Download from [openscad.org](https://openscad.org/) — Windows, Mac, Linux. Free.

## Generating output

For each part:
1. Open the `.scad` file in OpenSCAD.
2. Verify parameter values at the top of the file match your tier (single-cell / 40-cell / full-array).
3. **F5** to render preview (fast, for visual check).
4. **F6** to render full geometry (slower, accurate).
5. **File → Export → Export as STL** (for 3D-printed parts) or **DXF** (for laser-cut parts).

## File index

| File | Purpose | Output | Used by |
|---|---|---|---|
| `cell_housing.scad` | Pin-sleeve plate (parametric) | STL | Single-cell, 40-cell, full-array |
| `top_plate.scad` | Aluminum top plate | DXF (2D plate outline + holes) | All tiers |
| `pole_plate.scad` | Steel pole washer plate | DXF | All tiers |
| `enclosure.scad` | Bottom + top shells | STL | 40-cell, full-array |
| `pin_jig.scad` | Assembly jig for pins | STL | 40-cell, full-array |
| `force_jig.scad` | Force-test jig | STL | All tiers |

Each file has a parameter block at the top with `TIER = "single-cell"` / `"forty-cell"` / `"full-array"` switches.

## Rendering the entire stack

`stack_assembly.scad` is a top-level file that imports all parts and renders them assembled, for visual sanity-checking. Render at low resolution (`$fn = 20`) for preview; render at high resolution (`$fn = 100`) only for final geometry export.

## Tolerance conventions

All printed parts: nominal CAD dimensions are *target* dimensions. PETG print typically expands +0.05–0.10 mm per surface. Where this matters:
- Bores: CAD'd at 1.55 mm (nominal 1.5 mm + 0.05 mm allowance).
- External features: CAD'd at nominal; trim to fit if needed.
- Dowel-pin holes: CAD'd at 3.05 mm (nominal 3.0 mm).

Cut parts (DXF for laser/CNC): tolerance allowances per fab spec — see each `.scad` file's notes.
