using System.Collections.ObjectModel;
using WinNUT.Core.Models;

namespace WinNUT.App.Models;

/// <summary>One segment of a dotted NUT variable name (e.g. "battery.charge" -> battery -> charge), as a tree node.</summary>
public sealed class VarTreeNode
{
    public string Segment { get; }
    public string FullPath { get; }
    public ObservableCollection<VarTreeNode> Children { get; } = new();
    public UpsListEntry? Entry { get; set; }

    public VarTreeNode(string segment, string fullPath)
    {
        Segment = segment;
        FullPath = fullPath;
    }

    public bool IsLeaf => Children.Count == 0;
}
