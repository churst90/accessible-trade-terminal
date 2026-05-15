// stack_assembly.scad
// Top-level visual assembly. Imports each part and renders the
// stack for sanity-checking dimensions and clearances.
//
// NOT for export. Use individual part files for STL/DXF generation.
//
// Render: F5 with $fn=20 for fast preview; F6 with $fn=60 for accurate.

$fn = 30;

// =================================================================
// TIER (must match across all imported scads)
// =================================================================
TIER_PARAM = "single-cell";    // Change for full assembly preview

// =================================================================
// LAYER THICKNESSES (from 05_FULL_ARRAY_BUILD.md §6)
// =================================================================
DRIVER_FLOOR_GAP      = 4.0;     // standoffs from enclosure floor to driver PCB
DRIVER_PCB_THK        = 1.6;
DRIVER_TO_COIL_GAP    = 8.0;     // standoffs from driver PCB to coil PCB
PCB_THICKNESS         = 1.6;     // coil PCB
FERRITE_THICKNESS     = 0.5;
LOWER_POLE_THICKNESS  = 0.5;
HOUSING_THICKNESS     = 4.0;
UPPER_POLE_THICKNESS  = 0.5;
TOP_PLATE_THICKNESS   = 3.0;
PIN_PROTRUSION        = 0.5;

// Plate sizing per tier
function plate_xy(t) =
    t == "single-cell" ? [30, 30]
    : t == "forty-cell" ? [280, 35]
    : t == "full-array" ? [180, 190]
    : [0, 0];

PXY = plate_xy(TIER_PARAM);

// =================================================================
// LAYER COLORS
// =================================================================
COL_PCB     = [0.10, 0.10, 0.10, 1.0];   // black solder mask
COL_COIL    = [0.85, 0.55, 0.20, 1.0];   // copper, accent
COL_FERRITE = [0.40, 0.40, 0.45, 0.7];   // gray, semi-transparent
COL_POLE    = [0.50, 0.50, 0.55, 1.0];   // steel
COL_HOUSING = [0.20, 0.20, 0.25, 1.0];   // CF-PETG (dark gray)
COL_AL      = [0.85, 0.85, 0.90, 1.0];   // anodized aluminum
COL_PIN     = [0.80, 0.80, 0.85, 1.0];

// =================================================================
// STACK
// =================================================================

z_driver     = DRIVER_FLOOR_GAP;
z_pcb        = z_driver + DRIVER_PCB_THK + DRIVER_TO_COIL_GAP;
z_ferrite    = z_pcb + PCB_THICKNESS;
z_lower_pole = z_ferrite + FERRITE_THICKNESS;
z_housing    = z_lower_pole + LOWER_POLE_THICKNESS;
z_upper_pole = z_housing + HOUSING_THICKNESS;
z_top        = z_upper_pole + UPPER_POLE_THICKNESS;
z_pin_top    = z_top + TOP_PLATE_THICKNESS + PIN_PROTRUSION;

// Driver PCB visualization (smaller footprint than coil PCB)
color([0.10, 0.10, 0.10, 1.0])
    translate([(PXY[0] - PXY[0]*0.85) / 2, (PXY[1] - PXY[1]*0.55) / 2, z_driver])
        cube([PXY[0] * 0.85, PXY[1] * 0.55, DRIVER_PCB_THK]);

color(COL_PCB)
    translate([0, 0, z_pcb])
        cube([PXY[0], PXY[1], PCB_THICKNESS]);

color(COL_FERRITE)
    translate([0, 0, z_ferrite])
        cube([PXY[0], PXY[1], FERRITE_THICKNESS]);

color(COL_POLE)
    translate([0, 0, z_lower_pole])
        cube([PXY[0], PXY[1], LOWER_POLE_THICKNESS]);

color(COL_HOUSING)
    translate([0, 0, z_housing])
        cube([PXY[0], PXY[1], HOUSING_THICKNESS]);

color(COL_POLE)
    translate([0, 0, z_upper_pole])
        cube([PXY[0], PXY[1], UPPER_POLE_THICKNESS]);

color(COL_AL)
    translate([0, 0, z_top])
        cube([PXY[0], PXY[1], TOP_PLATE_THICKNESS]);

// Sample pin (visible above top plate at center)
color(COL_PIN)
    translate([PXY[0]/2, PXY[1]/2, z_top + TOP_PLATE_THICKNESS])
        cylinder(d=1.5, h=PIN_PROTRUSION, $fn=20);

// =================================================================
// LABELS
// =================================================================

translate([PXY[0] + 5, PXY[1]/2, 0])
    rotate([0, 0, 0])
        text(str("TIER: ", TIER_PARAM), size=4);
