using System;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Core.Services;

/// <summary>
/// Implementacion de <see cref="ITransactionService"/>: estandariza nombres
/// de Transaction (visibles en el undo stack del usuario) y garantiza rollback
/// si la accion lanza.
/// </summary>
public sealed class TransactionService : ITransactionService
{
    private readonly ILogger _logger;

    public TransactionService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<TransactionService>();
    }

    public void Run(Document doc, string transactionName, Action<Document> action)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (action is null) throw new ArgumentNullException(nameof(action));

        using var tx = new Transaction(doc, transactionName);
        tx.Start();
        try
        {
            action(doc);
            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.RollBack();
            _logger.Error(ex, "Transaction '{0}' fallo y se hizo rollback.", transactionName);
            throw;
        }
    }

    public T Run<T>(Document doc, string transactionName, Func<Document, T> action)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (action is null) throw new ArgumentNullException(nameof(action));

        using var tx = new Transaction(doc, transactionName);
        tx.Start();
        try
        {
            var result = action(doc);
            tx.Commit();
            return result;
        }
        catch (Exception ex)
        {
            tx.RollBack();
            _logger.Error(ex, "Transaction '{0}' fallo y se hizo rollback.", transactionName);
            throw;
        }
    }

    public void RunGroup(Document doc, string groupName, Action action)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (action is null) throw new ArgumentNullException(nameof(action));

        using var group = new TransactionGroup(doc, groupName);
        group.Start();
        try
        {
            action();
            group.Assimilate();   // colapsa todas las sub-transactions en 1 Ctrl-Z
        }
        catch (Exception ex)
        {
            group.RollBack();
            _logger.Error(ex, "TransactionGroup '{0}' fallo y se hizo rollback.", groupName);
            throw;
        }
    }
}
