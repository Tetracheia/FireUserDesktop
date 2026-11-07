using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace UserIdentityDesktop.Views;

/// <summary>CSS repeat(auto-fit, minmax(..., 1fr)) for native compose cards.</summary>
public sealed class ComposeAutoGrid : Panel
{
    public static readonly StyledProperty<double> MinimumItemWidthProperty=AvaloniaProperty.Register<ComposeAutoGrid,double>(nameof(MinimumItemWidth),220);
    public double MinimumItemWidth {get=>GetValue(MinimumItemWidthProperty);set=>SetValue(MinimumItemWidthProperty,value);}
    public static readonly StyledProperty<double> GapProperty=AvaloniaProperty.Register<ComposeAutoGrid,double>(nameof(Gap),10);
    public double Gap {get=>GetValue(GapProperty);set=>SetValue(GapProperty,value);}
    static ComposeAutoGrid(){AffectsMeasure<ComposeAutoGrid>(MinimumItemWidthProperty,GapProperty);}
    private int Columns(double width,int count)=>Math.Max(1,Math.Min(count,double.IsFinite(width)?(int)Math.Floor((width+Gap)/(Math.Max(1,MinimumItemWidth)+Gap)):1));
    protected override Size MeasureOverride(Size availableSize)
    {
        var children=Children.Where(x=>x.IsVisible).ToArray();if(children.Length==0)return default;
        var columns=Columns(availableSize.Width,children.Length);var cell=double.IsFinite(availableSize.Width)?Math.Max(0,(availableSize.Width-Gap*(columns-1))/columns):MinimumItemWidth;
        foreach(var child in children)child.Measure(new Size(cell,double.PositiveInfinity));
        var height=0d;for(var i=0;i<children.Length;i+=columns)height+=children.Skip(i).Take(columns).Max(x=>x.DesiredSize.Height)+(i==0?0:Gap);
        return new Size(double.IsFinite(availableSize.Width)?availableSize.Width:cell,height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var children=Children.Where(x=>x.IsVisible).ToArray();if(children.Length==0)return finalSize;
        var columns=Columns(finalSize.Width,children.Length);var cell=Math.Max(0,(finalSize.Width-Gap*(columns-1))/columns);var top=0d;
        for(var row=0;row<children.Length;row+=columns)
        {
            var height=children.Skip(row).Take(columns).Max(x=>x.DesiredSize.Height);
            for(var i=row;i<Math.Min(children.Length,row+columns);i++)children[i].Arrange(new Rect((i-row)*(cell+Gap),top,cell,height));
            top+=height+Gap;
        }
        return finalSize;
    }
}
