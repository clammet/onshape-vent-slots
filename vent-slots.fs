FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
import(path : "onshape/std/geometry.fs", version : "3070.0");

const SLOT_WIDTH_BOUNDS = { (millimeter) : [0.1, 3, 1000] } as LengthBoundSpec;
const SLOT_GAP_BOUNDS = { (millimeter) : [0, 3, 1000] } as LengthBoundSpec;
const SEGMENT_COUNT_BOUNDS = { (unitless) : [0, 0, 100] } as IntegerBoundSpec;
const SEGMENT_GAP_BOUNDS = { (millimeter) : [0, 2, 1000] } as LengthBoundSpec;
const EDGE_OFFSET_BOUNDS = { (millimeter) : [0, 2, 1000] } as LengthBoundSpec;
const CUT_DEPTH_BOUNDS = { (millimeter) : [0.01, 3, 1000] } as LengthBoundSpec;
const SLOT_ANGLE_BOUNDS = { (degree) : [-360, 0, 360] } as AngleBoundSpec;

/**
 * Adds one closed, rounded slot to a sketch.
 *
 * `left` and `right` are the extreme ends of the complete slot, so the
 * straight portions start one radius in from those values.
 */
function addRoundedSlot(sketch is Sketch, slotId is string, left is ValueWithUnits,
                        right is ValueWithUnits, centerY is ValueWithUnits,
                        width is ValueWithUnits)
{
    const radius = width / 2;
    const leftCenter = left + radius;
    const rightCenter = right - radius;

    skLineSegment(sketch, slotId ~ "_top", {
                "start" : vector(leftCenter, centerY + radius),
                "end" : vector(rightCenter, centerY + radius)
            });
    skArc(sketch, slotId ~ "_right", {
                "start" : vector(rightCenter, centerY + radius),
                "mid" : vector(right, centerY),
                "end" : vector(rightCenter, centerY - radius)
            });
    skLineSegment(sketch, slotId ~ "_bottom", {
                "start" : vector(rightCenter, centerY - radius),
                "end" : vector(leftCenter, centerY - radius)
            });
    skArc(sketch, slotId ~ "_left", {
                "start" : vector(leftCenter, centerY - radius),
                "mid" : vector(left, centerY),
                "end" : vector(leftCenter, centerY + radius)
            });
}

function requireOne(context is Context, query is Query, parameterName is string,
                    message is string) returns Query
{
    const entities = evaluateQuery(context, query);
    if (size(entities) != 1)
    {
        throw regenError(message, { "faultyParameters" : [parameterName], "entities" : query });
    }
    return entities[0];
}

function requireParallelSketchPlane(context is Context, query is Query,
                                    referencePlane is Plane, parameterName is string)
                                    returns Plane
{
    var sketchPlane;
    try
    {
        sketchPlane = evOwnerSketchPlane(context, {
                    "entity" : query,
                    "checkAllEntities" : true
                });
    }
    catch
    {
        throw regenError("Select closed regions from one sketch.", {
                    "faultyParameters" : [parameterName],
                    "entities" : query
                });
    }

    if (!parallelVectors(sketchPlane.normal, referencePlane.normal))
    {
        throw regenError("The sketch regions must be parallel to the target surface.", {
                    "faultyParameters" : [parameterName],
                    "entities" : query
                });
    }
    return sketchPlane;
}

annotation {
    "Feature Type Name" : "Vent slots",
    "Feature Type Description" : "Cut an angled array of optionally segmented slots inside a closed sketch region."
}
export const ventSlots = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation {
            "Name" : "Input sketch region",
            "Filter" : EntityType.FACE && SketchObject.YES,
            "MaxNumberOfPicks" : 1
        }
        definition.ventRegion is Query;

        annotation {
            "Name" : "Bounds / keep-outs",
            "Filter" : EntityType.FACE && SketchObject.YES
        }
        definition.bounds is Query;

        annotation {
            "Name" : "Target surface",
            "Filter" : EntityType.FACE && GeometryType.PLANE && BodyType.SOLID,
            "MaxNumberOfPicks" : 1
        }
        definition.targetSurface is Query;

        annotation { "Name" : "Slot angle" }
        isAngle(definition.slotAngle, SLOT_ANGLE_BOUNDS);

        annotation { "Name" : "Slot width" }
        isLength(definition.slotWidth, SLOT_WIDTH_BOUNDS);

        annotation { "Name" : "Gap between slots" }
        isLength(definition.slotGap, SLOT_GAP_BOUNDS);

        annotation { "Name" : "Segments per slot (0 = continuous)" }
        isInteger(definition.segmentsPerSlot, SEGMENT_COUNT_BOUNDS);

        annotation { "Name" : "Gap between segments" }
        isLength(definition.segmentGap, SEGMENT_GAP_BOUNDS);

        annotation { "Name" : "Offset from edges" }
        isLength(definition.edgeOffset, EDGE_OFFSET_BOUNDS);

        annotation { "Name" : "Cut depth" }
        isLength(definition.depth, CUT_DEPTH_BOUNDS);

        annotation { "Name" : "Flip depth direction", "Default" : false }
        definition.flipDepth is boolean;
    }
    {
        const ventRegion = requireOne(context, definition.ventRegion, "ventRegion",
                "Select exactly one shaded, closed sketch region.");
        const targetSurface = requireOne(context, definition.targetSurface, "targetSurface",
                "Select exactly one planar target face.");

        const targetBody = qOwnerBody(targetSurface);
        if (size(evaluateQuery(context, targetBody)) != 1)
        {
            throw regenError("The target surface must belong to one solid part.", {
                        "faultyParameters" : ["targetSurface"],
                        "entities" : definition.targetSurface
                    });
        }

        // evFaceTangentPlane respects the face orientation. Its negative normal is
        // therefore the usual direction into a solid selected on its outside face.
        const targetPlane = evFaceTangentPlane(context, {
                    "face" : targetSurface,
                    "parameter" : vector(0.5, 0.5)
                });
        const regionPlane = requireParallelSketchPlane(context, ventRegion, targetPlane,
                "ventRegion");

        const boundsEntities = evaluateQuery(context, definition.bounds);
        var boundsPlane;
        if (size(boundsEntities) > 0)
        {
            boundsPlane = requireParallelSketchPlane(context, definition.bounds, targetPlane,
                    "bounds");
        }

        var cutDirection = -targetPlane.normal;
        if (definition.flipDepth)
        {
            cutDirection = targetPlane.normal;
        }

        // Angle zero follows the input sketch X axis, which keeps the result stable
        // when the model is moved or the target face has a different canonical axis.
        const slotX = rotationMatrix3d(regionPlane.normal, definition.slotAngle) * regionPlane.x;
        const slotPlane = plane(targetPlane.origin, targetPlane.normal, slotX);
        const slotCSys = coordSystem(slotPlane);
        const regionBox = evBox3d(context, {
                    "topology" : ventRegion,
                    "cSys" : slotCSys,
                    "tight" : true
                });

        const minX = regionBox.minCorner[0];
        const maxX = regionBox.maxCorner[0];
        const minY = regionBox.minCorner[1];
        const maxY = regionBox.maxCorner[1];
        const spanY = maxY - minY;
        const pitch = definition.slotWidth + definition.slotGap;

        if (spanY < definition.slotWidth)
        {
            throw regenError("The vent region is narrower than one slot.", {
                        "faultyParameters" : ["slotWidth"],
                        "entities" : definition.ventRegion
                    });
        }

        var rowCount = floor((spanY + definition.slotGap) / pitch);
        if (rowCount < 1)
        {
            rowCount = 1;
        }

        var segmentCount = definition.segmentsPerSlot;
        if (segmentCount < 1)
        {
            segmentCount = 1;
        }

        if (rowCount * segmentCount > 2000)
        {
            throw regenError("This would create more than 2000 slot segments. Increase the gaps or reduce the segment count.", {
                        "faultyParameters" : ["slotWidth", "slotGap", "segmentsPerSlot"]
                    });
        }

        // Extend the pattern past the region. The later boolean clip produces exact
        // endpoints even for curved or concave sketch boundaries.
        const xMargin = definition.slotWidth + pitch;
        const patternLeft = minX - xMargin;
        const patternRight = maxX + xMargin;
        const patternLength = patternRight - patternLeft;
        const totalSegmentGap = (segmentCount - 1) * definition.segmentGap;
        const segmentLength = (patternLength - totalSegmentGap) / segmentCount;

        if (segmentLength <= definition.slotWidth)
        {
            throw regenError("The requested segment count and gap leave no room for rounded slots.", {
                        "faultyParameters" : ["segmentsPerSlot", "segmentGap", "slotWidth"]
                    });
        }

        const usedHeight = definition.slotWidth + (rowCount - 1) * pitch;
        const firstCenterY = (minY + maxY - usedHeight) / 2 + definition.slotWidth / 2;

        const slotSketchId = id + "slotSketch";
        var slotSketch = newSketchOnPlane(context, slotSketchId, {
                    "sketchPlane" : slotPlane
                });

        for (var row = 0; row < rowCount; row += 1)
        {
            const centerY = firstCenterY + row * pitch;
            for (var segment = 0; segment < segmentCount; segment += 1)
            {
                const left = patternLeft + segment * (segmentLength + definition.segmentGap);
                const right = left + segmentLength;
                addRoundedSlot(slotSketch, "slot_" ~ row ~ "_" ~ segment,
                        left, right, centerY, definition.slotWidth);
            }
        }
        skSolve(slotSketch);

        const slotRegions = qSketchRegion(slotSketchId);
        if (size(evaluateQuery(context, slotRegions)) == 0)
        {
            throw regenError("Failed to create closed slot profiles.", {});
        }

        const slotExtrudeId = id + "slotTools";
        opExtrude(context, slotExtrudeId, {
                    "entities" : slotRegions,
                    "direction" : cutDirection,
                    "endBound" : BoundingType.BLIND,
                    "endDepth" : definition.depth
                });
        const slotBodies = qCreatedBy(slotExtrudeId, EntityType.BODY);

        // Make the closed input region into a long prism. It can live on any plane
        // parallel to the target face; it does not have to be coincident with it.
        const targetBox = evBox3d(context, { "topology" : targetBody, "tight" : false });
        var maximumPlaneDistance = abs(dot(regionPlane.origin - targetPlane.origin,
                        targetPlane.normal));
        if (size(boundsEntities) > 0)
        {
            const boundsPlaneDistance = abs(dot(boundsPlane.origin - targetPlane.origin,
                            targetPlane.normal));
            if (boundsPlaneDistance > maximumPlaneDistance)
            {
                maximumPlaneDistance = boundsPlaneDistance;
            }
        }
        const reach = box3dDiagonalLength(targetBox) + definition.depth +
                maximumPlaneDistance + 1 * millimeter;
        const envelopeExtrudeId = id + "ventEnvelope";
        opExtrude(context, envelopeExtrudeId, {
                    "entities" : ventRegion,
                    "direction" : regionPlane.normal,
                    "endBound" : BoundingType.BLIND,
                    "endDepth" : reach,
                    "startBound" : BoundingType.BLIND,
                    "startDepth" : reach
                });

        const envelopeBodies = qCreatedBy(envelopeExtrudeId, EntityType.BODY);
        if (definition.edgeOffset > 0 * meter)
        {
            try
            {
                opOffsetFace(context, id + "insetEnvelope", {
                            "moveFaces" : qNonCapEntity(envelopeExtrudeId, EntityType.FACE),
                            "offsetDistance" : -definition.edgeOffset
                        });
            }
            catch
            {
                throw regenError("The edge offset is too large for the input sketch region.", {
                            "faultyParameters" : ["edgeOffset"],
                            "entities" : definition.ventRegion
                        });
            }
        }

        // SUBTRACT_COMPLEMENT keeps only the portion of every slot body that lies
        // inside the (possibly inset) envelope. Unlike INTERSECTION, this also works
        // when the slot query contains many disjoint bodies.
        opBoolean(context, id + "clipSlots", {
                    "tools" : envelopeBodies,
                    "targets" : slotBodies,
                    "operationType" : BooleanOperationType.SUBTRACT_COMPLEMENT,
                    "keepTools" : false
                });

        if (size(boundsEntities) > 0)
        {
            const boundsExtrudeId = id + "boundKeepouts";
            opExtrude(context, boundsExtrudeId, {
                        "entities" : definition.bounds,
                        "direction" : boundsPlane.normal,
                        "endBound" : BoundingType.BLIND,
                        "endDepth" : reach,
                        "startBound" : BoundingType.BLIND,
                        "startDepth" : reach
                    });
            const boundBodies = qCreatedBy(boundsExtrudeId, EntityType.BODY);

            if (definition.edgeOffset > 0 * meter)
            {
                try
                {
                    opOffsetFace(context, id + "expandKeepouts", {
                                "moveFaces" : qNonCapEntity(boundsExtrudeId, EntityType.FACE),
                                "offsetDistance" : definition.edgeOffset
                            });
                }
                catch
                {
                    throw regenError("The edge offset could not be applied to the selected bounds.", {
                                "faultyParameters" : ["edgeOffset", "bounds"],
                                "entities" : definition.bounds
                            });
                }
            }

            if (size(evCollision(context, {
                            "tools" : boundBodies,
                            "targets" : slotBodies
                        })) > 0)
            {
                opBoolean(context, id + "removeKeepouts", {
                            "tools" : boundBodies,
                            "targets" : slotBodies,
                            "operationType" : BooleanOperationType.SUBTRACTION,
                            "keepTools" : false
                        });
            }
            else
            {
                opDeleteBodies(context, id + "deleteUnusedKeepouts", {
                            "entities" : boundBodies
                        });
            }
        }

        if (size(evaluateQuery(context, slotBodies)) == 0)
        {
            throw regenError("No slot geometry remains inside the bounds. Reduce the offsets or change the spacing.", {
                        "faultyParameters" : ["ventRegion", "bounds", "edgeOffset"]
                    });
        }

        try
        {
            opBoolean(context, id + "cutVents", {
                        "tools" : slotBodies,
                        "targets" : targetBody,
                        "operationType" : BooleanOperationType.SUBTRACTION,
                        "keepTools" : false
                    });
        }
        catch
        {
            throw regenError("The slots do not intersect the target part in the selected depth direction.", {
                        "faultyParameters" : ["targetSurface", "depth", "flipDepth"],
                        "entities" : definition.targetSurface
                    });
        }

        // The generated sketch is an implementation detail, not feature output.
        opDeleteBodies(context, id + "cleanupSlotSketch", {
                    "entities" : qCreatedBy(slotSketchId, EntityType.BODY)
                });
    });
