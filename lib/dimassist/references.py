# -*- coding: utf-8 -*-
"""Grip extraction: turns an element crossed by a guide line into dimension
references, each stamped with its position along that line.

One extractor per family of categories:
    wall     -> HostObjectUtils side faces, or the wall centerline
    grid     -> the grid element itself
    instance -> FamilyInstance center reference planes
"""

from Autodesk.Revit.DB import (
    FamilyInstanceReferenceType,
    Line,
    Options,
    PlanarFace,
    Reference,
    ShellLayerType,
    ViewDetailLevel,
    XYZ,
)
from Autodesk.Revit.DB import HostObjectUtils

from dimassist import config as cfg
from dimassist import geometry as geo


class Hit(object):
    """A single dimension grip found along the guide line."""

    def __init__(self, distance, reference, element_id, tag):
        self.distance = distance      # feet from the guide line start
        self.reference = reference    # Reference handed to NewDimension
        self.element_id = element_id
        self.tag = tag                # "ext" | "int" | "center"

    def __repr__(self):
        return "<Hit %.4f %s %s>" % (self.distance, self.tag, self.element_id)


# Wall centerline references are expensive to compute; keep them per run.
_centerline_cache = {}


def reset_cache():
    _centerline_cache.clear()


# ------------------------------------------------------------------ walls
def _wall_centerline_reference(wall):
    """The 'Wall Centerline' reference, valid for dimensioning."""
    key = wall.Id.IntegerValue if hasattr(wall.Id, "IntegerValue") else int(wall.Id.Value)
    if key in _centerline_cache:
        return _centerline_cache[key]

    reference = None
    options = Options()
    options.ComputeReferences = True
    options.IncludeNonVisibleObjects = True
    options.DetailLevel = ViewDetailLevel.Fine
    try:
        geometry = wall.get_Geometry(options)
        if geometry is not None:
            for obj in geometry:
                if isinstance(obj, Line) and obj.Reference is not None:
                    reference = obj.Reference
                    break
    except Exception:
        reference = None

    _centerline_cache[key] = reference
    return reference


def _crossing_is_on_wall(wall, point):
    """Guard against faces whose infinite plane is crossed far off the wall."""
    location = wall.Location
    if location is None or not hasattr(location, "Curve"):
        return True
    flat = geo.flatten_curve(location.Curve)
    if flat is None:
        return True
    projected = geo.project_on_curve(flat, geo.flatten_point(point))
    if projected is None:
        return False
    normalized, distance = projected
    if normalized is not None and (normalized < -0.001 or normalized > 1.001):
        return False
    try:
        half_width = wall.Width * 0.75
    except Exception:
        half_width = 2.0
    return distance <= half_width + 0.02


def _wall_face_hits(wall, guide):
    hits = []
    length = geo.line_length(guide)
    layers = ((ShellLayerType.Exterior, "ext"), (ShellLayerType.Interior, "int"))

    for shell_layer, tag in layers:
        try:
            face_refs = HostObjectUtils.GetSideFaces(wall, shell_layer)
        except Exception:
            continue
        if not face_refs:
            continue
        for face_ref in face_refs:
            try:
                face = wall.GetGeometryObjectFromReference(face_ref)
            except Exception:
                continue
            # Curved walls give cylindrical faces; use the Centerline option there.
            if not isinstance(face, PlanarFace):
                continue
            distance = geo.plane_crossing(guide, face.Origin, face.FaceNormal)
            if distance is None or not geo.inside_segment(distance, length):
                continue
            if not _crossing_is_on_wall(wall, geo.point_at(guide, distance)):
                continue
            hits.append(Hit(distance, face_ref, wall.Id, tag))
    return hits


def _wall_centerline_hits(wall, guide):
    location = wall.Location
    if location is None or not hasattr(location, "Curve"):
        return []
    z = guide.GetEndPoint(0).Z
    points = geo.curves_intersect_xy(guide, location.Curve, z)
    if not points:
        return []
    reference = _wall_centerline_reference(wall)
    if reference is None:
        return []

    hits = []
    length = geo.line_length(guide)
    for point in points:
        distance = geo.distance_along(guide, geo.flatten_point(point, z))
        if geo.inside_segment(distance, length):
            hits.append(Hit(distance, reference, wall.Id, "center"))
    return hits


def extract_wall(wall, guide, wall_ref_mode):
    if wall_ref_mode == cfg.WALL_REF_CENTERLINE:
        return _wall_centerline_hits(wall, guide)
    hits = _wall_face_hits(wall, guide)
    if not hits:
        # Curved or curtain wall: centerline is the only usable grip.
        hits = _wall_centerline_hits(wall, guide)
    return hits


# ------------------------------------------------------------------ grids
def extract_grid(grid, guide):
    try:
        curve = grid.Curve
    except Exception:
        return []
    if curve is None:
        return []
    z = guide.GetEndPoint(0).Z
    points = geo.curves_intersect_xy(guide, curve, z)
    if not points:
        return []

    hits = []
    length = geo.line_length(guide)
    for point in points:
        distance = geo.distance_along(guide, geo.flatten_point(point, z))
        if geo.inside_segment(distance, length):
            hits.append(Hit(distance, Reference(grid), grid.Id, "center"))
    return hits


# ------------------------------------------------------------- instances
def extract_instance(instance, guide):
    """Center reference plane of a column, door or window.

    Picks the plane (left/right or front/back) whose normal is the most
    parallel to the guide line, so the dimension always reads across it.
    """
    try:
        transform = instance.GetTransform()
    except Exception:
        return []

    direction = geo.line_direction(guide)
    candidates = (
        (FamilyInstanceReferenceType.CenterLeftRight, transform.BasisX),
        (FamilyInstanceReferenceType.CenterFrontBack, transform.BasisY),
    )
    candidates = sorted(candidates,
                        key=lambda pair: abs(direction.DotProduct(pair[1])),
                        reverse=True)

    length = geo.line_length(guide)
    for reference_type, axis in candidates:
        if abs(direction.DotProduct(axis)) < 1.0e-6:
            continue
        try:
            refs = instance.GetReferences(reference_type)
        except Exception:
            continue
        if not refs:
            continue
        distance = geo.plane_crossing(guide, transform.Origin, axis)
        if distance is None or not geo.inside_segment(distance, length):
            continue
        return [Hit(distance, refs[0], instance.Id, "center")]
    return []


# ------------------------------------------------------------- dispatcher
def extract(element, guide, category_def, wall_ref_mode):
    if category_def.extractor == "wall":
        return extract_wall(element, guide, wall_ref_mode)
    if category_def.extractor == "grid":
        return extract_grid(element, guide)
    if category_def.extractor == "instance":
        return extract_instance(element, guide)
    return []
