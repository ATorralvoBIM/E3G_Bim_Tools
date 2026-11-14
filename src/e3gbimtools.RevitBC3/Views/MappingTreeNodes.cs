using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Views;

// ============================================================
//  Sprint B — Nodos del arbol de capitulos del mapeo.
//
//  Cada nodo es un wrapper INPC que envuelve una entidad del
//  modelo (Chapter / SubChapter / MappingRule) o, en el caso de
//  "Sin asignar", un agrupador virtual.  El TreeView se enlaza
//  a una coleccion de TreeNode y los HierarchicalDataTemplate de
//  XAML eligen el aspecto segun el tipo concreto.
//
//  El handler de drop (ChapterTreeDropHandler) recibe estos
//  nodos como DragInfo.SourceItem / DropInfo.TargetItem y muta
//  el modelo subyacente a traves de las propiedades Chapter /
//  SubChapter / Rule.
// ============================================================

/// <summary>Base comun para todos los nodos del arbol de capitulos.</summary>
public abstract class MappingTreeNode : INotifyPropertyChanged
{
    private string _header = string.Empty;
    public string Header
    {
        get => _header;
        set { if (_header != value) { _header = value; OnPropertyChanged(); } }
    }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<MappingTreeNode> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Nodo capitulo (envuelve <see cref="Chapter"/>).</summary>
public sealed class ChapterTreeNode : MappingTreeNode
{
    public Chapter Chapter { get; }
    public ChapterTreeNode(Chapter chapter) { Chapter = chapter; }
}

/// <summary>Nodo subcapitulo (envuelve <see cref="SubChapter"/>).</summary>
public sealed class SubChapterTreeNode : MappingTreeNode
{
    public SubChapter SubChapter { get; }
    public SubChapterTreeNode(SubChapter sub) { SubChapter = sub; }
}

/// <summary>Nodo regla (envuelve <see cref="MappingRule"/>).</summary>
public sealed class RuleTreeNode : MappingTreeNode
{
    public MappingRule Rule { get; }
    public RuleTreeNode(MappingRule rule) { Rule = rule; IsExpanded = false; }
}

/// <summary>
/// Nodo virtual "Sin asignar" que agrupa reglas con ChapterId == null
/// (AC3.1).  No corresponde a ninguna entidad persistida; existe solo
/// para visualizar las reglas huerfanas y permitir arrastrarlas a un
/// capitulo destino.
/// </summary>
public sealed class UnassignedTreeNode : MappingTreeNode
{
    public UnassignedTreeNode() { }
}
