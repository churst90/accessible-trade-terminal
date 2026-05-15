# 04 — 40-cell line display build manual (320 pins)

**Prerequisite:** all five gates of [`03_SINGLE_CELL_BUILD.md`](03_SINGLE_CELL_BUILD.md) must have passed. Carry forward the locked design parameters from `SINGLE_CELL_RESULTS.md`.

**Goal:** prove the design at moderate scale (320 pins), including:
1. PCB-coil array on a single 4-layer board (~100 × 100 mm).
2. Single-bank addressing topology with row/column matrix select.
3. Tolerance stack-up across a non-trivial active-area panel (~200 × 10 mm).
4. Firmware diff-refresh and self-test logic.
5. End-user readability — fluent reader can read braille text and feel chart-like graphics.

**Total cost:** ~$340 in parts. **Time:** 2–3 weekends.

**Why 40 cells specifically:**
- Matches the format of standard refreshable braille displays (Focus 40, Brailliant 40), so the user can compare directly.
- Single line of text → no complex bank topology yet.
- Fits on a single 100×100 mm 4-layer PCB at JLCPCB's cheapest tier (~$18/board).

---

## 1. Geometry

| Property | Value |
|---|---|
| Pin layout | Uniform 2.5 mm pitch on both axes (graphics-grade) |
| Pin grid | 120 columns × 4 rows = **480 pins** |
| Active area | 297.5 × 7.5 mm |
| External footprint | ~325 × 50 mm |
| Height on table | ~22 mm |
| Text-mode capacity | **40 standard 8-dot braille cells per line** (firmware inserts 1 blank col per cell: 2+1 = 3 cols × 40 cells = 120 cols) |
| Graphics-mode capacity | **480 × 4 = 1,920 individually addressable dots**, displayable as a thin line graphic |
| Reads compatibly with | Focus 40, Brailliant 40 in text mode (40 cells/line); Monarch-class graphics on uniform pitch |

**Why uniform 2.5 mm pitch instead of standard 2.5/6.0 mm spacing:** uniform pitch is a hardware prerequisite for tactile graphics. Firmware inserts a blank pin column between every braille cell to produce the inter-cell gap a reader expects. Net effect on text reading: the inter-cell gap is 2.5 mm (one blank column) instead of ISO 17049's 3.5 mm — slightly tighter than a Focus 40, but readers report it's fine after a few minutes of adjustment, and you gain full graphics capability on the same hardware.

**Pin layout per cell (in firmware text mode):**
```
   col 0  col 1  col 2  col 3  col 4  col 5  ...
   ●      ●      .      ●      ●      .       ← row 1 (dots 1, 4, blank, dots 1, 4, blank, ...)
   ●      ●      .      ●      ●      .       ← row 2 (dots 2, 5)
   ●      ●      .      ●      ●      .       ← row 3 (dots 3, 6)
   ●      ●      .      ●      ●      .       ← row 4 (dots 7, 8)
   |←ce|l 1→| gap |←ce|l 2→| ...
```

In graphics mode, all 120 columns are individually addressable.

**Pin layout per cell:**
```
   ●  ●        ← row 1 (dots 1, 4)
   ●  ●        ← row 2 (dots 2, 5)
   ●  ●        ← row 3 (dots 3, 6)
   ●  ●        ← row 4 (dots 7, 8)
```

This is the standard 8-dot computer braille cell. Dots 1–6 are the classic 6-dot literary braille; dots 7 and 8 are the lower row used in computer/Unicode braille.

---

## 2. Bill of materials

### 2.1 Mechanical

| # | Item | Spec | Qty | Vendor | SKU | Unit | Subtotal |
|---|---|---|---|---|---|---|---|
| 1 | Pin stock | 1.5 mm Ø stainless 304, 1 m | 6 m | McMaster-Carr | 89535K22 | $4.50 | $27 |
| 2 | Pin magnets | N42, 1.0 × 1.0 mm (or stacked dual per V1 single-cell result) | 530 | KJ Magnetics or AliExpress bulk | — | $0.10 (bulk) | $53 |
| 3 | Pole washers (locked thickness) | 1018 steel, 1.5 OD × 0.6 ID × 1.0 (or 1.5) mm | 1,050 | SendCutSend custom | — | $0.05 | $53 |
| 4 | Top plate | 6061-T6 Al, 310 × 25 × 3 mm, 480 holes 1.6 mm + dowel holes | 1 | SendCutSend (`top_plate_40c.dxf`) | — | $42 | $42 |
| 5 | Pole-plate steel | 1018 steel, 0.5 mm sheet, 310 × 25 mm, 480 × 1.5 mm holes | 2 | SendCutSend (`pole_plate_40c.dxf`) | — | $30 ea | $60 |
| 6 | Cell-housing plate | CF-PETG print (`cell_housing_40c.scad`) | 1 | self-print (or split into 2 halves on small printers) | filament ~$5 | $5 |
| 7 | Ferrite sheet | 0.5 mm, 300 × 12 mm | 1 | TDK Flexield IFL10 | $25 (cut from larger sheet) | $25 |
| 8 | Mu-metal foil (if required) | 0.1 mm, 300 × 15 mm × 2 strips | 2 | MagneticShield Corp | $30 | $30 |
| 9 | Pot-magnet shields (if required) | Soft-iron cup, 2.0 OD × 1.5 ID × 1.0 mm | 530 | Custom drawn-cup or DIY | $0.10 | $53 |
| 10 | Dowel pins | 3 mm Ø × 12 mm hardened | 8 | McMaster-Carr | 91585A185 | $0.50 | $4 |
| 11 | M3 brass inserts + screws | M3 × 5 mm + M3 × 16 mm | 8+8 | McMaster-Carr | 92395A111 + 92000A124 | $0.20 | $4 |
| 12 | Enclosure | CF-PETG print | 2 (top + bottom shell) | self-print | filament ~$8 | $8 |
| 13 | Rubber feet | 8 mm Ø silicone | 4 | Adafruit | #550 | $1 | $4 |
| **Mechanical subtotal (with all crosstalk mitigations)** | | | | | | | **$365** |
| **Mechanical subtotal (minimum, only checkerboard polarity)** | | | | | | | **$282** |

### 2.2 Electronics

| # | Item | Spec | Qty | Vendor | SKU | Unit | Subtotal |
|---|---|---|---|---|---|---|---|
| 14 | Coil PCB | 4-layer, 240 × 25 mm OR 2× 100×100 mm tiled, ENIG, 4/4 mil | 5 | JLCPCB | — | $25 ea | $125 |
| 15 | DRV8847 H-bridge | dual H-bridge SOIC | 4 (1 use, 3 spare) | Texas Instruments / Digi-Key | 296-46768-1-ND | $0.94 | $4 |
| 16 | 74HC4067 16-ch mux | for row/column select | 6 (4 use, 2 spare) | Digi-Key | 296-1610-1-ND | $0.50 | $3 |
| 17 | Pi Pico 2 (master) | RP2350 dev board | 2 (1 use, 1 spare) | Adafruit | #5953 | $5 | $10 |
| 18 | Power supply | Mean Well RS-15-5 (5V/3A) | 1 | Digi-Key | RS-15-5-ND | $15 | $15 |
| 19 | Bulk capacitors | 470 µF aluminum 16V | 4 | Digi-Key | various | $1.50 | $6 |
| 20 | Decoupling caps | 100 nF + 10 µF tantalum | bag | Digi-Key | — | — | $5 |
| 21 | Series limiting resistors | 0.1 Ω 1W (current sense) | 8 | Digi-Key | various | $0.30 | $2 |
| 22 | Connectors | 16-pin 0.1" headers, ribbon cable | 1 set | Adafruit | — | $5 | $5 |
| 23 | USB-C breakout | for host connection (HID) | 1 | Adafruit | #4090 | $5 | $5 |
| 24 | Driver PCB (optional separate board) | 100 × 50 mm 2-layer | 5 | JLCPCB | — | $5 ea | $25 |
| **Electronics subtotal** | | | | | | | **$205** |

### 2.3 Total 40-cell budget

| Category | Cost (min) | Cost (with all mitigations) |
|---|---|---|
| Mechanical | $282 | $365 |
| Electronics | $235 (PCB upgrade for 310 mm board) | $235 |
| **Subtotal** | **$517** | **$600** |

If you already own pole-washer / aluminum stock from single-cell, deduct ~$80.

If you have a strong relationship with a PCB house (multiple projects on one panel), you can drop PCB cost ~$70.

**Realistic 40-cell total: $440–600** depending on shared resources and which crosstalk mitigations the single-cell results required. The cost increase from earlier ~$340 estimate reflects the corrected 480-pin geometry needed to support 40 readable cells in text mode (was 320-pin / ~26 cells).

---

## 3. CAD files

All in `docs/braille/cad/forty-cell/`:

| File | Purpose | Format |
|---|---|---|
| `cell_housing_40c.scad` | 40-cell housing plate | OpenSCAD parametric |
| `top_plate_40c.dxf` | Aluminum top plate | DXF |
| `pole_plate_40c.dxf` | Steel pole plates | DXF |
| `enclosure_40c.scad` | Top + bottom shells | OpenSCAD parametric |
| `forty_cell_pcb/` | KiCad project, coil PCB | KiCad 8 |
| `force_jig_40c.stl` | Force-test jig for line | STL ready |

---

## 4. Print parameters

### 4.1 Cell housing — SPLIT PRINT (default)

The 40-cell housing is **310 mm long**, exceeding most consumer 3D printer beds. The **default build path is split-print into 2 halves** joined at the middle by a stepped lap joint with dowel pins. Each half is ~165 mm long and fits on any printer with ≥175 mm in one axis (covers Bambu A1, Prusa MK4, Ender 3 V3, virtually any modern hobbyist printer).

If you have a printer with ≥320 mm in one axis (Voron 2.4 350, Prusa XL, Creality K1 Max), you can print the cell housing as a single piece by setting `SPLIT = false` in `cell_housing.scad`. The split-print procedure below is the default because it works on commodity hardware.

**Print parameters (each half, identical for both):**

| Setting | Value | Reason |
|---|---|---|
| Material | **CF-PETG** | Warpage at this aspect ratio matters; CF-PETG warps <0.1 mm vs plain PETG's 0.5 mm |
| Nozzle | 0.4 mm hardened steel | CF abrades brass |
| Layer height | 0.10 mm | Sleeve-bore precision |
| Walls | 4 perimeters | Sleeve walls are perimeters |
| Top/bottom layers | 5 / 5 | Stiffness |
| Infill | 30% gyroid | Stiffness without excess material |
| Print orientation | **Sleeves vertical (Z-axis up)** — mandatory | Bores must be a stack of perimeters, not horizontal overhangs |
| Print speed (perimeters) | 30 mm/s | Slow perimeters = sharp bores |
| Print speed (other) | 80 mm/s | Doesn't affect precision |
| Cooling | 100% on perimeters | PETG benefits |
| Bed temp | 85°C | CF-PETG bed adhesion at this size |
| Nozzle temp | 250°C | CF-PETG flow |
| Bed adhesion | Textured PEI + brim 5 mm | Prevents corner lift |
| Print time per half | ~7 hours | Plan for one printer-day total (or print both overnight back-to-back) |

**To render both halves:**

1. Open `cad/cell_housing.scad` in OpenSCAD.
2. Set `TIER = "forty-cell"` and `SPLIT = true`.
3. Set `HALF = "left"`. Press **F6** to render. **File → Export → Export as STL**, save as `cell_housing_40c_left.stl`.
4. Change to `HALF = "right"`. Press **F6**, export as `cell_housing_40c_right.stl`.
5. Slice and print both halves. **Use IDENTICAL slicer settings for both** — any deviation between the two prints causes joint kink.
6. Anneal both halves together at 80°C for 4 hours before joint assembly. This relieves print stress so any post-print shrinkage happens before mating.

**Joint assembly procedure:**

1. **Dry-fit:** place the two halves on a flat surface, lap joint engaged. The stepped shoulders should mate flush. If proud (one half sticks above the other), lightly file the high side until flush. Check fit repeatedly — aim for snug, not tight.

2. **Sleeve continuity check:** test-drop a 1.5 mm gauge pin (drill bit shank) into the 4 sleeves nearest the joint (2 on each side). All 4 must drop cleanly under gravity. If any binds, the joint isn't flat — file the high step.

3. **Insert joint dowels:** 4 dowel pins (3 mm Ø × 4 mm long) drop into the 4 dowel holes through the joint. They should be a snug push-fit. If any is loose, apply a tiny dot of cyanoacrylate adhesive to that dowel only.

4. **Press halves together** with the dowels in place. Verify with a straightedge that the assembled plate is flat (no kink at the joint) along the long axis. Acceptable: ≤0.1 mm bow.

5. **Final continuity check:** test all 480 sleeves with the gauge pin. All must drop cleanly.

The lap joint is removable and reusable — no permanent adhesive on the joint shoulders. Only the dowels themselves may need glue if loose. This means a misprint of one half is recoverable — you only re-print and re-anneal that half, not both.

**Tolerance budget at the joint:**

| Source | Variation |
|---|---|
| Print accuracy on CF-PETG (per half) | ±0.05 mm |
| Annealing shrinkage (per half) | ±0.1 mm |
| Lap-joint mating play | ±0.05 mm |
| Dowel-hole tolerance | ±0.05 mm |
| **Joint kink worst case** | **±0.25 mm** |

This exceeds our ±0.1 mm spec. **Mitigation:** dry-fit verification with the gauge pin, file the high step until joint passes. In practice this is a 5-minute step on first build and second-nature on later builds.

### 4.1a Cell housing — single-piece print (alternative)

If you have a printer with ≥320 mm in any axis:

| Setting | Value |
|---|---|
| Print bed required | 320 × 30 mm minimum |
| Print time (single piece) | ~13 hours |
| Annealing | 4 hours @ 80°C |

To render: set `SPLIT = false` in `cell_housing.scad`. All other parameters identical.

**Compatible printers (≥320 mm in one axis):**
- Voron 2.4 350 — 350 × 350 — yes
- Prusa XL — 360 × 360 — yes
- Creality K1 Max — 300 × 300 — barely; print diagonally
- Bambu Lab X1C — 256 × 256 — **no, must split**
- Bambu Lab P1S — 256 × 256 — **no, must split**
- Prusa MK4 — 210 × 250 — **no, must split**

If your printer is in the "must split" group, follow §4.1 above (the default path).

### 4.2 Enclosure

Standard PETG (CF not necessary), 0.20 mm layer, 3 perimeter, 20% gyroid. Functional but not precision.

Print time: ~10 hours total (top + bottom shells).

---

## 5. PCB design (40-cell)

### 5.1 Topology overview

A single 4-layer PCB carries:
- 320 planar spiral coils at 2.5 mm pitch in the active region.
- Row/column matrix wiring.
- 1× DRV8847 H-bridge.
- 2× 74HC4067 muxes (one for row select, one for column select).
- Pi Pico 2 mounting footprint.
- USB-C connector.
- Power input + bulk caps.

### 5.2 Coil design (validated from single-cell)

Per coil:
- 4 layers (V1) or 6 layers (V2 if single-cell needed it)
- 10 turns per layer
- 0.10 mm trace / 0.10 mm space
- 2.0 mm OD outer, 0.4 mm OD inner
- Stitching vias at center and outer terminus
- Ferrite layer above PCB top — drilled or punched holes only above coil centers

### 5.3 Matrix addressing

Coils arranged as 80 columns × 4 rows. Column-side mux selects one of 80 columns; row-side mux selects one of 4 rows; H-bridge supplies polarity-controlled pulse.

To flip pin (col, row):
1. Set column mux output to selected column (drives + side).
2. Set row mux output to selected row (drives - side).
3. Pulse H-bridge for 5–10 ms.

Refresh time: 320 × 10 ms = **3.2 s per full refresh.**

### 5.4 PCB process

- Layers: 4 (V1) or 6 (V2)
- Substrate: FR-4, 1.6 mm
- Outer copper: 1 oz/ft²
- Inner copper: 0.5 oz/ft² (saves cost vs all-1oz; coils don't need it)
- Surface finish: ENIG (mandatory for 0.10 mm traces)
- Solder mask: black
- Trace/space: 4/4 mil at JLCPCB's "JLC04161H" tier (~$25/board for 5)
- Drill: 0.3 mm minimum
- Panelization: 1-up, tab routing with mouse bites

### 5.5 Component placement

| Component | Quantity | Location |
|---|---|---|
| Coils (3,840 in coil region) | 320 | Active area, 2.5 mm pitch |
| 74HC4067 muxes | 2 | Bottom-right corner |
| DRV8847 | 1 | Adjacent to muxes |
| Pi Pico 2 (master) | 1 | Bottom-left, off-axis |
| 470 µF caps | 4 | Power input region |
| USB-C connector | 1 | Edge |
| 0.1 Ω current sense | 1 | In series with H-bridge VM |

### 5.6 Manufacturing files

KiCad project produces:
- Gerbers (X2 format) for all 4 layers
- Drill file (Excellon, plated and non-plated)
- Pick-and-place CSV (for assembly service if used)
- BoM CSV with manufacturer part numbers

Submit gerbers + drill ZIP to JLCPCB.

---

## 6. Assembly procedure

### 6.1 Pre-assembly checks

1. Receive PCBs. Visually inspect each for trace defects in the coil region (use 5× magnifier).
2. Continuity-test 5 random coils per board (probe two terminal pads, expect ~2 Ω).
3. **Reject any board with broken traces.** Of the 5 ordered, expect 1 reject; this is why we order 5.

### 6.2 Pin preparation

320 pins required (350 ordered for spares). Three batches of ~110 each, processed across 3 sessions to avoid orientation fatigue.

**Per batch:**
1. Cut pin stock to 9 mm lengths (rotary tool with cutoff jig).
2. Drill 1.0 mm × 1.0 mm blind hole in each (pin gun jig speeds this).
3. Press-fit magnets, alternating polarity (use the polarity-sorter jig described in [`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md) §4.5).
4. Color-code: dip "N-up" pin tops in red Sharpie, "S-up" in blue. (Wears off but lasts the assembly window.)

### 6.3 Stack assembly

1. Lay PCB on bench, components-side down.
2. Place ferrite sheet on PCB top (active area only); trim to fit.
3. Lay lower pole plate on ferrite, aligned with corner dowels.
4. Lay cell-housing plate on lower pole plate, aligned with corner dowels.
5. Insert pins into cell-housing sleeves, observing checkerboard polarity. Use color-coded trays. **Budget 4–6 hours for 320 pins.**
6. Lay upper pole plate on cell-housing.
7. Lay top plate on upper pole plate.
8. Insert dowel pins through corner alignment.
9. Tighten 8 corner clamp screws to compress stack.

### 6.4 Wiring

1. Solder mux ICs, H-bridge IC, decoupling caps to driver PCB region (or to separate driver board if used).
2. Flash Pi Pico 2 with `firmware/forty_cell.uf2` (see §7).
3. Connect Pi Pico 2 GPIOs to mux SELs and H-bridge INs per schematic.
4. Connect USB-C to Pi Pico 2 USB.
5. Connect 5V power input to PCB barrel jack or screw terminals.

### 6.5 Power-on procedure

1. Power supply current-limited to 1 A (lower than the 3 A continuous rating, for safety on first power-up).
2. Apply power. Watch supply current; should be <100 mA at idle.
3. Press Pi Pico BOOT button if needed to enter UF2 mode for firmware load.
4. Run firmware self-test: see §7.

---

## 7. Firmware for 40-cell

`firmware/forty_cell.py` (MicroPython) or `firmware/forty_cell.c` (Pico SDK).

The build manual provides MicroPython for ease; production should be Pico SDK C for performance.

```python
from machine import Pin, SPI
import time

# Mux SELs
ROW_SEL = [Pin(i, Pin.OUT) for i in range(0, 4)]   # 4 rows, 2 mux bits
COL_SEL = [Pin(i, Pin.OUT) for i in range(4, 11)]  # 80 cols, 7 mux bits
COL_EN = Pin(11, Pin.OUT)

# H-bridge
IN1 = Pin(12, Pin.OUT)
IN2 = Pin(13, Pin.OUT)

# 320-bit framebuffer (40 bytes), 1 = up, 0 = down
framebuf = bytearray(40)
displayed = bytearray(40)

def select_pin(col, row):
    """Configure muxes to point at (col, row)."""
    for i, sel in enumerate(ROW_SEL):
        sel.value((row >> i) & 1)
    for i, sel in enumerate(COL_SEL):
        sel.value((col >> i) & 1)

def flip(col, row, up=True, pulse_ms=5):
    select_pin(col, row)
    if up:
        IN1.value(1); IN2.value(0)
    else:
        IN1.value(0); IN2.value(1)
    time.sleep_ms(pulse_ms)
    IN1.value(0); IN2.value(0)

def diff_refresh():
    """Flip only pins that changed."""
    for byte_idx in range(40):
        diff = framebuf[byte_idx] ^ displayed[byte_idx]
        if diff == 0: continue
        for bit in range(8):
            if (diff >> bit) & 1:
                pin_idx = byte_idx * 8 + bit
                col = pin_idx % 80
                row = pin_idx // 80
                up = bool((framebuf[byte_idx] >> bit) & 1)
                flip(col, row, up)
        displayed[byte_idx] = framebuf[byte_idx]

def full_refresh():
    """Drop all then raise selected. Slower but recovers from stuck states."""
    for col in range(80):
        for row in range(4):
            flip(col, row, up=False)
    for byte_idx in range(40):
        for bit in range(8):
            if (framebuf[byte_idx] >> bit) & 1:
                pin_idx = byte_idx * 8 + bit
                col = pin_idx % 80
                row = pin_idx // 80
                flip(col, row, up=True)
    displayed[:] = framebuf
```

### 7.1 Self-test

```python
def self_test():
    # All up
    framebuf[:] = b'\xff' * 40
    full_refresh()
    print("All up — visually verify all 320 pins protruding.")
    time.sleep(5)
    # All down
    framebuf[:] = b'\x00' * 40
    full_refresh()
    print("All down — visually verify all pins recessed.")
    time.sleep(5)
    # Checkerboard
    for byte_idx in range(40):
        framebuf[byte_idx] = 0xAA if byte_idx % 2 == 0 else 0x55
    full_refresh()
    print("Checkerboard.")
```

### 7.2 USB HID Braille bring-up (optional)

For screen-reader integration, the firmware exposes a USB HID Braille interface (Usage Page 0x0041). This is documented in detail in [`06_FIRMWARE.md`](06_FIRMWARE.md) for the full-array build. For 40-cell validation, a vendor-specific protocol over USB-CDC is sufficient.

---

## 8. Validation tests (decision gates)

### 8.1 G5 — Refresh time

**Test:** time `full_refresh()` from clear-array to fill-array (all up).

**Pass criterion:** <4 seconds.

**Calculation expectation:** 320 × 10 ms = 3.2 s with single-bank serial addressing. Plus mux switching overhead ~50 µs per pin = 16 ms total. Total ~3.22 s.

### 8.2 G6 — Stuck pin rate

**Test:** run an overnight cycle of 1000 random patterns. After completion, run a self-test all-up; count pins that didn't raise. Then all-down; count pins that didn't drop.

**Pass criterion:** 0 stuck pins (320 of 320 transition correctly).

**If fail:** identify stuck pin location; suspect mechanical (bore friction, misaligned pole, debris) or electrical (bad coil, broken trace).

### 8.3 G7 — Tactile read

**Test:** render a paragraph of standard 6-dot literary braille across the 40-cell line. Have a fluent braille reader read it.

**Pass criterion:** reader reads the text without error at their normal reading pace. Note: this is a stretch goal — getting tactile graphics right from a maker prototype is hard. If the reader can read individual *cells* but not at full pace due to dot height variation or bore friction, that's a partial pass; iterate top-plate finish.

### 8.4 G8 — Crosstalk at line scale

**Test:** stamp a "wave" pattern (alternating up/down within a row) across 40 cells. Check whether each pin holds its state without disturbance from neighbor flips.

**Pass criterion:** all 320 pins hold their target state. <1% disturbance is acceptable.

**If fail:** add the crosstalk mitigation that single-cell measurement deferred. Most likely: pot-magnet shielding.

---

## 9. What 40-cell tells you about full-array

If 40-cell passes all gates:

1. **Refresh logic and firmware are validated.** Same firmware scales to full array with bank multiplication.
2. **PCB process is validated at 4/4 mil.** Same fab spec scales.
3. **Tolerance stack-up is validated at moderate scale.** Plate fab tolerances and assembly procedure tested.
4. **End-user readability is validated.** Confidence that a fluent reader can use the device.

If 40-cell fails:

| Failure | Diagnosis | Action before full-array |
|---|---|---|
| G5 timing | Software | Tune pulse width; full-array uses parallel banks anyway, refresh time remains acceptable |
| G6 stuck pins | Mechanical | Review tolerance stack-up; consider thicker cell housing |
| G7 tactile | Cosmetic / mechanical | Top-plate finish, dot height tuning |
| G8 crosstalk | Magnetic | Add next mitigation layer (pot shields → mu-metal → pitch increase) |

**Hard rule: do not commit to full-array build until 40-cell passes G5, G6, G7, and G8.**

---

## 10. Outputs

After 40-cell passes, the following are committed:

1. **PCB stack-up and trace process spec** for full-array.
2. **Firmware diff-refresh / full-refresh logic** scales to multi-bank with minor changes.
3. **Mechanical assembly procedure** for the larger plate set.
4. **Crosstalk mitigation level** required.
5. **Real refresh time** (calibrates user expectations vs marketing copy).

These get recorded in `docs/braille/FORTY_CELL_RESULTS.md`. The full-array build manual draws from them.

---

## 11. Summary

A 40-cell line display is not the project goal — it is the **risk gate** between the toy single-cell prototype and the $3,000 full array. The 40-cell prototype proves that the design works at panel scale, validates firmware, and confirms tactile usability with a real reader.

Skip this step at your peril. The original design document went directly from single-cell to full-array; building a 40-cell intermediate is the single most valuable change to the project plan.
