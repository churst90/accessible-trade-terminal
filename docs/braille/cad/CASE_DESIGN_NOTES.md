# Case design notes

The enclosure (`enclosure.scad`) is sized to fit every internal component and printed in two pieces (top + bottom shells) that mate via a stepped lip with corner screws.

## What's inside (full-array tier, top to bottom)

| Layer | Z (mm from inside floor) | Thickness | Notes |
|---|---|---|---|
| Aluminum top plate | 21.2 | 3.0 mm | Captured in top-shell window seat |
| Upper pole washer plate | 17.7 | 0.5 mm | Steel sheet |
| Cell housing | 13.7 | 4.0 mm | CF-PETG print, holds pins + magnets |
| Lower pole washer plate | 13.2 | 0.5 mm | Steel sheet |
| Ferrite layer | 12.7 | 0.5 mm | TDK Flexield |
| Coil PCB | 11.1 | 1.6 mm | 4-layer FR-4, supported by 4 standoff posts at TP corners |
| Air + connectors | 3.1 | 8.0 mm | Standoff between coil PCB and driver PCB; ribbon-cable connectors and SMD components live here |
| Driver PCB | 1.5 | 1.6 mm | Carries DRV8847s, muxes, MCUs; supported by 4 standoff posts |
| Air + thru-hole leads | 0 | 4.0 mm | Standoff between driver PCB and enclosure floor |
| Enclosure floor (bottom shell wall) | -3.0 | 3.0 mm | Wall thickness |
| Rubber feet | -7.0 | 4.0 mm | 4 corner pads |

Total external height: ~32 mm. Active surface (top of pin) is 22 mm above the table.

## What the case provides

1. **Driver-PCB standoffs** — 4 internal posts molded into the bottom shell, with M3 brass heat-set inserts at the top. Driver PCB bolts to these.
2. **Coil-PCB standoffs** — 4 separate, taller posts (also molded into bottom shell, also with heat-set inserts). Coil PCB sits on these.
3. **Top-plate seat** — a 2 mm shoulder around the inside of the top-shell window holds the aluminum top plate flush at the correct Z.
4. **Cable channels** — slots in the side walls of the bottom shell let ribbon cables exit the bank-header connectors on the coil PCB, route around the enclosure interior, and reach the driver PCB.
5. **Rear ports** — USB-C and barrel-jack cutouts at the correct Z position to mate with the driver PCB's panel-mount connectors.
6. **Vent slots** — three rear-face slots for passive convection (full-array only).
7. **Fan port** — 40 mm fan cutout on the right side wall (full-array only) with mounting holes.
8. **Corner screws** — 4 M3 socket-head screws thread up from the bottom of the bottom shell into heat-set inserts in the lid lip. Screws are counterbored from outside so the bottom is flat against rubber feet.
9. **Branding strip** — recessed rectangle on the front lower edge for a logo or label, paint-fillable.

## Aesthetics

- **Chamfered top edges** — 4 mm chamfer on all four upper edges of the top shell. Softens the silhouette and looks intentional.
- **Hidden screws** — corner screws come up from the bottom; no visible fasteners on top or sides.
- **Recessed top plate** — aluminum plate sits flush in the window seat; from above, the case appears to be a single chamfered slab with a window of dots, not a box-with-plate-on-top.
- **Matte finish** — printed in matte black PETG-CF, then primed and painted with two coats of automotive matte black. The CF gives a slightly textured surface that looks closer to magnesium-alloy enclosures than plain plastic.
- **Branding strip** — optional paint-filled recess for a project logo.
- **Unbroken side surfaces** — vents and fan port are on the rear and right face only, leaving front and left faces clean for tactile-friendly handling.

## How fit is verified

Before final assembly, do a **dry stack-fit**:

1. Print bottom shell.
2. Install heat-set inserts at all 12 brass-insert locations (4 corner screws + 4 driver standoffs + 4 coil standoffs).
3. Test-fit driver PCB; should drop onto standoffs cleanly.
4. Test-fit coil PCB; should clear all driver-PCB components and connectors.
5. Test-fit pole plates, cell housing, top plate as a stack on top of the coil PCB.
6. Lower top shell over the assembly. Top plate should seat flush against the window-seat shoulder.
7. Insert 4 corner screws from below; tighten by hand. Lid should mate with no wobble.

If any step fails:
- **Driver PCB doesn't fit:** check standoff-to-standoff spacing matches PCB hole pattern. If standoffs are wrong, regenerate `enclosure.scad` with `DRIVER_PCB_HOLE_INSET` adjusted.
- **Coil PCB hits driver components:** increase `DRIVER_TO_COIL_GAP` from 8 to 10 mm.
- **Top plate sits too high or too low:** adjust `TOP_PLATE_NEST_DEPTH`.
- **Lid wobbles:** decrease `LIP_GAP` from 0.4 to 0.3 mm.
- **Lid won't close:** increase `LIP_GAP` to 0.5 mm.

## Tier-specific differences

| Feature | 40-cell | Full-array |
|---|---|---|
| External footprint | 304 × 59 mm | 204 × 214 mm |
| Total height (with feet) | ~32 mm | ~32 mm |
| Vent slots | No (single bank, low heat) | Yes (3 slots) |
| Fan port | No | Yes (40 mm right side) |
| Cable channels | 2 per side | 2 per side |
| Material recommendation | PETG plain | PETG-CF |

## What was wrong with the original enclosure

The previous version of `enclosure.scad` (V1, written 2026-04-30) was structurally minimal:
- Internal volume was sized too tight for the full stack (no standoffs modeled).
- Driver PCB had no defined mounting features.
- Top plate was assumed to "rest somewhere" without registration.
- Cable channels were not modeled — ribbon cables would have nowhere to route.
- Aesthetics were a plain rectangular box.

The 2026-05-01 redesign addresses all of these. It is now buildable.
