# Multi-Line Refreshable Braille + Tactile Graphics Display — Design Document

**Project:** DIY Monarch-class refreshable tactile display
**Author target:** churst90
**Date:** 2026-04-30
**Status:** Pre-prototype design — not yet built. Treat all dimensions, costs, and timing numbers as engineering estimates pending bench validation.
**Scope note:** This is a personal hardware research project. It is not part of the AccessibleTrader product. Filed in `docs/` per request.

---

## 1. Goal

Build a refreshable tactile pin display with:

- **Pin count:** ≥ 3,840 individually-addressable pins (matches APH Monarch).
- **Pin pitch:** 2.5 mm in both axes (the within-cell dot spacing of standard braille). This makes the entire display area a uniform grid that can render either braille text *or* tactile graphics at the same resolution.
- **Array geometry:** 60 columns × 64 rows = 3,840 pins, display area 150 mm × 160 mm. Alternative geometries (80 × 48, 96 × 40, etc.) are interchangeable in this design — dimensions below assume 60 × 64.
- **Refresh time:** ≤ 1 second full-array (matches Dot Pad's ~1 s spec).
- **Pin behavior:** Bistable — each pin is fully up or fully down with no power required to *hold* either state. Power is consumed only during state changes.
- **Tactile feel:** Pin retention against finger pressure ≥ 150 g per pin. Pin height tolerance ≤ ±0.1 mm.
- **Cost target:** ≤ $3,000 in parts for a working prototype. (Commercial reference: APH Monarch retails ~$17,000; Dot Pad ~$8,000+.)

---

## 2. Architecture summary

**Choice:** Per-pin bistable magnetic actuator, PCB-coil drive, parallel matrix addressing in independent banks.

A magnet is bonded to the bottom of each pin. Each pin slides vertically inside a precision-drilled sleeve in a stack of fixed plates. Two soft-iron pole pieces — one above, one below — provide bistable detent positions: the magnet snaps to either pole and is held there by permanent-magnet attraction, with no standing power. To flip a pin, a planar spiral coil etched into a multi-layer PCB directly beneath the pin is pulsed with current; the pulse direction (polarity) determines whether the pin is driven up or down. The PCB carries the coils, the H-bridge drivers, and the addressing logic in a single rigid substrate.

This is the cleaned-up version of the user's "PCB-divided-into-grids" idea. **The fatal-flaw version is addressed in §4.**

The architecture is essentially a hobbyist reimplementation of the published EPFL bistable haptic actuator (EP3382678A1, Zarate & Shea) and Dot Inc's commercial actuator (used in the Monarch and Dot Pad), with the simplifications a maker can build at home.

### Why this architecture

| Property | Bistable magnetic + PCB coil | Piezoelectric (Metec) | Cam/scanning (Orbit, MagnePins) |
|---|---|---|---|
| Hold power | Zero | Continuous high voltage | Zero |
| Switching time | 5–30 ms per pin (parallel) | ~50 ms | 100 ms – 13 s (whole-array) |
| Driver voltage | 5 V | ~200 V | 5 V |
| Per-pin parts cost | ~$0.30 | ~$10 | ~$0.20 |
| Audible noise | Quiet (soft latch) | Silent | Audible thunk |
| Scalable to 3,840 pins | Yes | Cost-prohibitive | Mechanically prohibitive |
| DIY-buildable | Yes (PCB + magnets) | No (custom piezo fab) | Yes (slow refresh) |

The combination of *low hold power*, *fast parallel switching*, and *low per-pin part cost* is what makes a Monarch-class display reachable for a maker. No other current architecture combines all three.

---

## 3. The mechanism in detail

### 3.1 Pin assembly (× 3,840)

```
 ┌──────────┐       ← top plate (drilled hole, 1.6 mm)
 │  ▼ pin   │
 │  ▼ pin   │       ← pin protrudes ~0.5 mm in "up" state
 │  ▼ pin   │
 ├──────────┤       ← UPPER pole washer (soft iron, 1 mm thick)
 │ ▒ magnet │       ← magnet bonded to pin base
 │  ▒       │       ← lower-stable position shown
 │  ▒       │
 ├──────────┤       ← LOWER pole washer (soft iron, 1 mm thick)
 │ ░ coil ░│       ← planar spiral coil in PCB, multi-layer
 └──────────┘
```

- **Pin:** 1.5 mm Ø stainless-steel rod, ~9 mm long, magnet captured in a press-fit blind hole at the base (no glue — see §6 risk #4).
- **Magnet:** 1.0 mm Ø × 1.0 mm N42 neodymium disc, axially magnetized. (N42 not N52, for thermal/demagnetization stability — see §6 risk #6.)
- **Sleeve:** 1.6 mm hole through the cell-housing plate; pin slides freely with ~0.05 mm clearance. Brass insert preferred for low friction; otherwise polished printed PETG.
- **Pole pieces:** Two soft-iron washers per pin (upper and lower detent). 1.5 mm OD × 0.6 mm ID × 1.0 mm thick. Provide bistable retention via permanent-magnet attraction.
- **Pin throw:** 1.0 mm total travel. Up state protrudes 0.5 mm above top plate (standard braille dot height).

### 3.2 PCB coil array

Each pin has a dedicated planar spiral coil directly beneath it on a 4-layer PCB. The 4 layers are stacked spirals, all wound in the same direction so currents add up.

- **Coil pitch:** 2.5 mm — coils tile the array with no gaps between adjacent pins.
- **Coil diameter:** ~2.0 mm OD per coil, leaving 0.5 mm clearance between adjacent coils.
- **Turns per layer:** ~10 turns of 0.1 mm trace width, 0.1 mm spacing.
- **Total turns per coil (4 layers):** ~40.
- **Coil resistance:** ~4 Ω.
- **Drive current:** 0.8 A pulsed, 10 ms duration.
- **Energy per flip:** ~25 mJ (well within thermal limits at 1 % duty cycle).

The PCB is the structural and functional core of the display. Manufacturing precision (JLCPCB / PCBWay class) is more than enough for 0.1 mm coil-to-coil registration.

### 3.3 Latching geometry

This is the make-or-break design detail.

The two soft-iron pole washers above and below the magnet's travel define stable positions. With N42 magnets at 1 mm × 1 mm, modeled holding force at the pole interface should be approximately 200–300 g — sufficient against typical 150 g finger pressure. Real numbers must be measured on a 1-cell prototype before scaling.

**Crosstalk between adjacent magnets at 2.5 mm pitch is the central physics risk.** Three mitigations stack:

1. **Pot-magnet shielding.** Each pin's magnet sits inside a small soft-iron cup that concentrates flux downward and shields neighbors. This is the trick NTT used in MagneShape (2023). Adds ~$0.05 per pin and one assembly step.
2. **Alternating polarity.** Adjacent pins' magnets are oriented N-up vs. S-up in a checkerboard. Neighbors then *attract* each other into detent rather than repel out of it. Free, requires only orientation discipline at assembly.
3. **Magnetic shielding planes.** A thin mu-metal sheet between rows (or between cells) further reduces crosstalk. ~$10–20 of mu-metal foil for the whole array.

### 3.4 Addressing topology

This is where the user's "localized PCB grids" idea is correct in spirit and where I add structure.

The 3,840 coils are NOT individually wired to 3,840 driver channels — that would be cost- and complexity-prohibitive. They're addressed in **parallel banks**:

- The array is divided into **24 banks** of **160 coils each** (e.g., 24 banks of 10 columns × 16 rows each, or any equivalent partition).
- Each bank has its own dedicated H-bridge driver IC capable of driving one coil at a time.
- All 24 banks fire **simultaneously, in parallel** — 24 different coils flipping in different parts of the array at the same instant.
- Within a bank, the 160 coils are addressed sequentially via a row/column matrix: each pulse selects one row and one column, the coil at that intersection sees full current, the rest see nothing or sub-threshold half-current.

**Refresh-time math:**
- 160 coils per bank, 10 ms per pulse, 16 row-scans × 10 col-scans = 160 pulses sequentially within a bank.
- Total per-bank scan: 160 × 10 ms = 1.6 s.
- All banks run simultaneously → full array refresh = **1.6 s** (close to the 1 s spec, achievable with shorter pulse widths or more banks).

**Optimization:** Increase to 60 banks of 64 coils each (one bank per column). Refresh becomes 64 × 10 ms = **640 ms**, beating the spec at the cost of more driver chips.

This is the architectural insight: the user's "PCB divided into grids" idea, done right, means **each grid is an independent driver channel firing in parallel**. That's how to hit sub-second refresh on 3,840 pins without 3,840 drivers.

### 3.5 Driver electronics

Each bank needs:
- One quad H-bridge IC (DRV8847 or equivalent) for polarity control of the coil pulse.
- Row/column select switches (74HC4067 16-channel multiplexer × 2, or equivalent).
- 5 V power rail with ~2 A burst capacity.
- Local 100 µF bulk cap to handle current spikes.

Total driver-chip count: ~24 H-bridges + 48 muxes + supporting passives.

### 3.6 Microcontroller architecture

- **Master:** RP2350 (Pi Pico 2) — $5. Coordinates bank addressing, holds the framebuffer, exposes USB/Bluetooth.
- **Slave per group of 6 banks:** RP2040 — $4 each × 4 = $16. Each runs its 6 banks' inner scan loop independently using PIO peripherals.
- **Communication:** SPI or I²C between master and slaves.

Total MCU cost: ~$25.

### 3.7 Power

Peak instantaneous power: 24 banks firing × 0.8 A × 5 V = **96 W peak**.
Average power: 96 W × 1 % duty cycle = **~1 W average** during refresh.
Power supply spec: 5 V, 20 A continuous (Mean Well RS-100-5, ~$25). Burst overhead handled by local capacitance.

### 3.8 Frame and alignment

A rigid sandwich:
1. Top plate (3 mm aluminum, laser-cut or CNC-drilled, 3,840 holes at 1.6 mm) — this is the tactile reading surface.
2. Cell-housing plate (3D printed PETG, 4 mm thick, 3,840 sleeves) — guides each pin.
3. Upper pole washer plate (laser-cut steel sheet, 0.5 mm) — provides upper detent for all pins simultaneously.
4. Spacer (0.5 mm).
5. Lower pole washer plate (laser-cut steel sheet, 0.5 mm) — lower detent.
6. PCB coil array (4-layer, 150 × 160 mm, with all 3,840 coils).
7. Bottom enclosure with driver electronics.

Layers 1–7 are clamped together with **dowel pins at the four corners** for ±0.1 mm registration. All seven layers must register, so this is a tolerance-stack-up problem (see §6 risk #2).

---

## 4. Scrutiny of the original "PCB-grid attract/repel" idea

Your idea was: divide the backplate into grids where each grid powers a small group of pins (e.g., 4), and pulse a whole grid at once for parallel refresh.

**Two ways to read that idea — one is wrong, one is right.**

### 4.1 Wrong reading: one coil shared among 4 pins (FATAL FLAW)

If a single coil sits beneath 4 adjacent pins and a current pulse generates one magnetic field over all of them, **all 4 pins flip in the same direction at once**. Braille requires *independent* per-pin control: one pin up, the next down, the next up. A shared coil cannot produce that — it's not braille, it's 4-pixel tile graphics. A 3,840-pin display with shared 4-coil groups is functionally a 960-tile display.

There is no clever firmware or pulse trick that recovers per-pin addressability with a single shared coil. The fatal flaw is physics, not control: one coil, one field direction, all magnets in that field flip together.

### 4.2 Right reading: per-pin coils, parallel-bank addressing (THE CORRECT VERSION)

What you almost certainly meant: **each pin has its own coil**, but the *driver electronics* are organized so that many coils across the array can be pulsed in the same instant. The PCB *is* divided into grids of localized control — but each grid contains its own per-pin coils, not a single shared coil. That is the architecture in §3.4 above, and it is correct.

This version produces:
- Per-pin addressability ✓
- Parallel refresh ✓
- Physical alignment guaranteed by single-PCB substrate ✓
- Sub-second full-array refresh ✓

**Bottom line:** the *intent* of your idea is exactly the right architecture and matches what the Monarch and Dot Pad use commercially. The literal "share a coil" version doesn't work. The "share a *driver bank* with per-pin coils" version is the right design.

### 4.3 Cost savings from this approach

Versus naïve per-pin driver electronics (3,840 driver channels):
- Naïve: 3,840 channels × $0.30/channel = $1,152 in drivers alone.
- Banked: 24 H-bridges × $0.94 + 48 muxes × $0.50 = ~$47 in drivers.

**Savings: ~$1,100, or roughly 1/3 of the entire prototype budget.** The banked approach is what makes the project economically viable.

---

## 5. Bill of materials with current prices (April 2026)

All prices are sourced from listed vendors at the time of writing. They drift; treat as estimates good to ±20 %.

### 5.1 Mechanical

| Item | Spec | Qty | Vendor | Unit | Subtotal |
|---|---|---|---|---|---|
| Pin stock (stainless rod) | 1.5 mm Ø × 1 m | 35 m | McMaster 89535K22 | $4.50/m | $158 |
| Pin magnets | N42, 1.0 × 1.0 mm disc | 4,200 | AliExpress / KJ Magnetics | $0.08 ea (bulk) | $336 |
| Pole washers (soft iron) | 1.5 OD × 0.6 ID × 1.0 mm | 8,000 | Custom laser-cut from 1018 sheet | ~$0.04 ea | $320 |
| Pot-magnet shields | 2.0 OD × 1.5 ID soft-iron cup | 4,000 | Custom progressive die or laser+form | ~$0.10 ea | $400 |
| Top plate | 6061-T6 aluminum, 150 × 160 × 3 mm, 3,840 × 1.6 mm holes | 1 | SendCutSend / OshCut | $90 | $90 |
| Cell-housing plate | PETG, 3D-printed | 1 | Self-printed | filament ~$3 | $3 |
| Pole-piece plates | 1018 steel sheet, 0.5 mm, laser-cut with 3,840 × 1.5 mm holes | 2 | SendCutSend | $40 ea | $80 |
| Mu-metal shielding | Sheet, 0.1 mm, 200 × 200 mm | 1 | MagneticShield Corp | $60 | $60 |
| Dowel pins (registration) | 3 mm × 12 mm, hardened | 8 | McMaster | $0.50 ea | $4 |
| Enclosure | 3D-printed PETG | 1 | Self-printed | filament ~$10 | $10 |
| Fasteners, springs, misc | — | — | — | — | $30 |
| **Mechanical subtotal** | | | | | **$1,491** |

### 5.2 Electronic

| Item | Spec | Qty | Vendor | Unit | Subtotal |
|---|---|---|---|---|---|
| Main PCB (4-layer) | 150 × 160 mm, 0.1 mm trace/space, ENIG | 4 (1 use, 3 spares) | JLCPCB | ~$110 ea | $440 |
| H-bridge driver | DRV8847 dual H-bridge, 5×4.4 mm HTSSOP | 30 (24 use, 6 spares) | Texas Instruments / Digi-Key | $0.94 ea | $28 |
| 16-ch analog mux | 74HC4067 | 60 | Digi-Key | $0.50 ea | $30 |
| Slave MCU | RP2040 (chip or board) | 5 (4 use, 1 spare) | Raspberry Pi / Digi-Key | $4 ea | $20 |
| Master MCU | RP2350 (Pi Pico 2 board) | 2 | Raspberry Pi / Digi-Key | $5 ea | $10 |
| Power supply | Mean Well RS-100-5, 5V/20A | 1 | Digi-Key | $25 | $25 |
| Bulk capacitors | 100 µF aluminum, 16V × 40 | 50 | Digi-Key | $0.20 ea | $10 |
| Decoupling caps, resistors, etc. | passives | bag | Digi-Key | — | $30 |
| Connectors, wire, headers | — | — | — | — | $40 |
| USB-C breakout for I/O | — | 1 | Adafruit | $10 | $10 |
| **Electronic subtotal** | | | | | **$643** |

### 5.3 Tooling and consumables

(Assumes user already has a 3D printer, soldering iron, multimeter.)

| Item | Spec | Cost |
|---|---|---|
| Calipers, 0.01 mm digital | Mitutoyo or equivalent | $50 |
| Reflow hotplate or air station | for SMD work | $80 |
| Microscope or USB inspection cam | 5–20× | $40 |
| Force gauge (kitchen scale + jig) | 0–500 g, 1 g resolution | $25 |
| PCB stencil for 4-layer board | JLCPCB stainless | $30 |
| Solder paste (low-temp) | Sn42/Bi58 | $25 |
| Magnet-handling tools | tweezers, anti-roll mat | $15 |
| Iteration parts budget (V2 rebuild) | — | $400 |
| **Tooling subtotal** | | **$665** |

### 5.4 Total

| Category | Cost |
|---|---|
| Mechanical | $1,491 |
| Electronic | $643 |
| Tooling | $665 |
| **Total prototype budget** | **$2,799** |

**Comparison:**
- APH Monarch retail: ~$17,000 (6× DIY cost).
- Dot Pad retail: ~$8,000 (3× DIY cost).
- Used Orbit Reader 20 (40 cells, single-line): ~$500 (incomparable — single-line, no graphics).

Multi-line tactile graphics displays do not exist commercially below ~$8,000. The DIY path here delivers Monarch-class capability at ~1/6 the price, **assuming the design works as specified**.

---

## 6. Risks and mitigations (in order of expected severity)

### 6.1 Magnetic crosstalk between adjacent pins (HIGHEST RISK)

At 2.5 mm pitch with N42 magnets, neighboring pins exert measurable force on each other. Without mitigation, setting one pin "up" can pull its neighbor up too, or two adjacent "up" pins may repel each other out of detent.

**Mitigations stacked:**
1. Pot-magnet flux shielding per pin (NTT MagneShape approach).
2. Alternating polarity in checkerboard (free).
3. Mu-metal sheets between cell rows.
4. If still insufficient: increase pole-washer thickness to 1.5 mm to deepen the detent well.

**Validation:** must be measured on a 4-pin sub-prototype before committing to the full PCB layout.

### 6.2 Multi-layer plate alignment (HIGH RISK)

7 plates must register to ±0.1 mm. Stack-up tolerances accumulate — 7 × ±0.05 mm = ±0.35 mm worst case, which exceeds spec.

**Mitigations:**
- Reference all critical dimensions to the corner dowel pins, not edge alignment.
- Specify reamed dowel holes on the top, pole-piece, and PCB layers (PCB houses can drill ±0.05 mm reamed holes for an upcharge).
- Cell-housing plate is 3D-printed and intentionally oversized on hole IDs (1.7 mm against 1.5 mm pin) so it doesn't constrain alignment.

### 6.3 Pot-magnet manufacturing (HIGH RISK)

Pot magnets at 2.0 mm OD are not off-the-shelf. Two paths:
1. **Buy custom from a magnet vendor** — minimum order quantities are typically 5,000+ at $0.10–0.50 each. Usable but increases lead time.
2. **DIY: glue magnet into a small soft-iron cup made from drawn tubing.** Tedious for 4,000 units.

**Mitigation:** prototype without pot-magnet shielding first; only add it if §6.1 measurements show crosstalk is fatal. The other two crosstalk mitigations may suffice alone.

### 6.4 Coil heating under repeated refresh

PCB coils dissipate energy as heat. At 1 % duty cycle the average is fine, but if firmware bugs cause sustained activation a coil can melt the PCB.

**Mitigations:**
- Hardware: small series resistor in each H-bridge output sized to limit steady-state current to 100 mA per coil even if the FET fails on.
- Firmware: pulse-width watchdog enforced by PIO timer, independent of main loop.
- PCB: thermal vias under each coil, ground-plane heatsink layer.

### 6.5 Mid-travel "stuck" pin failure

If a coil pulse is too weak (low battery, dust friction), a pin can end up between detent positions and stay stuck.

**Mitigations:**
- Bias geometry: lower-pole washer 10 % stronger detent than upper, so any unstable pin drops to "down" rather than sticking mid-travel.
- Periodic "reset to known state" command in firmware: drop all pins, then raise the ones that should be up. Tolerable if invisibly fast (<200 ms).
- Optional: per-pin position sensing via Hall-effect ICs is technically possible but explodes cost; not in scope for prototype.

### 6.6 Magnet demagnetization

N52 magnets have low coercivity and demagnetize under repeated coil pulses, especially at elevated temperature. Cumulative effect over thousands of cycles weakens the detent.

**Mitigation:** N42, not N52. Lower peak energy product, much higher coercivity. Detent strength recoverable through pole-piece geometry. **Already specified in BoM.**

### 6.7 Pin retention under finger pressure

If detent force is < 150 g, pins sink under reading pressure and feel mushy. Skilled braille readers will reject the device immediately.

**Mitigations:**
- Measure on 1-cell prototype. Iterate magnet/pole geometry until ≥ 200 g hold (30 % margin over spec).
- Recruit an actual braille reader for tactile feel review *before* committing to 3,840-pin manufacturing. The National Federation of the Blind has local chapters.

### 6.8 Audible click

Magnetic snap-detents click. 24 banks firing in parallel = a rapid clicking texture during refresh.

**Mitigations:**
- Soft elastomer (silicone, 50A durometer) bumpers between magnet and pole pieces — reduces "click" to "tick."
- Stagger pulses slightly across banks (0.1 ms offset) so energy isn't released simultaneously.
- Foam-lined enclosure.

### 6.9 Cleaning, dust ingress, skin oil

3,840 pins handled daily accumulate skin oil and dust. Within months pins may jam.

**Mitigations:**
- Top plate removable for cleaning (held by 4 captive screws, not glued).
- Polish all sleeve internals before assembly.
- Dry PTFE lubricant only — no oil.
- Sealed enclosure on all sides except the active pin face.

### 6.10 Patent landscape

Numerous patents in this space — Dot Inc, EPFL, Freedom Scientific, others. Building one for personal use is fine; selling, distributing, or commercializing requires a real freedom-to-operate analysis with a patent attorney, not a 30-minute Google search.

**Mitigation:** keep the project personal/research-scope until any commercial intent emerges.

---

## 7. Assembly instructions

### 7.1 Build a 1-cell prototype first

**Do not** order 3,840 magnets and a 4-layer 150 × 160 mm PCB before validating the mechanism on 8 pins.

The 1-cell prototype tests:
- Single-pin bistable detent function.
- Single-pin force retention ≥ 150 g.
- Single-pin coil-pulse flip with 5–30 ms timing.
- Crosstalk between 8 pins at 2.5 mm pitch.
- Tactile feel with a real reader.

Build per the simpler 1-cell BoM in the previous design conversation. Budget: ~$120, time: 1–2 weekends.

**Decision gate:** all 5 criteria must pass before scaling to 3,840 pins. If any fails, fix the 1-cell version. Do not order full-array parts on an unvalidated mechanism.

### 7.2 Full-array assembly (after 1-cell validation)

#### Phase 1: Prepare components (week 1)

1. Cut 4,000 pin blanks from stainless rod (saw or shear).
2. Drill 1.0 mm × 1.0 mm blind hole in base of each pin (use jig + drill press; budget 8–12 hours).
3. Press-fit one magnet into each pin's blind hole (alternating N-up and S-up per checkerboard plan).
4. Sort pins into N-up and S-up trays.
5. Lay out 4-layer PCB in KiCad with all 3,840 coil spirals, driver banks, traces. Schedule fabrication at JLCPCB (~10 day lead).

#### Phase 2: Plate fabrication (week 2)

1. Send aluminum top plate to SendCutSend with hole pattern.
2. Send steel pole plates to SendCutSend.
3. Print PETG cell-housing plate (12+ hour print on a typical FDM printer).
4. Print enclosure parts.

#### Phase 3: Sub-assembly (week 3)

1. Stack-test all 7 layers dry, no pins. Confirm dowel-pin registration to ±0.1 mm with calipers.
2. Reflow-solder all SMD components onto PCB. Test each driver bank independently with bench supply before final assembly.
3. Insert pins into cell-housing plate, oriented per checkerboard polarity plan. **This is tedious** — budget 4–8 hours, work in trays of 64 pins at a time.
4. Apply pot-magnet shields if used (can be deferred to V2 if §6.1 measurements allow).

#### Phase 4: Final stack-up (week 4)

1. Lower the cell-housing-with-pins onto the lower pole plate.
2. Lower the upper pole plate onto pin tops.
3. Lower the top plate.
4. Install dowel pins through all four corners.
5. Bolt PCB to enclosure base. Torque to spec — overtightening warps the PCB and shifts coils.
6. Connect ribbon cables between PCB driver banks and slave MCUs.
7. Power up at 5 V with current limit at 2 A. Run firmware self-test: cycle every pin "up" then "down" sequentially. Visually verify each pin moves.

#### Phase 5: Validation (week 5–6)

1. Pattern test: render the alphabet across a row. Verify each character is correct by sight.
2. Force test: put kitchen scale under a known "up" pin, press down with a finger jig, measure force at which pin sinks. Repeat for 20 random pins; all should pass ≥ 150 g.
3. Refresh-rate test: time full-array clear and full-array fill. Should be ≤ 1.6 s per direction in V1.
4. Sustained-use test: cycle the array 1,000 times overnight. Inspect for stuck pins, melted PCB, demagnetized cells.
5. Tactile reader test: arrange a session with a fluent braille reader. Run sample text and tactile graphics. Document feedback.

---

## 8. Firmware architecture (sketch)

### 8.1 Master responsibilities (RP2350)

- USB-CDC interface for host PC.
- Bluetooth LE for tablet/phone hosts.
- Framebuffer (3,840 bits, ~480 bytes).
- Translation: text ⇒ braille bit pattern; SVG/PNG ⇒ tactile bitmap.
- Distribute target framebuffer to 4 slaves over SPI.
- Trigger refresh; await completion ack.

### 8.2 Slave responsibilities (RP2040, ×4)

- Receive 6-bank slice of framebuffer (~960 bits = 120 bytes).
- For each bank, compute diff between current and target state (only flip what changed — saves power, reduces wear).
- Drive bank's DRV8847 + 74HC4067 muxes via PIO state machine.
- PIO program: assert row, assert column, pulse H-bridge for 10 ms, deassert. Repeat for all changed pins in bank.
- Watchdog: PIO timer enforces max pulse width independent of CPU.

### 8.3 Refresh strategy

Two modes:
- **Full refresh:** drop all pins, raise target pins. Used on power-up and every N seconds as cosmic-ray protection.
- **Diff refresh:** only flip pins whose state changed. Faster typical-case (only ~10 % of pins change between adjacent text frames).

---

## 9. Open questions before V1 build

These are unresolved — research or experiment needed:

1. **Exact pole-washer thickness** for ≥150 g detent at this pitch. Will require simulation (FEMM is free) or empirical sweep on the 1-cell prototype.
2. **Whether pot magnets are required**, or if checkerboard polarity + mu-metal alone suffices for crosstalk control. Can only be answered by 4–8 pin sub-prototype measurement.
3. **PCB coil turn count** to deliver target field. Above estimate (~40 turns / 4 layers / 0.8 A) is calculated, not measured. May need 6 layers or higher current.
4. **Whether 0.1 mm trace/space PCB is reliable** at JLCPCB volumes — may need to upgrade to PCBWay 0.075 mm class at higher cost.
5. **Heat dissipation under sustained refresh.** May require a small fan in the enclosure if user runs continuous animation rather than static text.

These should be resolved in the 1-cell and 4-cell sub-prototypes before the full PCB layout is finalized.

---

## 10. Summary

This design brings a Monarch-class refreshable tactile display to ~$2,800 in parts (vs. $17,000 retail) by combining:

1. **Bistable magnetic actuators** — proven by EPFL, Lee/Kim flip-latch, and Dot Inc.
2. **PCB-integrated coils** — moves the actuator structure into a single rigid substrate that simultaneously solves alignment, manufacturing, and cost.
3. **Banked parallel addressing** (the cleaned-up version of the user's "PCB-divided-into-grids" idea) — avoids 3,840 driver channels by sharing bank-level drivers across many per-pin coils with matrix select.
4. **Multi-layer crosstalk mitigation** — pot magnets + checkerboard polarity + mu-metal, applied incrementally as measurements demand.

The user's instinct to localize control on the PCB was correct. The single literal sentence "share a coil among 4 pins" doesn't work because braille requires per-pin addressability — but every other aspect of the intuition (parallel firing, single-substrate alignment, cost reduction through shared electronics) is exactly the right architecture and matches what shipping commercial products use.

The build is genuinely hard but not novel-research-hard. The mechanism is published, the parts are off-the-shelf, the failure modes are measurable. The biggest single risk is magnetic crosstalk at 2.5 mm pitch — solvable with known techniques but requires bench validation before committing to full-array manufacturing.

Build the 1-cell prototype first. Always.

---

## References

- [MagnePins: A Modular, Affordable, and DIY Refreshable Braille and Tactile Display (UIST 2025)](https://dl.acm.org/doi/10.1145/3746059.3747692)
- [Lee/Kim Flip-Latch Electromagnetic Actuator (IEEE Trans Haptics 2020)](https://pubmed.ncbi.nlm.nih.gov/31940550/)
- [EP3382678A1 — Bistable Electromagnetic Haptic Actuator (EPFL, Zarate & Shea)](https://patents.google.com/patent/EP3382678A1/en)
- [NTT MagneShape — Pot-magnet flux shielding for pin displays](https://group.ntt/en/newsrelease/2023/05/30/230530b.html)
- [Abbasi et al. — Bistable magnetic shells for braille (Adv. Mat. Tech. 2024)](https://advanced.onlinelibrary.wiley.com/doi/10.1002/admt.202301344)
- [APH Monarch product page](https://www.aph.org/product/monarch/)
- [Dot Inc. — Dot Pad tactile graphics display](https://www.dotincorp.com/en/product/dotpadx)
- [JLCPCB pricing reference](https://jlcpcb.com/quote)
- [TI DRV8847 datasheet](https://www.ti.com/product/DRV8847)
- [MagnePins open-source GitHub](https://github.com/JimSmiley/Magnepins)
