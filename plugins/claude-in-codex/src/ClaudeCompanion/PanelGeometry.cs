using System.Windows;

namespace ClaudeCompanion;

internal readonly record struct PanelConstraint(double Width,double MaxHeight,bool Above);

internal static class PanelGeometry
{
    internal const double MaximumHeight=480;
    internal const double Gap=12;
    private const double Inset=8;

    internal static PanelConstraint Constrain(Size host,Rect pet)
    {
        var above=Math.Max(0,pet.Top-Gap-Inset);
        var below=Math.Max(0,host.Height-pet.Bottom-Gap-Inset);
        // Choose a side from the anchor, not from the changing panel content height.
        var preferAbove=above>=240||above>=below;
        var room=Math.Max(above,below)<96?host.Height-Inset*2:preferAbove?above:below;
        return new(Math.Max(1,Math.Min(320,host.Width-Inset*2)),Math.Max(1,Math.Min(MaximumHeight,room)),preferAbove);
    }

    internal static Point Place(Size popup,Size host,Rect pet,bool above)
    {
        var left=Math.Clamp(pet.Right-popup.Width,Inset,Math.Max(Inset,host.Width-popup.Width-Inset));
        if(Math.Max(pet.Top-Gap-Inset,host.Height-pet.Bottom-Gap-Inset)<96)
        {
            var top=Math.Clamp(pet.Bottom-popup.Height,Inset,Math.Max(Inset,host.Height-popup.Height-Inset));
            return new(left-pet.Left,top-pet.Top);
        }
        return new(left-pet.Left,above?-popup.Height-Gap:pet.Height+Gap);
    }
}
