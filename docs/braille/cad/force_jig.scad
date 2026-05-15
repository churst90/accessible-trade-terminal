// force_jig.scad
// Force-test jig for measuring pin detent strength against finger pressure.
// Used at all gates G1, G6, G9.

PIN_DIAMETER = 1.5;
JIG_TIP_DIA = 4.0;        // mm, mimics finger contact patch on a single dot
JIG_TIP_HEIGHT = 1.0;
SHAFT_DIA = 8.0;
SHAFT_LENGTH = 50.0;
HANDLE_DIA = 20.0;
HANDLE_LENGTH = 25.0;

module force_jig() {
    union() {
        // Contact tip
        cylinder(d=JIG_TIP_DIA, h=JIG_TIP_HEIGHT, $fn=24);

        // Shaft
        translate([0, 0, JIG_TIP_HEIGHT])
            cylinder(d=SHAFT_DIA, h=SHAFT_LENGTH, $fn=20);

        // Handle (textured for grip)
        translate([0, 0, JIG_TIP_HEIGHT + SHAFT_LENGTH])
            cylinder(d=HANDLE_DIA, h=HANDLE_LENGTH, $fn=24);
    }
}

force_jig();

// =================================================================
// USAGE
// =================================================================
// 1. Place display on a kitchen scale (top plate facing up).
// 2. Tare scale to zero.
// 3. Holding the jig handle, gently press the tip down on a single
//    raised pin.
// 4. Watch the scale reading rise as you press.
// 5. Continue pressing until the pin sinks (you'll feel the "give").
// 6. Note the force at sink. This is the pin's detent hold force.
// 7. Repeat for 8-20 random pins; record results.
//
// Pass: average force >= 150 g.
//
// Print: PETG, 0.20 mm layer, 4 perimeters, 50% infill (jig must be
// stiff and dimensionally stable).
