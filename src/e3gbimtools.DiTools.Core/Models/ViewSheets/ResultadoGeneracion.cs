using System.Collections.Generic;
using System.Linq;

namespace E3G.BimTools.DiTools.Core.Models.ViewSheets;

/// <summary>
/// Resultado de UNA combinación instalación × nivel.  El generador acumula una
/// instancia por cada par procesado (con éxito o error) para mostrar el detalle
/// al usuario al terminar el lote.
/// </summary>
public sealed class ResultadoGeneracion
{
    public string Instalacion { get; set; } = string.Empty;
    public string Nivel { get; set; } = string.Empty;

    /// <summary>True si se creó la vista, la hoja y se colocó el viewport.</summary>
    public bool Exito { get; set; }

    /// <summary>Motivo del error o aviso ("" si todo correcto).</summary>
    public string Motivo { get; set; } = string.Empty;

    public string NombreVista { get; set; } = string.Empty;
    public string NumeroHoja { get; set; } = string.Empty;

    /// <summary><c>ElementId.Value</c> de la hoja creada (-1 si no se creó). Para el thumbnail.</summary>
    public long HojaId { get; set; } = -1;

    /// <summary>Texto compacto para el icono de estado en la UI.</summary>
    public string Estado => Exito ? "✓" : "✗";
}

/// <summary>
/// Resumen agregado de toda la ejecución del generador.  Es lo que la UI muestra
/// al final ("X hojas creadas, Y errores") junto con la lista detallada.
/// </summary>
public sealed class GeneracionResumen
{
    public IList<ResultadoGeneracion> Resultados { get; } = new List<ResultadoGeneracion>();

    /// <summary>Combinaciones completadas con éxito (hoja + vista + viewport).</summary>
    public int Creadas => Resultados.Count(r => r.Exito);

    /// <summary>Combinaciones con error.</summary>
    public int Errores => Resultados.Count(r => !r.Exito);

    /// <summary>Tiempo total de la operación en milisegundos.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Mensaje global (ej. precondición fallida que abortó todo el lote).</summary>
    public string Mensaje { get; set; } = string.Empty;

    public string ToSummary() =>
        string.IsNullOrEmpty(Mensaje)
            ? $"{Creadas} hoja(s) creadas, {Errores} con error.  ({ElapsedMs} ms)"
            : Mensaje;
}
