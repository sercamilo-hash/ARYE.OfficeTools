# -*- coding: utf-8 -*-
"""Candidate discovery, mode filtering and dimension creation."""

from Autodesk.Revit.DB import (
    FilteredElementCollector,
    Line,
    ReferenceArray,
)

from dimassist import config as cfg
from dimassist import geometry as geo
from dimassist import references as refs

# Gap between the chain and the overall dimension, in paper millimetres.
STACK_OFFSET_MM = 8.0


class LineResult(object):
    """Outcome of processing one guide line."""

    def __init__(self, element_id):
        self.element_id = element_id
        self.dimensions = []
        self.error = None

    @property
    def ok(self):
        return self.error is None and len(self.dimensions) > 0


# --------------------------------------------------------------- discovery
def collect_candidates(doc, view, enabled_keys):
    """Elements of the enabled categories that are visible in the view.

    Collected once per run, not once per guide line. Each entry carries its
    plan bounding box so lines can be rejected without touching geometry.
    """
    candidates = {}
    for key in enabled_keys:
        category_def = cfg.CATEGORY_BY_KEY.get(key)
        if category_def is None:
            continue
        collector = (FilteredElementCollector(doc, view.Id)
                     .OfCategory(category_def.builtin_category)
                     .WhereElementIsNotElementType())
        entries = []
        for element in collector:
            box = None
            try:
                bounding_box = element.get_BoundingBox(view)
                if bounding_box is None:
                    bounding_box = element.get_BoundingBox(None)
                if bounding_box is not None:
                    box = geo.bbox_xy(bounding_box)
            except Exception:
                box = None
            entries.append((element, box))
        candidates[key] = entries
    return candidates


def hits_for_line(guide, candidates, wall_ref_mode):
    """Every grip found along one guide line, sorted and de-duplicated."""
    guide_box = geo.curve_xy_box(guide)
    found = []

    for key, entries in candidates.items():
        category_def = cfg.CATEGORY_BY_KEY[key]
        for element, box in entries:
            if box is not None and not geo.boxes_overlap(guide_box, box, pad=0.05):
                continue
            try:
                found.extend(refs.extract(element, guide, category_def, wall_ref_mode))
            except Exception:
                continue

    return _merge(found)


def _merge(hits):
    """Sort along the line and drop grips that sit on top of each other."""
    hits.sort(key=lambda hit: hit.distance)
    merged = []
    for hit in hits:
        if merged and abs(hit.distance - merged[-1].distance) < geo.MERGE_TOL:
            continue
        merged.append(hit)
    return merged


# ------------------------------------------------------------ mode filters
def _first_hit_per_element(hits):
    seen = set()
    kept = []
    for hit in hits:
        key = str(hit.element_id)
        if key in seen:
            continue
        seen.add(key)
        kept.append(hit)
    return kept


def build_groups(hits, mode, include_thickness):
    """Split the grips into groups; every group becomes one Dimension.

    Returns a list of (hits, stack_level) tuples. stack_level 1 means the
    dimension is offset away from the guide line so it stacks above it.
    """
    if len(hits) < 2:
        return []

    if mode == cfg.MODE_OVERALL:
        return [([hits[0], hits[-1]], 0)]

    if mode == cfg.MODE_CLEAR:
        groups = []
        for index in range(len(hits) - 1):
            current, following = hits[index], hits[index + 1]
            if str(current.element_id) == str(following.element_id):
                continue  # that span is the element's own thickness
            groups.append(([current, following], 0))
        return groups

    chain = hits if include_thickness else _first_hit_per_element(hits)
    if len(chain) < 2:
        return []

    if mode == cfg.MODE_CHAIN_OVERALL:
        return [(chain, 0), ([hits[0], hits[-1]], 1)]

    return [(chain, 0)]


# -------------------------------------------------------------- creation
def stack_offset(view):
    """Paper millimetres converted to model feet for the active view scale."""
    try:
        scale = float(view.Scale)
    except Exception:
        scale = 100.0
    return geo.mm_to_feet(STACK_OFFSET_MM) * scale


def create_dimensions(doc, view, guide, groups, dimension_type, offset_step):
    """Create one Dimension per group. Raises on the first Revit refusal."""
    created = []
    for group_hits, stack_level in groups:
        reference_array = ReferenceArray()
        for hit in group_hits:
            reference_array.Append(hit.reference)
        if reference_array.Size < 2:
            continue

        placement = guide
        if stack_level:
            placement = geo.offset_line(guide, offset_step * stack_level)

        if dimension_type is not None:
            dimension = doc.Create.NewDimension(
                view, placement, reference_array, dimension_type)
        else:
            dimension = doc.Create.NewDimension(view, placement, reference_array)

        if dimension is not None:
            created.append(dimension)
    return created


def process_line(doc, view, guide_element, candidates, settings,
                 dimension_type, offset_step):
    """Full pipeline for a single guide line. Never raises."""
    result = LineResult(guide_element.Id)
    curve = guide_element.GeometryCurve

    if not isinstance(curve, Line):
        result.error = "Guide line is not straight (arcs and splines are not supported)."
        return result

    hits = hits_for_line(curve, candidates, settings["wall_ref"])
    if len(hits) < 2:
        result.error = ("Found %d grip(s); a dimension needs at least 2. "
                        "Check the enabled categories and that the line "
                        "really crosses them." % len(hits))
        return result

    groups = build_groups(hits, settings["mode"], settings["include_thickness"])
    if not groups:
        result.error = "No valid span for the selected mode."
        return result

    try:
        result.dimensions = create_dimensions(
            doc, view, curve, groups, dimension_type, offset_step)
    except Exception as error:
        result.error = "Revit refused the dimension: %s" % error
        return result

    if not result.dimensions:
        result.error = "Revit returned no dimension for this line."
    return result
