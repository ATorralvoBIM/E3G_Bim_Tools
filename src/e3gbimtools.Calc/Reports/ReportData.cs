// ============================================================================
//  ReportData.cs
//  DTOs del modelo de informe — agnóstico del formato (PDF, DOCX).
//
//  El ReportBuilder construye una instancia de ReportData; cada exporter
//  (PDF, DOCX) la traduce a su lenguaje sin tocar el dominio.  Mantener este
//  modelo simple y sin lógica de presentación es la clave para que añadir un
//  tercer formato (HTML, ODT…) sea sustituir el exporter, no el modelo.
//
//  Convenciones:
//    - Coordenadas e imágenes en píxeles (la conversión a mm/in es del exporter).
//    - Strings ya formateados (con su separador decimal regional).
//    - Null y string.Empty se tratan igual: se omite la sección/celda.
// ============================================================================
using System;
using System.Collections.Generic;

namespace e3gbimtools.Calc.Reports
{
    /// <summary>Documento completo de informe.</summary>
    public sealed class ReportData
    {
        public ReportBranding Branding { get; set; } = new ReportBranding();
        public ReportCover    Cover    { get; set; } = new ReportCover();
        public List<ReportSection> Sections { get; set; } = new List<ReportSection>();
        public List<ReportAnnex>   Annexes  { get; set; } = new List<ReportAnnex>();
    }

    /// <summary>Identidad visual del informe — logo, colores, footer.</summary>
    public sealed class ReportBranding
    {
        public string CompanyName   { get; set; } = "E3G Ingeniería";
        public string CompanyWeb    { get; set; } = "";
        public string CompanyCif    { get; set; } = "";
        /// <summary>Bytes PNG/JPG del logo (300×80 px sugerido). null = sin logo.</summary>
        public byte[] LogoBytes     { get; set; }
        public string PrimaryColor  { get; set; } = "#1A3A5E";   // azul corporativo
        public string AccentColor   { get; set; } = "#C62828";   // rojo crítico
        public string MutedColor    { get; set; } = "#888888";
        public string FooterText    { get; set; } = "";
        public string DocReference  { get; set; } = "";          // ej. "DOC-MEP-001"
    }

    /// <summary>Portada del informe.</summary>
    public sealed class ReportCover
    {
        public string Title       { get; set; } = "Informe de cálculo MEP";
        public string Subtitle    { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string ProjectCode { get; set; } = "";
        public string ClientName  { get; set; } = "";
        public string LocationStr { get; set; } = "";
        public string AuthorName  { get; set; } = "";
        public string AuthorRole  { get; set; } = "";
        public DateTime IssueDate { get; set; } = DateTime.Now;
        public string Revision    { get; set; } = "00";
        public string Norm        { get; set; } = "CTE DB-HS4";   // o "RITE"
        /// <summary>Imagen opcional (PNG/JPG) que va bajo el título — p.ej. portada del esquema.</summary>
        public byte[] HeroImage   { get; set; }
    }

    /// <summary>Una sección del informe (capítulo).  Puede llevar texto,
    /// criterios, tabla, imagen del esquema o camino crítico.</summary>
    public sealed class ReportSection
    {
        public string Title    { get; set; } = "";
        /// <summary>Numeración manual ("3.1", "Anexo A"). Vacío = sin numerar.</summary>
        public string Numbering{ get; set; } = "";
        /// <summary>Párrafos de prosa (uno por entrada).</summary>
        public List<string> Paragraphs { get; set; } = new List<string>();
        /// <summary>Criterios aplicados — pares clave/valor.</summary>
        public List<ReportCriterion> Criteria { get; set; } = new List<ReportCriterion>();
        /// <summary>Tabla de datos (cabecera + filas).</summary>
        public ReportTable Table { get; set; }
        /// <summary>Imágenes embebidas (esquema, gráficos).</summary>
        public List<ReportImage> Images { get; set; } = new List<ReportImage>();
        /// <summary>Bloque de camino crítico — null si la sección no lo lleva.</summary>
        public ReportCriticalPath CriticalPath { get; set; }
        /// <summary>Forzar salto de página antes de esta sección.</summary>
        public bool PageBreakBefore { get; set; } = true;
    }

    /// <summary>Criterio aplicado: clave + valor + unidad opcional + notas.</summary>
    public sealed class ReportCriterion
    {
        public string Key   { get; set; } = "";
        public string Value { get; set; } = "";
        public string Unit  { get; set; } = "";
        public string Note  { get; set; } = "";
    }

    /// <summary>Tabla genérica de datos.  Filas y columnas son strings ya
    /// formateados; el exporter solo se ocupa de pintarlos.</summary>
    public sealed class ReportTable
    {
        public List<ReportColumn> Columns { get; set; } = new List<ReportColumn>();
        public List<ReportTableRow> Rows  { get; set; } = new List<ReportTableRow>();
        /// <summary>Repetir cabecera en cada página (PDF) / sección (DOCX).</summary>
        public bool RepeatHeader { get; set; } = true;
    }

    public sealed class ReportColumn
    {
        public string Header { get; set; } = "";
        /// <summary>Peso relativo (1f, 2f, …) — el exporter calcula anchos.</summary>
        public float  Weight { get; set; } = 1f;
        public ReportAlign Align { get; set; } = ReportAlign.Left;
    }

    public enum ReportAlign { Left, Center, Right }

    public sealed class ReportTableRow
    {
        public List<string> Cells { get; set; } = new List<string>();
        /// <summary>Severidad opcional para coloreado (OK/Warning/Error).</summary>
        public ReportRowSeverity Severity { get; set; } = ReportRowSeverity.None;
        /// <summary>Marca como "accesorio" para estilo distinto (cursiva atenuada).</summary>
        public bool IsFitting { get; set; }
    }

    public enum ReportRowSeverity { None, Ok, Warning, Error }

    /// <summary>Imagen embebida.</summary>
    public sealed class ReportImage
    {
        public byte[] Bytes      { get; set; }
        /// <summary>Pie de figura ("Figura 3-1: esquema de principio").</summary>
        public string Caption    { get; set; } = "";
        /// <summary>Anchura objetivo en mm; 0 = ancho disponible.</summary>
        public float WidthMm     { get; set; } = 0;
        /// <summary>Forzar página propia (landscape automático en PDF).</summary>
        public bool FullPage     { get; set; } = false;
    }

    /// <summary>Bloque de camino crítico — secuencia de nodos con sus ΔP.</summary>
    public sealed class ReportCriticalPath
    {
        public string Summary { get; set; } = "";       // "ΔP peor caso: 38.2 kPa"
        public List<ReportCriticalPathStep> Steps { get; set; } = new List<ReportCriticalPathStep>();
    }

    public sealed class ReportCriticalPathStep
    {
        public int    Order   { get; set; }
        public string Element { get; set; } = "";       // "Tramo N3 → N4"
        public string Size    { get; set; } = "";
        public string FlowLps { get; set; } = "";
        public string DpPa    { get; set; } = "";
        public string AccPa   { get; set; } = "";       // ΔP acumulada
        public string Note    { get; set; } = "";
    }

    /// <summary>Anexo (tabla larga, listado, documento técnico).</summary>
    public sealed class ReportAnnex
    {
        public string Letter   { get; set; } = "A";     // "A", "B", "C"
        public string Title    { get; set; } = "";
        public List<ReportSection> Sections { get; set; } = new List<ReportSection>();
    }
}
