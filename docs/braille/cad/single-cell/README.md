# Single-cell CAD outputs

Single-cell prototype uses the same parametric source files as 40-cell and full-array. Set `TIER = "single-cell"` at the top of each `.scad` file in the parent `cad/` directory before exporting.

Files needed for single-cell build:

| Source | Set TIER to | Export as | Send to |
|---|---|---|---|
| `../cell_housing.scad` | `"single-cell"` | STL | self-print (CF-PETG) |
| `../top_plate.scad` | `"single-cell"` | DXF | SendCutSend (drilled) |
| `../pole_plate.scad` | `"single-cell"` | DXF | SendCutSend (laser) — order in 0.5, 1.0, 1.5 mm thicknesses |
| `../pin_jig.scad` | n/a | STL | self-print (PETG) |
| `../force_jig.scad` | n/a | STL | self-print (PETG) |

PCB design: see `single_cell_pcb/` (KiCad project).
