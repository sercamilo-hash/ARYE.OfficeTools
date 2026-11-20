# -*- coding: utf-8 -*-
"""Settings persistence and the registry of supported categories."""

from pyrevit import script

from Autodesk.Revit.DB import BuiltInCategory


# ------------------------------------------------------------------ modes
MODE_CHAIN = "chain"
MODE_CLEAR = "clear"
MODE_OVERALL = "overall"
MODE_CHAIN_OVERALL = "chain_overall"

MODE_LABELS = {
    MODE_CHAIN: "Chain",
    MODE_CLEAR: "Clear openings only",
    MODE_OVERALL: "Overall",
    MODE_CHAIN_OVERALL: "Chain + Overall",
}

# --------------------------------------------------------- wall reference
WALL_REF_FACES = "faces"
WALL_REF_CENTERLINE = "centerline"

WALL_REF_LABELS = {
    WALL_REF_FACES: "Faces",
    WALL_REF_CENTERLINE: "Centerline",
}


class CategoryDef(object):
    """One dimensionable category and how its grips are extracted."""

    def __init__(self, key, label, builtin_category, extractor):
        self.key = key
        self.label = label
        self.builtin_category = builtin_category
        self.extractor = extractor  # "wall" | "grid" | "instance"


CATEGORIES = [
    CategoryDef("walls", "Walls", BuiltInCategory.OST_Walls, "wall"),
    CategoryDef("grids", "Grids", BuiltInCategory.OST_Grids, "grid"),
    CategoryDef("columns", "Columns", BuiltInCategory.OST_Columns, "instance"),
    CategoryDef("struct_columns", "Structural Columns",
                BuiltInCategory.OST_StructuralColumns, "instance"),
    CategoryDef("doors", "Doors", BuiltInCategory.OST_Doors, "instance"),
    CategoryDef("windows", "Windows", BuiltInCategory.OST_Windows, "instance"),
]

CATEGORY_BY_KEY = dict((c.key, c) for c in CATEGORIES)


# --------------------------------------------------------------- defaults
DEFAULTS = {
    "line_style": "",
    "dim_type_name": "",
    "mode": MODE_CHAIN,
    "include_thickness": True,
    "use_selection": True,
    "delete_guides": True,
    "enabled_categories": ["walls"],
    "wall_ref": WALL_REF_FACES,
}

_SECTION = "dimension_assist"


def load():
    """Read saved settings, falling back to defaults on anything missing."""
    cfg = script.get_config(_SECTION)
    data = {}
    for key, default in DEFAULTS.items():
        try:
            data[key] = cfg.get_option(key, default)
        except Exception:
            data[key] = default
    return data


def save(data):
    """Persist settings so the next run opens with the same choices."""
    cfg = script.get_config(_SECTION)
    for key in DEFAULTS:
        cfg.set_option(key, data.get(key, DEFAULTS[key]))
    script.save_config()
