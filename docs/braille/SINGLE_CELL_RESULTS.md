# Single-cell results template

Fill this in as the single-cell prototype is built and validated. The values here become **locked design parameters** for the 40-cell and full-array builds.

---

## Build dates

- Parts ordered: __________
- Parts received: __________
- Assembly start: __________
- Assembly complete: __________
- Validation complete: __________

---

## Pole-washer thickness selection (G1)

Tested at 0.5 / 1.0 / 1.5 mm. Force-gauge measurements (8 pins per setting):

| Thickness | Pin 1 | Pin 2 | Pin 3 | Pin 4 | Pin 5 | Pin 6 | Pin 7 | Pin 8 | Mean | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 0.5 mm | | | | | | | | | | |
| 1.0 mm | | | | | | | | | | |
| 1.5 mm | | | | | | | | | | |

**LOCKED:** _____ mm pole-washer thickness.

## Magnet count and grade

- N42 single magnet: works / insufficient / __ g hold
- N42 stacked dual: works / __ g hold
- N50 single: works / __ g hold

**LOCKED:** ____ × N___ magnet, ____ mm thickness.

## Coil layer count (G2)

- 4-layer PCB, no ferrite: ___% flip success
- 4-layer PCB, with ferrite layer: ___% flip success
- 6-layer PCB, no ferrite: ___% flip success
- 6-layer PCB, with ferrite layer: ___% flip success

**LOCKED:** ___-layer coil PCB, with/without ferrite layer.

## Pulse parameters (G3)

- Drive current peak: ____ A
- Pulse duration: ____ ms
- Energy per flip: ____ mJ
- H-bridge supply: ____ V

**LOCKED.**

## Crosstalk mitigation level (G4)

- Checkerboard polarity alone: ___% neighbor disturbance
- + pot-magnet shields: ___% disturbance
- + mu-metal between rows: ___% disturbance

**LOCKED:** crosstalk mitigations adopted: __________________

## Tactile feel (G5)

- Reader name: __________
- Reader's experience level (years reading braille): ____
- Feedback: __________
- Pass / partial pass / fail

**Notes:** __________

---

## Carry-forward to 40-cell build

The following must be the same in 40-cell and full-array builds:

1. Pole-washer thickness: ____ mm
2. Magnet spec: ____
3. Coil PCB: ___ layers, with/without ferrite
4. Pulse: ____ A peak, ____ ms duration
5. Crosstalk mitigations: ____

If any single-cell parameter changed during validation (e.g., went from 1 mm to 1.5 mm pole washers), update CAD `pole_plate.scad` and re-render before ordering 40-cell parts.

## Lessons / surprises

(Free-form notes on what went unexpectedly. Things future-self will want to know.)

---

**Sign-off:** All five gates passed: yes / no.
**Decision:** Proceed to 40-cell / iterate single-cell V2 / abandon.
