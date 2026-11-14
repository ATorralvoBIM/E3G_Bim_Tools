using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Levels;

/// <summary>
/// Resultado consolidado de una operacion CopyLevels.  Pensado para resumir
/// al usuario en la UI ("Se copiaron N niveles, se omitieron M por colision...").
/// </summary>
public sealed class LevelCopyResult
{
    /// <summary>Niveles copiados con exito.</summary>
    public int Created { get; set; }

    /// <summary>
    /// Niveles omitidos porque ya existia uno con el mismo nombre final en el anfitrion
    /// (se evita duplicar; el usuario puede ajustar prefijo/sufijo y reintentar).
    /// </summary>
    public int SkippedExisting { get; set; }

    /// <summary>Vistas de planta creadas (0 si la opcion estaba desactivada).</summary>
    public int FloorPlansCreated { get; set; }

    /// <summary>Mensajes de error o advertencia individuales (por nivel) — para mostrar en un FriendlyErrorDialog si los hay.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Resumen humano usado en la barra de estado / mensaje final.</summary>
    public string ToSummary()
    {
        var msg = $"{Created} nivel(es) copiado(s)";
        if (SkippedExisting > 0) msg += $", {SkippedExisting} omitido(s) por colision de nombre";
        if (FloorPlansCreated > 0) msg += $", {FloorPlansCreated} vista(s) de planta creada(s)";
        if (Warnings.Count > 0) msg += $" · {Warnings.Count} aviso(s)";
        return msg + ".";
    }
}
