# 40-cell CAD outputs

Set `TIER = "forty-cell"` at the top of each parent `.scad` before exporting.

Note: 40-cell housing is 240 mm long. Printers with <250 mm Y axis must split. Bambu X1C, Voron 350, Prusa XL all fit. Prusa MK4 (210 mm Y) does NOT fit; either upgrade printer or split into 2× 120 mm sections joined with stepped lap joint + dowel pins.

Files needed:

| Source | Set TIER | Export | Send to |
|---|---|---|---|
| `../cell_housing.scad` | `"forty-cell"` | STL | self-print (CF-PETG MANDATORY) |
| `../top_plate.scad` | `"forty-cell"` | DXF | SendCutSend (drilled) |
| `../pole_plate.scad` | `"forty-cell"` | DXF | SendCutSend (laser, 0.5 mm) |
| `../enclosure.scad` (×2: top + bottom) | `"forty-cell"` | STL | self-print (PETG) |
| `../pin_jig.scad` | n/a | STL | self-print, 4 copies |

PCB design: see `forty_cell_pcb/` (KiCad project, separate task).

## Annealing reminder

After printing the cell housing: anneal at 80°C for 4 hours. Skipping causes warp over months at this scale.
