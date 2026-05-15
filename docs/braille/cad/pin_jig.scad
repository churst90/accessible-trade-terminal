// pin_jig.scad
// Assembly jig for pin preparation. Holds 16 pins simultaneously
// for batch drilling and magnet press-fit.
//
// Reduces magnet handling to a manageable rate (5 min per batch of 16
// instead of 30 sec per pin). Critical for full-array build where
// 3,840 pins must be assembled in alternating polarity.

PIN_DIAMETER = 1.5;
JIG_PITCH = 8.0;          // mm, generous spacing for finger access
PIN_HOLES_X = 4;
PIN_HOLES_Y = 4;
JIG_THICKNESS = 12.0;     // mm, tall enough to hold pin upright through drilling
PIN_HOLE_DEPTH = 9.0;     // mm, full pin length
TRAY_BORDER = 8.0;
TRAY_LIP = 3.0;
HANDLE_LENGTH = 30.0;

WIDTH = (PIN_HOLES_X - 1) * JIG_PITCH + 2 * TRAY_BORDER;
LENGTH = (PIN_HOLES_Y - 1) * JIG_PITCH + 2 * TRAY_BORDER + HANDLE_LENGTH;

module pin_jig() {
    difference() {
        union() {
            // Tray base
            cube([WIDTH, LENGTH, JIG_THICKNESS]);
            // Lip on three sides (so pins don't roll out)
            translate([0, 0, JIG_THICKNESS])
                cube([WIDTH, LENGTH - HANDLE_LENGTH, TRAY_LIP]);
        }

        // Pin pockets
        for (col = [0:PIN_HOLES_X-1], row = [0:PIN_HOLES_Y-1])
            translate([TRAY_BORDER + col * JIG_PITCH,
                       TRAY_BORDER + row * JIG_PITCH,
                       JIG_THICKNESS - PIN_HOLE_DEPTH])
                cylinder(d=PIN_DIAMETER + 0.2, h=PIN_HOLE_DEPTH + 1, $fn=20);

        // Magnet drop slot (visual aid: highlights blind-hole side)
        translate([WIDTH/2 - 3, LENGTH - HANDLE_LENGTH + 5, JIG_THICKNESS - 1])
            cube([6, 20, 2]);
    }

    // Label
    translate([WIDTH/2 - 8, LENGTH - HANDLE_LENGTH/2, JIG_THICKNESS])
        linear_extrude(1)
            text("16-PIN JIG", size=4, halign="center", valign="center");
}

pin_jig();

// =================================================================
// USAGE
// =================================================================
// 1. Print 4 jigs (use them in rotation; one for cutting, one for
//    drilling, two for magnet press).
// 2. Drop 16 cut pin blanks into pockets, blind-hole-side up.
// 3. Drill 1.0 mm blind hole in each (drill press, 1500 RPM).
// 4. Move jig to magnet press station.
// 5. Pick magnets one at a time with brass tweezers, drop into pocket
//    with desired polarity facing up. (Use magnetic-polarity tester
//    if magnets are visually identical.)
// 6. Press all 16 magnets simultaneously with a flat plate (3D-print
//    a press tool or use a glass slide).
// 7. Tap jig upside down over a tray; pins drop out, magnets stay
//    in.
// 8. Sort pins into N-up and S-up trays.
//
// Print: PETG, 0.20 mm layer, 3 perimeters, 30% infill.
