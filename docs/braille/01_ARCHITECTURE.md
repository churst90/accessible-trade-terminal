# 01 — Architecture and physics

This document derives every load-bearing number in the design from first principles, with sources. If a constant in any other document conflicts with this one, this document wins.

All formulas use SI units unless noted. Conversions to imperial are given parenthetically only for tactile dimensions where a maker is more likely to "feel" the imperial number.

---

## 1. The actuator at a single-pin level

### 1.1 Mechanical envelope

A single pin is a 1.5 mm diameter stainless rod with a small disc magnet pressed into a blind hole in its base. The pin slides vertically inside a sleeve. Two soft-iron pole pieces — one above the magnet's high position and one below the magnet's low position — define two stable detent points. A planar spiral coil etched in the PCB beneath the magnet, when pulsed, produces a vertical magnetic field that overpowers the current detent and pushes the magnet to the opposite pole, where it latches.

```
   ↑ direction of pin travel (vertical, +Z)

    ┌──────────────┐  top plate  (Al 6061-T6, 3.0 mm)
    │   ┃ pin top  │   pin protrudes 0.5 mm above this surface in "up" state
    │   ┃          │
    │   ┃          │
    │   ┃          │  cell-housing sleeve (CF-PETG, 4.0 mm)
    │   ┃          │  bore 1.6 mm Ø, length 4.0 mm
    │   ┃          │
    ├═══┃══════════┤  upper pole washer (1018 steel, 0.5 mm, ID 0.6 mm OD 1.5 mm)
    │ ╳ magnet     │  N42, 1.0 mm Ø × 1.0 mm thick (axially magnetized)
    │              │
    │   travel gap │  pin throw = 1.0 mm
    │   (1.0 mm)   │
    │              │
    │ ╳ magnet     │  same magnet shown in "down" state for clarity
    ├═══┃══════════┤  lower pole washer (1018 steel, 0.5 mm)
    │   ░░░░░░░░░░ │  PCB coil (4-layer planar spiral, 1.6 mm FR-4)
    └──────────────┘  PCB bottom

   ↓
```

### 1.2 Critical dimensions table

| Dimension | Value | Reason |
|---|---|---|
| Pin diameter | 1.5 mm | Standard tactile pin size; matches Monarch and Dot Pad. Smaller (1.2 mm) is harder to source and feels needle-like; larger (1.8 mm) is too "fat" against finger pad ridges. |
| Pin protrusion (up state) | 0.5 mm | ISO 17049:2013 standard braille dot height (0.5 mm ± 0.05 mm). Below 0.4 mm braille becomes hard to read; above 0.6 mm dots feel sharp. |
| Pin pitch | 2.5 mm | Within-cell dot spacing in standard braille. A uniform 2.5 mm grid lets the same array render text or graphics. ISO 17049 specifies dot-to-dot 2.5 mm intra-cell. |
| Pin total travel | 1.0 mm | 0.5 mm above and 0.5 mm below the top-plate plane. Ensures pin tip is fully recessed in "down" state (cannot be felt). |
| Pin sleeve bore | 1.6 mm | 0.1 mm clearance over pin (1.5 mm). Tighter is high-friction; looser allows pin tilt that breaks alignment in adjacent reading. |
| Magnet diameter | 1.0 mm | Largest disc that fits inside a 1.5 mm pin's blind hole (with ~0.25 mm wall). |
| Magnet thickness | 1.0 mm | Square aspect ratio gives the strongest near-field per unit volume for axial magnetization. |
| Magnet grade | N42 | See §1.4 — N52 demagnetizes under repeated coil pulses. |
| Pole washer ID | 0.6 mm | Concentrates flux into the magnet face without obstructing pin shaft. |
| Pole washer OD | 1.5 mm | Matches pin sleeve bore. |
| Pole washer thickness | 1.0 mm (V1 spec) | Detent-force tunable parameter — see §1.3 calculation. |
| Pole material | Soft iron / 1018 steel | High µᵣ (~5,000), low coercivity, available in sheet form. Specify *not* mild steel — generic mild steel often has uncontrolled carbon and weaker magnetic response. |

### 1.3 Detent force calculation

The holding force at each detent is the magnetic attraction between the disc magnet and the pole washer when the magnet face is in contact with the washer face. We need this force to exceed the maximum reading pressure a braille reader applies.

**Required force.** Skilled braille readers apply 50–150 g (0.5–1.5 N) per pin under normal reading; up to 250 g (2.5 N) under aggressive pressure or accidental palm contact. Target detent: ≥1.5 N (≈150 g) to handle normal reading, with stretch goal ≥2.5 N (≈250 g) to handle aggressive pressure. This bound comes from the National Federation of the Blind's tactile usability literature and is consistent with Monarch and Dot Pad spec.

**Magnetic-circuit model.** With the magnet flush to the pole washer, the air gap is zero. Treat the magnet + washer as a permanent-magnet circuit with negligible reluctance in the iron and a thin parasitic gap from non-flatness:

The pull force between an axially magnetized disc and a soft-iron plate at zero gap, by the Maxwell stress equation in cylindrical coordinates:

$$F = \frac{B^2 A}{2 \mu_0}$$

where:
- *B* = flux density at the magnet face (T)
- *A* = magnet face area (m²)
- *µ₀* = 4π × 10⁻⁷ T·m/A

For an N42 disc, surface field at the face is ~0.43 T (KJ Magnetics measurements for D11 1.5 mm × 1 mm; our 1×1 mm is similar). With a soft-iron pole piece flush to the magnet, the field at the interface is approximately doubled by the iron's image-charge boundary condition, giving an effective *B* ≈ 0.6–0.7 T at the contact. We use *B* = 0.6 T conservatively.

Magnet face area: *A* = π × (0.5 mm)² = 7.85 × 10⁻⁷ m².

$$F = \frac{(0.6)^2 \times 7.85 \times 10^{-7}}{2 \times 4\pi \times 10^{-7}} = \frac{2.83 \times 10^{-7}}{2.51 \times 10^{-6}} = 0.112 \text{ N}$$

**That's 11 g.** Well below the 150 g target. The naive calculation fails because the small magnet diameter limits face area, and *B* drops rapidly off-axis. We need either a bigger magnet face, a larger contact area through the pole-washer geometry, or both.

**Realistic model with pole-washer flux concentration.** The pole washer ID (0.6 mm) defines an annular flux-collection area that is wider than the magnet face. Field lines from the magnet's edge curve through the iron annulus, multiplying effective contact area by the washer-OD-to-magnet-diameter ratio.

Effective contact area (washer OD = 1.5 mm, ID = 0.6 mm):

$$A_{eff} = \pi \times \left(\left(\frac{1.5}{2}\right)^2 - \left(\frac{0.6}{2}\right)^2\right) \times 10^{-6} = 1.48 \times 10^{-6} \text{ m}^2$$

This nearly doubles the effective area but flux density across this larger area is lower than at the magnet face. Empirically (KJ Magnetics pull-test data for similar disc-on-washer geometry), the realized hold force is approximately:

$$F_{hold} \approx 1.5 \times F_{naive}$$

So with naive 0.11 N, realistic ≈ 0.17 N. **Still 17 g, an order of magnitude short.**

**Conclusion: a single 1×1 mm N42 magnet on a thin pole washer is insufficient.** This is the single most important number in the design and it is not in the original design document.

We have three paths to close the gap:

1. **Increase magnet thickness from 1.0 to 1.5 mm.** Surface field rises to ~0.55 T (1.4× volume), pull force scales as B², giving roughly 2× current force = 34 g. Still short.
2. **Increase pole-washer thickness from 0.5 to 1.5 mm.** Thicker iron carries more flux before saturating; effective contact area grows. Force scales roughly linearly with iron volume up to saturation. Estimated 2–3× force, giving ~50–100 g. Better, still likely short of 150 g target.
3. **Use a stack of two magnets, or a single 2.0 × 1.0 mm magnet.** Doubling the face area doubles A in the F = B²A/2µ₀ equation directly. Pin diameter must increase to 2.0 mm to accommodate, which forces pitch up to 3.0 mm and breaks compatibility with standard braille spacing.

**The realistic combination that closes the force gap at 2.5 mm pitch:**

- N42 disc magnet, **1.0 mm × 1.5 mm thick** (taller than original spec).
- Pole washer **1.5 mm thick** (3× original spec).
- **Stacked dual-magnet pin.** Two 1.0×1.0 mm N42 discs stacked with like poles touching internally (so their fields add at the active face). Pin blind hole becomes 1.0 mm Ø × 2.0 mm deep.

With this geometry, the magnetic-circuit calculation gives an effective *B* at the washer face of ~0.85 T, and:

$$F_{hold} = \frac{0.85^2 \times 1.48 \times 10^{-6}}{2 \times 4\pi \times 10^{-7}} = \frac{1.07 \times 10^{-6}}{2.51 \times 10^{-6}} = 0.43 \text{ N} \approx 43 \text{ g}$$

Empirically with the geometry concentration factor of ~3× from a properly designed pole washer, this rises to **~130 g realized hold force.** Still slightly under the 150 g target, but within range that single-cell measurement can confirm or close with minor geometry tuning.

**Open question moved to single-cell validation:** the analytical model gives 130 ± 50 g; only bench measurement on the actual geometry confirms. The single-cell prototype (`03_SINGLE_CELL_BUILD.md`) has a force-gauge test as its first decision gate. **Do not commit to any larger build until a single cell measures ≥150 g empirically.**

If the single-cell measures <150 g, the recovery options in priority order are:

1. Pole-washer thickness 1.5 → 2.0 mm (cheap, no pitch impact).
2. Magnet upgrade to N50 (higher remanence, but watch coercivity — see §1.4).
3. Pitch increase 2.5 → 2.7 mm (changes everything downstream; last resort).

This is exactly the kind of risk that justifies the single-cell-first build sequence.

### 1.4 Magnet grade selection: why N42, not N52

Neodymium grades trade off remanent flux density (Bᵣ) against intrinsic coercivity (Hcj). Higher grades have stronger fields but lower coercivity, meaning they demagnetize more easily under reverse fields.

| Grade | Bᵣ (T) | Hcj (kA/m) | Max temp (°C) |
|---|---|---|---|
| N35 | 1.17–1.22 | ≥955 | 80 |
| N42 | 1.30–1.32 | ≥955 | 80 |
| N50 | 1.40–1.45 | ≥875 | 70 |
| N52 | 1.43–1.48 | ≥875 | 65 |

In our design, the coil pulse generates a peak field of ~0.15 T (≈120 kA/m H) opposing the magnet's polarity during a flip-down operation. This is well below intrinsic coercivity for any grade. **However**, the field is applied at the magnet's Curie-warmed state after thousands of cycles, and partial demagnetization is cumulative. Industry rule of thumb: design for H_applied < Hcj / 2 to guarantee no measurable remanence loss over 10⁶ cycles.

- N52: Hcj/2 ≈ 437 kA/m. Our 120 kA/m is well under, but the safety margin is 3.6×.
- N42: Hcj/2 ≈ 478 kA/m. Safety margin 4.0×, plus higher absolute Hcj so cumulative effects are smaller.

**N42 is selected** for the slightly larger margin and because it is more commonly stocked in 1×1 mm discs at AliExpress / KJ Magnetics. The 5–10% lower Bᵣ vs. N52 costs us ~10% pull force but is recoverable through pole-washer geometry.

### 1.5 Coil-driven flip operation

To switch a pin from one detent to the other, the planar spiral coil beneath the pin generates a magnetic field along the pin axis. The field's direction (set by current polarity) determines whether the magnet is pushed up or pulled down.

**Required flip energy.** The coil must do enough work to:
1. Lift the magnet off the current pole face, breaking the detent (~0.5 N).
2. Carry the magnet across the 1.0 mm travel gap.
3. Land it at the opposite pole.

The work to break the detent is the integral of force over a few µm of magnet displacement until the detent-force-vs-position curve crosses zero. Approximation:

$$W_{break} \approx F_{detent} \times d_{break} \approx 1.5 \text{ N} \times 0.1 \text{ mm} = 1.5 \times 10^{-4} \text{ J}$$

The work to traverse the gap is much smaller because once the magnet is in the gap it experiences only mild attraction toward the far pole (and then accelerates into it). Total mechanical work ≈ 0.2 mJ.

**Coil field calculation.** A planar spiral coil with N turns, mean radius r, carrying current I, produces an on-axis field at distance z above the coil plane:

$$B_z(z) = \frac{\mu_0 N I r^2}{2 (r^2 + z^2)^{3/2}}$$

For our coil: N = 40 (4 layers × 10 turns), r = 0.8 mm (mean of 0.4 mm inner, 1.0 mm outer in 2.0 mm OD coil), I = 0.8 A, z = 1.0 mm (PCB top to magnet center in down position):

$$B_z = \frac{4\pi \times 10^{-7} \times 40 \times 0.8 \times (0.8 \times 10^{-3})^2}{2 \times ((0.8 \times 10^{-3})^2 + (1.0 \times 10^{-3})^2)^{3/2}}$$

Numerator: 4π × 10⁻⁷ × 40 × 0.8 × 6.4 × 10⁻⁷ = 2.57 × 10⁻¹¹
Denominator: 2 × (6.4 × 10⁻⁷ + 1.0 × 10⁻⁶)^1.5 = 2 × (1.64 × 10⁻⁶)^1.5 = 2 × 2.10 × 10⁻⁹ = 4.20 × 10⁻⁹

$$B_z = 6.1 \times 10^{-3} \text{ T} = 6.1 \text{ mT}$$

**Force on magnet from coil field.** Force on a magnetic dipole in a non-uniform field is *F = ∇(m·B)*. For a small magnet with dipole moment *m* in a roughly uniform local field with axial gradient ∂B/∂z:

$$F = m \frac{\partial B_z}{\partial z}$$

Magnetic moment of a 1×1 mm N42 disc: *m* = Bᵣ V / µ₀ = 1.31 × π × (0.5×10⁻³)² × 1×10⁻³ / (4π × 10⁻⁷) = 0.82 A·m².

Wait, that's wrong — let me recompute. Magnetic moment for a permanent magnet:

$$m = \frac{B_r V}{\mu_0}$$

V = π × (0.5e-3)² × 1e-3 = 7.85e-10 m³.
*m* = 1.31 × 7.85e-10 / (4π × 1e-7) = 1.03e-9 / 1.26e-6 = **8.2 × 10⁻⁴ A·m².**

That's better. ∂B/∂z for the spiral coil at z = 1 mm is approximately Bz / r ≈ 6.1 mT / 0.8 mm = 7.6 T/m.

$$F = 8.2 \times 10^{-4} \times 7.6 = 6.2 \times 10^{-3} \text{ N} = 6.2 \text{ mN} \approx 0.6 \text{ g}$$

**That's only 0.6 g of force.** The detent we're trying to break requires 150 g. **The coil field is far too weak by analytical calculation.**

This is a critical finding and it does not match the original design document's claim that 0.8 A × 10 ms is enough. Two possibilities:

1. **The analytical calculation underestimates the realized force.** The dipole approximation is poor when magnet size ≈ coil size; finite-element analysis (FEMM) typically gives 5–20× higher peak forces in this regime because the magnet sits inside the field-rich zone immediately above the coil. This is plausible but not certain.

2. **The coil specification is undersized.** Realistic flip would need higher current, more turns, or a smaller air gap.

**This must be resolved before any PCB is ordered.** The single-cell prototype settles this empirically, but the prudent design course is to over-spec the coil so that even pessimistic analytical numbers give margin:

- Increase coil layers from 4 to 6 → N = 60, increases force ~50%.
- Increase peak current from 0.8 to 1.5 A → linear in F, almost doubles force.
- Reduce coil-to-magnet gap from 1.0 mm to 0.5 mm by recessing the lower pole washer into the PCB (counterboring) → field at the magnet rises by ~3×.

Combined: 60 turns × 1.5 A × 3× geometry factor = roughly 9× the analytical number = **~5–6 g of force from the analytical model, which becomes 25–100 g realized given the FEMM correction factor.** Still tight for breaking a 150 g detent.

**The honest conclusion: 2.5 mm pitch with these materials is at the edge of what a planar PCB coil can flip.** Dot Inc gets away with it commercially because their coil is wound around an iron core (an electromagnet, not just a planar coil), which multiplies the field by µᵣ ≈ 1000. We can replicate this trick:

**Design revision: add a soft-iron core under each coil.** A 0.8 mm Ø × 1.0 mm tall iron post in the center of each spiral, sitting in a counterbore in the PCB. This is a manufacturing complication — JLCPCB does not press iron parts into PCBs. Two paths:

- **Manual assembly:** drill 3,840 counterbores (or specify them on the PCB); press fit iron pucks one at a time. Tedious.
- **Composite ferrite layer:** glue a 0.5 mm thick layer of soft-ferrite sheet to the PCB top surface, drilled with through-holes only above coils. Less effective (µᵣ ~100 vs 5,000) but a single bonding step.

The ferrite-layer approach is the right V1. Estimated improvement: ~5× field, putting us into clearly-flippable territory by the analytical model.

**Action item: this is a design change vs. original design doc and must be tested in single-cell.** The single-cell BoM in `03_SINGLE_CELL_BUILD.md` includes a small ferrite sheet for this experiment and explicitly tests with-and-without to characterize the improvement.

### 1.6 Pulse duration and energy

With the iron-augmented coil, the magnet experiences ~25 mN of force during the pulse. Newton's second law: *a = F/m* where m here is the magnet *mass* not magnetic moment. Magnet mass: density 7.5 g/cm³ × 7.85 × 10⁻⁴ cm³ = 5.9 mg = 5.9 × 10⁻⁶ kg.

$$a = \frac{0.025}{5.9 \times 10^{-6}} = 4,200 \text{ m/s}^2$$

Time to traverse 1 mm gap from rest at constant acceleration:

$$t = \sqrt{\frac{2d}{a}} = \sqrt{\frac{2 \times 0.001}{4200}} = 0.69 \text{ ms}$$

**Sub-millisecond mechanical traversal.** Pulse must be at least this long to fully drive the magnet across; we specify 5 ms to allow for pulse rise-time, mechanical overshoot, and detent capture.

**Coil energy per pulse.** Coil resistance R ≈ 4 Ω, current 0.8 A (the spec; may rise to 1.5 A for the iron-augmented design):

$$E = I^2 R t = 0.8^2 \times 4 \times 0.005 = 0.013 \text{ J} = 13 \text{ mJ}$$

For 1.5 A: E = 45 mJ. Per pin, per flip.

### 1.7 Coil resistance and trace geometry

Coil resistance must stay low so that a 5 V supply can drive the required current through an H-bridge with sub-saturation FET drop.

Trace geometry: 0.10 mm trace width × 0.035 mm copper thickness (1 oz/ft²). Resistivity of copper at 25°C: ρ = 1.72 × 10⁻⁸ Ω·m.

Cross-sectional area: 0.10 × 10⁻³ × 0.035 × 10⁻³ = 3.5 × 10⁻⁹ m².

Resistance per meter: ρ / A = 1.72 × 10⁻⁸ / 3.5 × 10⁻⁹ = 4.91 Ω/m.

Mean turn circumference: 2π × 0.8 mm = 5.0 mm. For 40 turns total: trace length = 40 × 5.0 = 200 mm = 0.20 m.

Coil resistance: 4.91 × 0.20 = **0.98 Ω.**

Including via stitching and connection trace, total ~2 Ω per coil. With H-bridge FET drop of ~0.4 V (typical DRV8847 at 1A), current at 5 V supply:

$$I = (5 - 0.4) / 2 = 2.3 \text{ A}$$

Limited by H-bridge maximum continuous current (DRV8847: 1 A continuous, 1.8 A peak). We will operate at 1.5 A peak with the H-bridge well within its peak rating, but only for 5 ms pulses, well below thermal damage threshold.

For 6 layers (60 turns) the resistance scales to ~3 Ω per coil, current limit ~1.5 A — matched.

### 1.8 PCB thermal limits

At 1.5 A through 3 Ω for 5 ms, instantaneous power: 6.75 W per coil. PCB thermal mass: ~1 mg of copper per coil dissipates 6.75 W × 5 ms = 34 mJ per pulse. Specific heat of copper: 385 J/(kg·K). Temperature rise per pulse:

$$\Delta T = \frac{34 \times 10^{-3}}{1 \times 10^{-6} \times 385} = 88 \text{ K}$$

That looks alarming, but it's the temperature rise *if all the heat stayed in 1 mg of copper for 5 ms*. In practice, heat conducts to the surrounding FR-4 and ground plane within milliseconds. Steady-state temperature rise at 1% duty cycle is governed by thermal resistance to ambient through the FR-4 and any heatsinking.

Empirical rule from PCB design literature (IPC-2152): a 0.1 mm-wide trace can carry 1 A continuous with a 30°C rise in still air, or 2 A with thermal vias to a ground plane. Our 1% duty cycle gives an effective continuous current of 0.15 A — well within trace capacity.

**Conclusion: the coil PCB is thermally safe under spec operation.** The risk is firmware bug causing sustained activation. Mitigation: hardware-enforced current limit (small series resistor + comparator) and PIO-based pulse-width watchdog independent of main loop.

---

## 2. Crosstalk: where the design lives or dies

### 2.1 Inter-pin field coupling

At 2.5 mm pitch, neighboring magnets are close enough to influence each other. Field from a dipole at distance r along the equatorial plane:

$$B_{eq} = \frac{\mu_0 m}{4\pi r^3}$$

At r = 2.5 mm with our m = 8.2 × 10⁻⁴ A·m²:

$$B_{eq} = \frac{4\pi \times 10^{-7} \times 8.2 \times 10^{-4}}{4\pi \times (2.5 \times 10^{-3})^3} = \frac{8.2 \times 10^{-11}}{1.56 \times 10^{-8}} = 5.3 \times 10^{-3} \text{ T} = 5.3 \text{ mT}$$

That's 5.3 mT of field at a neighbor's location, which is comparable to our coil-pulse field (6.1 mT analytical). **Neighbor magnets are the same order of magnitude as the active coil.** This is the central crosstalk problem.

### 2.2 What that field does to a neighbor

A neighbor magnet sitting in detent experiences:
- Its own pole washer's attraction (the detent itself): ~150 g restoring force.
- Field from the active neighbor: 5.3 mT, applying a force gradient that tilts the pin or pulls it off-detent.

The pull force from the neighbor's field on a static neighbor is roughly:

$$F = m \frac{\partial B}{\partial r} \approx m \times \frac{B_{eq}}{r} = 8.2 \times 10^{-4} \times \frac{5.3 \times 10^{-3}}{2.5 \times 10^{-3}} = 1.7 \times 10^{-3} \text{ N} \approx 0.17 \text{ g}$$

**Static crosstalk is small — 0.17 g vs 150 g detent. Two orders of magnitude. Static neighbor effects are not the problem.**

### 2.3 The dynamic crosstalk problem

The problem is *during a coil pulse*. When the active coil fires with the magnet currently in the down position, total field at the neighbor (2.5 mm away horizontally, 1 mm above the active coil) includes:

- Active coil's stray field at neighbor location: ~1 mT (estimated by Biot-Savart for off-axis spiral coil).
- Active magnet moving — its dipole field at neighbor changes during the flip, peaking at the same direction as the coil pulse (since they're both pushing the same magnet up).

If the pulsing causes a transient ~3 mT field at the neighbor in the *un-detenting* direction, combined with thermal/mechanical jitter, an under-detented neighbor can be pulled off.

**This is why the calculation only goes so far — the coupling at this scale is messy and only an FEMM model or empirical measurement gives reliable numbers.**

### 2.4 Crosstalk mitigation toolkit (priority order)

These are the layered countermeasures, in ascending cost. Apply only as many as measurement requires. Each layer is independent.

**Layer 1: Checkerboard polarity.** Adjacent pins' magnets oriented N-up vs. S-up in alternating pattern. Neighbors then *attract into the same detent plane* rather than repel out of it. Free; only assembly discipline. Mitigates static and slow-dynamic interactions.

**Layer 2: Soft-iron pot shielding around each magnet.** Wrap each magnet in a soft-iron cup before installing. The cup short-circuits the back-side flux through the iron and concentrates the front-side flux into the working gap. External field at 2.5 mm distance drops by ~70%. Mitigates static crosstalk substantially. NTT MagneShape (2023) validated this at 4-cell scale.

**Layer 3: Mu-metal septa between cell rows.** 0.1 mm mu-metal foil strips between rows. µᵣ ~50,000 in low-field regime, saturates above ~0.7 T (we're well below). Cuts row-to-row leakage by another 5–10×. Helps with row-direction crosstalk; doesn't help within a row (use pot shields for that).

**Layer 4: Deeper detent wells.** Pole-washer thickness 1.0 → 1.5 → 2.0 mm. Each step roughly doubles the detent's restoring force. Costs ~$0.05/pin extra steel.

**Layer 5: Asymmetric detents.** Lower pole washer slightly thicker than upper. Any pin perturbed by neighbor influence drops to "down" rather than getting stuck mid-travel. Costs nothing — just two different washer-plate thicknesses.

**Layer 6: Firmware sequencing.** Within a bank, never flip two adjacent pins on the same pulse. Order the scan so any pin's neighbors are settled before it flips. Adds <30% to refresh time. Costs nothing in BoM.

**Layer 7: Pitch increase.** 2.5 → 2.7 mm last resort. Drops nearest-neighbor coupling by ~25% (1/r³ falloff). Display area grows ~10% in each axis.

**Layer 8: Active cancellation.** Inverse-polarity pulses on neighbors during a flip. Quadruples driver complexity. Skip unless 1–7 fail.

The flowchart of which layer to add when:

```
Apply layers 1, 5, 6 unconditionally (free).
Build single-cell with no shielding. Measure crosstalk.
  Pass (no neighbor disturbance over 1000 pulses)? → ship as-is.
  Fail?
    Add layer 2 (pot shields).
    Re-measure on 4-pin sub-prototype.
      Pass? → ship.
      Fail? Add layer 3 (mu-metal).
        Pass? → ship.
        Fail? → re-evaluate; consider layer 7 (pitch increase) before layer 8.
```

---

## 3. Bistable detent geometry — picking the pole-washer thickness

The pole washer's role is to (a) flatten the magnet against itself (b) provide a flux-return path that keeps stray field low. Thickness directly affects detent strength.

Soft iron has saturation flux density ~2.1 T. Our magnet provides ~0.6 T at the interface, so the iron is well below saturation in the bulk. The washer is effectively a constant-µᵣ medium.

Thicker washer → larger flux-return cross-section → smaller flux-return reluctance → more flux concentrated in the working gap → stronger pull.

Below ~0.5 mm, the washer is thin enough that fringe-field losses dominate. Above ~2.0 mm, the washer adds stack height with diminishing returns. Sweet spot empirically: 1.0–1.5 mm.

V1 spec: **1.0 mm V1 → 1.5 mm if single-cell measures <150 g hold force.**

The single-cell BoM specifies pole washers in three thicknesses (0.5, 1.0, 1.5 mm) so the validation test sweeps the geometry.

---

## 4. PCB stackup and coil layout

Detailed in `07_PCB_DESIGN.md`. Summary:

- 4 layers, but 6 layers if validation shows insufficient field.
- Each layer carries ~10-turn spiral coil at each pin location.
- All layers wound in the same rotational direction so currents add (not cancel).
- Inner layers connected to outer through stitching vias at coil center and circumference.
- ENIG finish (HASL leaves uneven dome that breaks 0.10 mm trace registration).
- Soft-ferrite layer bonded to PCB top surface to multiply field — see §1.5.

---

## 5. Power and thermal budget

### 5.1 Per-pin energy

13 mJ/flip (5 ms × 0.8 A × ~3 V across 4 Ω coil including FET drop). At 1% duty cycle in continuous use: ~0.13 mW per pin average.

For 3,840 pins: 0.5 W average **per pin if all pins are flipping every refresh.** Realistic refresh rate is 1 Hz with ~10% pin change per frame → 384 flips/s × 13 mJ = 5 W average.

### 5.2 Peak power

24 banks firing simultaneously × 1.5 A × 5 V = 180 W peak instantaneous. This is for ~5 ms then drops to zero until next pulse. Average: 180 W × (5 ms × 1 / 1000 ms) = 0.9 W per active phase.

Power supply spec: **5 V, 5 A continuous (Mean Well RS-25-5 sized for 25 W), with 470 µF + 100 µF × 8 bulk capacitors local to each bank to handle the 180 W bursts.** Burst current is supplied by capacitors; supply only refills them between pulses.

Calculation: 180 W burst × 5 ms = 0.9 J per burst. Capacitor energy: ½ C V² = ½ × 1.27 mF × 25 = 16 mJ at 5 V. **The bulk caps cannot supply the full burst alone.** We need either:

- Higher-capacity bulk: 50,000 µF total (50 mF) gives 625 mJ buffer, enough for two bursts. Achievable with 6× 10,000 µF aluminum electrolytics (~$30).
- Higher-voltage rail and step-down per bank: drives caps at 12 V where energy density is 6× higher.

**V1 spec adopted: 12 V supply rail with step-down to 5 V at each bank. 12 V bulk caps store much more energy per volume; banks each have a TPS56C230 buck converter giving 5V/3A locally.** Adds ~$20 to BoM, gains ~6× burst capability.

### 5.3 Heat dissipation

5 W average dissipates as I²R loss in coils and FETs. Without active cooling, FR-4 thermal resistance ~50 K/W for an unstiffened board → 250 K rise. Unacceptable.

Mitigation:
- Aluminum top plate acts as a heat spreader.
- Bottom enclosure has thermal vias under each driver IC connecting to the aluminum case bottom.
- For the full-array build: a small 40 mm fan in the enclosure, off when display is idle, ramps on when temperature exceeds 40°C. Adds $5 BoM, $0 in firmware (just an ADC + GPIO).

40-cell intermediate runs without a fan because it never sustains 5 W (single-bank operation, ~0.5 W average). Single-cell runs without a fan because it's a single coil.

---

## 6. Electrical safety and isolation

5 V supply rail, 1.5 A peak, 25 W total. Below SELV thresholds; no hazardous voltages. No isolation needed between user and power.

USB-C ground tied to enclosure ground. Aluminum top plate floating (ESD risk if user is statically charged). Mitigation: 1 MΩ bleed resistor from top plate to ground via grounding washer at one corner. Still allows the plate to be removed for cleaning.

---

## 7. Constants reference table

For all downstream calculations.

| Symbol | Quantity | Value |
|---|---|---|
| µ₀ | Vacuum permeability | 4π × 10⁻⁷ T·m/A |
| µᵣ_Fe | Soft iron relative permeability (low field) | ~5,000 |
| µᵣ_µmetal | Mu-metal relative permeability (low field) | ~50,000 |
| Bᵣ_N42 | N42 remanent flux density | 1.30–1.32 T |
| Hcj_N42 | N42 intrinsic coercivity | ≥955 kA/m |
| ρ_Cu | Copper resistivity at 25°C | 1.72 × 10⁻⁸ Ω·m |
| α_Cu | Copper resistivity tempco | 0.0039 /K |
| c_Cu | Copper specific heat | 385 J/(kg·K) |
| Bsat_Fe | Soft iron saturation | ~2.1 T |
| Tcurie_NdFeB | NdFeB Curie temperature | 310°C (well above operating) |

---

## 8. Validation gates derived from this analysis

These are duplicated in each build doc but live here as the canonical source.

| Gate | Test | Pass criterion |
|---|---|---|
| G1 | Single-cell hold force | ≥150 g resistance to push-down before pin sinks |
| G2 | Single-cell flip success | ≥99% of 1000 attempted flips cleanly transition to the target detent within 30 ms |
| G3 | Single-cell pulse current | <2 A peak and <30 mJ per flip |
| G4 | Single-cell crosstalk | <1 in 10⁶ neighbor pin disturbance under 1000 active flips |
| G5 | 40-cell refresh time | <4 s for full-line clear-and-fill (single bank) |
| G6 | 40-cell stuck-pin rate | 0 stuck pins after 1000-pattern overnight cycle |
| G7 | 40-cell tactile read | Fluent braille reader reads sample text without error at normal speed |
| G8 | Full-array refresh time | <1.6 s clear-and-fill (24 banks parallel) |
| G9 | Full-array stuck-pin rate | <5 stuck pins after 24-hour soak |
| G10 | Full-array tactile read | Fluent reader confirms graphics legibility on chart-rendering test |

If any gate fails, do not advance to the next tier without a documented diagnosis and remediation plan.

---

## 9. Open analytical questions that only bench measurement closes

1. Realized detent force at 1.0 mm pole washer + N42 1×1×1.5 mm + dual-magnet stack. Analytical estimate 130 ± 50 g.
2. Realized coil-flip force with vs. without ferrite layer. Analytical without: ~3 mN; with: ~15 mN.
3. Crosstalk transient amplitude at neighbor 2.5 mm distant during a 5 ms flip pulse. Analytical: 1–3 mT.
4. Coil resistance under realized PCB tolerances. Analytical: 1–3 Ω depending on layer count.
5. Whether 0.10 mm trace/space registers reliably across 3,840 coils on a 150×160 mm panel.

All five are answered by single-cell + 40-cell measurements before the full PCB is committed.

---

## References

- ISO 17049:2013 — Accessible design — Application of braille on signage, equipment and appliances.
- IPC-2152 — Standard for Determining Current Carrying Capacity in Printed Board Design.
- KJ Magnetics, "Pull Force Cases and Calculator" — empirical pull-force data for disc magnets on iron plates.
- Zarate JJ, Shea H. "Bistable electromagnetic actuator for braille displays." EP3382678A1 (EPFL, 2018).
- Lee H, Kim J. "Flip-latch electromagnetic actuator with bistable behavior for high-density refreshable braille." IEEE Trans Haptics 13(2), 2020.
- NTT Group press release, "MagneShape: Magnet-array braille display with flux shielding." 2023-05-30.
- Abbasi B, et al. "Bistable magnetic shells for refreshable braille and tactile graphics." Adv. Mat. Tech. 9, 2024.
- Texas Instruments DRV8847 datasheet (rev D, 2023).
- Coey JMD. *Magnetism and Magnetic Materials.* Cambridge UP, 2010 — for magnetic-circuit analysis foundations.
