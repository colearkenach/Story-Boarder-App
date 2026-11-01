using System.Windows;
using System.Windows.Controls;
using StoryBoarder.ViewModels;

namespace StoryBoarder.Controls;

/// <summary>Items and arrows share one canvas (so arrows can sit between frames and notes) but need different container styles.</summary>
public sealed class BoardItemStyleSelector : StyleSelector
{
    public Style? ItemStyle { get; set; }
    public Style? ConnectorStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) =>
        item is ConnectorViewModel ? ConnectorStyle : ItemStyle;
}
