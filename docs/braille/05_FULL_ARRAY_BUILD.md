# 05 — Full-array build manual (3,840 pins, Monarch-class)

**Prerequisite:** all gates of [`03_SINGLE_CELL_BUILD.md`](03_SINGLE_CELL_BUILD.md) AND [`04_FORTY_CELL_BUILD.md`](04_FORTY_CELL_BUILD.md) must have passed. Carry forward all locked design parameters.

**Goal:** ship a working Monarch-class refreshable tactile display.

**Total cost:** ~$3,200 in parts (with all measurement-driven mitigations) or ~$2,800 minimum.

**Time:** 5–6 weeks.

---

## 1. Final geometry

| Property | Value |
|---|---|
| Pin count | 3,840 |
| Array layout | 60 columns × 64 rows (alternative: 80 × 48 or 96 × 40) |
| Pin pitch | 2.5 mm both axes (uniform grid for graphics + braille) |
| Active area | 150 × 160 mm (5.91 × 6.30 in) |
| External footprint | 180 × 190 mm (7.09 × 7.48 in) |
| Total height (table) | 22 mm (0.87 in) |
| Mass | ~800 g (1.76 lb) |
| Power input | 12 V / 4 A barrel jack OR 5 V / 5 A USB-C PD |
| Host interface | USB-C (HID Braille + custom vendor) |

**Chosen layout: 60 × 64.** This gives a roughly square aspect ratio, ideal for charts. If you prefer 96-column (matches Monarch's 96 × 40), see §11 for the parameter swap.

---

## 2. Bill of materials (priced cart, April 2026)

This is the expanded BoM with manufacturing-grade specifications. Where two prices are listed, "min" is the floor with no spares; "spec" is the recommended quantity to absorb iteration losses.

### 2.1 Mechanical

| # | Item | Spec | Qty | Vendor | SKU / part | Unit | Subtotal |
|---|---|---|---|---|---|---|---|
| 1 | Pin stock | 1.5 mm Ø stainless 304, 1 m | 50 m | McMaster-Carr | 89535K22 | $4.50/m | $225 |
| 2 | Pin magnets | N42 NdFeB, 1.0 × 1.0 × (1.0 or 1.5) mm | 4,200 (3,840+360 spare) | KJ Magnetics or AliExpress bulk | — | $0.08 ea | $336 |
| 3 | Pole washers | 1018 steel, 1.5 OD × 0.6 ID × 1.0 mm (or 1.5 from single-cell results) | 8,000 | SendCutSend custom | — | $0.04 ea | $320 |
| 4 | Pot-magnet shields (if required from G4) | Soft-iron cup, 2.0 OD × 1.5 ID × 1.0 mm | 4,000 | Custom progressive die OR DIY | $0.10 | $400 |
| 5 | Top plate | 6061-T6 Al, 180 × 190 × 3 mm, 3,840 holes Ø1.6 mm + dowel holes | 1 | SendCutSend "CNC routing" tier (`top_plate_full.dxf`) | $130 | $130 |
| 6 | Top plate anodize | Class 2 black anodize, type II clear | 1 | SendCutSend or Anodizing.com | $30 | $30 |
| 7 | Pole-plate steel | 1018, 0.5 mm sheet, 180 × 190 mm, 3,840 × 1.5 mm holes | 2 | SendCutSend laser (`pole_plate_full.dxf`) | $50 ea | $100 |
| 8 | Pole-plate passivate | Zinc clear chromate or oil | 2 | included | — | $20 |
| 9 | Cell-housing plate | CF-PETG 3D print (`cell_housing_full.scad`) | 1 | self-print | filament ~$25 | $25 |
| 10 | Ferrite sheet | 0.5 mm, 160 × 170 mm | 1 | TDK Flexield IFL10 | — | $60 |
| 11 | Mu-metal sheet (if required) | 0.1 mm, 180 × 200 mm | 1 | MagneticShield Corp | — | $80 |
| 12 | Dowel pins | 3 mm Ø × 16 mm hardened | 8 | McMaster-Carr | 91585A185 | $0.50 | $4 |
| 13 | M3 brass inserts | M3 × 5 mm | 16 | McMaster-Carr | 92395A111 | $0.50 | $8 |
| 14 | M3 fasteners | M3 × 16 mm cap screw | 16 | McMaster-Carr | 92000A124 | $0.20 | $3 |
| 15 | Enclosure | CF-PETG print, 200 × 220 × 30 mm shell | 2 (top + bottom) | self-print | filament ~$30 | $30 |
| 16 | Rubber feet | 12 mm Ø silicone | 4 | Adafruit | #550 | $1 | $4 |
| 17 | USB-C panel mount | 1 | Adafruit | #4090 | — | $5 | $5 |
| 18 | Barrel jack panel mount | 5.5/2.1 mm | 1 | Adafruit | #373 | $2 | $2 |
| **Mechanical subtotal (min)** | | | | | | | **$1,360** |
| **Mechanical subtotal (spec, all mitigations)** | | | | | | | **$1,840** |

### 2.2 Electronics

| # | Item | Spec | Qty | Vendor | SKU | Unit | Subtotal |
|---|---|---|---|---|---|---|---|
| 19 | Coil PCB | 4-layer (or 6-layer if required), 150 × 160 mm, 4/4 mil ENIG | 5 | JLCPCB JLC04161H tier | — | $135 | $675 |
| 20 | Driver PCB | 4-layer, 100 × 160 mm, 6/6 mil | 5 | JLCPCB | — | $30 | $150 |
| 21 | DRV8847 dual H-bridge | SOIC-16 PowerPAD | 30 (24+6 spare) | Texas Instruments | 296-46768-1-ND | $0.94 | $28 |
| 22 | 74HC4067 16-ch mux | TSSOP-24 | 60 (48+12 spare) | Digi-Key | 296-1610-1-ND | $0.50 | $30 |
| 23 | RP2040 (slave) | 6 slaves × 4 banks each | 8 (6+2 spare) | Adafruit | #4864 | $4 | $32 |
| 24 | RP2350 (master) | RP2350 chip + breakout | 2 | Adafruit | #5953 | $5 | $10 |
| 25 | Buck converter | TPS56C230 5V/3A point-of-load per bank | 30 (24+6 spare) | TI | 296-37956-1-ND | $1.50 | $45 |
| 26 | 12V supply | Mean Well RS-50-12, 4A barrel-jack output | 1 | Digi-Key | RS-50-12-ND | $30 | $30 |
| 27 | Bulk capacitors | 10,000 µF aluminum 16V | 6 | Nichicon | UVR1C103MED1TD | $5 | $30 |
| 28 | Decoupling caps | 100 nF ceramic + 10 µF tantalum, mixed | 200 | Digi-Key | various | bag | $25 |
| 29 | Passives | Resistors, ferrite beads | bag | Digi-Key | various | — | $30 |
| 30 | Ribbon cables | 16-conductor 100 mm | 6 | Adafruit | #4327 | $4 | $24 |
| 31 | Connectors | 16-pin 0.1" headers, mating sockets | 30 | Adafruit | #392 | $2 | $60 |
| 32 | USB-C cable | 1 m | 1 | Anker | — | $10 | $10 |
| 33 | Thermistors / temp sensors | TMP235 SOT-23 | 8 | Digi-Key | 296-39061-1-ND | $0.80 | $7 |
| 34 | 40 mm cooling fan | 12V, 4-wire PWM | 1 | Noctua NF-A4x10 5V (5V works on 12V derated) | — | $15 | $15 |
| 35 | Status LED | RGB indicator | 1 | Adafruit | #1763 | $2 | $2 |
| **Electronics subtotal** | | | | | | | **$1,203** |

### 2.3 Tooling and consumables (additional to single-cell + 40-cell)

| Item | Spec | Cost |
|---|---|---|
| PCB stencil | JLCPCB stainless steel framed, both PCBs | $40 |
| Solder paste (low-temp) | Sn42/Bi58 syringe | $25 |
| Hot-air station | for SMD reflow if not owned | $80 |
| Annealing oven | toaster oven $30 OR existing kitchen oven | $30 |
| Force gauge (if not bought for single-cell) | digital 0–1000 g | $30 |
| Pin handling jig | 3D-printed in advance from cad/pin_jig.stl | (filament $5) |
| Cleaning | IPA, dry PTFE, lint-free wipes | $20 |
| **Tooling subtotal** | | **$225** |

### 2.4 Total full-array budget

| Category | Min | Spec (all mitigations) |
|---|---|---|
| Mechanical | $1,360 | $1,840 |
| Electronics | $1,203 | $1,203 |
| Tooling | $225 | $225 |
| **Subtotal** | **$2,788** | **$3,268** |

Excluding tooling already owned from prior tiers: deduct ~$200.

**Realistic total: $2,800–3,300.**

Compare:
- APH Monarch retail: $17,000 (5–6× this)
- Dot Pad retail: $8,000 (2.5–3× this)

---

## 3. CAD files

All in `docs/braille/cad/full-array/`:

| File | Purpose | Format | Notes |
|---|---|---|---|
| `cell_housing_full.scad` | 150×160×4 mm CF-PETG plate | OpenSCAD | 3,840 sleeves; supports SPLIT_AT options for smaller printers |
| `top_plate_full.dxf` | Aluminum top plate | DXF | 180×190×3 mm, 3,840 H8 drilled holes |
| `pole_plate_full.dxf` | Steel pole plate | DXF | 180×190×0.5 mm, 3,840 holes |
| `enclosure_top_full.scad` | Top shell with active window | OpenSCAD | Captive-screw layout for top-plate removal |
| `enclosure_bottom_full.scad` | Bottom shell with PCB pillars | OpenSCAD | Heat-set inserts, fan port |
| `coil_pcb_full/` | KiCad project | KiCad 8 | 4-layer; alt 6-layer revision in `_v2/` |
| `driver_pcb_full/` | KiCad project, separate driver board | KiCad 8 | 4-layer |
| `pin_jig.stl` | Pin assembly jig | STL | Holds 16 pins, integrates with polarity sorter |

---

## 4. PCB design (full-array)

### 4.1 Coil PCB stack-up

V1 (4-layer, validated by single-cell pass):
- L1 (top): coil layer 1, ground pour outside coils, 1 oz copper
- L2: coil layer 2, 0.5 oz copper
- L3: coil layer 3, 0.5 oz copper
- L4 (bottom): coil layer 4, 1 oz copper, escape routing to bank connectors

V2 (6-layer, if single-cell needed extra field):
- L1, L3, L4, L6: coil layers (4 stacked spirals, all wound same direction)
- L2, L5: ground / power planes
- 1 oz outer, 0.5 oz inner

Stitching vias at coil center (0.3 mm drill) and outer terminus connect all coil layers in series.

### 4.2 Bank connectors

Coils grouped into 24 banks of 160 coils each. Each bank's row/column matrix terminates at a 16-pin 0.1" header on the PCB edge. Bank headers space-evenly around 4 sides.

Ribbon cable from each bank header to the driver PCB.

### 4.3 Coil PCB process spec

- Layers: 4 (V1) or 6 (V2)
- Substrate: FR-4 standard
- Thickness: 1.6 mm
- Outer copper: 1 oz/ft²
- Inner copper: 0.5 oz/ft²
- Surface finish: ENIG (mandatory for 4 mil traces)
- Solder mask: black matte
- Trace/space: 4/4 mil (JLCPCB JLC04161H tier)
- Drill: 0.3 mm minimum (for via stitching)
- Reamed dowel-hole upgrade: yes, on 3 mm holes used for stack alignment (+$15)
- Quantity: 5 (1 V1, 1 V2 backup, 3 spares for damage during reflow)

### 4.4 Driver PCB

Smaller, simpler: 4-layer 100 × 160 mm carries 24 H-bridges, 48 muxes, 6 RP2040 slaves, 1 RP2350 master, power conditioning. Trace/space 6/6 mil (standard tier). Cost ~$30/board, order 5.

### 4.5 Manufacturing files checklist

Before submitting:
- [ ] Run KiCad DRC; pass at 4/4 mil
- [ ] Run KiCad ERC; no errors
- [ ] Check via stitching density (1 via per 4 mm² of plane)
- [ ] Verify silkscreen doesn't overlap pads
- [ ] Generate Gerbers (X2 format) for all layers
- [ ] Generate Excellon drill files (plated + non-plated)
- [ ] Generate IPC-356 netlist for fab continuity test
- [ ] Generate pick-and-place CSV for assembly service if used
- [ ] BoM CSV with manufacturer part numbers (matches §2.2)
- [ ] Zip files together; visually verify in JLCPCB online viewer before paying

### 4.6 PCB-fab options

| Tier | JLCPCB SKU | Cost (5 of 150×160 4-layer) | Yield expectation |
|---|---|---|---|
| Standard 4-layer | JLC04161H | $135 each | ~5 dead coils per board (acceptable) |
| 4 mil specialist | JLC04161H w/ reamed dowels | $150 each | ~3 dead coils per board |
| PCBWay 0.075 mm | (different fab) | $200 each | ~1 dead coil per board |

V1 spec: standard JLC04161H. Upgrade to PCBWay if first batch shows >10 dead coils.

---

## 5. Print parameters (full-array)

### 5.1 Cell housing — the most critical print in the project

**Material: CF-PETG, mandatory.** Plain PETG warps 0.5–0.8 mm corner-to-center over 160 mm; CF-PETG warps <0.1 mm.

| Setting | Value | Reason |
|---|---|---|
| Printer | Bambu X1C / Voron 2.4 / Prusa XL | Need 160+ mm both axes; reliable |
| Nozzle | 0.4 mm hardened steel | CF abrades brass |
| Layer height | 0.10 mm | Sleeve precision |
| Walls | 4 perimeters | Sleeve walls *are* perimeters |
| Top/bottom layers | 5/5 | Stiffness |
| Infill | 30% gyroid | More than single-cell for stiffness; less than 50% to save print time |
| Print orientation | Pin sleeves vertical (mandatory) | Bores become a stack of perimeters |
| Print speed (perimeters) | 30 mm/s | Slow perimeters = sharp bores |
| Print speed (other) | 80 mm/s | Doesn't matter for precision |
| Cooling | 100% on perimeters | PETG benefits |
| Bed adhesion | Textured PEI + glue stick | PETG warps off smooth surfaces at this size |
| Print time estimate | 14–18 hours | Plan for one printer-day |
| Filament | ~150 g | Single spool sufficient |

### 5.2 Annealing — mandatory for full-array

After printing the cell housing:

1. Place on flat ceramic tile or aluminum plate.
2. Heat oven to 80°C (use a separate thermometer to verify; built-in thermometers often read low by 5–10°C).
3. Hold for 4 hours.
4. Power off oven; let cool to room temp with door closed (~5 hours).

This relieves print stress and prevents long-term warpage. Skipping this step has been documented to cause 0.3–0.5 mm warpage over 6 months at this footprint.

### 5.3 Bore tolerance verification

After annealing, before assembly:

1. Measure plate's outer dimensions vs CAD (±0.05 mm).
2. Test 20 random sleeves with 1.5 mm gauge pin: must slide freely under gravity.
3. Test 20 random sleeves with 1.4 mm gauge pin: pin must NOT fall through.
4. If >10% of sleeves fail either test, re-print or post-process (drilling out tight bores with a 1.55 mm bit).

### 5.4 Enclosure print

| Part | Material | Layer | Walls | Infill | Time |
|---|---|---|---|---|---|
| Bottom shell | PETG (plain OK) | 0.20 | 3 | 20% | ~10 h |
| Top shell | PETG | 0.20 | 3 | 20% | ~8 h |

Brass heat-set inserts: install with soldering iron at 250°C, 16 inserts at corner clamp points.

---

## 6. Final stack-up dimensions

For reference and to verify all parts together fit the enclosure design.

| Layer | Part | Thickness | Cumulative |
|---|---|---|---|
| 0 | Rubber foot | 4.0 mm | 4.0 |
| 1 | Bottom enclosure floor | 4.0 mm | 8.0 |
| 2 | Driver PCB | 1.6 mm | 9.6 |
| 3 | Driver-to-coil-PCB clearance | 1.0 mm | 10.6 |
| 4 | Coil PCB | 1.6 mm | 12.2 |
| 5 | Ferrite sheet | 0.5 mm | 12.7 |
| 6 | Lower pole washer plate | 0.5 mm | 13.2 |
| 7 | Cell housing (with pin travel inside) | 4.0 mm | 17.2 |
| 8 | Upper pole washer plate | 0.5 mm | 17.7 |
| 9 | Top plate (Al) | 3.0 mm | 20.7 |
| 10 | Pin protrusion above top plate (max) | 0.5 mm | 21.2 |
| **Total height (table to highest pin)** | | | **~21.2 mm** |

External enclosure adds top trim ~1 mm border above pin height for protection: **~22 mm height including enclosure top.**

---

## 7. Week-by-week assembly plan

### Week 1 — Order parts and prepare

- [ ] Order Mechanical items 1–5 (rod, magnets, pole washers, top plate, pole plates).
- [ ] Order ferrite, mu-metal, fasteners.
- [ ] Submit Coil PCB and Driver PCB Gerbers to JLCPCB.
- [ ] Print 2 cell-housing plates (1 use, 1 backup).
- [ ] Anneal cell housings.
- [ ] Print enclosure shells.
- [ ] Print pin assembly jig and force-test jig.

### Week 2 — Receive parts, validate plates

- [ ] Visually inspect aluminum top plate (no burrs, dowel holes round).
- [ ] Check pole plates for rust; touch up with oil if needed.
- [ ] Continuity-test 50 random coils on each PCB; reject boards with >5 broken traces.
- [ ] Reflow drivers onto driver PCB. Test bench supply 12 V → 5 V conversion.
- [ ] Flash master + slave MCUs with skeleton firmware; verify SPI communication.

### Week 3 — Pin preparation

- [ ] Cut 4,200 pin blanks. (Rotary tool with cutoff jig + magazine feed; ~6 hours.)
- [ ] Drill blind holes in all pins. (Drill press + jig; ~10 hours.)
- [ ] Press-fit magnets, alternating polarity per checkerboard plan. **(3,840 pins; budget 30 hours across 4 sessions.)**
- [ ] Color-code pin tops (red for N-up, blue for S-up).
- [ ] Sort into trays of 64 pins per tray (one tray = one column of array).

### Week 4 — Stack assembly

- [ ] Lay PCB on bench, components-side down.
- [ ] Bond ferrite sheet to PCB top with double-stick tape.
- [ ] Place lower pole plate on ferrite, dowel-aligned.
- [ ] Place cell-housing on lower pole plate, dowel-aligned.
- [ ] **Place pins, column by column, alternating from N-up tray and S-up tray.** Budget 4–6 hours; do not interrupt.
- [ ] Place upper pole plate.
- [ ] Place top plate.
- [ ] Insert dowel pins through corner alignment.
- [ ] Tighten 8 corner clamp screws.

### Week 5 — Wiring and power-up

- [ ] Connect ribbon cables: each bank header on coil PCB → corresponding bank input on driver PCB (24 connections).
- [ ] Connect master USB-C, power barrel jack, fan PWM, status LED.
- [ ] **Power up at 12 V with 1 A current limit** (less than full 4 A spec for safety).
- [ ] Run firmware self-test: cycle every pin up then down sequentially; visually verify each transitions.
- [ ] Identify and document any dead pins.
- [ ] Increase current limit to 4 A; run full self-test at speed.

### Week 6 — Validation

- [ ] G8: time full-array refresh; spec ≤1.6 s.
- [ ] G9: 24-hour soak test; <5 stuck pins after.
- [ ] G10: tactile read with fluent reader; render text and chart graphics.
- [ ] Document results in `FULL_ARRAY_RESULTS.md`.

---

## 8. Validation tests (decision gates)

### 8.1 G8 — Full-array refresh time

Run `firmware/full_array.py` self-test loop measuring time from frame-write to all-pins-settled.

**Pass:** ≤1.6 s for full clear-and-fill (V1 24-bank topology).

**If fail, escalate:**
- Up to 60-bank topology (additional driver chips); refresh becomes ~640 ms.
- Increase pulse current to reduce pulse width: gates check 4 A continuous H-bridge thermal.

### 8.2 G9 — Stuck-pin rate

24-hour soak at 1 refresh/second with random patterns.

**Pass:** ≤5 stuck pins of 3,840 (0.13%, acceptable for V1).

**If fail:**
- 5–20 stuck: identify locations; suspect mechanical. Re-stack assembly with more care.
- >20 stuck: systemic problem. Re-examine PCB QC, plate alignment.

### 8.3 G10 — Tactile read

Real fluent reader, 30+ minutes of usage testing:
- Render alphabet line; reader can read.
- Render Unicode 8-dot extended cell; reader can identify letters.
- Render simple bar chart (5 bars at varying heights); reader can identify max/min.
- Render line chart with 10 data points; reader can describe trend direction.

**Pass:** reader confirms readability and identifies graphics features at first attempt.

**If fail:** cosmetic top-plate finish, dot-height calibration, or fundamental geometry issues. Iterate.

---

## 9. Known-issue tracker (V1 → V2)

Realistic things that will probably need fixing in V1 → V2:

- Some ICs reflow with shorts; rework with hot air.
- A handful of dead pins from PCB defects; map them in firmware to skip rendering.
- Crosstalk margin tight; may need to add pot-magnet shielding (deferred from G4).
- Cell housing may warp slightly over 6 months; budget a re-print and re-assembly at 6-month mark.
- Buck converter ringing on the 5V rail under burst load; add output capacitor.

V2 build cost ~$500–800 (mostly PCB re-spin + cell-housing re-print + a few new ICs).

---

## 10. Operating envelope

After validation:
- Continuous power: 4 A @ 12 V = 48 W during refresh, ~0.5 W idle.
- Refresh rate: 1 Hz typical, 2 Hz max with active cooling.
- Operating temperature: 10–40°C ambient (limited by NdFeB demagnetization above 60°C — keep ambient + thermal rise < 60°C internal).
- Storage: 5–50°C, 0–80% RH non-condensing.
- Reading pressure: tested up to 250 g per pin.

---

## 11. Layout alternatives

The 60×64 layout is one of several viable.

| Layout | Active area | Aspect | Use case |
|---|---|---|---|
| 60 × 64 | 150 × 160 mm | 0.94 (near-square) | Charts, mixed text+graphics |
| 96 × 40 | 240 × 100 mm | 2.40 (Monarch) | Wide pages, text-heavy |
| 80 × 48 | 200 × 120 mm | 1.67 (paper page) | Books, long-form |
| 32 × 120 | 80 × 300 mm | 0.27 (vertical) | Single-stock charts |

To swap layouts, change the parameters at the top of `cell_housing_full.scad` and re-run all CAD generation. PCB design must also be re-laid (one bank per column for asymmetric layouts).

V1 uses 60 × 64 because:
- Best for a financial-charting use case (charts are roughly square).
- Single PCB at 150×160 mm is JLCPCB's largest standard size before higher tier.
- Symmetric mu-metal shielding pattern.

---

## 12. Summary

The full array is the project deliverable. By the time you build it:

- Single-cell has validated the actuator mechanism.
- 40-cell has validated the addressing topology and tactile usability.
- All design parameters are locked from measurement, not analysis.

Building the full array is then *engineering*, not *invention*: order parts, follow the procedure, validate against the gates.

**Total project: ~$3,200 in parts (plus ~$300 in tooling), ~10–12 weeks elapsed time, results in a Monarch-class display at 18% of retail cost.** That's the value proposition. It only holds if you don't skip the gate process.
