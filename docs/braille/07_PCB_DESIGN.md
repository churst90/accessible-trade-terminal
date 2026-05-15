# 07 — PCB design notes

This document is the deep-dive on coil PCB layout and manufacturing. It complements the build manuals' summary specs.

---

## 1. Coil PCB requirements

### 1.1 Functional requirements

For each pin in the array:
1. Generate a vertical magnetic field at the pin's magnet sufficient to break the detent and drive the magnet to the opposite pole.
2. Reverse polarity on command (driven from H-bridge).
3. Stay below thermal limits at 1% duty cycle continuous operation.
4. Register with the cell-housing plate to ±0.05 mm so the magnet sits centered above the coil.

### 1.2 Derived electrical parameters (from [`01_ARCHITECTURE.md`](01_ARCHITECTURE.md))

- Coil resistance: 1–3 Ω
- Drive current: 1.0–1.5 A peak
- Pulse duration: 5–10 ms
- Per-pulse energy: 25–60 mJ
- Field at magnet position: ≥30 mT (with ferrite-layer flux concentration)

---

## 2. Planar spiral coil design

### 2.1 Geometry

```
   Outer terminus (pad)
     ●─────────────────╮
        ╭─────────────╮│
        │  ╭─────────╮│ │
        │  │  ╭───╮  ││ │
        │  │  ●Vias││ │   ← Inner terminus, stitched to lower layer
        │  │  ╰───╯  ││ │
        │  ╰─────────╯│ │
        ╰─────────────╯ │
                        │
                        ▼
                   To H-bridge OUT pad
```

### 2.2 Trace dimensions

| Parameter | Value | Reason |
|---|---|---|
| Outer diameter | 2.0 mm | Largest coil that fits at 2.5 mm pitch with 0.5 mm clearance to neighbor |
| Inner diameter | 0.4 mm | Smallest hole to fit a stitching via |
| Trace width | 0.10 mm (4 mil) | JLCPCB standard tier minimum |
| Trace spacing | 0.10 mm (4 mil) | JLCPCB standard tier minimum |
| Turn pitch (trace + space) | 0.20 mm | Trace width + spacing |
| Turns per layer | (1.0 - 0.2)/0.2 = ~10 | Mean radius range / pitch |
| Layers used | 4 (V1) or 6 (V2) | Field strength requirement |
| Total turns (V1) | 40 | 4 layers × 10 turns |

### 2.3 Field calculation (recap from architecture doc)

For a planar spiral coil, the on-axis field at distance z above the coil:

$$B_z = \frac{\mu_0 N I}{2} \cdot \frac{r_{mean}^2}{(r_{mean}^2 + z^2)^{3/2}}$$

For our V1 coil:
- N = 40, I = 1.0 A, r_mean = 0.6 mm, z = 1.0 mm (PCB top to magnet center)

$$B_z = \frac{4\pi \times 10^{-7} \times 40 \times 1.0}{2} \times \frac{(0.6 \times 10^{-3})^2}{((0.6 \times 10^{-3})^2 + (1.0 \times 10^{-3})^2)^{3/2}}$$

= 2.51e-5 × (3.6e-7 / 1.49e-9) = 2.51e-5 × 241 = **6.05 mT**

This is the analytical value. With ferrite-layer multiplication (µᵣ ≈ 100 for a thin layer), the realized field at the magnet rises to **~30 mT** — sufficient by analytical model to flip a magnet against the detent (modeled in arch doc).

### 2.4 Coil resistance

Trace cross-section: 0.10 × 0.035 mm = 3.5e-9 m²
Resistivity copper @ 25°C: 1.72e-8 Ω·m
Resistance per meter: 1.72e-8 / 3.5e-9 = **4.91 Ω/m**

Total trace length per coil:
- Mean turn circumference: 2π × 0.6 = 3.77 mm
- 40 turns: 40 × 3.77 = 150 mm = 0.15 m

Coil resistance: 4.91 × 0.15 = **0.74 Ω.**

Plus via stitching (~0.05 Ω) and connection trace to bank header (~0.5 Ω at 100 mm length): **~1.3 Ω total coil resistance.**

At 5 V supply, with H-bridge FET drop ~0.4 V and current sense ~0.05 V:

$$I = (5.0 - 0.4 - 0.05) / 1.3 = 3.5 \text{ A}$$

That's well above the 1.5 A target, meaning we're current-limited by the H-bridge (DRV8847: 1 A continuous, 1.8 A peak), not by coil resistance. 

For 6-layer (60 turns): coil resistance ~2 Ω, current ~2.3 A — still H-bridge limited. Either way, the H-bridge sets the operating point.

### 2.5 Inductance

L of a planar spiral (Wheeler approximation):
$$L \approx \frac{N^2 \mu_0 r_{mean}}{2}$$

For N=40, r_mean=0.6 mm:
$$L \approx 40^2 \times 4\pi \times 10^{-7} \times 6 \times 10^{-4} / 2 = 6.0 \times 10^{-7} \text{ H} = 0.6 \text{ µH}$$

Time constant L/R ≈ 0.6 µH / 1.3 Ω = 460 ns. Pulse rise time is much shorter than 5 ms pulse width; current rises to steady-state in ~1 µs.

---

## 3. PCB stackup

### 3.1 V1 (4-layer)

```
   Top:    1 oz Cu  ───── coil L1, ground pour, ferrite-layer bond surface
   ──── 0.36 mm prepreg ────
   In1:    0.5 oz Cu ─── coil L2
   ──── 0.71 mm core ────
   In2:    0.5 oz Cu ─── coil L3
   ──── 0.36 mm prepreg ────
   Bot:    1 oz Cu  ───── coil L4, escape routing to bank connectors

   Total thickness: 1.6 mm ± 10%
```

### 3.2 V2 (6-layer)

Similar but with 6 coil layers and dedicated power/ground planes:

```
   L1:     1 oz Cu  ─── coil layer 1 + GND pour
   L2:     0.5 oz Cu ─── coil layer 2
   L3:     0.5 oz Cu ─── PWR plane
   L4:     0.5 oz Cu ─── GND plane
   L5:     0.5 oz Cu ─── coil layer 3
   L6:     1 oz Cu  ─── coil layer 4 + escape routing
```

V2 only used if single-cell measurements show 4-layer field is insufficient. Adds ~$25/board.

### 3.3 Via stitching

Connect coil layers in series (so currents add):

- Center via: 0.3 mm drill, 0.5 mm pad, drilled through L1 → L4. Connects all four coil layers at center.
- Outer stitching: 0.3 mm drill at outer terminus of each layer; alternating connection pattern so coils chain L1→L2→L3→L4 in series.

Total vias per coil: 5 (1 center + 4 outer). For 3,840 coils: 19,200 vias. Standard 4/4 mil tier supports up to ~25,000 vias on a 150×160 board.

#### 3.3a PCB mounting holes (must match enclosure standoffs)

**Coil PCB:** 4 × M3 clearance holes (3.4 mm Ø), one at each corner, **3 mm inset from each PCB edge.** Mounting holes must be in non-coil zones — the active coil region has 13–16 mm of border on each side, plenty of clearance.

**Driver PCB:** 4 × M3 clearance holes (3.4 mm Ø), one at each corner, **5 mm inset from each PCB edge.** No active circuitry within 3 mm of holes.

These match the standoff positions in `cad/enclosure.scad`:
- Coil-PCB standoffs at `(3, 3)`, `(TP_W - 3, 3)`, `(3, TP_H - 3)`, `(TP_W - 3, TP_H - 3)` in PCB-local coordinates
- Driver-PCB standoffs at `(5, 5)`, `(DPCB_W - 5, 5)`, `(5, DPCB_H - 5)`, `(DPCB_W - 5, DPCB_H - 5)`

If you change the enclosure `DRIVER_PCB_HOLE_INSET` or coil-PCB inset constants, **you must regenerate the PCB layout to match,** or the boards won't bolt down.

## 3.4 Surface finish

**ENIG mandatory.** HASL leaves uneven copper dome that breaks 4 mil trace alignment. ENIG (Electroless Nickel Immersion Gold) deposits a flat 3–6 µm layer over copper; preserves trace dimensions to ±2 µm.

Cost premium ENIG vs HASL: $30–50 per 5-board batch. Negligible.

### 3.5 Solder mask

Black matte. Two reasons:
1. Cosmetic: contrasts well with aluminum top plate.
2. Functional: black absorbs heat better than green; helps thermal radiation from the PCB.

---

## 4. Bank connector layout

24 banks. Each bank header on the PCB edge, distributed evenly:
- Banks 0–5: top edge (160 mm side)
- Banks 6–11: right edge
- Banks 12–17: bottom edge
- Banks 18–23: left edge

Each header: 16 pins (10 for column SEL, 4 for row SEL, 2 for H-bridge polarity).

Header spec: 0.1" pitch right-angle female, accepts ribbon-cable IDC connector.

---

## 5. Manufacturing-files checklist

Before submitting to JLCPCB:

### 5.1 KiCad project structure

```
docs/braille/cad/full-array/coil_pcb_full/
├── coil_pcb.kicad_pro
├── coil_pcb.kicad_sch
├── coil_pcb.kicad_pcb
├── lib/
│   └── braille_coil.kicad_sym  (symbol for parametric coil)
├── footprints/
│   └── PlanarSpiral2mm.kicad_mod
└── output/
    ├── gerbers/  (regenerated via "Plot Gerbers")
    ├── drill/    (regenerated via "Generate Drill Files")
    └── BOM.csv
```

### 5.2 Design rule check

JLCPCB's design rules (`JLC04161H` profile loaded from KiCad's "Setup":

- Min trace width: 0.10 mm
- Min trace clearance: 0.10 mm
- Min via diameter: 0.5 mm (with 0.3 mm drill)
- Min hole size: 0.3 mm
- Min hole-to-hole: 0.20 mm
- Min annular ring: 0.13 mm
- Edge clearance: 0.20 mm
- Solder-mask tolerance: 0.10 mm

DRC must report 0 errors before output.

### 5.3 Generate output files

KiCad menu: **File → Plot...**

- Format: Gerber
- Layers: F.Cu, F.Mask, F.SilkS, In1.Cu, In2.Cu, B.Cu, B.Mask, B.SilkS, Edge.Cuts
- Use Protel filename extensions: enabled (JLCPCB convention)
- Use auxiliary axis as origin: enabled

Then **File → Generate Drill Files**:
- Format: Excellon
- Drill units: mm
- Drill origin: auxiliary axis
- Generate map: yes (for verification)
- Plated/non-plated separate files: yes

### 5.4 Package for submission

```
zip coil_pcb_full.zip output/gerbers/*.gbr output/drill/*.drl
```

Upload to jlcpcb.com → Quick Order PCB → drag and drop ZIP.

### 5.5 Order parameters

| Parameter | Value |
|---|---|
| Layers | 4 |
| Material | FR-4 (Tg 130°C, standard) |
| Thickness | 1.6 mm |
| Outer copper | 1 oz |
| Inner copper | 0.5 oz |
| Solder mask | Black matte |
| Silkscreen | White |
| Surface finish | ENIG (RoHS) |
| Min trace/space | 4/4 mil |
| Min hole | 0.3 mm |
| Tolerance class | Standard |
| Test | Flying probe (free at JLCPCB; mandatory for catching broken traces) |
| Quantity | 5 |
| Lead time | Standard 7 days |
| Shipping | DHL Express (US delivery 3 days) |

Cost estimate at JLCPCB calculator (April 2026): **$135 per board × 5 = $675 plus shipping ~$50.**

---

## 6. Driver PCB design

Simpler 2- or 4-layer board, 100 × 160 mm, carries:
- 24× DRV8847 (each with bypass cap + thermal relief)
- 48× 74HC4067
- 6× RP2040 + 1× RP2350
- 24× TPS56C230 buck converter (one per bank)
- Power input: barrel jack + USB-C PD
- USB-C connector for host
- 24× 16-pin headers for ribbon cables to coil PCB

### 6.1 Trace and process

- 6/6 mil trace/space (standard tier)
- 1.6 mm FR-4
- ENIG (consistency with coil PCB)
- 4-layer with internal power+ground planes for current capacity

### 6.2 Cost

5 boards at JLCPCB standard 4-layer: ~$30 each = $150.

---

## 7. PCB QC after receipt

Before assembly:

1. **Visual inspection.** Use 5× magnifier. Look for:
   - Broken traces in the coil region (especially fine 4 mil)
   - Solder mask defects exposing copper
   - Drill marks (non-plated mounting holes)
   - Edge damage from depanelization

2. **Continuity test.** Use multimeter on continuity beep mode:
   - Probe random coil terminals; expect ~1.3 Ω with comparable readings.
   - Test 50 coils per board, 5 boards. ~250 tests.
   - Time: ~30 minutes per board.

3. **Reject if:**
   - >5 dead coils on one board → use as last spare
   - >10 dead coils → reject and request replacement from JLCPCB
   - Catastrophic defect (drill in wrong place, layer misalignment) → reject

Of 5 boards ordered, expect 1–2 to have minor defects. The "best" 1–2 are used for the V1 build; remainder stay as spares.

---

## 8. PCB risks specific to this design

### 8.1 Inner-layer alignment

Multilayer PCBs have inner-layer registration tolerance of ±0.05 mm at JLCPCB. Our coil layers register to outer pads; if registration shifts, the coil center may not align with the magnet. Mitigation: align entire coil array to the corner dowel-hole reference, not to outer-layer features.

### 8.2 Thermal breakdown

If a single coil is held continuously energized (firmware bug), 1.5 A × 1.3 Ω = 3 W in a 4 mm² copper area. Local temperature rise > 200°C in seconds; FR-4 chars at 250°C; copper trace lifts off. **Hardware mitigations:** PIO watchdog (firmware), series fuse on each bank's 5V rail (2A fast-blow), per-bank thermal sensor (TMP235) with auto-shutdown.

### 8.3 EMI from coil pulses

Pulsed 1.5 A through a 1.6 mm-thick coil produces measurable RF emission in the kHz–MHz range. Likely under FCC unintentional-radiator limits but worth measuring before any commercial path.

Mitigation: ground pours on all unused board area; common-mode chokes on bank power feeds; EMI shielding can in V2 if needed.

### 8.4 Magnet-induced trace degradation

The cumulative magnetic field in the coil layer over millions of cycles can theoretically magnetize the copper traces. Copper is diamagnetic (very weakly repelled by fields); permanent magnetization is not expected in non-ferromagnetic copper. Verify with long-soak test before declaring this a non-issue.

---

## 9. V2 considerations

If V1 4-layer 4 mil PCB shows yield issues:

- **PCBWay tier:** 0.075 mm trace/space available. ~$200/board for 5. Better yield, +$325 over V1.
- **6-layer:** more turns per coil, lower current required. ~$200/board for 5. +$325.
- **Module-per-bank:** split 24 banks into 24 separate small boards (50 × 50 mm each), each with own driver. ~$15/board × 24 = $360 vs $675. Cheaper but requires ribbon-cabling 24 boards together.

V2 design sprint after V1 build complete and validated.

---

## 10. Summary

PCB design is conservative on field strength (within analytical margin if ferrite layer works as expected) and aggressive on density (4 mil trace at 3,840 coil count). Yield risk is the single biggest concern; mitigated by ordering 5 boards and screening.

JLCPCB at 4/4 mil tier with ENIG is the right manufacturing choice. PCBWay is the upgrade path if yield issues emerge. Both are commodity-priced; no NDA, no specialty fab required.
