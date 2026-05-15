# 03 — Single-cell build manual (8 pins)

**Goal:** validate the actuator mechanism on 8 pins before ordering anything for the larger builds. **Total cost ~$110, time 1–2 weekends.** This is the first physical build; everything depends on it passing.

**What this prototype proves:**
1. A 1.5 mm pin with a stacked-magnet base sits in a bistable detent with ≥150 g hold force.
2. A planar PCB coil can flip the magnet between detents with realistic current and pulse duration.
3. A ferrite layer above the PCB provides enough field multiplication for reliable flipping.
4. Adjacent pins at 2.5 mm pitch do not disturb each other under repeated flipping.
5. The mechanism is acceptable to a fluent braille reader by tactile feel.

**What this prototype does NOT prove:**
- Multi-bank addressing (only one bank here).
- Tolerance stack-up at panel scale.
- Heat dissipation under sustained refresh.
- HID Braille protocol integration.

Those come at the 40-cell tier (`04_FORTY_CELL_BUILD.md`) and full-array tier (`05_FULL_ARRAY_BUILD.md`).

---

## 1. Bill of materials

All prices in USD as of April 2026. Vendor SKUs are real, not approximate.

### 1.1 Mechanical parts

| # | Item | Spec | Qty | Vendor | SKU / Link | Unit | Subtotal |
|---|---|---|---|---|---|---|---|
| 1 | Pin stock | 1.5 mm Ø stainless 304 rod, 1 m | 1 | McMaster-Carr | 89535K22 | $4.50 | $4.50 |
| 2 | Pin magnets | N42 NdFeB, 1.0 mm Ø × 1.0 mm thick, axially magnetized | 20 (16 use, 4 spare) | KJ Magnetics | D11-N52 (closest stocked, sub N42 if available) | $0.30 | $6.00 |
| 3 | Pole washers, 0.5 mm | 1018 steel, 1.5 OD × 0.6 ID × 0.5 mm | 16 | SendCutSend custom (DXF in cad/pole_washer.dxf) | — | $0.30 | $4.80 |
| 4 | Pole washers, 1.0 mm | 1018 steel, 1.5 OD × 0.6 ID × 1.0 mm | 16 | SendCutSend custom | — | $0.40 | $6.40 |
| 5 | Pole washers, 1.5 mm | 1018 steel, 1.5 OD × 0.6 ID × 1.5 mm | 16 | SendCutSend custom | — | $0.50 | $8.00 |
| 6 | Soft ferrite sheet | 0.5 mm thick, µᵣ ≥ 100, 50 × 50 mm | 1 | TDK Flexield IFL10 / Digi-Key 445-7124-ND | $12 | $12.00 |
| 7 | Top plate | 6061-T6 aluminum, 30 × 30 × 3 mm, 8 holes 1.6 mm at 2.5 mm pitch + 4 dowel holes 3.1 mm | 1 | SendCutSend (DXF in cad/top_plate_sc.dxf) | — | $18 | $18.00 |
| 8 | Cell-housing plate | CF-PETG print (use cad/cell_housing_sc.scad) | 1 | self-print | filament ~$1 | $1.00 |
| 9 | Dowel pins | 3 mm Ø × 12 mm hardened steel | 4 | McMaster-Carr | 91585A185 | $0.50 | $2.00 |
| 10 | Spring clamp screws | M3 × 16 mm, brass | 4 | McMaster-Carr | 92000A124 | $0.20 | $0.80 |
| 11 | Brass heat-set inserts | M3 × 5 mm | 4 | McMaster-Carr | 92395A111 | $0.50 | $2.00 |
| **Mechanical subtotal** | | | | | | | **$65.50** |

### 1.2 Electronics

| # | Item | Spec | Qty | Vendor | SKU | Unit | Subtotal |
|---|---|---|---|---|---|---|---|
| 12 | Coil PCB | 2-layer FR-4, 30 × 30 mm, 8 spirals, ENIG | 5 (2 use, 3 spare) | JLCPCB (gerbers in cad/single_cell_pcb/) | — | $5 (incl. shipping over 5 boards) | $25 |
| 13 | H-bridge driver breakout | DRV8847 dev board | 1 | Pololu | #2998 | $7 | $7 |
| 14 | Pi Pico 2 | RP2350 dev board | 1 | Adafruit | #5953 | $5 | $5 |
| 15 | USB-C cable | 1 m | 1 | (already owned) | — | — | — |
| 16 | Bench supply | 5 V, 3 A | 1 | (already owned) | — | — | — |
| 17 | Hookup wire | 22 AWG silicone, assorted colors | 1 set | Digi-Key | various | $8 | $8 |
| 18 | 0.1" headers | 40-pin breakaway | 2 | Adafruit | #392 | $2 | $4 |
| 19 | Solderless breadboard | 400 tie-points | 1 | (already owned) | — | — | — |
| 20 | Decoupling capacitors | 100 nF ceramic, 10 µF tantalum | 5 ea | Digi-Key | various | bag | $2 |
| **Electronics subtotal** | | | | | | | **$51** |

### 1.3 Tooling (assumes 3D printer, soldering iron, multimeter, calipers already owned)

| # | Item | Spec | Why needed | Cost |
|---|---|---|---|---|
| T1 | 1.4 mm gauge pin | Precision drill bit set | Sleeve-bore validation | (in drill bit set, owned) |
| T2 | 1.5 mm gauge pin | Precision drill bit set | Pin-bore go-gauge | (owned) |
| T3 | Force gauge | Kitchen scale 0–500g, 1g resolution | Detent force measurement | $25 |
| T4 | Magnifier or USB microscope | 5–20× | Magnet handling, soldering inspection | $40 |
| T5 | Magnet handling tools | Brass tweezers (non-magnetic) | Avoid steel-to-magnet sticking | $8 |
| T6 | Anti-roll mat | 200 × 300 mm rubber sheet | Prevent magnets from rolling and sticking | $5 |
| **Tooling subtotal (new only)** | | | | **$78** |

### 1.4 Total single-cell budget

| Category | Cost |
|---|---|
| Mechanical | $65.50 |
| Electronics | $51 |
| Tooling (new) | $78 |
| **Grand total V1** | **$194.50** |

If you already own a force gauge and magnifier, drop tooling to ~$13 → **$129.50 total**.

If you have a way to share PCB orders (panel with another project), drop PCB cost to ~$10 → **~$110 total**.

---

## 2. CAD files needed

All files live in `docs/braille/cad/single-cell/`.

| File | Purpose | Format | Notes |
|---|---|---|---|
| `cell_housing_sc.scad` | Pin sleeve plate | OpenSCAD parametric | 8 sleeves at 2.5 mm pitch, 4 dowel holes, 1.5 mm sleeve bore (+0.1 mm clearance) |
| `top_plate_sc.dxf` | Aluminum top plate | DXF (laser/CNC ready) | 30×30×3 mm, 8 × Ø1.6 mm holes at 2.5 mm pitch, 4 dowel holes |
| `pole_plate_sc.dxf` | Pole-washer carrier plate | DXF (laser/CNC ready) | 30×30 mm steel sheet, 8 × Ø1.5 mm holes at 2.5 mm pitch, 4 dowel holes — order in 0.5, 1.0, and 1.5 mm thicknesses |
| `enclosure_sc.scad` | Bottom enclosure with PCB mount | OpenSCAD parametric | Holds the PCB, has bumpers and a window for wire egress |
| `single_cell_pcb/` | KiCad project for coil PCB | KiCad 8 | 2-layer, 8 spirals, ENIG; gerbers also pre-generated for upload to JLCPCB |

Files are written in the `cad/` subdirectory. See [`cad/README.md`](cad/README.md) for parameter conventions.

---

## 3. Print parameters for cell housing (CRITICAL)

The cell-housing plate's bores *are* the pin alignment fixture. Print quality directly determines whether pins slide freely or jam.

### 3.1 Material

**CF-PETG** (carbon-fiber filled PETG). Recommended brands:
- Polymaker PolyMax PETG-CF
- Bambu PETG-CF
- Prusament PETG-CF

Plain PETG works but warps over time at this aspect ratio (footprint > 6 × thickness). CF-PETG warps <0.1 mm; plain PETG warps 0.5–0.8 mm.

**Do NOT use:**
- PLA: too brittle, dimensional drift with humidity.
- ABS: dimensional fine but warps badly without enclosed printer.
- TPU: too flexible — sleeves would deform under pin friction.
- Plain PLA+: same dimensional drift as PLA.

### 3.2 Slicer settings (Bambu Studio / Cura / PrusaSlicer)

| Setting | Value | Reason |
|---|---|---|
| Nozzle | 0.4 mm hardened steel (for CF) | CF abrades brass nozzles |
| Layer height | 0.10 mm | Sleeve-bore precision; smaller = better, slower |
| Print speed (perimeters) | 30 mm/s | Slower perimeters = sharper bore walls |
| Print speed (infill) | 80 mm/s | Doesn't matter for precision |
| Walls | 4 perimeters | Sleeve walls *are* perimeters; they must be solid |
| Top/bottom layers | 5 / 5 | Stiffness |
| Infill | 50% gyroid | Stiffness, dampens vibration during pin operation |
| Print orientation | Sleeves vertical (Z-axis) | Bores become a stack of perimeters; circular and smooth |
| Supports | None | Orientation eliminates overhangs |
| Cooling | 100% on perimeters | PETG benefits from cooling for dimensional stability |
| Bed temperature | 80°C | CF-PETG needs warm bed |
| Nozzle temperature | 250°C | CF-PETG flow temp |
| Retraction | 1.0 mm @ 30 mm/s | CF-PETG; less than plain PETG |
| Z-hop | 0.2 mm | Avoid blob marks on plate top |

### 3.3 Bore tolerance verification

After printing:

1. With calipers, measure the printed plate's external dimensions. Should match CAD ±0.05 mm.
2. With a 1.5 mm gauge pin (drill bit shank), test fit each of the 8 sleeve bores.
   - **Pin slides smoothly under gravity:** PASS.
   - **Pin sticks or requires force:** bore is undersized. Re-print with bore CAD diameter +0.1 mm.
   - **Pin falls through with significant rattle:** bore is oversized. Re-print with bore CAD diameter -0.05 mm.
3. With a 1.4 mm gauge pin, test fit each bore. Pin should sit firmly without falling.
   - **Pin falls through:** bore exceeds 1.4 + tolerance, definitely too loose.
4. Repeat bore test 3× for repeatability.

Acceptance: all 8 bores accept 1.5 mm pin with smooth slide, reject 1.4 mm pin from falling through.

### 3.4 Annealing (recommended)

After printing, anneal the cell-housing plate:
1. Place in a kitchen oven on a flat ceramic tile.
2. Heat to 80°C, hold for 2 hours.
3. Power off oven, let cool to room temp with door closed (~3 hours).

This relieves internal print stresses and prevents long-term warpage.

---

## 4. PCB design (single-cell)

### 4.1 Coil PCB layout

A single 2-layer board, 30 × 30 mm. 8 planar spiral coils on a 2 × 4 grid at 2.5 mm pitch. Each coil:

- Outer diameter: 2.0 mm
- Inner diameter: 0.4 mm
- Trace width: 0.10 mm
- Trace spacing: 0.10 mm
- Turns per layer: 10
- Layers used: 2 (top + bottom, both wound same direction so currents add)
- Vias: stitching at coil center (0.3 mm drill, 0.5 mm pad) and circumference

### 4.2 Connection traces

Each coil has two terminals (start + end). Routed through the PCB to a 16-pin 0.1" header at the board edge:

- Pin 1, 3, 5, 7, 9, 11, 13, 15: coil starts (one per coil)
- Pin 2, 4, 6, 8, 10, 12, 14, 16: coil ends

H-bridge driver connects to (start, end) of one coil at a time via breadboard wires.

### 4.3 PCB stack-up and process

- 2 layers, 1 oz copper outer, FR-4 substrate, 1.6 mm thickness
- ENIG finish (HASL leaves uneven dome that breaks 0.10 mm trace alignment)
- Trace/space minimum: 0.10 mm (JLCPCB standard tier)
- Drill minimum: 0.3 mm
- Solder mask: green or matte black (cosmetic preference — choose black for contrast against pin tops)
- Silkscreen: minimal; coil number 1–8 next to each terminal pair

### 4.4 Order procedure

1. Open `cad/single-cell/single_cell_pcb/` in KiCad 8.
2. Run DRC; should pass at 0.10 mm trace/space.
3. Run "Plot Gerbers" with JLCPCB defaults.
4. Zip the gerbers + drill files.
5. Upload to jlcpcb.com → Quick Order PCB.
6. Spec: 2 layer, FR-4 standard, 1.6 mm, 1 oz copper, ENIG, no panelization.
7. Quantity: 5. Cost ~$25 (incl. shipping to US, 7–10 days).

---

## 5. Assembly procedure

### 5.1 Order of operations

1. Print and validate cell-housing plate (§3).
2. Order PCBs (§4).
3. Order steel pole-washer plates (3 thicknesses) — combine with aluminum top plate order at SendCutSend to save shipping.
4. Order magnets, ferrite sheet, dowel pins, screws, hardware.
5. Wait for parts (~2 weeks).
6. Stack-fit dry assembly.
7. Magnetic assembly.
8. Wire H-bridge to PCB.
9. Run validation tests.

### 5.2 Magnet press-fit into pin

For each pin:

1. Cut a 9 mm length of 1.5 mm SS rod (rotary tool with cutoff wheel, or hacksaw with miter box).
2. Deburr both ends with a small file.
3. Drill a 1.0 mm Ø × 1.0 mm deep blind hole in one end (this is the magnet pocket).
   - Use a drill press with a v-block to hold the pin vertical.
   - Use a 1.0 mm carbide drill bit. Steel drill bits will dull immediately on stainless.
   - Run at 1500 RPM with a single drop of cutting oil.
4. With brass tweezers, pick up one N42 magnet from the magnet tray.
5. Determine polarity by approach to a steel reference (any iron will do): note which face attracts more strongly. The strongly-attracting face is one pole; the other is the opposite. **Mark the strong face with a sharpie dot — this is "N-up" by your convention.**
6. Press the magnet into the pin's blind hole using a 0.9 mm rod and gentle finger pressure on a hard surface.
7. The magnet should be flush with the pin end (not protruding, not recessed).
8. **For checkerboard polarity:** alternate which face you press in. Pin 1: dot face exposed (N-up). Pin 2: dot face buried (S-up). Pin 3: N-up. Etc.

This is tedious; budget 2 hours for 16 pins (with spares).

### 5.3 Dry stack-fit

Before adding magnets:

1. Stack: PCB → 1.0 mm pole plate (lower) → cell-housing plate → 1.0 mm pole plate (upper) → top plate.
2. Insert 4 dowel pins through corner holes; alignment locked.
3. Test-insert 1.5 mm gauge pin (no magnet) into each of the 8 sleeve positions.
4. Pin should pass through cleanly; if any binds, re-print or ream cell housing.

### 5.4 Magnetic stack-fit

1. Disassemble dry stack.
2. Place lower pole plate on PCB (with the ferrite sheet sandwiched between PCB and lower plate, ferrite-side up — see [`01_ARCHITECTURE.md`](01_ARCHITECTURE.md) §1.5).
3. Place cell-housing plate on lower pole plate; align with corner dowels.
4. Insert one magnetic pin into each sleeve, oriented per checkerboard plan. The pin will snap to the lower pole washer's detent (you'll feel/hear it).
5. Place upper pole plate over cell-housing; align with dowels.
6. Lower the top plate onto the assembly; align with dowels.
7. Press top plate down gently. All 8 pins should now sit at lower detent (recessed).
8. Tighten 4 corner clamp screws (M3 × 16 mm into brass heat-set inserts in the bottom enclosure) to compress stack.

### 5.5 PCB-to-driver wiring

1. Solder 16-pin 0.1" header to PCB edge.
2. Solder corresponding header to DRV8847 breakout.
3. Connect:
   - DRV8847 OUT1 → coil 1 start (PCB pin 1)
   - DRV8847 OUT2 → coil 1 end (PCB pin 2)
4. Connect Pi Pico 2:
   - GP0 → DRV8847 IN1
   - GP1 → DRV8847 IN2
   - GP2 → DRV8847 nFAULT
   - 5V → DRV8847 VM (motor supply)
   - GND → DRV8847 GND
5. Connect bench supply 5V/3A current-limited to DRV8847 VM.

For the single-cell prototype we only test one coil at a time; subsequent coils can be tested by moving the wire pair to a different terminal pair on the PCB header.

---

## 6. Firmware for single-cell test

A single Python file on the Pi Pico 2 running MicroPython is sufficient.

`firmware/single_cell.py`:
```python
from machine import Pin, PWM
import time

IN1 = Pin(0, Pin.OUT)
IN2 = Pin(1, Pin.OUT)
FAULT = Pin(2, Pin.IN, Pin.PULL_UP)

def flip_up(pulse_ms=5):
    IN1.value(1); IN2.value(0)
    time.sleep_ms(pulse_ms)
    IN1.value(0); IN2.value(0)

def flip_down(pulse_ms=5):
    IN1.value(0); IN2.value(1)
    time.sleep_ms(pulse_ms)
    IN1.value(0); IN2.value(0)

def cycle_test(n=1000, period_ms=50):
    """Cycle pin between detents n times. Listen for click and watch for fault."""
    fail = 0
    for i in range(n):
        flip_up()
        time.sleep_ms(period_ms)
        if not FAULT.value():
            print(f"Fault at cycle {i} on flip up")
            fail += 1
        flip_down()
        time.sleep_ms(period_ms)
        if not FAULT.value():
            print(f"Fault at cycle {i} on flip down")
            fail += 1
    print(f"Completed {n} cycles, {fail} faults")
```

### 6.1 Pulse current calibration

Default is 5 ms pulses. If pin fails to flip:
1. Increase pulse duration: 5 → 10 → 20 ms.
2. If still fails, raise supply voltage 5 V → 6 V (within DRV8847 rating up to 18 V; coils can take it for short pulses).
3. If still fails, current is the issue — check DRV8847 internal limit.

Records of which pulse parameters work for which pin become the "operating envelope" for the design.

---

## 7. Validation tests (decision gates)

### 7.1 G1 — Static detent hold force

**Setup:** assemble one pin in the up-detent state (flip pin up via firmware; remove power). Place the assembled stack on a kitchen scale, top plate up. Tare the scale.

**Test:** with a small jig (3D-printed, see `cad/force_jig_sc.stl`) that touches only the pin top, push down on the pin progressively. Watch the scale; it reads the force you're applying. Continue until you feel the pin sink.

**Pass criterion:** pin holds against ≥150 g push before sinking. Record force at sink for each of 8 pins.

**If fail:**
- Force <100 g: redesign pole washers thicker (1.5 → 2.0 mm).
- Force 100–150 g: try N50 magnets, or stack 2× magnets per pin.
- Force <50 g: fundamental issue; reconsider geometry.

### 7.2 G2 — Flip success rate

**Setup:** firmware running cycle_test(1000, period_ms=50). 8 pins wired one at a time.

**Test:** for each pin, run 1000 flip-up / flip-down cycles. Watch visually that pin transitions between detents each time (use magnifier).

**Pass criterion:** ≥99% of flips succeed. Allow 1% failures attributed to debris or timing edge cases.

**If fail:**
- <50% success: coil field too weak. Add ferrite layer, or increase coil layer count, or shrink magnet-coil gap.
- 50–95% success: marginal. Tune pulse duration, reduce coil-magnet gap, or swap to 6-layer coil PCB.

### 7.3 G3 — Pulse current and energy

**Setup:** insert a 0.1 Ω current-sense resistor in series with the H-bridge VM line. Measure voltage across resistor with oscilloscope.

**Test:** run flip_up() and capture waveform. Calculate I_peak and energy = ∫I²R dt over the pulse duration.

**Pass criterion:** I_peak <2 A; energy per flip <30 mJ.

**If fail:**
- I_peak >2 A: reduce pulse duration or add series limiting resistor; risk of H-bridge overheating at scale.
- Energy >50 mJ: PCB thermal load too high; redesign coil for lower resistance or shorter pulses.

### 7.4 G4 — Crosstalk (most important)

**Setup:** flip pin 4 (center of 8-pin grid) to the up state. Confirm via visual inspection. Set pin 4 aside.

**Test:** run cycle_test on pin 3 (adjacent) for 1000 cycles. After completion, visually inspect pin 4. Did it move?

Repeat for pin 5 (also adjacent), and pin 1 (diagonal, 2.5 × √2 = 3.5 mm distant).

**Pass criterion:** pin 4 does not move during any of the neighbor cycle tests.

**If fail:**
- Pin 4 disturbed by adjacent: add pot-magnet shielding (next escalation per [`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md) §4.4).
- Pin 4 disturbed by diagonal: rare; suggests fundamental field strength too high. Consider tighter pole washers.

### 7.5 G5 — Tactile feel

**Setup:** all 8 pins in checkerboard polarity, alternating up/down state.

**Test:** locate a fluent braille reader (NFB local chapter, school for the blind, or a friend who reads braille). Have them feel the surface. Ask:
- Does it feel like braille dots?
- Are individual dots distinguishable?
- Do dots feel firm or mushy?
- Any sharp edges?

**Pass criterion:** reader confirms dots feel like standard braille (firm, distinct, ~0.5 mm height).

**If fail:**
- Mushy: detent force too low (see G1).
- Sharp: pin tops not deburred or top plate hole edges are sharp; chamfer them.
- Indistinct: dots either too low (<0.4 mm) or too widely spaced (verify pitch).

---

## 8. Iteration plan

If V1 single-cell fails any gate:

| Failure | V2 changes | V2 cost delta |
|---|---|---|
| G1 force <100g | 1.5 mm pole washers, 2-magnet stack per pin | +$8 |
| G1 force 100–150g | N50 magnets | +$3 |
| G2 flip <50% | 6-layer PCB, ferrite layer | +$15 |
| G2 flip 50–95% | Pulse tuning (firmware only) | $0 |
| G3 current high | Series resistor, redesigned coil | $5 |
| G4 crosstalk | Pot-magnet shielding | +$5 |
| G5 tactile | Cosmetic top-plate finishing | $0 |

Maximum V2 BoM: $194 + $36 = $230. Still far below V1 of larger tiers.

**Hard rule: do not commit to 40-cell or full-array build until single-cell V1 or V2 passes all 5 gates.**

---

## 9. Outputs after gate-pass

Once all 5 gates pass, the following are committed knowledge:

1. **Pole-washer thickness (V1 1.0 mm or V2 1.5 mm).** Locks pole-plate spec for 40-cell and full array.
2. **Magnet count per pin (V1 1× or V2 2×).** Locks pin-blind-hole depth for 40-cell and full array.
3. **Coil PCB layer count (V1 2-layer or V2 6-layer).** Locks coil PCB spec for 40-cell.
4. **Ferrite layer y/n.** Locks PCB stack-up for 40-cell.
5. **Pulse parameters (duration, current).** Locks driver settings for 40-cell.
6. **Crosstalk mitigation level (none / pot-shield / mu-metal).** Locks shielding strategy for 40-cell.
7. **Tactile feel calibration:** detent force achievable at this geometry.

These get recorded in `docs/braille/SINGLE_CELL_RESULTS.md` (template provided in the doc folder), and the 40-cell build manual draws from them.

---

## 10. Summary

Cost: ~$110–195. Time: 1–2 weekends to assemble + 1 weekend of testing. Outcome: a definitive answer to whether the architecture works.

**This single prototype prevents the most likely failure mode of the project: ordering $3,000 of parts on a design that doesn't actually flip pins.**

If you cut corners anywhere in the build process, do not cut corners here. The whole project rides on this measurement.
