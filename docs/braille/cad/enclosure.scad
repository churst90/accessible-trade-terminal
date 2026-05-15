// enclosure.scad
// Top + bottom enclosure shells.
//
// REDESIGNED 2026-05-01 to actually fit all internal components
// with proper mounting features, cable management, and intentional
// aesthetics. Replaces earlier minimal box.
//
// Material: PETG-CF preferred (full-array only); PETG plain at
// 40-cell tier. Print: 0.20 mm layer, 4 perimeters, 25% gyroid.
// Bottom-shell screw bosses must be solid -- modify slicer to
// 100% infill in those volumes if your slicer supports modifier
// objects.
//
// Aesthetics: chamfered top edges, recessed pin window flush with
// top plate, hidden screws (counterbored from bottom), branding
// strip at front lower edge for a logo or label.

// =================================================================
// TIER + WHICH SHELL
// =================================================================
TIER = "full-array";        // "forty-cell" or "full-array"
                            // (single-cell needs no enclosure -- skip it)
SHELL = "bottom";           // "bottom" or "top"

// =================================================================
// INTERIOR STACK -- must accommodate every component
// =================================================================

// Layer thicknesses (from 05_FULL_ARRAY_BUILD.md section 6)
PCB_THK              = 1.6;     // Coil PCB
DRIVER_PCB_THK       = 1.6;     // Driver PCB (sits below coil PCB)
DRIVER_TO_COIL_GAP   = 8.0;     // Standoff height between driver and coil PCB
                                // (room for SMD components on top of driver PCB
                                // and connectors at bottom of coil PCB)
FERRITE_THK          = 0.5;
LOWER_POLE_THK       = 0.5;
HOUSING_THK          = 4.0;
UPPER_POLE_THK       = 0.5;
TOP_PLATE_THK        = 3.0;
PIN_PROTRUSION       = 0.5;
DRIVER_FLOOR_GAP     = 4.0;     // Standoffs from enclosure floor to driver PCB
                                // (space for thru-hole leads and bottom-mounted
                                // components)

// Cumulative interior height (Z from inner enclosure floor to top of pins)
INTERIOR_HEIGHT = DRIVER_FLOOR_GAP
                + DRIVER_PCB_THK
                + DRIVER_TO_COIL_GAP
                + PCB_THK
                + FERRITE_THK
                + LOWER_POLE_THK
                + HOUSING_THK
                + UPPER_POLE_THK
                + TOP_PLATE_THK;

// =================================================================
// ENCLOSURE FOOTPRINT (border around active stack)
// =================================================================

WALL                = 3.0;      // mm, enclosure wall thickness
BORDER              = 12.0;     // mm border between top plate and enclosure edge
                                // (room for cable management + structural rigidity)
LIP_HEIGHT          = 5.0;      // top-shell lip that overlaps bottom-shell wall
LIP_GAP             = 0.4;      // print clearance for lip fit

// Top-plate dimensions per tier (matches uniform-pitch grid, plus
// border for plate handling and dowel-pin reference holes).
//   - 40-cell: 80 cols x 4 rows at 2.5 mm pitch -> active 197.5 x 7.5 mm
//             plus border -> plate 220 x 25 mm
//   - full-array: 60 cols x 64 rows at 2.5 mm pitch -> active 147.5 x 157.5 mm
//             plus border -> plate 180 x 190 mm
function top_plate_xy(t) =
    t == "forty-cell"  ? [310, 25]
    : t == "full-array" ? [180, 190]
    : [0, 0];

TP = top_plate_xy(TIER);

// Driver PCB sits below coil PCB; for full-array we make it
// smaller than coil PCB so it has clearance from edges. For 40-cell
// the driver PCB matches the coil PCB length but is narrower.
//
// MOUNTING HOLE POSITIONS (M3 clearance, drilled through PCB):
//   Driver PCB has 4 holes at corners, INSET 5 mm from each PCB edge.
//   Coil PCB has 4 holes at corners, INSET 3 mm from each PCB edge.
//
// PCB DESIGN MUST MATCH THESE. See 07_PCB_DESIGN.md for the
// matching mounting-hole spec.
function driver_pcb_xy(t) =
    t == "forty-cell"  ? [290, 22]
    : t == "full-array" ? [160, 100]
    : [0, 0];

DPCB = driver_pcb_xy(TIER);

// Outer envelope = top-plate footprint + border on all sides
ENV_X = TP[0] + 2 * BORDER;
ENV_Y = TP[1] + 2 * BORDER;

// =================================================================
// AESTHETIC PARAMETERS
// =================================================================

CHAMFER         = 4.0;      // mm chamfer on outer top edges
FOOT_INSET      = 15.0;     // mm from corner to rubber-foot indent center
FOOT_DIAMETER   = 14.0;     // mm rubber-foot recess
FOOT_DEPTH      = 2.5;
BRANDING_W      = 80;       // mm, logo strip width on front lower edge
BRANDING_H      = 6;        // mm, logo strip height
BRANDING_DEPTH  = 0.6;      // mm recessed (paint-fill or use color change)

// =================================================================
// MOUNTING FEATURES
// =================================================================

CORNER_BOSS_DIA = 9.0;          // M3 brass-insert outer boss
INSERT_DIA      = 4.5;          // M3 heat-set insert hole ID
SCREW_CLR_DIA   = 3.4;          // M3 screw clearance
SCREW_HEAD_DIA  = 6.5;          // M3 socket-head counterbore

// Bottom screws (corner-mount, accessed from underneath, hidden)
CORNER_INSET    = 8.0;
NUM_CORNERS     = 4;

// Driver-PCB standoff posts (hold the driver PCB above the floor)
DRIVER_STANDOFF_H   = DRIVER_FLOOR_GAP;
DRIVER_STANDOFF_DIA = 6.0;
DRIVER_STANDOFF_INSERT_DIA = 4.5;       // M3 heat-set
DRIVER_PCB_HOLE_INSET = 5.0;            // mm from PCB corner

// Coil-PCB standoff posts (separate posts, taller; hold coil PCB above driver PCB)
COIL_STANDOFF_H = DRIVER_FLOOR_GAP + DRIVER_PCB_THK + DRIVER_TO_COIL_GAP;
COIL_STANDOFF_DIA = 6.0;

// Top plate registration nest (recess in top shell to receive top plate)
TOP_PLATE_NEST_DEPTH = TOP_PLATE_THK + 0.3; // small clearance

// Cable channel cutouts (sides of bottom shell, for ribbon cables to exit
// from coil PCB bank headers down past driver PCB)
CABLE_CHANNEL_W = 18.0;
CABLE_CHANNEL_H = DRIVER_TO_COIL_GAP + PCB_THK + 1; // height of channel
NUM_CHANNELS_PER_SIDE = 2;          // ribbon cables exit through these

// =================================================================
// PORT CUTOUTS (rear face)
// =================================================================

USB_C_W         = 9.5;
USB_C_H         = 4.5;
USB_C_FROM_BOTTOM = 8.0;            // mm above enclosure floor

BARREL_JACK_DIA = 8.5;
BARREL_FROM_BOTTOM = 8.0;
BARREL_FROM_USB    = 18.0;

// Ventilation slots on rear face (full-array only)
VENT_SLOT_W = 30;
VENT_SLOT_H = 3;
VENT_ROWS   = 3;

// Fan port (full-array only)
FAN_SIZE    = 40;
FAN_OFFSET_FROM_BOTTOM = 6;

// =================================================================
// MAIN
// =================================================================

if (SHELL == "bottom") bottom_shell();
else                   top_shell();

// =================================================================
// BOTTOM SHELL
// =================================================================

module bottom_shell() {
    bot_h = INTERIOR_HEIGHT - TOP_PLATE_THK + WALL;
    // (top plate is captured in TOP shell, so bottom shell only needs
    // to contain everything UP TO the upper pole plate.)

    difference() {
        union() {
            // Outer body
            outer_body(bot_h);

            // Internal posts (standoffs) added before subtraction
            internal_standoffs();
        }

        // Hollow interior
        translate([WALL, WALL, WALL])
            cube([ENV_X - 2*WALL, ENV_Y - 2*WALL, bot_h + 1]);

        // Standoff insert holes (drilled INTO standoffs after they're
        // created above; this difference subtracts them properly)
        standoff_insert_holes();

        // Corner inserts for top-shell screws (heat-set inserts)
        corner_insert_holes(bot_h);

        // Rear-face ports
        rear_face_ports(bot_h);

        // Cable channels (sides)
        cable_channels(bot_h);

        // Bottom rubber-foot indents
        for (x = [FOOT_INSET, ENV_X - FOOT_INSET],
             y = [FOOT_INSET, ENV_Y - FOOT_INSET]) {
            translate([x, y, -0.1])
                cylinder(d=FOOT_DIAMETER, h=FOOT_DEPTH, $fn=36);
        }

        // Bottom screw access (counterbored from outside, into corner bosses)
        for (x = [CORNER_INSET, ENV_X - CORNER_INSET],
             y = [CORNER_INSET, ENV_Y - CORNER_INSET]) {
            translate([x, y, -0.1])
                cylinder(d=SCREW_CLR_DIA, h=bot_h+1, $fn=24);
            translate([x, y, -0.1])
                cylinder(d=SCREW_HEAD_DIA, h=2.6, $fn=24);
        }

        // Branding strip on front face (recessed)
        translate([(ENV_X - BRANDING_W) / 2,
                   -0.1,
                   8])
            cube([BRANDING_W, BRANDING_DEPTH + 0.1, BRANDING_H]);
    }
}

module outer_body(h) {
    // Chamfered rectangular body. Chamfer applied only at top edge.
    hull() {
        translate([0, 0, 0])
            cube([ENV_X, ENV_Y, h - CHAMFER]);
        translate([CHAMFER/2, CHAMFER/2, h - 0.1])
            cube([ENV_X - CHAMFER, ENV_Y - CHAMFER, 0.1]);
    }
}

module internal_standoffs() {
    // Driver PCB standoffs (4 corners of driver PCB)
    dpcb_x_off = (ENV_X - DPCB[0]) / 2;
    dpcb_y_off = (ENV_Y - DPCB[1]) / 2;

    for (sx = [DRIVER_PCB_HOLE_INSET, DPCB[0] - DRIVER_PCB_HOLE_INSET],
         sy = [DRIVER_PCB_HOLE_INSET, DPCB[1] - DRIVER_PCB_HOLE_INSET]) {
        translate([dpcb_x_off + sx, dpcb_y_off + sy, WALL])
            cylinder(d=DRIVER_STANDOFF_DIA, h=DRIVER_STANDOFF_H, $fn=24);
    }

    // Coil PCB standoffs (offset to clear driver PCB; placed at coil PCB corners)
    cpcb_x_off = (ENV_X - TP[0]) / 2;       // coil PCB ~ same footprint as top plate
    cpcb_y_off = (ENV_Y - TP[1]) / 2;

    for (sx = [3, TP[0] - 3], sy = [3, TP[1] - 3]) {
        translate([cpcb_x_off + sx, cpcb_y_off + sy, WALL])
            cylinder(d=COIL_STANDOFF_DIA, h=COIL_STANDOFF_H, $fn=24);
    }
}

module standoff_insert_holes() {
    dpcb_x_off = (ENV_X - DPCB[0]) / 2;
    dpcb_y_off = (ENV_Y - DPCB[1]) / 2;

    for (sx = [DRIVER_PCB_HOLE_INSET, DPCB[0] - DRIVER_PCB_HOLE_INSET],
         sy = [DRIVER_PCB_HOLE_INSET, DPCB[1] - DRIVER_PCB_HOLE_INSET]) {
        translate([dpcb_x_off + sx, dpcb_y_off + sy,
                   WALL + DRIVER_STANDOFF_H - 6])
            cylinder(d=DRIVER_STANDOFF_INSERT_DIA, h=6.5, $fn=20);
    }

    cpcb_x_off = (ENV_X - TP[0]) / 2;
    cpcb_y_off = (ENV_Y - TP[1]) / 2;
    for (sx = [3, TP[0] - 3], sy = [3, TP[1] - 3]) {
        translate([cpcb_x_off + sx, cpcb_y_off + sy,
                   WALL + COIL_STANDOFF_H - 6])
            cylinder(d=DRIVER_STANDOFF_INSERT_DIA, h=6.5, $fn=20);
    }
}

module corner_insert_holes(h) {
    for (x = [CORNER_INSET, ENV_X - CORNER_INSET],
         y = [CORNER_INSET, ENV_Y - CORNER_INSET]) {
        translate([x, y, h - 6])
            cylinder(d=INSERT_DIA, h=6.5, $fn=20);
    }
}

module rear_face_ports(h) {
    // Rear face = +Y wall
    rear_y = ENV_Y - WALL - 0.1;

    // USB-C
    translate([(ENV_X - USB_C_W) / 2, rear_y, USB_C_FROM_BOTTOM])
        cube([USB_C_W, WALL + 0.2, USB_C_H]);

    // Barrel jack
    translate([(ENV_X - USB_C_W) / 2 - BARREL_FROM_USB,
               rear_y, BARREL_FROM_BOTTOM + BARREL_JACK_DIA / 2])
        rotate([-90, 0, 0])
            cylinder(d=BARREL_JACK_DIA, h=WALL + 0.2, $fn=32);

    // Vent slots (full-array only)
    if (TIER == "full-array") {
        for (i = [0:VENT_ROWS-1]) {
            translate([(ENV_X - VENT_SLOT_W) / 2,
                       rear_y, h - 25 - i * 6])
                cube([VENT_SLOT_W, WALL + 0.2, VENT_SLOT_H]);
        }
    }

    // Fan port (full-array only) on right face
    if (TIER == "full-array") {
        translate([ENV_X - WALL - 0.1,
                   (ENV_Y - FAN_SIZE) / 2,
                   FAN_OFFSET_FROM_BOTTOM])
            cube([WALL + 0.2, FAN_SIZE, FAN_SIZE]);
        // Fan mounting holes (4 corners)
        fan_screw_inset = 4;
        for (fx = [fan_screw_inset, FAN_SIZE - fan_screw_inset],
             fy = [fan_screw_inset, FAN_SIZE - fan_screw_inset]) {
            translate([ENV_X - WALL - 0.1,
                       (ENV_Y - FAN_SIZE) / 2 + fy,
                       FAN_OFFSET_FROM_BOTTOM + fx])
                rotate([0, 90, 0])
                    cylinder(d=3.2, h=WALL + 0.2, $fn=20);
        }
    }
}

module cable_channels(h) {
    // Cable channels on left and right walls; ribbon cables from coil
    // PCB bank headers exit through these. Place at coil-PCB Z height.
    cable_z = WALL + COIL_STANDOFF_H - PCB_THK - 1;

    spacing = (ENV_Y - 2 * BORDER) / (NUM_CHANNELS_PER_SIDE + 1);

    for (i = [1:NUM_CHANNELS_PER_SIDE]) {
        y_pos = BORDER + i * spacing - CABLE_CHANNEL_W / 2;
        // Left wall
        translate([-0.1, y_pos, cable_z])
            cube([WALL + 0.2, CABLE_CHANNEL_W, CABLE_CHANNEL_H]);
        // Right wall (skip if fan is on this side)
        if (TIER != "full-array") {
            translate([ENV_X - WALL - 0.1, y_pos, cable_z])
                cube([WALL + 0.2, CABLE_CHANNEL_W, CABLE_CHANNEL_H]);
        }
    }
}

// =================================================================
// TOP SHELL
// =================================================================

module top_shell() {
    // Top shell carries the aluminum top plate in a flush nest;
    // overlaps the bottom shell with a lip; chamfered top edges
    // for aesthetics.

    top_h = WALL + LIP_HEIGHT;

    difference() {
        union() {
            // Outer body (chamfered)
            outer_body_top(top_h);

            // Lip extending DOWN into bottom shell
            translate([WALL + LIP_GAP, WALL + LIP_GAP, -LIP_HEIGHT])
                difference() {
                    cube([ENV_X - 2 * (WALL + LIP_GAP),
                          ENV_Y - 2 * (WALL + LIP_GAP),
                          LIP_HEIGHT]);
                    translate([WALL, WALL, -0.1])
                        cube([ENV_X - 2 * (WALL + LIP_GAP) - 2*WALL,
                              ENV_Y - 2 * (WALL + LIP_GAP) - 2*WALL,
                              LIP_HEIGHT + 1]);
                }
        }

        // Top-plate window (cut all the way through)
        win_x_off = (ENV_X - TP[0]) / 2;
        win_y_off = (ENV_Y - TP[1]) / 2;

        translate([win_x_off, win_y_off, -LIP_HEIGHT - 1])
            cube([TP[0], TP[1], top_h + LIP_HEIGHT + 2]);

        // Top-plate registration nest -- a step that the aluminum top
        // plate sits flush in (the window is the FULL top-plate footprint
        // but we add a 2 mm shoulder around the inner edge of the window
        // for the plate to rest on).
        // Implementation: the window above is full size; we add a chamfered
        // edge around the top of the window for the top plate to seat.
        plate_seat_w = 2;       // mm of seat lip
        translate([win_x_off - plate_seat_w,
                   win_y_off - plate_seat_w,
                   top_h - TOP_PLATE_THK - 0.3])
            cube([TP[0] + 2 * plate_seat_w,
                  TP[1] + 2 * plate_seat_w,
                  TOP_PLATE_THK + 0.5]);

        // Corner screw clearance holes (drilled from outside down through
        // top shell into bottom shell heat-set inserts)
        for (x = [CORNER_INSET, ENV_X - CORNER_INSET],
             y = [CORNER_INSET, ENV_Y - CORNER_INSET]) {
            translate([x, y, -LIP_HEIGHT - 1])
                cylinder(d=SCREW_CLR_DIA, h=top_h + LIP_HEIGHT + 2, $fn=24);
        }

        // Decorative chamfered upper edges (already in outer_body_top)
        // -- nothing to subtract here; chamfer is built in.
    }
}

module outer_body_top(h) {
    // Chamfered top with an inset on all four upper edges for aesthetics
    hull() {
        cube([ENV_X, ENV_Y, h - CHAMFER]);
        translate([CHAMFER/2, CHAMFER/2, h - 0.1])
            cube([ENV_X - CHAMFER, ENV_Y - CHAMFER, 0.1]);
    }
}

// =================================================================
// PRINT NOTES
// =================================================================
//
// PRINT BOTH SHELLS SEPARATELY by changing SHELL above and re-rendering.
//
// Bottom shell:
//   - Orient: open side UP (so internal standoffs and inserts print clean)
//   - Layer: 0.20 mm
//   - Walls: 4 perimeters
//   - Infill: 25 percent gyroid
//   - Supports: minimal (USB-C and barrel jack cutouts may need supports;
//     turn on tree supports only inside cable channels and rear-face ports)
//   - Filament: PETG-CF for full-array (warpage at 200x220 mm matters);
//     PETG plain is fine for 40-cell.
//   - Print time:
//       40-cell:    ~9 hours
//       full-array: ~16 hours
//
// Top shell:
//   - Orient: top-side UP (chamfered face up). Window cut prints a
//     bridging gap; if your printer struggles with bridges, orient
//     window-side up instead and add supports.
//   - Layer: 0.20 mm; for visible chamfered face, drop to 0.16 mm for
//     smoother surface
//   - Walls: 4 perimeters
//   - Infill: 25 percent
//   - Print time:
//       40-cell:    ~6 hours
//       full-array: ~10 hours
//
// FINISHING:
//   - Install M3 brass heat-set inserts at all corner-screw and
//     standoff locations (soldering iron at 250C, push insert flush).
//   - Spray top shell with primer + two coats of matte automotive paint
//     for a finished look (matte black is the project default).
//   - Branding strip: paint-fill the recess with contrasting color, or
//     skip if you don't want branding.
//
// FIT CHECK:
//   - Before final assembly, dry-fit bottom shell with all PCBs and the
//     full pin stack. Verify the top-plate plane sits flush with the
//     top-shell window seat when the lip mates with the bottom shell.
//   - If too tight: increase LIP_GAP from 0.4 to 0.5 mm.
//   - If lid wobbles: reduce LIP_GAP to 0.3 mm.
