# Full-array CAD outputs

Set `TIER = "full-array"` at the top of each parent `.scad` before exporting.

The full-array cell housing is 180 × 190 mm. Printers must support both axes. Bambu X1C (256 mm), Voron 2.4 350, Prusa XL all fit. CF-PETG mandatory.

Files needed:

| Source | Set TIER | Export | Send to | Cost |
|---|---|---|---|---|
| `../cell_housing.scad` | `"full-array"` | STL | self-print (CF-PETG) | $25 filament |
| `../top_plate.scad` | `"full-array"` | DXF | SendCutSend "drilled CNC" tier + Class 2 black anodize | $130 + $30 |
| `../pole_plate.scad` | `"full-array"` | DXF | SendCutSend (laser, 0.5 mm) × 2 plates | $50 ea = $100 |
| `../enclosure.scad` (top + bottom) | `"full-array"` | STL | self-print (PETG plain or CF) | $30 filament |
| `../pin_jig.scad` | n/a | STL | self-print, 8 copies | $5 filament |

PCB design: see `coil_pcb_full/` and `driver_pcb_full/` (KiCad projects, separate task).

## Critical print parameters (cell housing)

- **Material: CF-PETG** (mandatory; plain PETG warps).
- **Layer: 0.10 mm**
- **Walls: 4 perimeters**
- **Print orientation: sleeves vertical (Z-axis)** — non-negotiable
- **Print time: 14–18 hours**
- **Anneal: 80°C × 4 hours** post-print

See `../05_FULL_ARRAY_BUILD.md` §5 for full procedure.
