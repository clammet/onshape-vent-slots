"""Independent CAD geometry checks; see README.md for the Onshape checks."""

import math
import unittest

from OCC.Core.BRep import BRep_Builder
from OCC.Core.BRepAlgoAPI import BRepAlgoAPI_Common, BRepAlgoAPI_Cut
from OCC.Core.BRepBuilderAPI import BRepBuilderAPI_MakeFace, BRepBuilderAPI_MakePolygon
from OCC.Core.BRepCheck import BRepCheck_Analyzer
from OCC.Core.BRepGProp import brepgprop
from OCC.Core.BRepPrimAPI import BRepPrimAPI_MakeCylinder, BRepPrimAPI_MakePrism
from OCC.Core.GProp import GProp_GProps
from OCC.Core.TopAbs import TopAbs_SOLID
from OCC.Core.TopExp import TopExp_Explorer
from OCC.Core.TopoDS import TopoDS_Compound
from OCC.Core.gp import gp_Ax2, gp_Dir, gp_Pnt, gp_Vec


HEIGHT = 3


def prism(points):
    wire = BRepBuilderAPI_MakePolygon()
    for x, y in points:
        wire.Add(gp_Pnt(x, y, 0))
    wire.Close()
    face = BRepBuilderAPI_MakeFace(wire.Wire()).Face()
    return BRepPrimAPI_MakePrism(face, gp_Vec(0, 0, HEIGHT)).Shape()


def rectangle(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def cut(target, tool):
    operation = BRepAlgoAPI_Cut(target, tool)
    assert operation.IsDone(), "CAD subtraction failed"
    return operation.Shape()


def volume(shape):
    properties = GProp_GProps()
    brepgprop.VolumeProperties(shape, properties)
    return properties.Mass()


def solids(shape):
    explorer = TopExp_Explorer(shape, TopAbs_SOLID)
    result = []
    while explorer.More():
        result.append(explorer.Current())
        explorer.Next()
    return result


def envelope(outer, holes=()):
    shape = prism(outer)
    for hole in holes:
        shape = cut(shape, prism(hole))
    return shape


def inset(outer, holes=(), clearance=2):
    """Model inward side bands plus corner cylinders on a planar prism.

    Outer loops are counterclockwise; hole loops are clockwise. The material
    is to the left of each edge, equivalent to thickness2 on an outward-facing
    extrusion side. Subtracting tools successively has the same set difference
    as subtracting their union in Onshape.
    """
    result = envelope(outer, holes)
    if clearance == 0:
        return result
    for loop in (outer, *holes):
        for start, end in zip(loop, loop[1:] + loop[:1]):
            dx, dy = end[0] - start[0], end[1] - start[1]
            length = math.hypot(dx, dy)
            nx, ny = -dy * clearance / length, dx * clearance / length
            band = prism([
                start, end, (end[0] + nx, end[1] + ny),
                (start[0] + nx, start[1] + ny),
            ])
            result = cut(result, band)
            corner = BRepPrimAPI_MakeCylinder(
                gp_Ax2(gp_Pnt(*start, 0), gp_Dir(0, 0, 1)), clearance, HEIGHT
            ).Shape()
            result = cut(result, corner)
    assert BRepCheck_Analyzer(result).IsValid(), "Invalid inset result"
    return result


def slot_pattern():
    # Continuous 45-degree rows spanning the entire test geometry.
    builder = BRep_Builder()
    pattern = TopoDS_Compound()
    builder.MakeCompound(pattern)
    scale = math.sqrt(0.5)
    for row in range(-50, 51):
        y = row * 3.5
        points = [(scale * (x - yy), scale * (x + yy))
                  for x, yy in rectangle(-200, y - 1, 200, y + 1)]
        builder.Add(pattern, prism(points))
    return pattern


class ClearanceGeometryTests(unittest.TestCase):
    def test_closing_neck_preserves_both_lobes(self):
        dumbbell = [
            (0, 0), (20, 0), (20, 8.1), (30, 8.1), (30, 0), (50, 0),
            (50, 20), (30, 20), (30, 11.9), (20, 11.9), (20, 20), (0, 20),
        ]
        pattern = slot_pattern()
        for offset, expected_count in [(0, 1), (1.8, 1), (1.9, 2), (2, 2), (2.2, 2)]:
            with self.subTest(offset=offset):
                result = inset(dumbbell, clearance=offset)
                self.assertEqual(len(solids(result)), expected_count)
                for x in [5, 35]:
                    # Each lobe must retain a large area AND actual vent cuts.
                    probe = prism(rectangle(x, 5, x + 10, 15))
                    area = BRepAlgoAPI_Common(result, probe).Shape()
                    self.assertAlmostEqual(volume(area), 100 * HEIGHT, places=5)
                    vents = BRepAlgoAPI_Common(area, pattern).Shape()
                    self.assertGreater(volume(vents), 40 * HEIGHT)

    def test_hole_corner_clearance(self):
        outer = rectangle(0, 0, 40, 40)
        hole = list(reversed(rectangle(10, 10, 30, 30)))
        result = inset(outer, [hole])
        # Side-only bands leave the diagonal pocket near (30, 30) unprotected.
        too_close = prism(rectangle(30.5, 30.5, 31, 31))
        overlap = BRepAlgoAPI_Common(result, too_close).Shape()
        self.assertAlmostEqual(volume(overlap), 0, places=6)
        # A point beyond the requested corner distance must remain usable.
        usable = prism(rectangle(31.6, 31.6, 31.8, 31.8))
        overlap = BRepAlgoAPI_Common(result, usable).Shape()
        self.assertAlmostEqual(volume(overlap), 0.04 * HEIGHT, places=6)
        # Analytic area: inset outer square minus hole with a round 2 mm buffer.
        expected_area = 36**2 - (20**2 + 80 * 2 + math.pi * 2**2)
        self.assertAlmostEqual(volume(result), expected_area * HEIGHT, places=5)

    def test_port_panel_keeps_large_center(self):
        outer = rectangle(0, 0, 100, 100)
        holes = [list(reversed(rectangle(x0, y0, x1, y1)))
                 for x0, x1 in [(3.8, 32), (40, 60), (68, 96.2)]
                 for y0, y1 in [(0, 26), (65, 96.2)]]
        result = inset(outer, holes)
        probe = prism(rectangle(5, 30, 95, 60))
        center = BRepAlgoAPI_Common(result, probe).Shape()
        self.assertAlmostEqual(volume(center), 90 * 30 * HEIGHT, places=4)
        vents = BRepAlgoAPI_Common(center, slot_pattern()).Shape()
        self.assertGreater(volume(vents), 0.5 * volume(center))

    def test_exhausted_region_is_empty(self):
        result = inset(rectangle(0, 0, 3, 20))
        self.assertEqual(len(solids(result)), 0)
        self.assertAlmostEqual(volume(result), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
