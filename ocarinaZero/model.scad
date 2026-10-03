// Baseline CAD geometry, with all dimensions in millimetres; +Y follows the throat and +Z is up.
// Positive modules describe air/cutting volumes; skin modules describe outer material.
// solidBody subtracts the former from the latter. The thin spawn cube spans Y=-1 to 0,
// so the generator can retain its Y=0 face and construct the inlet boundary rectangle.
// The final two calls display BOTH objects. Export each module separately as ASCII STL
// using the wrapper procedure in README; exporting this complete scene combines them.

skinThickness = 5;

throatWidth = 5;
throatLength = 50;
throatHeight = 5;

voicingHoleWidth = 5;
voicingHoleLength = 5;
voicingHoleHeight = skinThickness;
voicingHoleX = -voicingHoleWidth / 2;
voicingHoleY = throatLength;
voicingHoleZ = throatHeight / 2;

voicingEdgeLength = 10;

pressureChamberWidth = 50;
pressureChamberLength = 50;
pressureChamberHeight = 50;
pressureChamberX = -pressureChamberWidth / 2;
pressureChamberY = throatLength;
pressureChamberZ = -pressureChamberHeight + throatHeight / 2;


module throatPositive() {
    translate([-throatWidth / 2, -1, -throatHeight / 2])
    cube([throatWidth, throatLength + 2, throatHeight]);
}

module throatSkin() {
    translate([-(pressureChamberWidth + skinThickness * 2) / 2, 0, pressureChamberZ - skinThickness])
    cube([pressureChamberWidth + skinThickness * 2, throatLength - 1, pressureChamberHeight + skinThickness * 2]);
}

module voicingHolePositive() {
    translate([voicingHoleX, voicingHoleY, voicingHoleZ - 1])
    cube([voicingHoleWidth, voicingHoleLength, voicingHoleHeight + 2]);
}

module voicingEdgePositive() {
    translate([voicingHoleWidth / 2, voicingHoleY + voicingHoleLength - 1, throatHeight / 2])
    rotate([0, -90, 0])
    linear_extrude(height = voicingHoleHeight)
    polygon([
        [0, 0],
        [skinThickness + 1, 0],
        [skinThickness + 1, voicingEdgeLength + 1]
    ]);
}

module pressureChamberPositive() {
    translate([pressureChamberX, pressureChamberY, pressureChamberZ])
    cube([pressureChamberWidth, pressureChamberLength, pressureChamberHeight]);
}

module pressureChamberSkin() {
    translate([pressureChamberX - skinThickness, pressureChamberY - skinThickness, pressureChamberZ - skinThickness])
    cube([pressureChamberWidth + skinThickness * 2, pressureChamberLength + skinThickness * 2, pressureChamberHeight + skinThickness * 2]);
}

module positiveGroup() {
    union() {
        throatPositive();
        voicingHolePositive();
        voicingEdgePositive();
        pressureChamberPositive();
    }
}

module skinGroup() {
    union() {
        throatSkin();
        pressureChamberSkin();
    }
}

module solidBody() {
    difference() {
        skinGroup();
        positiveGroup();
    }
}

module spawnPlane() {
    translate([-throatWidth / 2, -1, -throatHeight / 2])
    cube([throatWidth, 1, throatHeight]);
}


*solidBody();
spawnPlane();