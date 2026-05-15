# 02 — Comparison vs. Monarch / Dot Pad and red-team analysis

This document does three jobs:

1. Side-by-side comparison vs. the published Monarch and Dot Pad designs.
2. Argument for why this design is *arguably better* in specific dimensions, and where it concedes.
3. Red-team flaw analysis — what could go wrong with this design that the original document understates or omits, and what we'd do about it.

The red-team is deliberately uncharitable to our design. Every concern raised here has a corresponding mitigation, but several are open and only single-cell measurement closes them.

---

## 1. Architecture comparison

### 1.1 The Monarch / Dot Pad actuator

What is publicly known from Dot Inc patents (KR101762486, KR102054756, US10984675 family), Monarch teardowns, and Dot Inc engineering presentations:

- **Per-pin actuator module:** each pin is a self-contained module with its own coil, magnet, pole pieces, and pin shaft. Modules are roughly 4 × 4 × 8 mm, mass-produced as discrete parts.
- **Coil:** wound copper wire (not planar PCB). Several hundred turns of 50 µm wire around a soft-iron bobbin. This gives ~10× more turns per unit volume than a planar PCB coil and a ~1000× field multiplication from the iron core.
- **Magnet:** ~1.5 mm cylindrical NdFeB, axially magnetized, sized to fit inside the coil bobbin's central bore.
- **Bistable detent:** two soft-iron pole pieces above and below; the magnet snaps to either by permanent attraction. Hold force ~250 g.
- **Driver:** custom multi-channel driver ASIC, addressing modules in groups via multiplexing. The driver IC is application-specific to the actuator module and not separately sold.
- **Module assembly:** each module is reflow-soldered or pin-headered to a backplane PCB that routes the matrix-addressed signals.
- **Module cost (estimated from BoM teardowns):** ~$1.50–$2.50 per module. For Monarch's 3,840 modules: ~$6,000–$10,000 in actuators alone, which is consistent with Monarch's $17,000 retail.

### 1.2 Our actuator

- **No discrete modules.** Pins, magnets, and pole pieces are stacked in flat plate layers; coils are etched into a single 4-layer PCB beneath.
- **Coil:** 40-turn planar spiral on PCB (4 layers × 10 turns). No iron bobbin (replaced by ferrite layer above PCB — see [`01_ARCHITECTURE.md`](01_ARCHITECTURE.md) §1.5). Roughly 0.1× the magnetomotive force of Dot Inc's coil at the same current.
- **Magnet:** N42 1×1×1.5 mm disc (slightly oversized vs. original spec to compensate for weaker coil — derived in §1.3 of architecture doc).
- **Bistable detent:** two laser-cut soft-iron pole-washer plates spanning the entire array. **One plate, 3,840 holes**, instead of 3,840 individually fitted pole pieces.
- **Driver:** off-the-shelf DRV8847 H-bridge per bank with 74HC4067 mux for row/column addressing. No custom silicon.
- **Assembly:** lay pins into the cell-housing plate, sandwich, clamp with corner dowels. No per-pin handling in production.
- **Per-pin parts cost:** ~$0.30. For 3,840 pins: ~$1,150. Comparable to Dot Inc but with totally different cost structure.

### 1.3 Side-by-side

| Attribute | Monarch / Dot Pad | This design | Winner |
|---|---|---|---|
| Per-pin actuator cost | ~$2 | ~$0.30 | **Ours** (6.6×) |
| Per-pin field strength | ~50 mT (iron-cored coil) | ~6 mT planar, ~30 mT with ferrite layer | Monarch |
| Refresh speed (full array) | ~10 s on Monarch | ~1.6 s analytical (V1), ~640 ms (60 banks) | **Ours** |
| Hold force (per pin) | ~250 g | ~150 g target (V1) | Monarch |
| Power consumption (idle) | Zero | Zero | Tie (both bistable) |
| Power consumption (refresh) | ~1 W (slow refresh) | ~5 W (fast parallel refresh) | Monarch |
| Driver IC count | ~120 (custom ASIC) | ~30 (off-the-shelf) | **Ours** |
| Manufacturing complexity | High (discrete modules) | Medium (plate stack + PCB) | **Ours** |
| Per-pin field margin | High (overdesigned coil) | Tight (analytically borderline) | Monarch |
| Tolerance to debris | High (sealed module per pin) | Low (open sleeve, common chamber) | Monarch |
| Repairability | Module-replaceable | Plate-replaceable; individual pins not | Monarch |
| Patent freedom | Owned by Dot Inc | We tread on KR/US claims | Monarch |
| Audible noise | Quiet | Quiet (per design) | Tie |
| Total BoM | ~$6,000+ | ~$3,000 | **Ours** |

**Summary:** our design wins on cost, refresh speed (banked parallel), and driver simplicity. Monarch wins on physical robustness, force margin, debris tolerance, and patent freedom.

---

## 2. Why Monarch chose what they chose

This is the steelman for the Monarch design. Our design is not strictly better — there are real engineering reasons Dot Inc made the choices they did.

### 2.1 Why discrete modules instead of plate-stack

**Robustness and repair.** A 3,840-pin array has 3,840 opportunities to fail. If one pin sticks, in our design the entire stack must be opened, the bad pin found in a lattice of identical neighbors, and either repaired or the whole plate replaced. In Monarch's design, the bad module is identified by software self-test and physically swapped without disassembling the rest.

For a $17,000 commercial product with a warranty obligation, modularity is non-negotiable. For a $3,000 personal prototype, it isn't.

**Manufacturing yield.** A 4-layer PCB with 3,840 coils at 0.10 mm trace/space has thousands of opportunities for a single broken trace to disable a coil. Yield is multiplicative: 99.99% per-trace × thousands of traces = ~95% board yield. With per-pin modules, a bad coil is one bad module discarded at QC; the rest are fine.

**Tolerance accumulation.** The Monarch design's per-pin module has its own internal alignment of magnet, coil, pole piece, and pin shaft. The module-to-backplane interface is then a single low-precision connection. In our design, the magnet alignment depends on the cell-housing-plate hole, the upper pole washer hole, the lower pole washer hole, and the PCB coil center all being co-axial within ±0.1 mm — across 7 plates. **The Monarch design has 1 high-precision interface per pin; ours has 4.**

This is the single most important reason Dot Inc didn't do what we're doing. They knew it.

### 2.2 Why iron-cored wound coils

**Field strength.** Wound coils with iron cores produce 100–1000× the field of equivalent-volume planar PCB coils. Monarch's actuator works with comfortable margin; ours analytically squeaks by.

**Power efficiency.** Iron-cored coils require less current for the same field. Monarch can operate the entire array on a USB bus power budget (~7.5 W). Ours requires a 25 W external supply.

**Driver-rating headroom.** Monarch's coils run at low currents (~100 mA) which means the per-bank multiplexed driver can address many channels with a small total driver capacity. Our coils run at 1.5 A peak, requiring beefier (and hotter) drivers.

### 2.3 Why custom driver ASIC

**Channel count.** Driving 3,840 channels with off-the-shelf parts requires either (a) lots of jelly-bean parts, our approach, with the cost of board area and routing complexity, or (b) one custom IC that integrates everything, Dot Inc's approach. At Monarch's volume (target 10,000+ units lifetime), the NRE for an ASIC amortizes; at our volume (1 unit), it doesn't.

**Driver-coil matching.** Custom ASIC lets the driver be optimized for the exact coil characteristics — pulse shaping, current limiting, even sense feedback — in a way generic H-bridges can't.

**Integration.** A single ASIC means one solder package per ~32 pins, not the cluster of H-bridge + mux + level shifter we use.

### 2.4 Why slow refresh

This one is partly Monarch's choice and partly a consequence of their architecture. Monarch's ~10 s refresh comes from sequential addressing of all 3,840 pins through a relatively narrow driver channel count. They could have built more banks for faster refresh; they chose not to because:

- Tactile graphics are typically static reference images, not animations. 10 s is fine for the use case.
- Faster refresh requires more parallel drivers, more burst current, more EMI, more power.
- Slower refresh runs cooler, reducing thermal demands on the modules.

For a financial-charting use case (the AccessibleTrader application this is intended for), faster refresh genuinely matters — markets move; static charts don't. **Our design's faster refresh is a real win for this specific application** even though Monarch's slower refresh is right for theirs.

### 2.5 Bottom line on Monarch's choices

Monarch is engineered for: warranty-grade reliability, USB bus power, regulatory certification, manufacturing at modest volumes, and patent freedom around earlier piezo-bimorph displays.

Ours is engineered for: build-it-yourself feasibility, parts availability with no NDA, fast refresh for active applications, and a 6× cost reduction.

Different engineering targets, different right answers. The honest claim is not "ours is better" but "ours is better for our use case and worse for theirs."

---

## 3. Where this design is *arguably* better

Restricting "better" to attributes that matter for a maker building a financial-trading tactile display:

### 3.1 Parts accessibility

Every part in our BoM is buyable today, by anyone, with no relationship, NDA, or minimum order quantity beyond hobbyist quantities. Monarch's actuator modules cannot be bought separately at any price. This is the single biggest practical advantage.

### 3.2 Refresh speed

For tactile charting where the display must follow real-time market data, our 1–1.6 s refresh is a transformative usability difference vs. Monarch's ~10 s. A trader monitoring a 1-minute candle can see the candle close haptically as it forms; on Monarch, the candle would already be 10 candles old by the time the chart finishes refreshing.

### 3.3 Cost

$3,000 vs. $17,000. For an individual user and especially for a software developer building tools for blind users (where giving away or discounting hardware to early users is a viable distribution strategy), this 6× ratio is the difference between "feasible" and "a few well-funded universities."

### 3.4 Open documentation

This documentation set itself. Monarch has no public protocol, no public schematic, no developer SDK without a partnership agreement. Ours has every parameter, every part number, every manufacturing step, with sources. A determined maker can replicate it; a researcher can extend it; an accessibility shop can adapt it.

### 3.5 Customizability

Because it's a stack of plates and a PCB, the design can be reconfigured. Want a 60-cell line display instead of a 60×64 matrix? Same plates, different sizes. Want 1.5 mm pitch for higher resolution? Same architecture, different parameters. The Monarch's discrete-module approach makes this kind of reconfiguration effectively impossible without a new tooling run.

---

## 4. Red-team analysis — flaws in this design

This section is deliberately critical. Each flaw has a counter, but several are open.

### 4.1 The coil field is analytically borderline

**Flaw:** [`01_ARCHITECTURE.md`](01_ARCHITECTURE.md) §1.5 derives an analytical force on the magnet from the planar coil at ~6 mN, vs. a detent of ~150 g (1.5 N) that must be broken to flip. The dipole approximation that gives 6 mN is known to underestimate by 5–20× in this regime, but that's still 30–120 mN — only 2–8% of the detent.

We *think* this is closed by the ferrite-layer addition (§1.5 last subsection), pushing field into the clearly-flippable range. But that's an analytical estimate, not a measurement.

**Worst case:** the single-cell prototype cannot reliably flip the magnet with available coil current (1.5 A) and we have to redesign — bigger coils, more layers, smaller air gap, or even an iron-core flux concentrator pressed into the PCB. Each fix adds cost and complexity.

**Mitigation:** the single-cell prototype is *specifically* designed to test this with multiple geometries (with/without ferrite layer, 4 vs 6 coil layers, 1.5 vs 1.0 mm gap). Decision gate G2 requires ≥99% flip success; if it fails, we iterate cell geometry before scaling.

**Risk if mitigation fails:** the entire architecture collapses and we revert to a wound-coil iron-core actuator (Monarch-style). At that point we've wasted the single-cell budget (~$110), which is acceptable.

### 4.2 Detent force is analytically tight

**Flaw:** [`01_ARCHITECTURE.md`](01_ARCHITECTURE.md) §1.3 estimates ~130 g hold force, against a 150 g target. That's a 13% shortfall before any tolerance accumulation.

**Worst case:** real-world hold force is 100 g; pins sink under normal reading pressure; users feel mush instead of dots. This is a tactile-feel failure, not a functional one — pins still flip, they just don't hold against fingers.

**Mitigation:** §1.3 lists three escalation paths (thicker washer, stronger magnet, looser pitch). Single-cell prototype tests all three; decision gate G1 requires ≥150 g.

### 4.3 Tolerance stack-up across 7 layers

**Flaw:** the design assumes all 7 layers register to ±0.1 mm corner-to-corner. With realistic fab tolerances:

- PCB drill: ±0.05 mm (PCB house spec).
- Laser-cut steel pole plates: ±0.10 mm typical.
- Drilled aluminum top plate: ±0.025 mm if specified H8.
- 3D-printed cell housing: ±0.15 mm typical for good FDM, ±0.05 mm if using a high-end printer with calibration.

Worst-case tolerance stack: 0.05 + 0.10 + 0.10 + 0.025 + 0.15 = **0.43 mm**. Way over our 0.1 mm budget.

The original design doc's mitigation — "reference all critical dimensions to corner dowel pins" — is correct in principle but doesn't fully solve the problem. The dowel pins themselves are seated in 3 mm holes through every plate; if any plate's dowel hole is mispositioned, the whole stack shifts.

**Mitigation:** 
1. Specify reamed dowel holes (±0.012 mm H7) on all plates that carry critical features. PCB houses can do this for an upcharge.
2. Use the cell-housing plate's *sleeve* as the alignment feature for pins, not the dowel pins themselves. Even if the cell-housing plate is shifted by 0.3 mm relative to the pole plates, as long as pin sleeve and pole washer hole are co-axial (which is enforced by the cell-housing plate's *own* hole spacing), the pin still threads through both.
3. Make the pole-washer hole 0.3 mm larger than the pin OD (1.8 mm hole for 1.5 mm pin). The magnet (1.0 mm Ø) sees the 0.6 mm inner diameter of the washer with comfortable margin even with 0.4 mm misalignment.

This works because the pin's *position* doesn't have to be precisely registered to the pole washers — only the pin's *axis* has to pass through the washer center, and that's determined by the cell-housing plate alone.

**Effectively, only the cell-housing plate must hold tight pin-position tolerance. All other plates have generous holes.** This is a critical design refinement vs. the original document and must be reflected in the CAD files.

### 4.4 The cell housing is the single point of precision failure

**Flaw:** following from §4.3, the entire array's pin-position accuracy depends on a single 3D-printed plastic part. PETG at 150×160 mm is known to warp 0.5–0.8 mm corner-to-center over time, especially under thermal cycling.

**Worst case:** initial assembly is good, but after 6 months of room-temperature variation the cell housing warps and pins start sticking or jam.

**Mitigation:**
1. Use **CF-PETG** (carbon-fiber filled) for the cell housing. Warpage drops to <0.1 mm over the same span. Filament cost: ~$45/kg vs. $25/kg for plain PETG. Negligible.
2. **Anneal** the cell housing in an oven at 80°C for 2 hours after printing. Relieves internal stress that causes long-term warping.
3. **Thicker plate (5 mm vs 4 mm).** More structural stiffness, less prone to warp under its own weight.
4. **Alternative material consideration:** for V2, switch the cell housing to a CNC-machined aluminum or PEEK plate. Aluminum machined at $200/plate is dramatically more dimensionally stable but adds cost.

V1: CF-PETG + annealing. V2 (if warpage measured): aluminum.

### 4.5 Magnet handling at assembly is a nightmare

**Flaw:** 4,000 N42 1×1×1.5 mm magnets must be handled and oriented (alternating N-up / S-up) during pin assembly. Magnets at this size:

- Stick to anything iron, including each other, including the steel tweezers.
- Snap together when they get within ~5 mm, often pinching skin or damaging the magnet face.
- Are unable to be visually distinguished N-up vs S-up; orientation is determined by behavior.
- Have an ~5% scratch rate from handling, even with experienced technicians.

Realistic assembly time: 30 seconds per pin × 4,000 pins = **33 hours** of magnet-handling, in a single sitting that cannot be interrupted (you cannot leave a half-assembled checkerboard half-done; orientation discipline degrades).

**Worst case:** assembly takes 60+ hours, multiple days, with cumulative orientation errors propagating through the array. Final test reveals random pins inverted; full disassembly required.

**Mitigation:**
1. **Pre-assembled pin subassemblies:** a small jig holds 16 pins at a time, magnets are fed into a hopper and oriented automatically by polarity-sorter. Cost: ~$50 in 3D-printed parts + a Hall-effect sensor. Time per batch: 5 minutes. Time for 4,000 pins: ~21 hours, doable in 3-4 sessions.
2. **Two pin types pre-assembled:** order 2,000 N-up pins and 2,000 S-up pins as separate runs; assembly just becomes "place from N-up tray, then S-up tray, alternating." Removes orientation logic from the assembly step.
3. **Color-code pin tops:** dip the top of N-up pins in red ink and S-up in blue. Visual orientation check at any stage. Wears off but lasts the assembly window.

Adopt all three. This is the most underestimated risk in the original document.

### 4.6 The HID Braille protocol path is incomplete

**Flaw:** for the display to actually work with Windows screen readers and AccessibleTrader, it must speak the Windows HID Braille usage page (0x0041) protocol. The original design doesn't document this; [`06_FIRMWARE.md`](06_FIRMWARE.md) (forthcoming) will, but it's a significant firmware effort.

**Worst case:** physical display works; software integration takes another 2-3 weeks of firmware development and testing across screen readers (NVDA, JAWS, Narrator). Plus, HID Braille protocol limits us to 8-dot cell graphics, not the full 96×40 pin matrix — same constraint Tyler hit on Monarch.

**Mitigation:** parallel HID Braille (for screen reader compatibility) + custom vendor protocol on a separate HID collection (for AccessibleTrader full-resolution graphics). Documented in firmware doc. Adds firmware complexity but doesn't impact hardware build.

### 4.7 Audible noise from 24 banks firing in parallel

**Flaw:** magnetic snap detents click. 24 banks each firing every 10 ms means a continuous "ratcheting" sound during refresh. In a quiet trading environment this could be intrusive.

**Worst case:** users find the sound distracting and disable rapid refresh, defeating the speed advantage.

**Mitigation:**
1. Soft elastomer (50A silicone) bumpers between magnet and pole pieces. Reduces "click" to "tick."
2. Stagger pulses across banks by 0.1–0.2 ms. Spreads the energy in time, eliminating beating.
3. Foam-lined enclosure interior dampens transmitted sound.
4. Firmware option: "quiet refresh mode" that uses 60-bank topology at 640 ms refresh but sequences banks individually so peak click rate is one bank at a time, much quieter.

### 4.8 Skin oil and dust ingress

**Flaw:** 3,840 open sleeves with pins moving through them are a magnet for skin oil, dust, dead skin cells. Within 3-6 months of daily use, sleeve friction increases enough to start causing flip failures.

**Worst case:** display works fine for 6 months then degrades to unusable; user has no way to clean it without dismantling.

**Mitigation:**
1. **Removable top plate** (4 captive screws, no glue). User can lift the top plate, swab the pin tops with isopropyl alcohol, replace.
2. **Dry PTFE lubricant** applied to pin shafts at assembly. Re-application possible during deep cleaning.
3. **Foam dust skirt** around the active area of the top plate (like a keyboard's keycap collar). Reduces dust ingress 90%+.
4. **Sealed enclosure on all non-tactile sides.** No back vents.
5. **Optional:** sneeze-guard plastic film over the array surface. Adds ~0.05 mm to perceived dot height (acceptable) and eliminates direct skin contact with pins. Replaceable monthly.

### 4.9 Unit-cell magnetic coupling is non-uniform across the array

**Flaw:** at the *edges* of the array, pins have fewer neighbors than pins in the *center*. Edge pins experience less crosstalk; center pins more. This means a single calibration of pulse current may over-drive edges and under-drive center.

**Worst case:** center pins fail to flip while edge pins do; arrays appear to have a "dead zone" in the middle.

**Mitigation:**
1. Per-pin or per-bank pulse-current calibration in firmware. The framebuffer can drive each bank independently with different current.
2. The design's fundamental detent strength is set by geometry, not coupling, so the *static* detent is uniform; only the *transient flip* is affected by neighbor density. A 10-20% pulse-current increase for center banks is sufficient.

### 4.10 PCB yield at 0.10 mm trace/space across 150×160 mm

**Flaw:** JLCPCB's standard tier supports 0.10 mm trace/space. At a 150×160 mm panel size with 3,840 coils × 40 turns each = ~150,000 trace segments, a single broken trace (manufacturing defect rate ~0.001%) yields:

- Probability of zero defects: 99.999% ^ 150,000 = ~22%.
- **Expected number of defective coils per board: ~5.**

That's "fine, mostly works" at scale, but 5 dead pins out of 3,840 is ~0.13% — visible to a careful reader. JLCPCB's "JLC04161H" tier explicitly covers up to 6 mil trace/space at higher yield; 4 mil is best-effort.

**Worst case:** every fabricated board has 5-15 dead coils that need to be identified, mapped, and either repaired (impractical) or accepted as known-bad pins.

**Mitigation:**
1. **Order 5 boards, screen each.** First-pass test each coil with a continuity meter before any assembly. Reject boards with >3 defective coils. (Hence the 5× quantity in BoM.)
2. **Upgrade tier:** JLCPCB's 4/4 mil tier ($165/board vs $110) advertises higher yield. Worth the upcharge if standard-tier yield is low.
3. **Firmware dead-pin map:** at first power-up, run a self-test that flips every pin and senses (via a Hall sensor wand passed over the surface, or by visual inspection) which fail. Store the dead-pin map and skip those addresses during render.

### 4.11 Mu-metal saturation and aging

**Flaw:** mu-metal's permeability degrades with mechanical work and saturates above ~0.7 T. If a pin's magnet ever gets close enough to a mu-metal sheet to drive it into saturation, the sheet's permeability stays degraded permanently in that local zone.

**Worst case:** localized "blind spots" in the mu-metal shielding accumulate over thousands of refresh cycles, gradually degrading crosstalk performance.

**Mitigation:**
1. Mu-metal sheets are ≥1 mm from any magnet (cell-housing plate thickness keeps them well separated).
2. If shielding degrades, the mu-metal sheet is removable and replaceable — it's a sandwich layer, not glued.
3. Worst-case fallback: this is a 5-year aging issue, not a 1-year build issue. V1 ships with mu-metal; V2 considers permanent ferrite-magnet shielding (immune to fatigue).

### 4.12 The single-PCB-substrate is a single point of failure

**Flaw:** in our design, the entire array's coils are on one PCB. If the PCB cracks (drop, twist, manufacturing defect), the whole array dies. Monarch's modules each have their own coil PCB; a single bad PCB takes out one pin.

**Worst case:** user drops the device on a corner; PCB cracks; entire array becomes a paperweight.

**Mitigation:**
1. **Aluminum top plate + bottom enclosure** sandwich the PCB structurally; no flex stress reaches the PCB itself.
2. **PCB is bonded** (not just bolted) to the bottom enclosure to distribute stress.
3. **Keep spare PCBs** (the BoM specifies ordering 5 — only 1 used; 4 spares in case of damage years later).
4. V2 consideration: split the coil PCB into 4 quadrants, separately bolted, with ribbon-cable interconnects. Damage to one quadrant takes out 25%, not 100%.

### 4.13 H-bridge thermal stress at 1.5 A peak

**Flaw:** DRV8847 is rated 1 A continuous, 1.8 A peak. We're operating at 1.5 A peak with 5 ms pulses. Pulse thermal capacity is fine analytically, but if firmware bugs cause sustained activation, the H-bridge fries.

**Worst case:** firmware deadlock leaves a single coil energized for >1 s; H-bridge dies; bank goes dark.

**Mitigation:**
1. **PIO hardware watchdog**: pulse width enforced by Pi Pico PIO timer, completely independent of CPU. Even a CPU lockup cannot extend a pulse beyond 10 ms.
2. **Series resistor per bank**: 0.5 Ω resistor in the H-bridge output limits steady-state current to 5 V / 0.5 Ω = 10 A worst-case (limited by H-bridge SOA), but realistic limit with FET on-resistance drops to ~3 A continuous, well within IC rating.
3. **Thermal cutoff IC** (TMP235) on each bank's H-bridge package; trips and disables driver above 100°C.

### 4.14 RP2040 PIO peripheral count vs needed bank count

**Flaw:** RP2040 has 8 PIO state machines (2 PIO blocks × 4 SMs). We have 6 banks per slave MCU, each needing 1 PIO SM for pulse generation. Plus the SPI link to master = 7 SMs needed per slave. Tight.

**Worst case:** PIO contention forces firmware compromises; refresh time slips.

**Mitigation:**
1. RP2350 instead of RP2040 has 12 PIO SMs (3 blocks × 4 SMs), comfortable margin. Spec RP2350 for slaves too. ~$2 more per slave; trivially affordable.
2. Or: 6 banks per slave is too many; use 4 slaves × 6 banks = 24 banks; reduce to 6 slaves × 4 banks = 24 banks. More slave MCUs but fewer PIO contentions per slave. Each slave needs 5 SMs (4 banks + SPI) — fits comfortably in RP2040.

V1: 6 slaves × 4 banks, RP2040. Adopted.

---

## 5. Where the design might be **fundamentally** wrong

This section names the ways the entire architecture could be invalidated by single-cell measurement.

### 5.1 If the planar coil cannot generate enough field

**What kills it:** measured flip force <50 mN even with ferrite layer, 6 coil layers, and 2 A current. No reasonable redesign closes the gap.

**Probability:** 15–25% based on the analytical pessimism.

**Recovery:** abandon planar coil; use wound coils (50-µm magnet wire on a printed bobbin) per pin. Per-pin actuator volume grows from "embedded in PCB" to "mounted on PCB." Architecture becomes much closer to Dot Inc's, with all the cost and complexity that implies. **At that point, we're not 6× cheaper than Monarch any more; we're maybe 2× cheaper. Project value drops substantially.**

**Indicator:** single-cell prototype G2 fails after exhausting all coil-redesign options.

### 5.2 If crosstalk at 2.5 mm pitch cannot be controlled

**What kills it:** even with all 8 mitigation layers, neighbor pins disturb each other ≥1% of the time during a flip, and we cannot guarantee static state.

**Probability:** 5–10%. Less likely than 5.1 but not negligible.

**Recovery:** loosen pitch to 3.0 mm (changes everything; abandons standard braille spacing for graphics-only operation). Tactile-graphics legibility unaffected; braille-text legibility marginal but acceptable.

**Indicator:** 4-pin sub-prototype G4 fails.

### 5.3 If tolerance stack-up causes >10% pin failure

**What kills it:** single 8-pin cell builds reliably, 40-cell line builds reliably, but the full 150×160 mm panel has accumulated tolerance errors that cause edge pins to bind, jam, or sit at wrong heights.

**Probability:** 20%. This is the silent-killer scenario — small problems that only manifest at scale.

**Recovery:** quadrant the cell-housing plate (4 separately-printed parts dowel-joined), reducing per-part precision burden. Or: machine the cell-housing plate from aluminum at $200, dramatic dimensional improvement.

**Indicator:** 40-cell prototype reveals position-dependent failure.

### 5.4 If patent enforcement happens

**What kills it:** Dot Inc or APH brings a patent infringement action against the project even at personal-research scale.

**Probability:** <2%. Personal research is generally protected; Dot Inc is unlikely to sue a hobbyist; APH is mission-driven, not litigation-driven.

**Recovery:** redesign around contested claims. Most likely contested: bistable magnetic detent (the basic flip-latch). Alternative: bistable elastomer shells (Abbasi 2024) — different mechanism, no Dot Inc claim.

**Indicator:** cease and desist letter.

---

## 6. Patent and freedom-to-operate

### 6.1 Relevant patents

- **US10984675 / KR101762486 / KR102054756** — Dot Inc, "Refreshable display module having pin actuators." Claims cover bistable magnetic actuator with coil flux-drive and magnet-on-pin geometry. **Likely covers our design.**
- **EP3382678A1** — EPFL Zarate & Shea, "Bistable electromagnetic actuator." Filed 2017, claims a similar structure but with a wound coil. Our planar coil may or may not infringe specific claims depending on construction.
- **US9892649** — APH, related to Monarch interface. Probably does not affect our design (interface, not actuator).
- **JP2019-XXXXXX** (NTT) — MagneShape pot-magnet shielding. Our use of this technique may infringe specific claims.

### 6.2 Personal research vs. commercial

**Personal research and bench prototypes are protected** under research-use exemptions in most jurisdictions, especially when (a) no sales occur, (b) no public demonstration, (c) no commercial benefit beyond the research itself.

**Commercial production is not protected.** Distributing assembled units, selling kits, or licensing the design for commercial use requires a Freedom-to-Operate (FTO) opinion from a patent attorney specializing in haptics or display patents.

### 6.3 What to do

1. **Do not commercialize without an FTO opinion.** Period.
2. **If commercial intent emerges**, the path is:
   a. Have an attorney review the actual built design vs. Dot Inc's claims.
   b. If infringement exists, attempt a license from Dot Inc (they may say no; they're a sole-source vendor for a reason).
   c. If license unavailable, redesign around the contested claims. Most likely path: switch to bistable elastomer shells (Abbasi 2024), which is not in Dot Inc's claim set.
3. **For now, document everything**, build at personal scale, and treat any commercial path as a separate future project.

---

## 7. Net assessment

**Is this design genuinely better than Monarch?** No, in absolute terms. Monarch is more robust, more mature, more reliable, more thoroughly de-risked.

**Is it better for our use case?** Yes, on cost, refresh speed, and accessibility of parts. Whether it's *good enough* for the AccessibleTrader application is the question the build process answers.

**Are we copying Monarch?** Partly, in the sense that we use the same actuator principle (bistable magnet + coil drive). Not, in the sense that the substrate-integrated coil array, banked-parallel addressing, and off-the-shelf driver topology are genuinely novel implementation choices that Monarch did not make.

**Will the design work as specified?** The honest answer is: **the architecture is sound; the analytical numbers are tight in two places (coil field and detent force) that single-cell measurement can confirm or close.** If those measurements pass, scaling to 40-cell and full-array is engineering work, not invention.

**Should we proceed?** Yes, to single-cell prototype. Spend the $110 and the weekend. The gates close at single-cell with $110 at risk. Past that, gates close at 40-cell with $340 at risk. By the time we commit the full $3,000, every architectural risk has been retired by physical measurement.

The build sequence *is* the risk-management plan.
