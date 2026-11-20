# -*- coding: utf-8 -*-
"""Pure geometry helpers. No UI, no transactions, no document edits.

All distances are in Revit internal units (decimal feet).
"""

from Autodesk.Revit.DB import XYZ, Line, Arc, SetComparisonResult

# Two grips closer than this are treated as the same grip (~0.8 mm).
MERGE_TOL = 0.0026
# Slack used when testing "does this crossing fall inside the drawn segment".
SEGMENT_TOL = 1.0e-4

MM_PER_FOOT = 304.8


def mm_to_feet(value_mm):
    return value_mm / MM_PER_FOOT


def flatten_point(point, z=0.0):
    """Drop a point onto a horizontal plane."""
    return XYZ(point.X, point.Y, z)


def flatten_curve(curve, z=0.0):
    """Copy of the curve projected onto the Z=z plane. None if unsupported."""
    if isinstance(curve, Line):
        return Line.CreateBound(flatten_point(curve.GetEndPoint(0), z),
                                flatten_point(curve.GetEndPoint(1), z))
    if isinstance(curve, Arc):
        start = flatten_point(curve.GetEndPoint(0), z)
        end = flatten_point(curve.GetEndPoint(1), z)
        middle = flatten_point(curve.Evaluate(0.5, True), z)
        try:
            return Arc.Create(start, end, middle)
        except Exception:
            return None
    return None


def line_direction(line):
    return (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize()


def line_length(line):
    return line.GetEndPoint(0).DistanceTo(line.GetEndPoint(1))


def distance_along(line, point):
    """Signed distance from the line start to the projection of `point`."""
    return (point - line.GetEndPoint(0)).DotProduct(line_direction(line))


def point_at(line, distance):
    return line.GetEndPoint(0) + line_direction(line) * distance


def inside_segment(distance, length, tol=SEGMENT_TOL):
    return -tol <= distance <= length + tol


def plane_crossing(line, plane_origin, plane_normal):
    """Distance along `line` where its infinite extension meets a plane.

    Returns None when the line runs parallel to the plane.
    """
    denominator = line_direction(line).DotProduct(plane_normal)
    if abs(denominator) < 1.0e-9:
        return None
    numerator = (plane_origin - line.GetEndPoint(0)).DotProduct(plane_normal)
    return numerator / denominator


def offset_line(line, distance):
    """Copy of the line shifted perpendicularly, in the view plane."""
    normal = XYZ.BasisZ.CrossProduct(line_direction(line)).Normalize()
    shift = normal * distance
    return Line.CreateBound(line.GetEndPoint(0) + shift,
                            line.GetEndPoint(1) + shift)


def curve_xy_box(curve):
    """Rough plan bounding box of a curve as (min_x, min_y, max_x, max_y)."""
    points = [curve.GetEndPoint(0), curve.GetEndPoint(1)]
    if isinstance(curve, Arc):
        points.append(curve.Evaluate(0.5, True))
    xs = [p.X for p in points]
    ys = [p.Y for p in points]
    return (min(xs), min(ys), max(xs), max(ys))


def bbox_xy(bounding_box):
    return (bounding_box.Min.X, bounding_box.Min.Y,
            bounding_box.Max.X, bounding_box.Max.Y)


def boxes_overlap(box_a, box_b, pad=0.0):
    """Cheap rejection test before touching any geometry."""
    return not (box_a[2] + pad < box_b[0] or box_b[2] + pad < box_a[0] or
                box_a[3] + pad < box_b[1] or box_b[3] + pad < box_a[1])


def curves_intersect_xy(curve_a, curve_b, z=0.0):
    """Plan intersection points of two curves. Empty list when they miss."""
    flat_a = flatten_curve(curve_a, z)
    flat_b = flatten_curve(curve_b, z)
    if flat_a is None or flat_b is None:
        return []
    try:
        # CPython 3 engine: the `out` argument comes back inside the tuple.
        comparison, results = flat_a.Intersect(flat_b)
    except Exception:
        return []
    if comparison != SetComparisonResult.Overlap or results is None:
        return []
    return [results.get_Item(i).XYZPoint for i in range(results.Size)]


def project_on_curve(curve, point):
    """(normalized_parameter, distance) of `point` against a bounded curve."""
    try:
        result = curve.Project(point)
    except Exception:
        return None
    if result is None:
        return None
    try:
        normalized = curve.ComputeNormalizedParameter(result.Parameter)
    except Exception:
        normalized = None
    return (normalized, result.Distance)
