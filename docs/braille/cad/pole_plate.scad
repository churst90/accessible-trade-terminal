// pole_plate.scad
// Steel pole-washer plate.
// Output: DXF for laser-cut service.
//
// SPEC NOTES:
//   - Material: 1018 soft iron sheet, mu_r ~5000.
//   - Thickness: 0.5 mm (V1) -- order also in 1.0 and 1.5 mm for single-cell
//     comparison (see 03_SINGLE_CELL_BUILD.md gate G1).
//   - Two plates per stack: upper and lower pole. Same DXF for both.
//   - Surface: oil or zinc clear chromate (1018 rusts otherwise).
//   - Laser cut acceptable in 0.5-1.5 mm sheet -- kerf small enough.

// =================================================================
// TIER SELECTION
// =================================================================
TIER = "single-cell";
// TIER = "forty-cell";
// TIER = "full-array";

// =================================================================
// CONSTANTS
// =================================================================
WASHER_HOLE_DIA = 1.5;          // mm; same as pin OD; pin passes through, magnet
                                 // (1.0 mm) seats on the iron annulus around the
                                 // pin. Magnet smaller than washer hole intentionally
                                 // so it can pass through; the washer's OUTER
                                 // boundary is irrelevant since the entire plate
                                 // IS the iron return path.
PIN_PITCH = 2.5;
CELL_GAP = 6.0;
PLATE_THICKNESS = 0.5;          // mm (informational; DXF is 2D)
DOWEL_HOLE = 3.05;              // mm, H8 (laser tolerance)
DOWEL_HOLE_INSET = 5.0;
EDGE_MARGIN = 5.0;

// Uniform 2.5 mm pitch on both axes for all tiers (graphics-capable).
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

projection(cut=true) pole_plate_3d();

module pole_plate_3d() {
    difference() {
        cube([PLATE_X, PLATE_Y, PLATE_THICKNESS]);

        x_offset = (PLATE_X - active_x()) / 2;
        y_offset = (PLATE_Y - active_y()) / 2;

        // Pin holes (pin passes through; washer function is the iron
        // return path, not a blocking aperture)
        for (col = [0:COLS-1], row = [0:ROWS-1]) {
            translate([x_offset + pin_x(col), y_offset + pin_y(row), -0.1])
                cylinder(d=WASHER_HOLE_DIA, h=PLATE_THICKNESS+0.2, $fn=16);
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
// VENDOR NOTES
// =================================================================
// Material: 1018 cold-rolled steel sheet.
// Thickness: 0.5 mm (V1 default). For single-cell V2 testing, also
//            order at 1.0 and 1.5 mm.
// Cutting: laser OK at 0.5-1.5 mm.
// Surface: oil-coat for storage. For final assembly, zinc clear
//          chromate or passivate to prevent rust.
// Tolerance: laser kerf ~0.10 mm; hole tol +0.10 / -0.05.
// Quantity per stack: 2 (upper + lower).
