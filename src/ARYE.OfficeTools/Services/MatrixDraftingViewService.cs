using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Storage;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Draws the office-format matrix into a drafting view as native TextNotes
    /// and detail lines, updating in place on every run.
    ///
    /// Update mechanism: every element this service creates is tagged with the
    /// same Extensible Storage schema propagation uses, under a fixed sentinel
    /// building/group id pair reserved for the matrix. An update deletes ONLY
    /// elements carrying that tag — user annotations added to the view survive.
    ///
    /// The view is remembered by UniqueId in MatrixSettings (rename-proof);
    /// when the stored view was deleted, a fresh one is created.
    /// </summary>
    internal sealed class MatrixDraftingViewService
    {
        public const string DefaultViewName = "ZFA MATRIX — BauTools";
        public const string TextTypeName = "BauTools 3/32\"";

        /// <summary>Sentinel ids marking matrix-drawn elements — a parallel key inside the propagation tag schema.</summary>
        private static readonly Guid MatrixTagBuildingId = new Guid("7a2f95d1-30cb-49e9-9a34-5f1c6b7e0d42");
        private static readonly Guid MatrixTagGroupId = new Guid("d0c3b8a6-4f21-4b7e-8c55-9e2a1d64f713");

        /// <summary>3/32 inch in feet — the office annotation text size.</summary>
        private const double TextSizeFt = 3.0 / 32.0 / 12.0; // 0.0078125
        private const double SizeTolerance = 1e-6;

        // Simple, legible layout constants (view scale is forced to 1:1).
        private const double CharWidthFactor = 0.62;   // approximate glyph width at 3/32"
        private const double CellPadding = TextSizeFt; // left/right padding inside a column
        private const double RowHeight = TextSizeFt * 2.4;

        /// <summary>
        /// Creates or updates the drafting view. Must run OUTSIDE a transaction
        /// (it opens its own) and persists the model (view UniqueId) itself.
        /// Returns a human-readable summary for the result dialog.
        /// </summary>
        public string CreateOrUpdate(Document doc, OfficeMatrix matrix, ZfaProjectModel model)
        {
            if (matrix == null || matrix.Buildings.Count == 0)
            {
                return "Nothing to draw — compute the matrix first.";
            }

            var summary = new List<string>();
            ViewDrafting view;

            using (var tx = new Transaction(doc, "BauTools: ZFA matrix drafting view"))
            {
                tx.Start();

                view = ResolveView(doc, model, summary);
                model.Matrix.DraftingViewUniqueId = view.UniqueId;
                if (view.Scale != 1)
                {
                    view.Scale = 1; // plugin-owned view: text sizes are paper sizes
                }

                int deleted = DeleteMatrixElements(doc, view);
                if (deleted > 0)
                {
                    summary.Add($"{deleted} previously drawn element(s) replaced. User annotations were not touched.");
                }

                TextNoteType textType = FindOrCreateTextType(doc, summary);
                int drawn = Draw(doc, view, textType, matrix, model);
                summary.Insert(0, $"View '{view.Name}': {drawn} element(s) drawn.");

                new ZfaStorageService().Save(doc, model);
                tx.Commit();
            }

            summary.Add(DescribeSheetPlacement(doc, view));
            return string.Join("\n", summary);
        }

        // ── View & type resolution ────────────────────────────────────────────

        private static ViewDrafting ResolveView(Document doc, ZfaProjectModel model, List<string> summary)
        {
            if (!string.IsNullOrEmpty(model.Matrix.DraftingViewUniqueId))
            {
                if (doc.GetElement(model.Matrix.DraftingViewUniqueId) is ViewDrafting stored)
                {
                    return stored;
                }
                summary.Add("The previously used drafting view no longer exists — a new one was created.");
            }

            ViewFamilyType draftingType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .First(t => t.ViewFamily == ViewFamily.Drafting);
            ViewDrafting view = ViewDrafting.Create(doc, draftingType.Id);
            TrySetViewName(view, DefaultViewName);
            return view;
        }

        private static void TrySetViewName(ViewDrafting view, string desiredName)
        {
            try
            {
                view.Name = desiredName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // A view with that name already exists; keep the default name.
            }
        }

        /// <summary>
        /// Finds a TextNoteType whose Text Size equals 3/32" (tolerance 1e-6 ft);
        /// when none exists, duplicates one and sets the size.
        /// </summary>
        private static TextNoteType FindOrCreateTextType(Document doc, List<string> summary)
        {
            List<TextNoteType> types = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();

            TextNoteType match = types.FirstOrDefault(t =>
            {
                Parameter size = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                return size != null && Math.Abs(size.AsDouble() - TextSizeFt) < SizeTolerance;
            });
            if (match != null)
            {
                return match;
            }

            TextNoteType byName = types.FirstOrDefault(t => t.Name == TextTypeName);
            if (byName != null)
            {
                // Our named type exists but drifted in size — put it back.
                byName.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(TextSizeFt);
                return byName;
            }

            var created = (TextNoteType)types.First().Duplicate(TextTypeName);
            created.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(TextSizeFt);
            summary.Add($"Text type '{TextTypeName}' created (no existing type was 3/32\").");
            return created;
        }

        // ── Update-in-place ───────────────────────────────────────────────────

        private static int DeleteMatrixElements(Document doc, ViewDrafting view)
        {
            List<ElementId> ids = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => PropagatedElementTag.Matches(e, MatrixTagBuildingId, MatrixTagGroupId))
                .Select(e => e.Id)
                .ToList();
            if (ids.Count > 0)
            {
                doc.Delete(ids);
            }
            return ids.Count;
        }

        private static void Tag(Element element, DateTime runUtc)
        {
            PropagatedElementTag.Write(element, "zfa-matrix", MatrixTagBuildingId, MatrixTagGroupId, runUtc);
        }

        // ── Drawing ───────────────────────────────────────────────────────────

        private sealed class TableCell
        {
            public string Text;
            public bool RightAligned;
        }

        private int Draw(Document doc, ViewDrafting view, TextNoteType textType,
            OfficeMatrix matrix, ZfaProjectModel model)
        {
            DateTime runUtc = DateTime.UtcNow;
            int created = 0;

            int deductionCount = matrix.DeductionColumns.Count;
            int columnCount = 6 + deductionCount; // LEVEL, GROSS, COMMERCIAL, deductions…, NET, ENERGY, ZFA

            // ── Assemble the body rows (below the two header rows) ───────────
            var body = new List<TableCell[]>();          // rows of cells
            var separatorAfterBodyRow = new HashSet<int>(); // horizontal line under these body rows

            foreach (OfficeMatrixBuilding building in matrix.Buildings)
            {
                body.Add(TitleRow(columnCount, building.BuildingName));
                foreach (OfficeMatrixLine line in building.Levels)
                {
                    body.Add(DataRow(matrix, line, line.LevelName));
                }
                body.Add(DataRow(matrix, building.Totals, "TOTALS"));
                separatorAfterBodyRow.Add(body.Count - 1);
            }
            if (matrix.GrandTotal != null)
            {
                body.Add(DataRow(matrix, matrix.GrandTotal, "TOTAL — ALL BUILDINGS"));
                separatorAfterBodyRow.Add(body.Count - 1);
            }

            // ── Column widths from text lengths ──────────────────────────────
            string energyHeader = matrix.EnergyEnabled
                ? $"ENERGY DEDUCTION ({FormatPercent(matrix.EnergyPercent)}%)"
                : "ENERGY DEDUCTION (off)";
            var headers = new List<string> { "LEVEL", "GROSS FLOOR AREA", "COMMERCIAL GROSS AREA" };
            headers.AddRange(matrix.DeductionColumns.Select(c => c.ToUpperInvariant()));
            headers.Add("NET FLOOR AREA");
            headers.Add(energyHeader);
            headers.Add("ZONING FLOOR AREA");

            var widths = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                int longest = headers[c].Length;
                foreach (TableCell[] row in body.Where(r => r.Length == columnCount))
                {
                    longest = Math.Max(longest, (row[c].Text ?? "").Length);
                }
                widths[c] = longest * TextSizeFt * CharWidthFactor + 2 * CellPadding;
            }

            var columnX = new double[columnCount + 1];
            for (int c = 0; c < columnCount; c++)
            {
                columnX[c + 1] = columnX[c] + widths[c];
            }
            double tableWidth = columnX[columnCount];

            // ── Header (two rows: group labels, then deduction names) ────────
            double y = 0;
            int firstDeductionCol = 3;
            int netCol = 3 + deductionCount;

            // Row 1: LEVEL/GROSS/COMMERCIAL/NET/ENERGY/ZFA span both header rows;
            // DEDUCTIONS spans its columns horizontally.
            double headerMidY = y - RowHeight;                    // vertical center of the two rows
            foreach (int c in new[] { 0, 1, 2, netCol, netCol + 1, netCol + 2 })
            {
                created += Note(doc, view, textType,
                    columnX[c] + CellPadding, headerMidY, headers[c], false, runUtc);
            }
            if (deductionCount > 0)
            {
                double deductionsCenter =
                    (columnX[firstDeductionCol] + columnX[firstDeductionCol + deductionCount]) / 2;
                created += CenteredNote(doc, view, textType,
                    deductionsCenter, y - RowHeight * 0.5, "DEDUCTIONS", runUtc);
                for (int i = 0; i < deductionCount; i++)
                {
                    created += Note(doc, view, textType,
                        columnX[firstDeductionCol + i] + CellPadding, y - RowHeight * 1.5,
                        headers[firstDeductionCol + i], false, runUtc);
                }
                // Line separating "DEDUCTIONS" from the category names beneath it.
                created += DetailLine(doc, view,
                    new XYZ(columnX[firstDeductionCol], y - RowHeight, 0),
                    new XYZ(columnX[firstDeductionCol + deductionCount], y - RowHeight, 0), runUtc);
            }
            double headerBottom = y - 2 * RowHeight;

            // ── Body rows ─────────────────────────────────────────────────────
            double rowY = headerBottom;
            for (int r = 0; r < body.Count; r++)
            {
                TableCell[] row = body[r];
                double textY = rowY - RowHeight * 0.5;
                if (row.Length == 1)
                {
                    created += Note(doc, view, textType,
                        columnX[0] + CellPadding, textY, row[0].Text, false, runUtc);
                }
                else
                {
                    for (int c = 0; c < columnCount; c++)
                    {
                        if (string.IsNullOrEmpty(row[c].Text))
                        {
                            continue; // blank cell, like the office sheet
                        }
                        created += row[c].RightAligned
                            ? Note(doc, view, textType,
                                columnX[c + 1] - CellPadding, textY, row[c].Text, true, runUtc)
                            : Note(doc, view, textType,
                                columnX[c] + CellPadding, textY, row[c].Text, false, runUtc);
                    }
                }
                rowY -= RowHeight;
                if (separatorAfterBodyRow.Contains(r))
                {
                    created += DetailLine(doc, view,
                        new XYZ(0, rowY, 0), new XYZ(tableWidth, rowY, 0), runUtc);
                }
            }
            double tableBottom = rowY;

            // ── Grid: outer box, header underline, column verticals ──────────
            created += DetailLine(doc, view, new XYZ(0, 0, 0), new XYZ(tableWidth, 0, 0), runUtc);
            created += DetailLine(doc, view,
                new XYZ(0, headerBottom, 0), new XYZ(tableWidth, headerBottom, 0), runUtc);
            for (int c = 0; c <= columnCount; c++)
            {
                created += DetailLine(doc, view,
                    new XYZ(columnX[c], 0, 0), new XYZ(columnX[c], tableBottom, 0), runUtc);
            }

            // ── Footer ────────────────────────────────────────────────────────
            DateTime stamp = (model.Matrix.LastComputedUtc ?? runUtc).ToLocalTime();
            string user = model.Matrix.LastComputedBy ?? Environment.UserName;
            created += Note(doc, view, textType, 0, tableBottom - RowHeight,
                $"Computed {stamp:yyyy-MM-dd HH:mm} · {user} · BauTools ZFA — updates in place, do not edit by hand",
                false, runUtc);

            return created;
        }

        private static TableCell[] TitleRow(int columnCount, string text) =>
            new[] { new TableCell { Text = text ?? "" } };

        private TableCell[] DataRow(OfficeMatrix matrix, OfficeMatrixLine line, string label)
        {
            var cells = new List<TableCell>
            {
                new TableCell { Text = label },
                new TableCell { Text = Format(line.MainGross), RightAligned = true },
                new TableCell { Text = Format(line.CommercialGross), RightAligned = true }
            };
            foreach (string column in matrix.DeductionColumns)
            {
                cells.Add(new TableCell
                {
                    Text = line.Deductions.TryGetValue(column, out double deducted) ? Format(deducted) : "",
                    RightAligned = true
                });
            }
            cells.Add(new TableCell { Text = Format(line.Net), RightAligned = true });
            cells.Add(new TableCell
            {
                Text = matrix.EnergyEnabled ? Format(line.Energy) : "",
                RightAligned = true
            });
            cells.Add(new TableCell { Text = Format(line.Zfa), RightAligned = true });
            return cells.ToArray();
        }

        private static string Format(double value) =>
            value.ToString("N2", CultureInfo.InvariantCulture);

        private static string Format(double? value) => value.HasValue ? Format(value.Value) : "";

        private static string FormatPercent(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private int Note(Document doc, ViewDrafting view, TextNoteType type,
            double x, double y, string text, bool rightAligned, DateTime runUtc)
        {
            TextNote note = TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, type.Id);
            note.HorizontalAlignment = rightAligned
                ? HorizontalTextAlignment.Right
                : HorizontalTextAlignment.Left;
            Tag(note, runUtc);
            return 1;
        }

        private int CenteredNote(Document doc, ViewDrafting view, TextNoteType type,
            double x, double y, string text, DateTime runUtc)
        {
            TextNote note = TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, type.Id);
            note.HorizontalAlignment = HorizontalTextAlignment.Center;
            Tag(note, runUtc);
            return 1;
        }

        private int DetailLine(Document doc, ViewDrafting view, XYZ from, XYZ to, DateTime runUtc)
        {
            if (from.DistanceTo(to) < doc.Application.ShortCurveTolerance)
            {
                return 0;
            }
            DetailCurve curve = doc.Create.NewDetailCurve(view, Line.CreateBound(from, to));
            Tag(curve, runUtc);
            return 1;
        }

        // ── Sheet placement report ────────────────────────────────────────────

        private static string DescribeSheetPlacement(Document doc, ViewDrafting view)
        {
            Viewport viewport = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .FirstOrDefault(v => v.ViewId == view.Id);
            if (viewport == null)
            {
                return "Not on a sheet yet — place it once; future updates happen in place.";
            }
            var sheet = doc.GetElement(viewport.SheetId) as ViewSheet;
            string label = sheet == null ? "a sheet" : $"sheet {sheet.SheetNumber} — {sheet.Name}";
            return $"Placed on {label} — no need to re-place; future updates happen in place.";
        }
    }
}
