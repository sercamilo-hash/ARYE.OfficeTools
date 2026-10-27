using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Export
{
    /// <summary>
    /// Writes the office-format ZFA matrix to a CSV that Excel opens directly:
    /// per-building blocks with a TOTALS row, dynamic deduction columns, a
    /// grand-total row and the FAR check. Values only — the formulated export
    /// is <see cref="XlsxMatrixExporter"/>.
    /// </summary>
    public sealed class CsvMatrixExporter : IZfaMatrixExporter
    {
        public string FileExtension => ".csv";

        public void Export(string filePath, OfficeMatrix matrix, ZoningParameters parameters)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var sb = new StringBuilder();

            sb.AppendLine("BauTools - Zoning Floor Area Matrix");
            sb.AppendLine("Generated," + Escape(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            sb.AppendLine("District," + Escape(parameters.District ?? ""));
            sb.AppendLine("Lot area (sf)," + Number(parameters.LotAreaSqFt));
            sb.AppendLine("Max FAR," + Number(parameters.MaxFar));
            sb.AppendLine("Energy deduction," + (matrix.EnergyEnabled
                ? Number(matrix.EnergyPercent) + "% of NET"
                : "disabled"));
            sb.AppendLine();

            sb.Append("BUILDING,LEVEL,GROSS FLOOR AREA,COMMERCIAL GROSS AREA");
            foreach (string column in matrix.DeductionColumns)
            {
                sb.Append(',').Append(Escape(column.ToUpperInvariant()));
            }
            sb.AppendLine(",NET FLOOR AREA,ENERGY DEDUCTION,ZONING FLOOR AREA");

            foreach (OfficeMatrixBuilding building in matrix.Buildings)
            {
                foreach (OfficeMatrixLine line in building.Levels)
                {
                    AppendLine(sb, matrix, building.BuildingName, line);
                }
                AppendLine(sb, matrix, building.BuildingName, building.Totals);
            }
            if (matrix.GrandTotal != null)
            {
                AppendLine(sb, matrix, "", matrix.GrandTotal);
            }

            sb.AppendLine();
            sb.AppendLine("Allowed ZFA (lot x FAR)," + Number(matrix.AllowedZfaSqFt));
            sb.AppendLine("Remaining," + Number(matrix.RemainingSqFt));
            sb.AppendLine("Over FAR," + (matrix.IsOverFar ? "YES" : "no"));
            if (matrix.UnclassifiedAreaCount > 0)
            {
                sb.AppendLine("WARNING," + Escape(
                    $"{matrix.UnclassifiedAreaCount} unclassified area(s) counted as gross — totals are not final"));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void AppendLine(StringBuilder sb, OfficeMatrix matrix,
            string buildingName, OfficeMatrixLine line)
        {
            sb.Append(Escape(buildingName)).Append(',')
              .Append(Escape(line.LevelName)).Append(',')
              .Append(Number(line.MainGross)).Append(',')
              .Append(Number(line.CommercialGross));
            foreach (string column in matrix.DeductionColumns)
            {
                sb.Append(',');
                if (line.Deductions.TryGetValue(column, out double deducted))
                {
                    sb.Append(Number(deducted));
                }
            }
            sb.Append(',').Append(Number(line.Net))
              .Append(',').Append(matrix.EnergyEnabled ? Number(line.Energy) : "")
              .Append(',').Append(Number(line.Zfa))
              .AppendLine();
        }

        private static string Number(double value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Nullable cells stay empty — the office sheet leaves them blank, not 0.00.</summary>
        private static string Number(double? value) =>
            value.HasValue ? Number(value.Value) : string.Empty;

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }
    }
}
