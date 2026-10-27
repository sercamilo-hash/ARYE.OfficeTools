using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Export
{
    /// <summary>
    /// Writes the office-format matrix as a real .xlsx with LIVE FORMULAS —
    /// no external packages: the file is a ZipArchive holding hand-built
    /// SpreadsheetML parts (netstandard2.0 BCL only, so it loads under both
    /// Revit 2024/net48 and 2025+/net8 without adding a dependency).
    ///
    /// Layout mirrors the office sheet: a labeled energy-percent cell that the
    /// ENERGY formulas reference, a two-row header with a merged DEDUCTIONS
    /// span, yellow fill on the ENERGY column, per-building blocks with TOTALS
    /// rows, and a grand-total row that sums the buildings' TOTALS cells.
    /// Every formula cell also carries its computed cached value, so the file
    /// opens showing numbers even before Excel recalculates.
    /// </summary>
    public sealed class XlsxMatrixExporter : IZfaMatrixExporter
    {
        // Style indexes — must match the cellXfs order in BuildStylesXml().
        private const int StyleDefault = 0;
        private const int StyleBold = 1;
        private const int StyleNumber = 2;
        private const int StyleNumberEnergy = 3;
        private const int StyleNumberBold = 4;
        private const int StyleNumberBoldEnergy = 5;
        private const int StyleHeader = 6;
        private const int StyleHeaderEnergy = 7;

        /// <summary>The cell holding the energy percentage that every ENERGY formula references.</summary>
        private const string EnergyPercentCellRef = "$B$5";

        public string FileExtension => ".xlsx";

        public void Export(string filePath, OfficeMatrix matrix, ZoningParameters parameters)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            using (FileStream stream = File.Create(filePath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
                WriteEntry(zip, "_rels/.rels", RootRelsXml);
                WriteEntry(zip, "xl/workbook.xml", WorkbookXml);
                WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
                WriteEntry(zip, "xl/styles.xml", BuildStylesXml());
                WriteEntry(zip, "xl/worksheets/sheet1.xml", BuildSheetXml(matrix, parameters));
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        // ── Worksheet ─────────────────────────────────────────────────────────

        private static string BuildSheetXml(OfficeMatrix matrix, ZoningParameters parameters)
        {
            int deductionCount = matrix.DeductionColumns.Count;
            // 1-based columns: A LEVEL, B GROSS, C COMMERCIAL, D.. deductions, then NET/ENERGY/ZFA.
            int firstDeductionCol = 4;
            int lastDeductionCol = 3 + deductionCount;
            int netCol = 4 + deductionCount;
            int energyCol = 5 + deductionCount;
            int zfaCol = 6 + deductionCount;

            var rows = new List<string>();
            var merges = new List<string>();

            // ── Meta block (rows 1–6) ────────────────────────────────────────
            rows.Add(Row(1, Cell("A", 1, StyleBold, "ZONING FLOOR AREA — BauTools")));
            rows.Add(Row(2,
                Cell("A", 2, StyleDefault, "District"),
                Cell("B", 2, StyleDefault, parameters.District ?? "")));
            rows.Add(Row(3,
                Cell("A", 3, StyleDefault, "Lot area (sf)"),
                NumberCell("B", 3, StyleNumber, parameters.LotAreaSqFt)));
            rows.Add(Row(4,
                Cell("A", 4, StyleDefault, "Max FAR"),
                NumberCell("B", 4, StyleNumber, parameters.MaxFar)));
            // The labeled percent cell the ENERGY formulas reference. 0 keeps
            // the formulas alive while the deduction is disabled.
            rows.Add(Row(5,
                Cell("A", 5, StyleDefault, "Energy deduction % of NET (0 = disabled)"),
                NumberCell("B", 5, StyleNumberEnergy, matrix.EnergyEnabled ? matrix.EnergyPercent : 0)));
            rows.Add(Row(6,
                Cell("A", 6, StyleDefault, "Allowed ZFA (lot × FAR)"),
                FormulaCell("B", 6, StyleNumberBold, "B3*B4", matrix.AllowedZfaSqFt)));

            // ── Two-row header (rows 8–9) ────────────────────────────────────
            var header1 = new List<string>
            {
                Cell(ColumnLetter(1), 8, StyleHeader, "LEVEL"),
                Cell(ColumnLetter(2), 8, StyleHeader, "GROSS FLOOR AREA"),
                Cell(ColumnLetter(3), 8, StyleHeader, "COMMERCIAL GROSS AREA")
            };
            if (deductionCount > 0)
            {
                header1.Add(Cell(ColumnLetter(firstDeductionCol), 8, StyleHeader, "DEDUCTIONS"));
                if (deductionCount > 1)
                {
                    merges.Add($"{ColumnLetter(firstDeductionCol)}8:{ColumnLetter(lastDeductionCol)}8");
                }
            }
            header1.Add(Cell(ColumnLetter(netCol), 8, StyleHeader, "NET FLOOR AREA"));
            header1.Add(Cell(ColumnLetter(energyCol), 8, StyleHeaderEnergy, "ENERGY DEDUCTION"));
            header1.Add(Cell(ColumnLetter(zfaCol), 8, StyleHeader, "ZONING FLOOR AREA"));
            rows.Add(Row(8, header1.ToArray()));

            var header2 = new List<string>();
            for (int i = 0; i < deductionCount; i++)
            {
                header2.Add(Cell(ColumnLetter(firstDeductionCol + i), 9, StyleHeader,
                    matrix.DeductionColumns[i].ToUpperInvariant()));
            }
            rows.Add(Row(9, header2.ToArray()));

            // Vertical merges for the single-label columns spanning both header rows.
            foreach (int col in new[] { 1, 2, 3, netCol, energyCol, zfaCol })
            {
                merges.Add($"{ColumnLetter(col)}8:{ColumnLetter(col)}9");
            }

            // ── Building blocks ──────────────────────────────────────────────
            int nextRow = 10;
            var buildingTotalsRows = new List<int>();

            foreach (OfficeMatrixBuilding building in matrix.Buildings)
            {
                int nameRow = nextRow++;
                rows.Add(Row(nameRow, Cell("A", nameRow, StyleBold, building.BuildingName ?? "")));
                merges.Add($"A{nameRow}:{ColumnLetter(zfaCol)}{nameRow}");

                int firstLevelRow = nextRow;
                foreach (OfficeMatrixLine line in building.Levels)
                {
                    rows.Add(BuildLevelRow(matrix, line, nextRow,
                        firstDeductionCol, lastDeductionCol, netCol, energyCol, zfaCol));
                    nextRow++;
                }
                int lastLevelRow = nextRow - 1;

                int totalsRow = nextRow++;
                buildingTotalsRows.Add(totalsRow);
                rows.Add(BuildSumRow("TOTALS", totalsRow, matrix, building.Totals,
                    firstDeductionCol, netCol, energyCol, zfaCol,
                    col => firstLevelRow <= lastLevelRow
                        ? $"SUM({ColumnLetter(col)}{firstLevelRow}:{ColumnLetter(col)}{lastLevelRow})"
                        : "0"));

                nextRow++; // blank spacer row between buildings
            }

            // ── Grand total: sums the buildings' TOTALS cells ────────────────
            if (matrix.GrandTotal != null)
            {
                int grandRow = nextRow++;
                rows.Add(BuildSumRow("TOTAL — ALL BUILDINGS", grandRow, matrix, matrix.GrandTotal,
                    firstDeductionCol, netCol, energyCol, zfaCol,
                    col => string.Join("+",
                        buildingTotalsRows.Select(r => ColumnLetter(col) + r.ToString(CultureInfo.InvariantCulture)))));
            }

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<cols>");
            sb.Append(Col(1, 1, 22)); // LEVEL (also the meta labels)
            sb.Append(Col(2, zfaCol, 16));
            sb.Append("</cols>");
            sb.Append("<sheetData>");
            foreach (string row in rows)
            {
                sb.Append(row);
            }
            sb.Append("</sheetData>");
            if (merges.Count > 0)
            {
                sb.Append("<mergeCells count=\"").Append(merges.Count).Append("\">");
                foreach (string merge in merges)
                {
                    sb.Append("<mergeCell ref=\"").Append(merge).Append("\"/>");
                }
                sb.Append("</mergeCells>");
            }
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static string BuildLevelRow(OfficeMatrix matrix, OfficeMatrixLine line, int r,
            int firstDeductionCol, int lastDeductionCol, int netCol, int energyCol, int zfaCol)
        {
            var cells = new List<string> { Cell("A", r, StyleDefault, line.LevelName ?? "") };
            if (line.MainGross.HasValue)
            {
                cells.Add(NumberCell("B", r, StyleNumber, line.MainGross.Value));
            }
            if (line.CommercialGross.HasValue)
            {
                cells.Add(NumberCell("C", r, StyleNumber, line.CommercialGross.Value));
            }
            for (int i = 0; i < matrix.DeductionColumns.Count; i++)
            {
                if (line.Deductions.TryGetValue(matrix.DeductionColumns[i], out double deducted))
                {
                    cells.Add(NumberCell(ColumnLetter(firstDeductionCol + i), r, StyleNumber, deducted));
                }
            }

            string netFormula = matrix.DeductionColumns.Count > 0
                ? $"B{r}-SUM({ColumnLetter(firstDeductionCol)}{r}:{ColumnLetter(lastDeductionCol)}{r})"
                : $"B{r}";
            cells.Add(FormulaCell(ColumnLetter(netCol), r, StyleNumber, netFormula, line.Net));
            cells.Add(FormulaCell(ColumnLetter(energyCol), r, StyleNumberEnergy,
                $"{ColumnLetter(netCol)}{r}*{EnergyPercentCellRef}/100", line.Energy));
            cells.Add(FormulaCell(ColumnLetter(zfaCol), r, StyleNumber,
                $"{ColumnLetter(netCol)}{r}-{ColumnLetter(energyCol)}{r}", line.Zfa));
            return Row(r, cells.ToArray());
        }

        /// <summary>A TOTALS or grand-total row: every numeric column is a formula over other rows.</summary>
        private static string BuildSumRow(string label, int r, OfficeMatrix matrix, OfficeMatrixLine totals,
            int firstDeductionCol, int netCol, int energyCol, int zfaCol,
            Func<int, string> formulaForColumn)
        {
            var cells = new List<string>
            {
                Cell("A", r, StyleBold, label),
                FormulaCell("B", r, StyleNumberBold, formulaForColumn(2), totals.MainGross ?? 0),
                FormulaCell("C", r, StyleNumberBold, formulaForColumn(3), totals.CommercialGross ?? 0)
            };
            for (int i = 0; i < matrix.DeductionColumns.Count; i++)
            {
                totals.Deductions.TryGetValue(matrix.DeductionColumns[i], out double deducted);
                int col = firstDeductionCol + i;
                cells.Add(FormulaCell(ColumnLetter(col), r, StyleNumberBold, formulaForColumn(col), deducted));
            }
            cells.Add(FormulaCell(ColumnLetter(netCol), r, StyleNumberBold, formulaForColumn(netCol), totals.Net));
            cells.Add(FormulaCell(ColumnLetter(energyCol), r, StyleNumberBoldEnergy,
                formulaForColumn(energyCol), totals.Energy));
            cells.Add(FormulaCell(ColumnLetter(zfaCol), r, StyleNumberBold, formulaForColumn(zfaCol), totals.Zfa));
            return Row(r, cells.ToArray());
        }

        // ── SpreadsheetML fragments ───────────────────────────────────────────

        private static string Row(int r, params string[] cells) =>
            $"<row r=\"{r.ToString(CultureInfo.InvariantCulture)}\">{string.Concat(cells)}</row>";

        private static string Cell(string column, int row, int style, string text) =>
            $"<c r=\"{column}{row.ToString(CultureInfo.InvariantCulture)}\" s=\"{style}\" t=\"inlineStr\">" +
            $"<is><t>{XmlEscape(text)}</t></is></c>";

        private static string NumberCell(string column, int row, int style, double value) =>
            $"<c r=\"{column}{row.ToString(CultureInfo.InvariantCulture)}\" s=\"{style}\">" +
            $"<v>{NumberText(value)}</v></c>";

        private static string FormulaCell(string column, int row, int style, string formula, double cachedValue) =>
            $"<c r=\"{column}{row.ToString(CultureInfo.InvariantCulture)}\" s=\"{style}\">" +
            $"<f>{XmlEscape(formula)}</f><v>{NumberText(cachedValue)}</v></c>";

        private static string Col(int min, int max, double width) =>
            $"<col min=\"{min}\" max=\"{max}\" width=\"{width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>";

        private static string NumberText(double value) =>
            Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>1 → A, 2 → B, …, 27 → AA. The matrix never nears the two-letter limit.</summary>
        public static string ColumnLetter(int columnOneBased)
        {
            if (columnOneBased < 1) throw new ArgumentOutOfRangeException(nameof(columnOneBased));
            var sb = new StringBuilder();
            int n = columnOneBased;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }

        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        // ── Static parts ──────────────────────────────────────────────────────

        private const string ContentTypesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        private const string RootRelsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string WorkbookXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"ZFA Matrix\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "<calcPr fullCalcOnLoad=\"1\"/>" +
            "</workbook>";

        private const string WorkbookRelsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        /// <summary>
        /// Minimal style sheet. numFmtId 4 is the built-in "#,##0.00".
        /// Fill 2 is the pale yellow of the ENERGY column.
        /// </summary>
        private static string BuildStylesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"2\">" +
            "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
            "</fonts>" +
            "<fills count=\"3\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF2CC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"8\">" +
            // 0 default
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            // 1 bold
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            // 2 number
            "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            // 3 number + energy fill
            "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\"/>" +
            // 4 bold number
            "<xf numFmtId=\"4\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
            // 5 bold number + energy fill
            "<xf numFmtId=\"4\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\"/>" +
            // 6 header: bold, centered, wrapped
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\">" +
            "<alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            // 7 header + energy fill
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\">" +
            "<alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";
    }
}
