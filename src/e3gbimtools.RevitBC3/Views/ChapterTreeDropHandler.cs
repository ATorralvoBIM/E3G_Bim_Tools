using System;
using System.Linq;
using System.Windows;
using E3G.BimTools.RevitBC3.Mapping;
using GongSolutions.Wpf.DragDrop;

namespace E3G.BimTools.RevitBC3.Views;

/// <summary>
/// Sprint B — Handler de drag-and-drop para el arbol de capitulos.
///
/// Acepta los siguientes drops (AC2.2, AC3.2, AC4.1, AC5.1):
///   - MappingRule → ChapterTreeNode:     asigna rule.ChapterId = chapter.Id, sub = null.
///   - MappingRule → SubChapterTreeNode:  asigna rule.ChapterId = sub.ChapterId, sub.Id.
///   - MappingRule → UnassignedTreeNode:  rule queda huerfana (chapterId = null).
///   - SubChapterTreeNode → ChapterTreeNode: mueve el subcapitulo (y sus reglas) al
///     capitulo destino reasignando sub.ChapterId y limpiando rule.SubChapterId de
///     las reglas que apuntaban a ese sub fuera del nuevo capitulo si chocan.
///   - ChapterTreeNode → ChapterTreeNode: reordena (intercambia Order entre origen
///     y destino, AC5.1).
///
/// Tras cualquier mutacion notifica al MainWindow para refrescar el arbol,
/// recalcular conflictos (Sprint A: AC2.3, AC4.2) y persistir.
/// </summary>
public sealed class ChapterTreeDropHandler : IDropTarget
{
    private readonly RevitBC3MainWindow _owner;

    public ChapterTreeDropHandler(RevitBC3MainWindow owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public void DragOver(IDropInfo dropInfo)
    {
        // Por defecto: prohibido.
        dropInfo.Effects = DragDropEffects.None;
        dropInfo.DropTargetAdorner = null;

        var source = UnwrapSource(dropInfo.Data);
        var target = dropInfo.TargetItem;

        if (source is null || target is null) return;

        // Caso 1: arrastrar regla.
        if (source is MappingRule)
        {
            if (target is ChapterTreeNode
                || target is SubChapterTreeNode
                || target is UnassignedTreeNode)
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            }
            return;
        }

        // Caso 2: arrastrar subcapitulo a otro capitulo.
        if (source is SubChapterTreeNode subNode)
        {
            if (target is ChapterTreeNode chNode
                && !string.Equals(subNode.SubChapter.ChapterId, chNode.Chapter.Id, StringComparison.Ordinal))
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            }
            return;
        }

        // Caso 3: arrastrar capitulo sobre otro capitulo (reorden).
        if (source is ChapterTreeNode srcChapter
            && target is ChapterTreeNode dstChapter
            && !ReferenceEquals(srcChapter, dstChapter))
        {
            dropInfo.Effects = DragDropEffects.Move;
            // gong-wpf-dragdrop expone solo "Insert" y "Highlight" como adorners
            // estandar.  "Insert" pinta la linea horizontal entre items, que es
            // lo que queremos para reordenar capitulos al mismo nivel.
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            return;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        var source = UnwrapSource(dropInfo.Data);
        var target = dropInfo.TargetItem;
        if (source is null || target is null) return;

        try
        {
            // MappingRule -> nodo del arbol.
            if (source is MappingRule rule)
            {
                if (target is ChapterTreeNode ch)
                {
                    rule.ChapterId = ch.Chapter.Id;
                    rule.SubChapterId = null;
                    _owner.OnRuleMovedByDrop(rule);
                    return;
                }
                if (target is SubChapterTreeNode sub)
                {
                    rule.ChapterId = sub.SubChapter.ChapterId;
                    rule.SubChapterId = sub.SubChapter.Id;
                    _owner.OnRuleMovedByDrop(rule);
                    return;
                }
                if (target is UnassignedTreeNode)
                {
                    rule.ChapterId = null;
                    rule.SubChapterId = null;
                    _owner.OnRuleMovedByDrop(rule);
                    return;
                }
            }

            // SubChapter -> Chapter (mover subcapitulo).
            if (source is SubChapterTreeNode subNode
                && target is ChapterTreeNode targetCh
                && !string.Equals(subNode.SubChapter.ChapterId, targetCh.Chapter.Id, StringComparison.Ordinal))
            {
                _owner.MoveSubChapterToChapter(subNode.SubChapter, targetCh.Chapter);
                return;
            }

            // Chapter -> Chapter (reorden).
            if (source is ChapterTreeNode srcCh
                && target is ChapterTreeNode dstCh
                && !ReferenceEquals(srcCh, dstCh))
            {
                _owner.ReorderChapter(srcCh.Chapter, dstCh.Chapter, dropInfo.InsertPosition);
                return;
            }
        }
        catch (Exception ex)
        {
            // Defensive: nunca dejar que un crash del handler tire la ventana.
            System.Diagnostics.Debug.WriteLine($"[ChapterTreeDropHandler] {ex}");
        }
    }

    /// <summary>
    /// El dato del drop puede venir como una unica instancia o como
    /// IEnumerable (cuando arrastras una seleccion).  Devuelve el primer
    /// elemento "interesante" o null.
    /// </summary>
    private static object? UnwrapSource(object? data)
    {
        if (data is null) return null;
        if (data is MappingRule || data is MappingTreeNode) return data;
        if (data is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is MappingRule || item is MappingTreeNode) return item;
            }
        }
        return null;
    }
}
