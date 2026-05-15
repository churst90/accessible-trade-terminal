# 08 — Tactile assembly walkthroughs

Step-by-step assembly instructions designed to be followed by a blind builder using touch alone — no visual cues required. Each step describes what you should feel, what should click into place, and what wrong-feels mean something is misaligned.

Read this aloud through a screen reader, or print in braille, or have a sighted partner read it as you build. **The instructions assume the parts are organized in labeled containers in front of you on a clean, magnet-safe (non-steel) work surface.**

Three walkthroughs follow:
1. **Single-cell prototype** — 8 pins, no enclosure, bench-top
2. **40-cell line display** — 320 pins, in enclosure
3. **Full-array** — 3,840 pins, in enclosure

Each tier assumes the previous tier was built and validated.

---

## Universal preparation (do this before each build)

### Setting up the work area

1. Clear a flat work surface at least 60 × 60 cm. **It must not be steel** — magnets will stick to a steel desk and contaminate everything. A wood desk, a thick rubber mat, or a sheet of plywood placed on top of a steel desk all work.

2. Place a **rubber anti-roll mat** (textured silicone, the kind used to line drawers) across the work area. Magnets dropped on this mat stay where they fall and don't roll into hard-to-find places.

3. Have a **magnet sweeper or strong magnet on a stick** within reach. If a small magnet falls on the floor, you'll never find it by hand; sweep the floor and it will jump to the magnet.

4. Use **brass tweezers, not steel.** Steel tweezers grab magnets and won't let go. Brass tweezers are non-magnetic — magnets sit cleanly in them. The packaging will say "non-magnetic" or "brass."

### Organizing parts by feel

Set up labeled trays with distinctive shapes or textures so you can identify them by touch:

- **Pin tray (silver pins, slightly heavier than they look):** small box, label on the lid in braille or with a tactile sticker. For full-array, two trays — one for "N-up" pins (color-coded red on top, but you'll identify them by being in the labeled tray, not by color), one for "S-up" pins.
- **Magnet tray (very small discs, shockingly strong):** a non-steel container, ideally divided into compartments. Magnets must NOT be near pole washers or each other in storage; they will pile up.
- **Pole-washer trays (thin steel discs with center holes):** three trays for single-cell (0.5 / 1.0 / 1.5 mm thicknesses); one tray for 40-cell and full-array.
- **Top plate (a single flat aluminum rectangle, slick anodized surface, distinctive heft):** in its own tray, anodized side up.
- **PCB (thin rigid board with smooth solder mask, sharp edges, components on one side):** flat in a static-safe tray, components-side DOWN by default.
- **Cell housing (3D-printed plastic plate, slightly textured matte surface, holes in a regular grid):** in a tray.
- **Cell housing should feel different from pole washers:** pole washers are a stack of thin discs that clink when shaken; cell housing is a single rigid plate with no give.

### Tactile orientation conventions

For every part with a "front," "back," "top," or "bottom":

- **The PCB has components on one side and a flat solder-mask surface on the other.** The components-side has small bumps and rectangular features. The solder-mask side feels nearly smooth. Throughout: PCB is placed components-side DOWN unless stated otherwise.
- **The top plate has one bevelled edge.** Run your finger around the perimeter; one edge feels gently sloped down rather than sharp 90°. **The bevelled edge is the FRONT** — the edge nearest you when reading.
- **The cell housing has corner dowel holes — 4 small round holes near each corner.** Two of them (the back corners) are slightly larger than the other two (the front corners). This asymmetry is intentional and tells you which side is the back.
- **The pole washer plates are symmetric** — they don't have a defined orientation other than the dowel holes. Either way works.

If two parts seem indistinguishable by feel, label them with a small braille sticker or a piece of tape with a knot tied at one corner.

---

## Walkthrough 1 — Single-cell prototype

**What you're building:** a tiny tactile array, 8 pins on a 2 × 4 grid at 2.5 mm pitch. About the size of a postage stamp. You'll mount it on a small breadboard with a microcontroller; the whole thing fits on a piece of paper.

**Time:** 4–6 hours of focused work, ideally split across two sessions (assembly day + testing day).

### Phase 1 — Pin preparation (about 90 minutes)

You're making 8 functional pins (and 8 spares) by drilling a small blind hole in each pin and pressing a magnet into it.

1. **Sit at the work surface with the rubber mat under your hands.** The pin tray (raw 1.5 mm stainless steel rod, cut into 9 mm lengths) is on your left. The magnet tray on your right. The pin jig (a small 3D-printed block with 16 round pockets in a grid) in front of you. Your drill press is to your left, beside the pin tray.

2. **Take 16 pins from the tray** — feel for their cylindrical shape and roughly equal length. Place them into the pin jig pockets, one at a time. Each pin should drop in by gravity until only about 2 mm of pin sticks above the jig surface. If a pin won't drop, the pocket has debris; tap the jig upside down on the mat to clear it.

3. **Verify orientation by feel:** all 16 pins should stick up the same height (about 2 mm above the jig). Run your fingertips lightly across the tops; they should feel uniform. Any pin sticking up higher or lower is misseated — pull it out and re-insert.

4. **Move the jig to the drill press.** Center the first pin under the drill bit (a 1.0 mm carbide bit). The drill press has a depth stop set for 1.0 mm hole depth. Lower the drill onto the first pin; you should feel a brief resistance, then nothing as the depth stop hits. Lift, advance the jig to the next pin, repeat.

5. **All 16 pins drilled.** They now have a tiny blind hole in the top end. Run your finger over each one — the top end should feel like it has a small dimple in the center, not a flat circle.

6. **Now press magnets in.** Carry the jig to the magnet station. Do this in a clear area; if you drop a magnet, it WILL leap to the steel of the jig and stick to whatever metal is nearest. Take one magnet at a time with brass tweezers from the magnet tray.

7. **Drop the magnet onto the pin top.** It will snap into the blind hole because of the magnet's own attraction to the iron in the steel pin. You'll feel a small clicking sensation through the tweezers. The magnet is now seated.

8. **Verify magnet seating** by running your finger lightly over the pin top. The magnet should be flush with the pin end — no protrusion, no recessing. If a magnet is sitting proud (sticking up), use a flat tool (back of a spoon, or another pin) to press it down until flush. If the magnet is missing entirely (tweezers picked it up but didn't drop it), do that pin again.

9. **All 16 pins now have magnets seated.** This pin set is your single-cell stock. The 8 best go into the prototype; 8 are spares for later iterations.

### Phase 2 — Plate preparation (about 30 minutes)

10. **Locate the cell-housing plate** (3D-printed, has a 2 × 4 grid of small holes through it). Run your finger across the top — you should feel 8 evenly-spaced sleeve openings in the right pattern. Run your finger around the perimeter — find the 4 corner dowel holes. Note which corners feel the same vs different (back corners are slightly larger).

11. **Locate the lower pole washer plate** (thin steel sheet with 8 holes matching the cell housing). It will be magnetic — be careful where you set it down or magnets will jump to it. Place it on the work mat with no magnets within 30 cm.

12. **Locate the upper pole washer plate** — identical to lower. Set it on the mat near the lower one, also clear of magnets.

13. **Locate the aluminum top plate** (small, smooth anodized surface, 8 holes that match the housing). Find the bevelled edge — that's the front.

14. **Locate the PCB.** Components-side has bumps; solder-mask side is nearly smooth. Place components-side DOWN on the mat (smooth side up).

15. **Locate the ferrite sheet** (a thin, slightly flexible sheet, about the size of a postage stamp, 0.5 mm thick). One side has adhesive backing; peel off the protective film by feeling for the edge of the film and lifting it.

### Phase 3 — Stack assembly (about 60 minutes)

The order, from bottom to top: PCB → ferrite sheet → lower pole plate → cell housing with pins → upper pole plate → top plate.

16. **Start with the PCB.** Smooth side (solder-mask side) up. The 8 coils are etched into the PCB underneath the smooth surface — you can't feel them but they're there in a 2 × 4 grid in the center.

17. **Apply the ferrite sheet.** Adhesive side down. Position the ferrite over the center of the PCB, covering the 2 × 4 coil region. Press it down firmly with your palm; it will stick to the solder mask. The ferrite should not overhang any PCB edge.

18. **Place the lower pole plate.** Set it on top of the ferrite, oriented so its 4 corner holes line up with the cell housing's eventual corner-dowel positions (you'll insert dowels through them in step 22). The pole plate should sit flat against the ferrite. Run your finger around — there should be no overhang of the pole plate beyond the PCB edge.

19. **Place the cell housing.** Set it on top of the lower pole plate, oriented with the bevelled-edge convention (back-of-housing, with larger dowel holes, away from you). Align by feel — the cell housing's outer perimeter should be flush with the pole plate below. Push down gently; you should feel it sit flat.

20. **Insert the pins one at a time.** Pick up a pin from your "good pins" tray with brass tweezers (or by hand if comfortable — pins are small but not delicate). Drop it magnet-end-first into one of the 8 sleeves in the cell housing. As it falls through the sleeve, the magnet on its lower end will SNAP onto the lower pole washer below. You will feel and hear a small click.

21. **Verify each pin is seated.** Run your finger over the cell-housing top surface. You should feel 8 small recesses where the pin tops sit BELOW the housing surface (because the pins are in their lower-detent state, magnet pulled down). If any pin's top is sticking up above the housing surface, that pin has snapped to the upper-detent — push it down gently with your fingertip; you'll feel and hear a click as it transitions.

22. **Insert dowel pins** through the 4 corner holes. Each dowel is a small steel cylinder, 3 mm Ø, 12 mm long. Push each dowel through the corner of the cell housing, through the lower pole plate, and stop when you feel resistance (it shouldn't go all the way through into the PCB). The dowels lock the cell housing's position relative to the pole plate.

23. **Place the upper pole plate.** Slip it down over the dowel pins (the dowel pins will guide it into position). It rests on top of the cell housing. Press it down until it sits flat.

24. **Place the top plate.** Slip it down over the dowel pins, bevelled edge facing you (front). It rests on top of the upper pole plate. Press it down flat.

25. **Verify the stack with your fingertips:** running your finger across the top plate, you should feel a smooth flat anodized surface with 8 tiny holes in a 2 × 4 grid. Some pins may feel like tiny bumps just below the holes (that's the pin tip in the down-detent, recessed below the top plate); others may feel like nothing (pin in up-detent, but you haven't powered the device yet so all should be in the down-detent).

26. **Clamp the stack.** Either with corner clamps or by gluing the dowel pins in place with a tiny dot of cyanoacrylate at each corner. Light pressure is enough — the stack does not need to be tightly bolted because gravity holds it together once flat.

### Phase 4 — Wiring (about 60 minutes)

You're connecting the PCB to a microcontroller via a small breadboard.

27. **Locate the breadboard, microcontroller (Pi Pico 2), DRV8848 H-bridge breakout, and hookup wires.**

28. **Insert the Pi Pico 2 into the breadboard** straddling the center channel. The USB-C end faces away from you. Press it firmly until the pins are fully seated.

29. **Insert the H-bridge breakout** into the breadboard, on the opposite side from the Pico.

30. **Connect power:** a wire from the Pico's `5V` pin (find it by counting pins — 5V is third from the USB end on one side) to the H-bridge's `VM` pin. Another wire from any `GND` pin on the Pico to the H-bridge's `GND` pin.

31. **Connect signals:** Pico GP0 → H-bridge IN1; Pico GP1 → H-bridge IN2; Pico GP2 → H-bridge nFAULT.

32. **Connect to the PCB:** the PCB has a row of 16 pin headers (8 coils × 2 wires each). For first test, connect the H-bridge's OUT1 to PCB pin 1 and OUT2 to PCB pin 2 — this is the wires for coil 1. You'll move these wires to test other coils.

33. **Plug the Pico's USB-C cable into your computer.** The Pico will appear as a removable drive named RPI-RP2.

34. **Drag `single_cell.uf2` (built from `firmware/single_cell.py`) onto the drive.** The drive will disappear when the Pico reboots and starts running the firmware.

### Phase 5 — Validation (gates G1 through G5)

Detailed test procedures are in `03_SINGLE_CELL_BUILD.md` §7. The summary by feel:

- **G1 hold force:** put the assembly on a kitchen scale, press a single pin with the force jig until you feel and hear it sink. Read the scale reading at sink. Must be ≥150 g.
- **G2 flip success:** run firmware that flips a single pin 1000 times. Listen for clicks (each flip clicks). Run your finger over the top plate after each cycle; the pin should alternately feel raised then recessed.
- **G3 current:** measure with a multimeter across the current-sense resistor; verify peak <2 A.
- **G4 crosstalk:** flip pin 4 to the up state. Run pin 3 cycle test 1000 times. Re-check pin 4's state — it should still be up (raised).
- **G5 tactile:** invite a fluent braille reader to feel the surface and tell you if dots feel like dots.

If all 5 gates pass, congratulations — your single-cell works. The architecture is validated and you can proceed to 40-cell. **Record the parameters that worked in `SINGLE_CELL_RESULTS.md`.**

---

## Walkthrough 2 — 40-cell line display

**What you're building:** a long thin display, about 22 cm wide and 2.5 cm tall, with 320 pins in a uniform 80 × 4 grid at 2.5 mm pitch. Reads as 40 standard 8-dot braille cells AND can display tactile graphics (a chart, an image strip).

**Time:** 2–3 weekends. About 30–40 hours total.

**Differences from single-cell:** much larger plate, an enclosure to put it all in, ribbon cables instead of breadboard wires, and you must handle 320 magnets instead of 16. The pin assembly is the time-consuming part.

### Phase 1 — Pin preparation (about 12–15 hours)

The same pin-prep procedure as single-cell, but with 350 pins instead of 16. Do this in batches across multiple sessions; you will get tired and start making orientation mistakes if you do it all in one sitting.

1. **Each batch is 16 pins** using the pin jig (same jig as single-cell). At ~10 minutes per batch (cut, drill, magnet-press), 350 pins = ~22 batches = ~4 hours of focused work plus rest breaks.

2. **CRITICAL: alternate magnet polarity in checkerboard pattern.** Half the pins (175) are "N-up" — magnet's north pole points UP toward the pin's free end. Other half (175) are "S-up." When the array is assembled, adjacent pins alternate polarity (column 1 N-up, column 2 S-up, column 3 N-up, etc.; each column also alternates by row).

3. **How to tell N-up from S-up by feel:** you can't, directly. Use one of these methods:
   - **Polarity tester device** ($10–20, has a green/red LED that flips depending on which face of a magnet is tested). For a blind builder, the tester's audible beep version (some models beep differently for N vs S) is essential.
   - **Reference magnet method:** use one labeled magnet as your reference. Touch each new magnet to the reference; opposite poles attract strongly, same poles repel. With practice you can sort 60+ per minute.
   - **Audio-tagged trays method:** keep an "N-up" tray with a small bell or rattle inside. The "S-up" tray is silent. Audio cue tells you which tray you're reaching into.

4. **Sort prepared pins into two large trays:** "N-up" tray (175 pins) and "S-up" tray (175 pins), with 25 spares of each. Put a tactile divider between the trays so you don't mix them.

5. **CRITICAL: do NOT interrupt this sorting once started.** If you pause for hours, returning to the work creates orientation drift. Plan to do the sorting in 2-hour blocks with clear "stopping points."

### Phase 2 — Bottom-shell preparation (about 90 minutes)

You print the enclosure shells before this stage. 40-cell bottom shell is a long thin tray about 25 cm × 6 cm × 3 cm.

6. **Locate the bottom shell.** It has a tactile asymmetry: the rear face has the USB-C and barrel-jack cutouts (you can feel the rectangular USB-C slot and the round barrel-jack hole). The front face is unbroken. Set it on the mat with the rear toward you.

7. **Install heat-set inserts.** The bottom shell has 12 small holes for M3 brass heat-set inserts: 4 at corners (for top-shell screws), 4 around the driver-PCB position (for driver mounting), and 4 around the coil-PCB position (for coil mounting).

8. **Heat your soldering iron to 250°C.** Hold a brass insert with the tweezers; touch the iron to the top of the insert (NOT the body). When the insert is hot enough, it sinks into the plastic by gravity. Press gently with the iron tip until the insert is flush with the plastic surface. **Be careful — the insert will be very hot for ~30 seconds after.**

9. **Verify each insert by feel:** insert flush with surface, no protrusion above. If proud, push it deeper with a cool tool (back of pliers).

10. **Repeat for all 12 inserts.** Budget 30 minutes; this is tedious but error-recoverable (a misplaced insert can be re-installed).

### Phase 3 — Driver PCB installation (about 30 minutes)

11. **Pick up the driver PCB.** It's the smaller of the two PCBs (about 20 cm × 2.2 cm). Run your finger along the surface — components-side has visible bumps; the other side is smooth. The driver PCB has 4 mounting holes, one at each corner, 5 mm in from the edge.

12. **Lower the driver PCB onto the bottom shell.** The driver-PCB standoffs (3D-printed posts inside the shell) come up from the floor and fit through the 4 mounting holes. Components-side UP. Component connectors face the rear of the shell where the USB-C and barrel-jack cutouts are.

13. **Verify alignment:** the PCB should sit flat on all 4 standoffs without rocking. The USB-C connector on the PCB should align with the rear-face cutout in the shell. If misaligned, lift and rotate 180°.

14. **Bolt the driver PCB down.** Use 4 M3 × 6 mm screws; thread them through the PCB into the heat-set inserts in the standoffs. Tighten by hand — snug, not torqued hard.

### Phase 4 — Coil PCB installation (about 30 minutes)

15. **Pick up the coil PCB.** Larger and longer than the driver PCB. The active coil region (where the coils are etched) is in the center; the bank-header connectors are along the edges. The 4 mounting holes are at corners, 3 mm in from PCB edge.

16. **Connect bank-header ribbon cables.** Each bank header on the coil PCB receives a ribbon cable that connects to a corresponding header on the driver PCB. For 40-cell there are only 1–2 banks total. Plug each ribbon connector firmly until you feel/hear it click.

17. **Lower the coil PCB onto its standoffs.** The coil-PCB standoffs are taller posts (about 11 mm tall) at the corners of the coil-PCB footprint, separate from the driver-PCB standoffs. The PCB sits ~8 mm above the driver PCB top. Components-side DOWN (the active coil region faces UP toward where the magnets will be).

18. **Verify the coil PCB sits flat** on all 4 standoffs without flexing.

19. **Bolt down with 4 M3 × 6 mm screws.**

### Phase 4.5 — Cell-housing joint assembly (split-print only, about 20 minutes)

**You do this BEFORE step 20 if the cell housing was printed as two halves.** (If you have a single-piece print, skip this phase and continue at step 20.)

a. **Locate the two cell-housing halves** in their separate trays. Each is about 165 mm long. Run your finger along the long axis to feel the asymmetric joint at one end of each half — you'll feel a step where the plate thickness drops to half-height. **The half-thickness step is the "lap" that mates with the other half.**

b. **Distinguish left from right by feel.** The left half has its lap step on the RIGHT end (the inboard end). The right half has its lap step on the LEFT end. Run a finger along the top surface of each piece — the half whose top steps DOWN at the inboard end is the left; the half whose top STAYS flush and bottom steps UP is the right.

c. **Set both halves on the work mat** with the lap-step ends facing each other.

d. **Slowly bring the two laps together.** They should slide into each other; you'll feel the half-thickness step of one half settling onto the half-thickness step of the other. If they don't mate flush, one of the steps is proud — set them aside, take a small flat file, and lightly file the proud step (about 5 strokes), then re-test. Repeat until the joint mates flush.

e. **Run your fingertips across the top surface, spanning the joint.** It should feel uniformly flat — no step, no ridge, no gap. If you feel a step, the joint isn't seated; press the halves together more firmly or check for debris in the joint.

f. **Locate the 4 joint dowel pins** (3 mm Ø × 4 mm long; smaller than the corner dowel pins you'll use later for stack assembly). They live in their own tray.

g. **Find the 4 joint dowel holes** by feel — they are 4 small holes along the centerline of the joint, distributed across the plate's short axis. Run your finger across the joint area; the holes should be obvious as small dimples.

h. **Insert each dowel pin into its hole.** Push each pin down until it sinks below the plate top surface (about 4 mm). The pins should be a snug push-fit. If any pin slides in loosely, you'll need to add a tiny dot of cyanoacrylate adhesive — but most prints are tight enough to skip this.

i. **Final joint check:** run your fingertips across the entire 310 mm length of the assembled plate. It should feel like one continuous slab. The 480 sleeves should form an unbroken grid; test-drop a gauge pin (or a single 1.5 mm pin from your spares) into the 4 sleeves nearest the joint to verify they're aligned.

The joint plate is now ready for stack assembly. **From this point, it behaves identically to a single-piece print** — the rest of the procedure is unchanged.

### Phase 5 — Pole plates and cell housing (about 30 minutes)

20. **Place the ferrite sheet** on top of the coil PCB, adhesive side down, centered over the coil region. Press firmly with your palm.

21. **Place the lower pole plate** on top of the ferrite. Align the 4 corner dowel holes with the eventual cell-housing position. The pole plate is magnetic; magnets in your tray nearby will jump if too close — stay vigilant.

22. **Place the cell housing** (assembled from joint phase, or single-piece) on top of the lower pole plate, oriented per the back/front asymmetry. The 4 corner dowel holes line up with the lower-pole-plate dowel holes.

23. **Insert corner dowel pins** through the 4 corner holes. Push each dowel down until it bottoms out — about 12 mm of insertion. The dowels lock everything below them in alignment.

### Phase 6 — Pin insertion (about 4–5 hours)

This is the longest single step. You must place 320 pins, alternating polarity, into the cell housing without making orientation mistakes.

24. **Sit comfortably** with the bottom shell + driver PCB + coil PCB + lower pole + cell housing all stacked in front of you. The "N-up" tray is to your left, "S-up" tray is to your right (or use the audio convention from step 3).

25. **Start at the top-left sleeve of the cell-housing grid.** This is column 0, row 0. By convention, this gets an N-up pin.

26. **Pick up an N-up pin from the left tray.** Drop it magnet-end-first into the sleeve. You will hear/feel a soft click as the magnet snaps to the lower pole washer below.

27. **Move to column 1, row 0.** This gets an S-up pin (alternating). Pick up an S-up pin from the right tray. Drop it in.

28. **Continue along row 0:** column 2 → N-up, column 3 → S-up, column 4 → N-up, etc. **All 80 columns of row 0.**

29. **Move to row 1.** First column of row 1 ALSO alternates — by convention, row 1 column 0 is S-up (because row 0 column 0 was N-up; checkerboard).

30. **Continue. After every 8 pins, run your finger across the row** to verify all are seated (none sticking up, all flush or recessed). If any pin is "up," push it down to "down."

31. **Take a 10-minute break every 80 pins** (every full row). This is important — fatigue causes orientation errors.

32. **After all 320 pins inserted**, run your fingertips across the entire cell-housing top surface in a careful scan. Every sleeve should have a pin in it; surface should feel uniformly "all pins down" with no protrusions.

### Phase 7 — Upper pole and top plate (15 minutes)

33. **Place the upper pole plate** down over the dowel pins. Press flat.

34. **Place the aluminum top plate** down over the dowel pins, bevelled edge to the front.

35. **Top shell goes on last.** Lower the top shell onto the bottom shell; the top-shell lip slides down inside the bottom-shell rim. The top plate sits flush in the top-shell window seat.

36. **Bolt down with 4 M3 × 16 mm corner screws** from underneath. The screws thread up through the bottom shell into the heat-set inserts in the top shell.

37. **Final tactile check:** the assembled display should feel like a long thin slab, smooth aluminum top with 320 tiny holes in a regular grid, no rough edges, no rocking on the table.

### Phase 8 — Wiring and validation

The remainder follows §6 and §7 of `04_FORTY_CELL_BUILD.md`. Connect USB-C cable to the rear of the device, plug into computer, flash firmware. Run the validation suite (G5 through G8): refresh time, stuck-pin count, tactile read by a fluent reader, and crosstalk under line-scale operation.

---

## Walkthrough 3 — Full-array (3,840 pins)

**What you're building:** a substantial tactile display, about 20 × 22 cm, with 3,840 pins in a uniform 60 × 64 grid at 2.5 mm pitch. Equivalent capability to the APH Monarch.

**Time:** 5–6 weeks of part-time work, about 80–100 hours total. Most of that is pin assembly.

**Differences from 40-cell:** much larger plates, multi-bank PCB with 24 ribbon cables, optional fan, much more pin handling.

### Phase 1 — Print the enclosure (3–4 days elapsed, mostly waiting on prints)

1. Print the bottom shell in CF-PETG. ~16 hours print time on a Bambu X1C or equivalent.
2. Print the top shell in CF-PETG. ~10 hours.
3. Print 8 pin jigs and 1 force jig in standard PETG. ~3 hours each.
4. After all prints complete, anneal the cell-housing plate (still to be printed; print it now if not done) at 80°C × 4 hours.

### Phase 2 — Pin preparation (about 30–35 hours, spread across weeks)

This is the largest single time investment. The procedure is the same as 40-cell (drill, magnet-press, sort by polarity), but for 4,000+ pins (3,840 needed plus 200 spares).

5. **Plan for 4 sessions of about 8 hours each.** Do not attempt to do all of this in one weekend; orientation drift will ruin batches.

6. **Per session:** prepare 1,000 pins (about 60 jigs × 16 pins each). Take a 10-minute break every batch. Drink water, stretch, vary your work position.

7. **Sort each session's output into the master "N-up" and "S-up" trays.** Tag each session's output with the date so if a batch later shows orientation problems, you can isolate which session.

8. **Color-code the pin tops** (optional but recommended): dip the top end of N-up pins in red Sharpie, S-up in blue. Sighted helpers can verify at a glance during assembly. Color does not affect blind assembly directly, but provides backup verification.

### Phase 3 — Bottom shell prep (about 90 minutes)

9. Same as 40-cell phase 2, but with 12 inserts (matches enclosure design). Heat each insert with the soldering iron to 250°C, install, verify flush.

### Phase 4 — Driver PCB installation (about 60 minutes)

10. The full-array driver PCB is significantly larger than the 40-cell version, ~16 × 10 cm. It carries 24 H-bridges, 48 muxes, 6 slave MCUs, 1 master MCU, 24 buck converters, 24 thermal sensors, and a fan controller.

11. Lower onto its 4 standoffs, components-side UP, USB-C connector aligned to rear-cutout. Bolt down with 4 M3 × 6 mm.

12. **The driver PCB has 24 bank headers along its edges.** These will receive ribbon cables in the next phase.

### Phase 5 — Coil PCB installation (about 90 minutes)

13. **The coil PCB is the largest and most fragile part of the build.** Handle by edges only. Solder mask is delicate — fingerprints OK, abrasion not OK.

14. **Connect ribbon cables.** 24 ribbon cables, one per bank, connect bank headers on the coil PCB to corresponding bank headers on the driver PCB. Each cable is 16-pin, ~10 cm long. Plug each end firmly until you feel a click. The ribbon cables route through the side cable channels of the bottom shell.

15. **Verify each ribbon cable is fully seated** by gently tugging — if it comes loose, re-seat. A loose cable means a dead bank later.

16. **Lower the coil PCB onto its 4 standoffs.** The standoffs are at the corners of the 180 × 190 mm coil-PCB footprint, with 3 mm inset from each edge. Components-side DOWN.

17. **Verify the PCB sits flat.** Run your hand across the top surface; no rocking, no flexing, no high spots. If any standoff is too tall, the PCB will rock — file the tall standoff down by ~0.5 mm (a small steel file works) until flat.

18. **Bolt down with 4 M3 × 6 mm corner screws.**

### Phase 6 — Pole plates, ferrite, cell housing (about 45 minutes)

19. **Apply the ferrite sheet** (160 × 170 mm) to the top surface of the coil PCB, centered over the active coil region. Adhesive side down.

20. **Place the lower pole plate** (180 × 190 mm steel) on top of the ferrite. Magnetic — keep magnets in their trays at least 60 cm away.

21. **Place the cell housing** (CF-PETG, 180 × 190 × 4 mm) on top of the lower pole plate. Orient with the back-corner asymmetry toward the rear of the shell.

22. **Insert 4 dowel pins** through the corner holes; push down until they bottom out (~12 mm).

### Phase 7 — Pin insertion (about 30–40 hours, MULTIPLE sessions)

This is the project's longest phase. **3,840 pins, alternating polarity, must be placed without errors.**

23. **Plan 4–6 sessions of about 6 hours each.** Start at column 0, row 0. The full array is 60 × 64.

24. **Use the audio-tagged tray method or polarity-tester to maintain orientation discipline.** A single mistake means a flipped pin that always responds opposite to expectations — this can ruin a row's worth of testing.

25. **Pace: about 1 pin per 4 seconds with practice.** 3,840 pins × 4 sec = ~4.3 hours of pure insertion time, but with breaks and verification, plan for 30+ hours.

26. **After every 64 pins (one full column or row), run your fingertips across to verify all pins are seated and at the same height.** Catch errors early.

27. **CRITICAL: stop and rest if you start losing track of orientation.** A single wrong pin is recoverable (pull it out, re-insert opposite); a whole batch of wrong-orientation pins requires re-disassembly.

28. **After all 3,840 pins inserted, do a final full-surface scan.** Every sleeve has a pin; all pins are at lower-detent (slight recess); no pins are missing or sticking up.

### Phase 8 — Upper pole, top plate, top shell (15 minutes)

29. Same as 40-cell phase 7. Upper pole, top plate, top shell, 4 corner screws from underneath.

### Phase 9 — Wiring, fan, and final validation

30. **Connect USB-C cable** to the rear cutout. The cable is the only external connection.

31. **Install the cooling fan** (full-array only) by snapping it into the right-side cutout and connecting its 4-pin connector to the driver PCB.

32. **Plug in.** First power-on uses a 1 A current-limited supply for safety. If something is wrong (short, stuck pin), the supply will current-limit before damaging anything.

33. **Run firmware self-test.** This cycles all 3,840 pins. You will hear a continuous clicking sound for about 1.6 seconds per refresh as banks fire.

34. **Run validation gates G8, G9, G10:** full-array refresh time, 24-hour soak test, tactile read by a fluent reader.

If all pass, you have a working 3,840-pin tactile display. **Document results in `FULL_ARRAY_RESULTS.md`.**

---

## Common failure modes and tactile diagnostics

### "A pin won't flip"

- **Run firmware self-test on that pin.** If clicking sound is heard but pin doesn't move, the pin is mechanically stuck — debris in sleeve, magnet missing, or magnet inverted.
- **Pull the top plate** (4 corner screws), upper pole, and the stuck pin out. Inspect the sleeve with a finger; it should feel smooth. If gritty, clean with a cotton swab dipped in IPA. Replace pin (use a spare) and reassemble.

### "An entire bank is dead"

- Run firmware diagnostics; identify which bank.
- Check the ribbon cable for that bank: pull it gently; if it disconnects easily, re-seat it firmly.
- If still dead after re-seating, the H-bridge IC for that bank may be damaged; replacement requires SMD rework (ask a sighted helper or accept the dead bank as ~160 dead pins out of 3,840).

### "Tactile feel feels mushy"

- Detent force is below 150 g. Possible causes: pole washers too thin, magnets dropped during assembly, or cell housing has bowed. Disassemble enough to swap to thicker pole washers; expect to add 1.5 mm pole washers vs the V1 1.0 mm.

### "Buzzing sound during refresh"

- Normal at 24-bank parallel firing. Foam-line the enclosure interior or accept the sound. If excessive, firmware can stagger banks across 0.1 ms increments, eliminating sync resonance.

---

## Closing

This walkthrough was written assuming you (the builder) cannot rely on visual inspection. Every step describes what you should *feel*. Most assembly errors are recoverable by disassembly and reassembly; the only unrecoverable error is a damaged PCB from a magnet drop or impact during stack assembly.

If a step doesn't make sense by feel, **stop and ask for help** — a sighted partner spending 5 minutes describing what they see is better than 5 hours of misassembly to redo.

Build slowly. Sleep on it between phases. Test as you go.

The single-cell prototype is the most important part of this entire process. **Do not skip ahead.**
