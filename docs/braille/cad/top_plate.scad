// top_plate.scad
// Aluminum top plate (drilled, anodized).
// Output: 2D outline + holes; export as DXF for SendCutSend or OshCut "drilled" service.
//
// SPEC NOTES:
//   - Material: 6061-T6 aluminum, 3 mm thick.
//   - Hole tolerance: H8 (1.6 +0.025 / 0).
//   - Surface: Class 2 black anodize, type II clear (NOT type III; type III
//     is too rough on skin).
//   - DO NOT laser-cut 1.6 mm holes in 3 mm aluminum -- specify drill or punch.

// =================================================================
// TIER SELECTION
// =================================================================
TIER = "single-cell";
// TIER = "forty-cell";
// TIER = "full-array";

// =================================================================
// CONSTANTS
// =================================================================
PIN_HOLE_DIA = 1.6;             // mm, pin slides through
PIN_PITCH = 2.5;                // mm
CELL_GAP = 6.0;                 // mm (40-cell only)
PLATE_THICKNESS = 3.0;          // mm (informational; DXF is 2D)
DOWEL_HOLE = 3.025;             // mm, H7 reamed for dowel reference
DOWEL_HOLE_INSET = 5.0;         // mm
EDGE_MARGIN = 5.0;              // mm border between active area and plate edge

// =================================================================
// TIER PARAMS
// =================================================================
// Uniform 2.5 mm pitch on both axes for all tiers (graphics-capable).
// See cell_housing.scad note on USE_CELL_GAPS.
function tier_params(t) =
    t == "single-cell"  ? [2, 4, 30, 30, false]
    : t == "forty-cell" ? [120, 4, 310, 25, false]
    : t == "full-array" ? [60, 64, 180, 190, false]
    : [0, 0, 0, 0, false];

p = tier_params(TIER);
COLS    = p[0];
ROWS    = p[1];
PLATE_X = p[2];
PLATE_Y = p[3];
USE_CELL_GAPS = p[4];

function pin_x(col) =
    USE_CELL_GAPS
        ? floor(col/2) * (PIN_PITCH + CELL_GAP) + (col % 2) * PIN_PITCH
        : col * PIN_PITCH;

function pin_y(row) = row * PIN_PITCH;

function active_x() =
    USE_CELL_GAPS
        ? floor(COLS/2) * (PIN_PITCH + CELL_GAP) - CELL_GAP + PIN_PITCH
        : (COLS - 1) * PIN_PITCH;

function active_y() = (ROWS - 1) * PIN_PITCH;

// =================================================================
// 2D PROJECTION FOR DXF
// =================================================================

projection(cut=true) top_plate_3d();

module top_plate_3d() {
    difference() {
        cube([PLATE_X, PLATE_Y, PLATE_THICKNESS]);

        x_offset = (PLATE_X - active_x()) / 2;
        y_offset = (PLATE_Y - active_y()) / 2;

        // Pin holes
        for (col = [0:COLS-1], row = [0:ROWS-1]) {
            translate([x_offset + pin_x(col), y_offset + pin_y(row), -0.1])
                cylinder(d=PIN_HOLE_DIA, h=PLATE_THICKNESS+0.2, $fn=20);
        }

        // Dowel reference holes
        for (x = [DOWEL_HOLE_INSET, PLATE_X - DOWEL_HOLE_INSET],
             y = [DOWEL_HOLE_INSET, PLATE_Y - DOWEL_HOLE_INSET]) {
            translate([x, y, -0.1])
                cylinder(d=DOWEL_HOLE, h=PLATE_THICKNESS+0.2, $fn=20);
        }
    }
}

// =================================================================
// VENDOR NOTES (paste into SendCutSend custom-spec field)
// =================================================================
// Material: 6061-T6 aluminum, 3 mm.
// Holes: drill or punch at 1.60 mm, tolerance H8 (+0.025 / 0).
// Dowel holes: ream to 3.025 mm, tolerance H7.
// Finish: Class 2 black anodize, matte.
// Quantity: 1 (and confirm "drilled" not "lasered" for 1.6 mm holes).
