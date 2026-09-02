*Built with AI*

# Vent slots for Onshape

`vent-slots.fs` defines an Onshape custom feature that cuts angled rows of
rounded ventilation slots into a planar face.

## Inputs

- **Input sketch region** — select one shaded region from a closed sketch. The
  sketch may be coincident with the target face or on any parallel plane.
- **Bounds / keep-outs** — optional shaded, closed sketch regions that the
  slots must not enter. These regions must also be parallel to the target.
- **Target surface** — one planar face on the solid part to cut.
- **Slot angle** — measured from the input sketch's X axis.
- **Slot width** and **Gap between slots** — control the row pitch.
- **Segments per slot** — `0` or `1` makes each row continuous; `2` or more
  splits each row into that many pieces.
- **Gap between segments** — clear distance between adjacent pieces.
- **Offset from edges** — erodes the input region and expands keep-outs by this
  amount. This guarantees at least the requested clearance at both slot ends;
  it also maintains the clearance along every other boundary.
- **Cut depth** — blind cut depth. Enable **Flip depth direction** if the chosen
  face is oriented the other way.

The feature accepts a sketch *region* rather than loose sketch edges. A shaded
region exists only when the sketch loop is closed, so an open sketch cannot be
selected. The feature also validates that exactly one input region was selected
and reports a focused regeneration error if the input later becomes invalid.

## Install

1. In an Onshape document, create a **Feature Studio** tab.
2. Replace its contents with [`vent-slots.fs`](vent-slots.fs).
3. If the new Feature Studio template has a newer FeatureScript version than
   `3044`, update both version numbers on the first two lines to match it.
4. Commit the Feature Studio, open a Part Studio, and add **Vent slots** from
   **Custom features in this workspace**.

## Notes and limits

- The target must currently be planar. Wrapping slots over cylinders or general
  curved surfaces needs a separate projection/wrapping strategy.
- Very large arrays are rejected above 2,000 generated segments to avoid
  pathological Part Studio regeneration times.
- An offset larger than a narrow or highly concave region can eliminate the
  usable area; the feature reports this instead of silently making invalid cuts.
- Keep-out regions are optional. Holes already present inside the main input
  sketch region are naturally treated as bounds and receive the same clearance.

The implementation uses Onshape's documented sketch, extrusion, face-offset,
and boolean operations. See the official [FeatureScript standard library
reference](https://cad.onshape.com/FsDoc/library.html) and [custom slot
tutorial](https://cad.onshape.com/FsDoc/tutorials/create-a-slot-feature.html).
