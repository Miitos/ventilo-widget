using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;

namespace VentiloTray;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private FlyoutWindow? _flyout;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Mode démo (captures, GIF) : --demo=<Thème>, panneau figé, rien n'est envoyé ni enregistré
        var demo = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--demo="))?["--demo=".Length..];
        if (demo is not null)
        {
            _flyout = new FlyoutWindow(demo);
            _flyout.ShowFlyout();
            return;
        }

        _singleInstance = new Mutex(true, "VentiloTray.SingleInstance", out bool created);
        if (!created)
        {
            Environment.Exit(0);
            return;
        }

        // Le démarrage auto pointe toujours vers l'exécutable en cours (utile après une mise à jour/déplacement)
        try { if (Startup.IsEnabled) Startup.Set(true); } catch { }

        _flyout = new FlyoutWindow();

        // Lancé au démarrage de Windows (--tray) : on reste discret dans la barre des tâches
        if (!Environment.GetCommandLineArgs().Contains("--tray"))
            _flyout.ShowFlyout();
    }
}
