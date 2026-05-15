# DIY Monarch-class Refreshable Tactile Display — Build Documentation

**Project:** A 3,840-pin refreshable braille and tactile-graphics display, built at maker scale, for a target BoM of ~$3,000 vs. ~$17,000 retail (APH Monarch).

**Status:** Pre-prototype. Design is paper-validated against published actuator physics and known commercial implementations (Dot Inc, EPFL Zarate & Shea, NTT MagneShape). No silicon, plates, or magnets ordered yet. **Single-cell prototype is the first physical build and gates the entire project.**

**Author:** Tyler Hurst (churst90)
**Last revised:** 2026-05-01

---

## How to read this documentation

This is a **build-grade** document set, not a design overview. It is structured to be read by any of three audiences:

1. **You, the original author**, returning to the project after months away and needing to remember exact decisions.
2. **A collaborator or contractor** who must order parts, run a print, or solder a board without verbal handoff.
3. **A future commercial reviewer** evaluating whether the design clears patent and engineering hurdles to scale.

If a number is missing, that is a bug in the document, not an exercise for the reader. Open an issue or fix it inline.

---

## Document map

| # | File | Purpose | Read when |
|---|---|---|---|
| 00 | [`../BRAILLE_TACTILE_DISPLAY_DESIGN.md`](../BRAILLE_TACTILE_DISPLAY_DESIGN.md) | Original design narrative (kept for historical context — do not edit further) | Reading background |
| 01 | [`01_ARCHITECTURE.md`](01_ARCHITECTURE.md) | Magnetic-circuit physics, coil field derivation, thermal limits, every constant traced to a source | Sanity-checking the actuator before ordering parts |
| 02 | [`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md) | Side-by-side vs. Dot Inc / Monarch, why Monarch made its choices, red-team flaw register | Deciding whether the architecture is genuinely better or just different |
| 03 | [`03_SINGLE_CELL_BUILD.md`](03_SINGLE_CELL_BUILD.md) | 8-pin prototype: full BoM, vendor SKUs, print parameters, every assembly step, four-criterion decision gate | First physical build — start here |
| 04 | [`04_FORTY_CELL_BUILD.md`](04_FORTY_CELL_BUILD.md) | 320-pin single-line display: scales the cell, validates addressing, gates the full array | After single-cell passes |
| 05 | [`05_FULL_ARRAY_BUILD.md`](05_FULL_ARRAY_BUILD.md) | 3,840-pin Monarch-class array: full week-by-week, cart-priced BoM, alignment procedure | After 40-cell passes |
| 06 | [`06_FIRMWARE.md`](06_FIRMWARE.md) | RP2350 master + RP2040 slave architecture, PIO programs, USB HID Braille interface, diff-refresh algorithm | When physical build is reading at all and you need to talk to it |
| 07 | [`07_PCB_DESIGN.md`](07_PCB_DESIGN.md) | Coil-PCB stackup, planar spiral design rules, JLCPCB tier choice, manufacturing-file checklist | Before submitting any board to fab |
| 08 | [`08_TACTILE_ASSEMBLY_WALKTHROUGH.md`](08_TACTILE_ASSEMBLY_WALKTHROUGH.md) | Step-by-step blind-accessible assembly procedure for all three tiers — describes every step in tactile terms, no visual cues required | When physically assembling any tier |
| — | [`cad/`](cad/) | Parametric OpenSCAD source for every printed/cut part. One file per part, parameter block at top, scales single-cell → 40-cell → full-array via constants | Generating STL/DXF for fab orders |
| — | [`firmware/`](firmware/) | Reference firmware skeleton (later — not needed before the 40-cell build) | When wiring up the master MCU |

---

## Build-gate flow

```
   [single-cell prototype]   ──pass─▶   [40-cell line display]   ──pass─▶   [full 3,840-pin array]
        ~$110, 1–2 weekends         ~$340, 2–3 weekends                ~$3,200, 5–6 weeks
                │                          │                                  │
              fail                       fail                                fail
                ▼                          ▼                                  ▼
        iterate the cell            iterate the line                iterate the bank or layer
   (do NOT order PCB)            (do NOT order full PCB)
```

**Each gate has explicit pass/fail criteria.** They are duplicated in each build doc and consolidated in [`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md) §4.

---

## What is different about this design

In one sentence: **every other DIY braille display either gives up addressability (cam/scanning) or gives up scale (single-cell research demos). This design keeps both by moving the actuator into the PCB substrate.**

Concretely, the contributions over published prior art:

1. **Single-substrate coil array.** Every other bistable-magnetic actuator in the literature uses discrete wound coils mounted to a structural plate. We put 3,840 planar spiral coils on one 4-layer FR-4 board. This solves alignment (the PCB *is* the alignment fixture), drops cost, and makes the magnetic geometry repeatable to PCB-fab tolerance (±0.05 mm) instead of hand-build tolerance (±0.3 mm).

2. **Banked parallel addressing with off-the-shelf parts.** Driving 3,840 individual channels would cost ~$1,150 in driver silicon. We use 24 banks × 160 coils each, sharing one DRV8847 H-bridge per bank with row/column matrix select inside the bank. Driver cost drops to ~$50. All parts are stocked at Digi-Key with no NDA. **Dot Inc's commercial actuator uses ASIC drivers; ours uses jelly-bean parts.**

3. **Maker-buildable end-to-end.** No custom magnets (N42 1×1 mm discs are AliExpress commodity), no custom piezo (we don't use piezo), no custom silicon (DRV8847, 74HC4067, RP2040 are all stocked), no custom PCB process (4-layer, 4 mil trace/space, ENIG — JLCPCB standard tier). The hardest custom part is the soft-iron pole washers, and even those are SendCutSend laser-cut.

The full comparison vs. Monarch is in [`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md), including a candid red-team analysis of where this design might fail and why Monarch chose the tradeoffs it did.

---

## Cost summary

| Tier | Pins | BoM (parts) | Tooling delta | Time |
|---|---|---|---|---|
| Single cell | 8 | ~$110 | $0 (assumes you own a printer + iron) | 1–2 weekends |
| 40-cell line | 320 | ~$340 | ~$50 (force gauge, calipers if not owned) | 2–3 weekends |
| Full array | 3,840 | ~$2,800 | ~$300 (annealing oven, stencil, scope if not owned) | 5–6 weeks |
| **Total V1 path** | | **~$3,250** | **~$350** | **~10–12 weeks** |

Reference retail prices for context:
- APH Monarch (2024): ~$17,000
- Dot Pad (2022): ~$8,000–$8,500
- Orbit Reader 40 (single-line, 40 cells, no graphics): ~$2,500
- BLITAB (defunct, 2020): ~$2,500 advertised, never shipped

This design's full-array tier is roughly equivalent capability to the Monarch at **18% of retail** in parts cost. It is **not** equivalent in finish, durability, certification, or warranty, and that gap is what you are trading off — see the red-team in [`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md).

---

## Physical envelope

When complete and sitting on a table, the full-array display measures:

- **Active tactile surface:** 150 mm × 160 mm (~5.9 × 6.3 in)
- **External footprint:** ~180 mm × 190 mm (with enclosure border)
- **Height above table:** ~22 mm (~0.87 in) — see stack-up in [`05_FULL_ARRAY_BUILD.md`](05_FULL_ARRAY_BUILD.md) §6
- **Mass:** ~800 g (~1.8 lb)
- **Connectors:** USB-C (host), 5V/4A barrel jack (power)

The Monarch is ~30 mm thick and ~2.4 kg by comparison. We come out thinner and lighter primarily because we omit the battery, screen, speakers, and onboard CPU — this is a tactile *display*, not a standalone device.

---

## Patent and IP

This design treads on territory covered by Dot Inc and EPFL patents. **Personal research and bench prototypes are fine.** Commercial production is not — see [`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md) §6 for the FTO discussion. If commercial intent emerges, retain a patent attorney before any sales activity.

---

## Open questions before committing to V1

These are the unknowns that bench measurement on the single-cell prototype must close. Each is also tracked in the relevant build doc:

1. Exact pole-washer thickness for ≥150 g detent at 2.5 mm pitch with N42 1×1 mm magnets. ([`01_ARCHITECTURE.md`](01_ARCHITECTURE.md) §3.3)
2. Whether pot-magnet shielding is required, or if checkerboard polarity + mu-metal alone suffices. ([`02_COMPARISON_AND_REDTEAM.md`](02_COMPARISON_AND_REDTEAM.md) §3)
3. PCB coil turn count and layer count to deliver target field strength. ([`07_PCB_DESIGN.md`](07_PCB_DESIGN.md) §2)
4. Whether 0.10 mm trace/space at JLCPCB standard tier is reliable enough for 3,840-coil yield. ([`07_PCB_DESIGN.md`](07_PCB_DESIGN.md) §3)
5. Heat dissipation under sustained refresh. ([`01_ARCHITECTURE.md`](01_ARCHITECTURE.md) §5)

The single-cell prototype answers 1, 2, 3, and partly 5. The 40-cell prototype answers 4 and the rest of 5.

**Do not order full-array parts on an unvalidated single-cell mechanism.** This is the single most important rule in this document set.
