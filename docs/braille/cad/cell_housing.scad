// cell_housing.scad
// Parametric pin-sleeve plate. Used at all three tiers.
//
// Render quality: F5 (preview) for visual check; F6 (full) before export.
// Output: STL ready for FDM print in CF-PETG.
// Print parameters: see docs/braille/03_SINGLE_CELL_BUILD.md or 05_FULL_ARRAY_BUILD.md.
//
// SPLIT-PRINT MODE (forty-cell only):
//   The 40-cell housing is 310 mm long, exceeding most consumer
//   printer beds. Set SPLIT = true to produce one of two halves.
//   Set HALF = "left" or HALF = "right" to choose which half to render.
//   Each half is ~165 mm long, fits on a 180 mm Y-axis printer.
//   Halves are joined by a stepped lap joint with 4 dowel pins
//   (printed as part of the geometry; details below).

// =================================================================
// TIER SELECTION (uncomment one)
// =================================================================
TIER = "forty-cell";          // "single-cell", "forty-cell", "full-array"

// =================================================================
// SPLIT-PRINT (forty-cell only; ignored for other tiers)
// =================================================================
SPLIT = true;                 // true = print in 2 halves
HALF  = "left";               // "left" or "right" (render one at a time)

// =================================================================
// CONSTANTS — design-locked, do not change without measurement
// =================================================================
PIN_DIAMETER = 1.5;
SLEEVE_BORE = 1.55;
PIN_PITCH = 2.5;
PLATE_THICKNESS = 4.0;
DOWEL_HOLE = 3.05;
DOWEL_HOLE_INSET = 5.0;

// Split-joint geometry (forty-cell only, when SPLIT == true)
SPLIT_LAP_LENGTH = 20.0;      // mm of overlap in the lap joint (each half
                              // contributes 10 mm of overlap)
SPLIT_LAP_THICKNESS = PLATE_THICKNESS / 2;  // step depth = half plate thickness
SPLIT_DOWEL_DIA = 3.05;       // 3 mm dowel + clearance for joint dowels
SPLIT_NUM_DOWELS = 4;         // 4 dowels along the joint Y axis (across plate width)

// =================================================================
// TIER-DEPENDENT PARAMETERS
// All tiers use uniform 2.5 mm pitch in both axes (graphics-capable).
// Forty-cell: 120 cols x 4 rows uniform; reads as 40 cells in
// firmware text mode (2 dot cols + 1 blank col per cell, 120/3 = 40).
// =================================================================
function tier_params(t) =
    t == "single-cell"  ? [2, 4, 30, 30]
    : t == "forty-cell" ? [120, 4, 310, 25]
    : t == "full-array" ? [60, 64, 180, 190]
    : [0, 0, 0, 0];

params = tier_params(TIER);
COLS    = params[0];
ROWS    = params[1];
PLATE_X = params[2];
PLATE_Y = params[3];

// Split is only valid for forty-cell; force false elsewhere
DO_SPLIT = SPLIT && (TIER == "forty-cell");

// =================================================================
// GEOMETRY
// =================================================================

if (DO_SPLIT)
    cell_housing_half(HALF);
else
    cell_housing_whole();

// -----------------------------------------------------------------
// Whole (single-piece) cell housing
// -----------------------------------------------------------------
module cell_housing_whole() {
    difference() {
        cube([PLATE_X, PLATE_Y, PLATE_THICKNESS]);

        sleeve_x_offset = (PLATE_X - active_x()) / 2;
        sleeve_y_offset = (PLATE_Y - active_y()) / 2;

        for (col = [0:COLS-1], row = [0:ROWS-1])
            translate([sleeve_x_offset + sleeve_x_at(col),
                       sleeve_y_offset + sleeve_y_at(row),
                       -0.1])
                cylinder(d=SLEEVE_BORE, h=PLATE_THICKNESS+0.2, $fn=24);

        for (x = [DOWEL_HOLE_INSET, PLATE_X - DOWEL_HOLE_INSET],
             y = [DOWEL_HOLE_INSET, PLATE_Y - DOWEL_HOLE_INSET])
            translate([x, y, -0.1])
                cylinder(d=DOWEL_HOLE, h=PLATE_THICKNESS+0.2, $fn=24);
    }
}

// -----------------------------------------------------------------
// Half (split-print) cell housing
//
//   Both halves share the SAME outer profile and sleeve grid, but
//   each half includes only its half of the sleeves and a stepped
//   lap joint at the inboard edge.
//
//   The split is at X = PLATE_X / 2 = 155 mm.
//
//   Lap joint geometry, viewed in cross-section (X axis horizontal,
//   Z axis vertical):
//
//     Left half (HALF = "left"):
//       outer surface at top:   ┌─────────────┐
//                               │             ├──────┐  ← top step
//                               │             │ step │
//                               │             │      │
//       outer surface at bot:   └─────────────┴──────┘
//                                       split center →┤
//
//     Right half (HALF = "right"):
//       outer surface at top:          ┌──────┬───────────┐
//                                      │      │           │
//                                      │ step │           │
//       outer surface at bot:   ┌──────┴──────┴───────────┘
//                               ┤← split center
//
//   When mated, the two steps interlock.  Dowel pins through the
//   joint Y direction lock against shear.
// -----------------------------------------------------------------
module cell_housing_half(which) {
    half_x = PLATE_X / 2;          // = 155 mm for 40-cell
    is_left = (which == "left");

    // Body of this half (with no joint geometry yet):
    //   left half occupies X in [0, half_x + lap/2]
    //   right half occupies X in [half_x - lap/2, PLATE_X]
    body_x_start = is_left ? 0 : half_x - SPLIT_LAP_LENGTH/2;
    body_x_end   = is_left ? half_x + SPLIT_LAP_LENGTH/2 : PLATE_X;
    body_x_len   = body_x_end - body_x_start;

    difference() {
        union() {
            // Main body (full thickness)
            translate([body_x_start, 0, 0])
                cube([body_x_len, PLATE_Y, PLATE_THICKNESS]);
        }

        // Subtract the half of the lap joint that this half doesn't occupy:
        //   left half: remove TOP half of plate from x = half_x to body end
        //   right half: remove BOTTOM half of plate from body start to half_x
        if (is_left) {
            translate([half_x, -0.1, SPLIT_LAP_THICKNESS])
                cube([SPLIT_LAP_LENGTH/2 + 0.1, PLATE_Y + 0.2,
                      SPLIT_LAP_THICKNESS + 0.1]);
        } else {
            translate([half_x - SPLIT_LAP_LENGTH/2 - 0.1, -0.1, -0.1])
                cube([SPLIT_LAP_LENGTH/2 + 0.1, PLATE_Y + 0.2,
                      SPLIT_LAP_THICKNESS + 0.1]);
        }

        // Sleeves: only render those within this half's X range
        sleeve_x_offset = (PLATE_X - active_x()) / 2;
        sleeve_y_offset = (PLATE_Y - active_y()) / 2;
        for (col = [0:COLS-1], row = [0:ROWS-1]) {
            sx = sleeve_x_offset + sleeve_x_at(col);
            // Only include sleeves whose center is in this half's body
            if (sx >= body_x_start && sx <= body_x_end) {
                translate([sx, sleeve_y_offset + sleeve_y_at(row), -0.1])
                    cylinder(d=SLEEVE_BORE, h=PLATE_THICKNESS+0.2, $fn=24);
            }
        }

        // Original 4-corner dowel holes -- only the 2 corners on this half
        if (is_left) {
            for (y = [DOWEL_HOLE_INSET, PLATE_Y - DOWEL_HOLE_INSET])
                translate([DOWEL_HOLE_INSET, y, -0.1])
                    cylinder(d=DOWEL_HOLE, h=PLATE_THICKNESS+0.2, $fn=24);
        } else {
            for (y = [DOWEL_HOLE_INSET, PLATE_Y - DOWEL_HOLE_INSET])
                translate([PLATE_X - DOWEL_HOLE_INSET, y, -0.1])
                    cylinder(d=DOWEL_HOLE, h=PLATE_THICKNESS+0.2, $fn=24);
        }

        // Joint dowel holes -- 4 dowels along Y, in the lap region.
        // Both halves share these so dowels stitch the joint.
        // Dowels are at the joint center line and pass through the
        // FULL plate thickness.
        for (i = [0:SPLIT_NUM_DOWELS-1]) {
            y_pos = PLATE_Y * (i + 1) / (SPLIT_NUM_DOWELS + 1);
            translate([half_x, y_pos, -0.1])
                cylinder(d=SPLIT_DOWEL_DIA, h=PLATE_THICKNESS+0.2, $fn=24);
        }
    }
}

// -----------------------------------------------------------------
// Helper functions
// -----------------------------------------------------------------
function sleeve_x_at(col) = col * PIN_PITCH;
function sleeve_y_at(row) = row * PIN_PITCH;
function active_x() = (COLS - 1) * PIN_PITCH;
function active_y() = (ROWS - 1) * PIN_PITCH;

// =================================================================
// NOTES FOR THE FABRICATOR
// =================================================================
//
// SINGLE-PIECE PRINT (single-cell or full-array):
//   1. CF-PETG mandatory at full-array size (warpage).
//   2. Sleeves print vertical for circular bores (Z = print Z-axis).
//   3. Anneal at 80 C, 2 h for single-cell, 4 h for full-array.
//   4. Verify bore tolerance with 1.5 mm pin gauge.
//
// SPLIT PRINT (forty-cell):
//   1. Render each half separately by setting HALF = "left" then
//      re-rendering with HALF = "right". Export each as STL.
//   2. Print each half on a printer with at least 175 mm Y axis
//      (each half is ~165 mm long).
//   3. Use the same print parameters for both halves -- consistency
//      between the two prints is critical for clean lap mating.
//   4. Anneal both halves together at 80 C for 4 hours BEFORE
//      assembling, so any post-print shrinkage happens before mating.
//   5. JOINT ASSEMBLY:
//      a. Dry-fit the two halves at the lap joint. The stepped
//         shoulders should mate flush; if they don't seat fully,
//         lightly file the proud step until they do (file gently
//         and check fit repeatedly -- aim for snug, not tight).
//      b. Place 4 dowel pins (3 mm Ø x 4 mm long stainless or steel)
//         into the 4 dowel holes through the joint. They should be
//         a snug push-fit; if loose, use a tiny dot of cyanoacrylate.
//      c. Press the halves together. Verify with a straightedge
//         that the assembled plate is flat (no kink at the joint)
//         and that the sleeve grid is continuous (1.5 mm gauge pin
//         drops cleanly into all sleeves spanning the joint).
//      d. The lap joint provides shear strength; the dowels provide
//         alignment. The joint is removable and reusable -- no glue
//         needed unless dowels are loose.
//
// VERIFICATION AFTER JOINT ASSEMBLY:
//   - Total length 310 mm +/- 0.3 mm, measured corner-to-corner.
//   - Joint kink: check with straightedge across the long axis.
//     Should be flat to within 0.1 mm.
//   - Sleeve continuity: gauge pin should drop into ALL 480 sleeves.
//     Pay special attention to the sleeves nearest the joint.
//
// IF A HALF WARPS DURING PRINT:
//   - Anneal that half alone (80 C, 4 hours) to relieve stress;
//     re-test fit.
//   - If warp persists, re-print that half with print bed at 85 C
//     and add a brim. CF-PETG is dimensionally stable but sensitive
//     to bed adhesion at this aspect ratio.
