# -*- coding: utf-8 -*-
#! python3
"""Lines to Dimensions - Dimension Assist.

Reads detail lines used as dimension guides, finds what each line crosses,
creates the dimensions and removes the guides that succeeded.
Revit 2025 / 2026 / 2027, pyRevit CPython 3 engine.
"""

import os

from pyrevit import forms, revit, script

from Autodesk.Revit.DB import (
    BuiltInCategory,
    CurveElement,
    DimensionStyleType,
    DimensionType,
    FilteredElementCollector,
    Line,
    ViewPlan,
)

from dimassist import builder
from dimassist import config as cfg
from dimassist import references

__title__ = "Lines to\nDimensions"
__author__ = "KHL Architecture"

output = script.get_output()
doc = revit.doc
uidoc = revit.uidoc
active_view = doc.ActiveView

XAML_FILE = os.path.join(os.path.dirname(__file__), "ui.xaml")


# ============================================================== collectors
def collect_guide_lines(view):
    """Every view-owned detail line in the active view."""
    collector = (FilteredElementCollector(doc, view.Id)
                 .OfCategory(BuiltInCategory.OST_Lines)
                 .WhereElementIsNotElementType())
    lines = []
    for element in collector:
        if not isinstance(element, CurveElement):
            continue
        if element.OwnerViewId != view.Id:
            continue
        lines.append(element)
    return lines


def style_name(curve_element):
    try:
        return curve_element.LineStyle.Name
    except Exception:
        return None


def collect_line_styles(view_lines):
    """Styles actually used in the view, so the list stays short."""
    names = set()
    for element in view_lines:
        name = style_name(element)
        if name:
            names.add(name)
    return sorted(names)


def collect_linear_dimension_types():
    types = []
    for dimension_type in FilteredElementCollector(doc).OfClass(DimensionType):
        try:
            if dimension_type.StyleType != DimensionStyleType.Linear:
                continue
            if not dimension_type.Name:
                continue
        except Exception:
            continue
        types.append(dimension_type)
    types.sort(key=lambda item: item.Name)
    return types


def selected_guide_lines(view):
    selection = revit.get_selection()
    lines = []
    for element in selection:
        if not isinstance(element, CurveElement):
            continue
        if element.OwnerViewId != view.Id:
            continue
        lines.append(element)
    return lines


# ==================================================================== UI
class DimensionAssistWindow(forms.WPFWindow):
    """Configuration panel. Returns its settings through `self.result`."""

    CATEGORY_CHECKBOXES = {
        "walls": "chkWalls",
        "grids": "chkGrids",
        "columns": "chkColumns",
        "struct_columns": "chkStructColumns",
        "doors": "chkDoors",
        "windows": "chkWindows",
    }

    MODE_RADIOS = {
        cfg.MODE_CHAIN: "rbChain",
        cfg.MODE_CLEAR: "rbClear",
        cfg.MODE_OVERALL: "rbOverall",
        cfg.MODE_CHAIN_OVERALL: "rbChainOverall",
    }

    def __init__(self, xaml_file, line_styles, dimension_types,
                 settings, selection_count):
        forms.WPFWindow.__init__(self, xaml_file)
        self.dimension_types = dimension_types
        self.result = None

        for name in line_styles:
            self.cbLineStyle.Items.Add(name)
        if line_styles:
            saved = settings.get("line_style")
            index = line_styles.index(saved) if saved in line_styles else 0
            self.cbLineStyle.SelectedIndex = index

        for dimension_type in dimension_types:
            self.cbDimType.Items.Add(dimension_type.Name)
        if dimension_types:
            names = [item.Name for item in dimension_types]
            saved = settings.get("dim_type_name")
            index = names.index(saved) if saved in names else 0
            self.cbDimType.SelectedIndex = index

        self.rbSelection.IsChecked = bool(settings.get("use_selection"))
        self.rbAllInView.IsChecked = not self.rbSelection.IsChecked
        self.txtSourceInfo.Text = "%d line(s) currently selected." % selection_count

        mode = settings.get("mode", cfg.MODE_CHAIN)
        radio_name = self.MODE_RADIOS.get(mode, "rbChain")
        getattr(self, radio_name).IsChecked = True

        self.chkThickness.IsChecked = bool(settings.get("include_thickness"))
        self.chkDelete.IsChecked = bool(settings.get("delete_guides"))

        enabled = settings.get("enabled_categories") or []
        for key, control_name in self.CATEGORY_CHECKBOXES.items():
            getattr(self, control_name).IsChecked = key in enabled

        self.cbWallRef.SelectedIndex = (
            1 if settings.get("wall_ref") == cfg.WALL_REF_CENTERLINE else 0)

    # ------------------------------------------------------------ handlers
    def cancel_clicked(self, sender, args):
        self.result = None
        self.Close()

    def create_clicked(self, sender, args):
        if self.cbLineStyle.SelectedIndex < 0:
            forms.alert("Pick a guide line style first.", title="Dimension Assist")
            return

        enabled = [key for key, control_name in self.CATEGORY_CHECKBOXES.items()
                   if getattr(self, control_name).IsChecked]
        if not enabled:
            forms.alert("Tick at least one category to dimension.",
                        title="Dimension Assist")
            return

        mode = cfg.MODE_CHAIN
        for mode_key, radio_name in self.MODE_RADIOS.items():
            if getattr(self, radio_name).IsChecked:
                mode = mode_key
                break

        selected_type = None
        if self.cbDimType.SelectedIndex >= 0 and self.dimension_types:
            selected_type = self.dimension_types[self.cbDimType.SelectedIndex]

        self.result = {
            "line_style": str(self.cbLineStyle.SelectedItem),
            "dim_type": selected_type,
            "dim_type_name": selected_type.Name if selected_type else "",
            "mode": mode,
            "include_thickness": bool(self.chkThickness.IsChecked),
            "use_selection": bool(self.rbSelection.IsChecked),
            "delete_guides": bool(self.chkDelete.IsChecked),
            "enabled_categories": enabled,
            "wall_ref": (cfg.WALL_REF_CENTERLINE
                         if self.cbWallRef.SelectedIndex == 1
                         else cfg.WALL_REF_FACES),
        }
        self.Close()


# ================================================================== report
def show_report(results, deleted_count):
    succeeded = [item for item in results if item.ok]
    failed = [item for item in results if not item.ok]
    total_dimensions = sum(len(item.dimensions) for item in succeeded)

    output.print_md("# Dimension Assist")
    output.print_md(
        "**%d** of **%d** guide line(s) processed - "
        "**%d** dimension(s) created - **%d** guide line(s) deleted."
        % (len(succeeded), len(results), total_dimensions, deleted_count))

    if failed:
        output.print_md("## Lines that need attention")
        rows = [[output.linkify(item.element_id), item.error] for item in failed]
        output.print_table(table_data=rows, columns=["Guide line", "Reason"])
    else:
        output.print_md("All guide lines produced a dimension.")


# ==================================================================== main
def main():
    if not isinstance(active_view, ViewPlan):
        forms.alert("Dimension Assist v1 runs on plan views only.\n"
                    "Open a floor plan, ceiling plan or plan callout and try again.",
                    title="Dimension Assist", exitscript=True)

    view_lines = collect_guide_lines(active_view)
    if not view_lines:
        forms.alert("No detail lines found in the active view.",
                    title="Dimension Assist", exitscript=True)

    line_styles = collect_line_styles(view_lines)
    dimension_types = collect_linear_dimension_types()
    if not dimension_types:
        forms.alert("No linear dimension type found in this project.",
                    title="Dimension Assist", exitscript=True)

    settings = cfg.load()
    preselected = selected_guide_lines(active_view)

    window = DimensionAssistWindow(XAML_FILE, line_styles, dimension_types,
                                   settings, len(preselected))
    window.show_dialog()
    if window.result is None:
        script.exit()

    chosen = window.result
    cfg.save(chosen)

    # ------------------------------------------------------ pick the guides
    if chosen["use_selection"]:
        guides = [line for line in preselected
                  if style_name(line) == chosen["line_style"]]
        if not guides:
            forms.alert("None of the selected elements is a detail line with the "
                        "style '%s'.\nSelect the guide lines, or switch to "
                        "'All lines of this style in the active view'."
                        % chosen["line_style"],
                        title="Dimension Assist", exitscript=True)
    else:
        guides = [line for line in view_lines
                  if style_name(line) == chosen["line_style"]]
        if not guides:
            forms.alert("No line with the style '%s' in this view."
                        % chosen["line_style"],
                        title="Dimension Assist", exitscript=True)

    # ---------------------------------------------------------------- run
    references.reset_cache()
    candidates = builder.collect_candidates(
        doc, active_view, chosen["enabled_categories"])
    offset_step = builder.stack_offset(active_view)

    results = []
    deleted_count = 0

    with revit.Transaction("Create dimensions from guide lines"):
        with forms.ProgressBar(title="Dimensioning {value} of {max_value}",
                               cancellable=True) as progress:
            for index, guide in enumerate(guides, 1):
                if progress.cancelled:
                    break
                progress.update_progress(index, len(guides))
                results.append(builder.process_line(
                    doc, active_view, guide, candidates, chosen,
                    chosen["dim_type"], offset_step))

        if chosen["delete_guides"]:
            for result in results:
                if not result.ok:
                    continue
                try:
                    doc.Delete(result.element_id)
                    deleted_count += 1
                except Exception:
                    pass

    show_report(results, deleted_count)


main()
