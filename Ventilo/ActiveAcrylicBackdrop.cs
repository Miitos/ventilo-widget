using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Ventilo;

/// <summary>
/// Acrylique qui reste « actif » même sans le focus (DesktopAcrylicBackdrop passe en
/// couleur unie dans ce cas). Utilisé par le mode démo, où le panneau reste affiché.
/// </summary>
internal sealed class ActiveAcrylicBackdrop : SystemBackdrop
{
    private readonly SystemBackdropTheme _theme;
    private DesktopAcrylicController? _controller;

    public ActiveAcrylicBackdrop(ElementTheme theme)
    {
        _theme = theme == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _controller = new DesktopAcrylicController();
        _controller.SetSystemBackdropConfiguration(new SystemBackdropConfiguration { IsInputActive = true, Theme = _theme });
        _controller.AddSystemBackdropTarget(target);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
    }
}
