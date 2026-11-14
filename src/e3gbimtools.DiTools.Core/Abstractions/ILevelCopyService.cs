using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Levels;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Servicio CopyLevels: lista niveles existentes en un vinculo y copia los
/// seleccionados al documento anfitrion.  Toda la logica de transacciones se
/// canaliza por <see cref="ITransactionService"/> para que la operacion sea
/// deshacible con un solo Ctrl-Z desde Revit.
/// </summary>
public interface ILevelCopyService
{
    /// <summary>
    /// Recoge todos los vinculos cargados del documento anfitrion (RevitLinkInstance
    /// con LoadedStatus.Loaded) y los presenta como una lista de pares
    /// (instanceId, etiqueta legible).  La etiqueta combina el nombre del tipo y,
    /// si existen varias instancias del mismo vinculo, distingue por instanceId.
    /// </summary>
    IReadOnlyList<RevitLinkOption> GetLoadedLinks(Document hostDocument);

    /// <summary>
    /// Lee los niveles del vinculo identificado por <paramref name="linkInstanceId"/>
    /// y devuelve descriptores ligeros (sin retener referencias a Element del link).
    /// Marca <see cref="LevelDescriptor.ExistsInHost"/> si en el anfitrion ya hay
    /// un nivel con el mismo nombre.
    /// </summary>
    IReadOnlyList<LevelDescriptor> GetLevelsFromLink(Document hostDocument, long linkInstanceId);

    /// <summary>
    /// Copia los niveles indicados al anfitrion aplicando las opciones de
    /// prefijo/sufijo y, opcionalmente, creando una vista de planta por nivel.
    /// Toda la operacion va en una unica TransactionGroup nombrada para que
    /// el undo del usuario la deshaga de un golpe.
    /// </summary>
    LevelCopyResult CopyLevels(
        Document hostDocument,
        long linkInstanceId,
        IReadOnlyList<LevelDescriptor> levelsToCopy,
        LevelCopyOptions options);
}

/// <summary>
/// Opcion del combo de vinculos disponibles.  La UI lo enlaza directamente.
/// </summary>
public sealed class RevitLinkOption
{
    public long InstanceId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}
