using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Export
{
    /// <summary>
    /// Seam for the export format. Both implementations render the same
    /// OfficeMatrix the UI table shows — CSV as plain rows, XLSX with live
    /// formulas mirroring the office sheet.
    /// </summary>
    public interface IZfaMatrixExporter
    {
        string FileExtension { get; }

        void Export(string filePath, OfficeMatrix matrix, ZoningParameters parameters);
    }
}
