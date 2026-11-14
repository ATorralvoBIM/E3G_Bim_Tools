using System;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Envoltorio sobre <see cref="Transaction"/> y <see cref="TransactionGroup"/>
/// que estandariza los nombres de transaccion (visibles en el undo stack
/// del usuario) y centraliza el rollback ante excepciones.
/// </summary>
public interface ITransactionService
{
    /// <summary>Ejecuta <paramref name="action"/> dentro de una Transaction nombrada.</summary>
    void Run(Document doc, string transactionName, Action<Document> action);

    /// <summary>Ejecuta <paramref name="action"/> dentro de una Transaction nombrada y devuelve un resultado.</summary>
    T Run<T>(Document doc, string transactionName, Func<Document, T> action);

    /// <summary>
    /// Ejecuta <paramref name="action"/> dentro de un TransactionGroup nombrado
    /// (para operaciones compuestas que el usuario quiere deshacer con un solo Ctrl-Z).
    /// </summary>
    void RunGroup(Document doc, string groupName, Action action);
}
