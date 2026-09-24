# Geometry regression checks

FeatureScript needs Onshape's modeling runtime. Compile `vent-slots.fs` in a
Feature Studio and run these checks in a disposable Part Studio after changes
to clipping or edge clearance.

Use 2 mm slot width, 1.5 mm slot gap, 45 degree angle, continuous slots,
0.5 mm minimum fragment length, and a through-thickness blind cut unless a
case specifies otherwise.

1. **Narrow connection closes:** Extrude a dumbbell profile with two 20 x 20 mm
   squares separated by 10 mm, connected by a centered 3.8 mm wide bridge.
   Select its top face with no input sketch. Try edge offsets of 0, 1.8, 1.9,
   2, and 2.2 mm. Both squares must retain vents at every setting; only the
   narrow bridge loses usable area when the offset reaches half its width.
2. **Port panel:** Make a plate with three rectangular holes near its top and
   three rectangular notches opening into its bottom edge. Leave some 3.8 mm
   webs and a large solid area in the center. At 2 mm edge offset, the center
   and every surviving side/top area must receive slots. Measure clearance
   from slot edges to the outside, hole edges, and hole corners: at least 2 mm.
3. **Exhausted area:** Use a 3 mm wide rectangle with 2 mm edge offset. Expect
   "The edge offset leaves no usable vent area." No temporary bodies should
   remain after canceling the failed feature.
4. **Sketch envelope:** Repeat the dumbbell with a single sketch region as
   the envelope on a larger target plate, first coincident with the face and
   then on a parallel plane 10 mm away. Both lobes must receive vents. Repeat
   from the opposite side of the plate to check normal orientation.
5. **Curved boundaries:** Use a circular plate with a circular hole and a
   profile containing tangent arcs. Check 0 and 2 mm offsets and measure both
   inner and outer clearance. There must be no leftover clearance-tool solids.
6. **Later clipping:** Add a keep-out across one lobe, set its separate offset
   to 1 mm, and repeat with three segments per slot. All surviving fragments
   in both lobes must be cut. Increasing the minimum fragment length must
   remove only short fragments, not other fragments from the same row.

`test_clearance_geometry.py` independently checks the boundary-band geometry
with Open CASCADE (`pythonocc-core`). Run it with a Python environment that has
that package installed:

```sh
python tests/test_clearance_geometry.py
```

These local checks validate the geometric construction, not FeatureScript
compilation, Onshape query tracking, or Parasolid behavior. They do not replace
the Onshape checks above.
