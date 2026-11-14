// ============================================================================
//  IReportExporter.cs
//  Contrato común a PDF y DOCX (y futuros formatos).  Recibe un ReportData
//  ya construido y lo escribe a un Stream o a una ruta.
// ============================================================================
using System.IO;

namespace E3G.Saneamiento.Reports
{
    public interface IReportExporter
    {
        /// <summary>Extensión sin punto: "pdf", "docx".</summary>
        string FileExtension { get; }
        /// <summary>MIME type para diálogos / preview en navegador.</summary>
        string MimeType { get; }

        /// <summary>Escribe el informe a la ruta dada (sobrescribe si existe).</summary>
        void Export(ReportData data, string outputPath);

        /// <summary>Escribe el informe a un Stream — útil para tests o
        /// integraciones que evitan el FS.</summary>
        void Export(ReportData data, Stream output);
    }
}
