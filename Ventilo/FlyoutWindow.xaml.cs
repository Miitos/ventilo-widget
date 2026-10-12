using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Ventilo;

public sealed partial class FlyoutWindow : Window
{
    private static readonly string[] Winds = { "Calme", "Brise légère", "Petite brise", "Brise", "Vent frais", "Grand vent", "Bourrasque" };
    private static readonly (string Name, Color Hex)[] LightTemps =
    {
        ("Chaude", Palette.C(0xFFFFB547)), ("Neutre", Palette.C(0xFFFFE6A8)), ("Froide", Palette.C(0xFFBFE3FF)),
    };

    private readonly AppSettings _settings;
    private readonly bool _demo; // mode démo : panneau figé, rien n'est envoyé ni enregistré
    private readonly FanClient _client = new();
    private readonly TrayIcon _tray = new();
    private readonly UISettings _ui = new();
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly Decor _decor;

    private readonly List<(Button Btn, Border Bar, TextBlock Num)> _speedButtons = new();

    private Palette _p;
    private bool _visible;
    private bool _loadingSettings;
    private DateTime _lastHidden = DateTime.MinValue;
    private bool? _connected;
    private bool _sendFailed;

    // Synchro d'état : flux /events du firmware
    private CancellationTokenSource? _eventsCts;
    private bool _eventsConnected;
    private bool _synced; // le firmware publie les entités « Etat … »

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private double _lastTextRefresh;
    private double _fanAngle;
    private double _spinRate; // °/s, avec inertie

    private FanState S => _settings.State;

    public FlyoutWindow(string? demoTheme = null)
    {
        _demo = demoTheme is not null;
        _settings = _demo
            ? new AppSettings { Theme = demoTheme!, Transient = true, State = new FanState { On = true, Speed = 4, Light = true } }
            : AppSettings.Load();

        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (_appWindow.Presenter is OverlappedPresenter op)
        {
            op.IsResizable = false;
            op.IsMaximizable = false;
            op.IsMinimizable = false;
            op.IsAlwaysOnTop = true;
            op.SetBorderAndTitleBar(false, false);
        }
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Title = "Ventilateur";
        _appWindow.Closing += (_, e) => { e.Cancel = true; HideFlyout(); };
        int corner = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        SystemBackdrop = _demo
            ? new ActiveAcrylicBackdrop(Palette.Resolve(_settings.Theme, _ui).Theme)
            : new DesktopAcrylicBackdrop();
        Activated += OnActivated;

        _client.Host = _settings.Host;
        _decor = new Decor(DecorArt, DecorSway, ParticleLayer);
        BuildSpeedButtons();
        foreach (var label in Palette.Labels) ThemeChoice.Items.Add(label);

        _tray.Clicked += ToggleFlyout;
        _tray.QuitRequested += Quit;
        _ui.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyTheme);
        SystemEvents.UserPreferenceChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTray);

        _p = Palette.Resolve(_settings.Theme, _ui);
        if (_demo) { _eventsConnected = true; _synced = true; }
        ApplyTheme();
        if (!_demo) StartEvents();
    }

    // ───────────────────────── Affichage du panneau ─────────────────────────

    private void ToggleFlyout()
    {
        if (_visible) { HideFlyout(); return; }
        // Le clic sur l'icône désactive d'abord le panneau (qui se ferme) : ne pas le rouvrir aussitôt
        if ((DateTime.UtcNow - _lastHidden).TotalMilliseconds < 350) return;
        ShowFlyout();
    }

    public void ShowFlyout()
    {
        ShowPage(settings: false);
        ApplyTheme();
        PositionWindow();

        _appWindow.Show(true);
        Activate();
        Native.SetForegroundWindow(_hwnd);
        _visible = true;

        // À l'ouverture, l'hélice tourne déjà à sa vitesse : l'inertie ne joue que sur les changements
        _spinRate = TargetSpinRate();
        _lastFrame = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
        if (!_eventsConnected) _ = CheckConnectionAsync();
    }

    private void HideFlyout()
    {
        if (!_visible) return;
        _visible = false;
        _lastHidden = DateTime.UtcNow;
        CompositionTarget.Rendering -= OnRendering;
        _appWindow.Hide();
        _settings.Save();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (!_demo && e.WindowActivationState == WindowActivationState.Deactivated) HideFlyout();
    }

    private void PositionWindow()
    {
        // Hauteur mesurée sur la page principale ; la page Paramètres réutilise la même
        Root.Height = double.NaN;
        Root.Measure(new Windows.Foundation.Size(360, double.PositiveInfinity));
        double h = Math.Ceiling(Root.DesiredSize.Height);
        Root.Height = h;

        double scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        int w = (int)Math.Ceiling(360 * scale);
        int hh = (int)Math.Ceiling(h * scale);
        int margin = (int)(12 * scale);

        Native.POINT anchor;
        var r = _tray.GetRect();
        if (r is { } rect) anchor = new Native.POINT { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
        else Native.GetCursorPos(out anchor);

        var mi = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromPoint(anchor, Native.MONITOR_DEFAULTTONEAREST), ref mi);
        var work = mi.rcWork;
        var mon = mi.rcMonitor;

        int x = work.Right - w - margin;
        int y = work.Bottom - hh - margin;
        if (work.Top > mon.Top) y = work.Top + margin;      // barre des tâches en haut
        if (work.Left > mon.Left) x = work.Left + margin;   // barre des tâches à gauche

        _appWindow.MoveAndResize(new RectInt32(x, y, w, hh));
    }

    private void ShowPage(bool settings)
    {
        MainPage.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (SettingsPage.Visibility == Visibility.Visible) ShowPage(settings: false);
            else HideFlyout();
        }
    }

    private void BuildSpeedButtons()
    {
        var style = (Style)Application.Current.Resources["FlatButton"];
        for (int n = 1; n <= 6; n++)
        {
            SpeedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bar = new Border { Height = 12 + 7 * n, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Bottom };
            var barHost = new Grid { Height = 56 };
            barHost.Children.Add(bar);
            var num = new TextBlock { Text = n.ToString(), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(barHost);
            stack.Children.Add(num);
            var btn = new Button
            {
                Style = style,
                Content = stack,
                Padding = new Thickness(6, 8, 6, 6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = n,
            };
            AutomationProperties.SetName(btn, $"Vitesse {n}");
            btn.Click += Speed_Click;
            Grid.SetColumn(btn, n - 1);
            SpeedGrid.Children.Add(btn);
            _speedButtons.Add((btn, bar, num));
        }
    }

    // ───────────────────────── Thème et rendu de l'état ─────────────────────────

    private static SolidColorBrush B(Color c) => new(c);

    private void ApplyTheme()
    {
        _p = Palette.Resolve(_settings.Theme, _ui);
        Root.RequestedTheme = _p.Theme;
        Root.Background = B(_p.Tint);

        foreach (var tb in new[] { FanNameText, SpeedTitle, SettingsTitle, AssumedTitle }) tb.Foreground = B(_p.Text);
        StatusText.Foreground = B(_p.Text2);
        LastText.Foreground = B(_p.Text3);
        WindText.Foreground = B(_p.Text3);
        AssumedText.Foreground = B(_p.Text2);
        ConnText.Foreground = B(_p.Text2);

        FanRing.Stroke = B(_p.Ring);
        FanRing.Fill = B(_p.RingFill);
        Hub.Stroke = B(_p.Ring);
        foreach (var blade in new[] { Blade1, Blade2, Blade3 }) blade.Fill = B(_p.Blade);
        ColorDot.Stroke = B(_p.Ring);

        Footer.Background = B(_p.Footer);
        Footer.BorderBrush = B(_p.Divider);
        foreach (var b in new[] { SettingsButton, BackButton }) { b.Background = B(Colors.Transparent); b.Foreground = B(_p.Text2); }

        _decor.SetKind(_p.Decor);
        var decorVisible = _p.Decor == DecorKind.None ? Visibility.Collapsed : Visibility.Visible;
        DecorLayer.Visibility = decorVisible;
        ParticleLayer.Visibility = decorVisible;

        RefreshState();
    }

    private void RefreshState()
    {
        var s = S;
        string dir = s.Reverse ? "Hiver" : "Été";

        FanNameText.Text = string.IsNullOrWhiteSpace(_settings.Name) ? "Ventilateur" : _settings.Name;
        StatusText.Text = s.On ? $"Vitesse {s.Speed} · {dir}" : "Éteint";
        WindText.Text = Winds[s.On ? s.Speed : 0];
        RefreshLastText();

        PowerButton.Background = B(s.On ? _p.Accent : _p.Ctrl);
        PowerButton.BorderBrush = B(s.On ? Colors.Transparent : _p.CtrlBorder);
        PowerButton.Foreground = B(s.On ? _p.OnAccent : _p.Text);
        AutomationProperties.SetName(PowerButton, s.On ? "Éteindre" : "Allumer");

        foreach (var (btn, bar, num) in _speedButtons)
        {
            int n = (int)btn.Tag;
            bool lit = s.On && n <= s.Speed, sel = s.On && n == s.Speed;
            btn.Background = B(_p.Ctrl);
            btn.BorderBrush = B(sel ? _p.Accent : _p.CtrlBorder);
            bar.Background = B(lit ? _p.Accent : _p.Bar);
            bar.Opacity = lit && !sel ? 0.55 : 1;
            num.Foreground = B(sel ? _p.Accent : _p.Text2);
            num.FontWeight = sel ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }

        var temp = LightTemps[s.LightColor % 3];
        LightButton.Background = B(s.Light ? _p.Accent : _p.Ctrl);
        LightButton.BorderBrush = B(s.Light ? Colors.Transparent : _p.CtrlBorder);
        LightButton.Foreground = B(s.Light ? _p.OnAccent : _p.Text);
        LightSub.Text = s.Light ? "Allumée" : "Éteinte";

        foreach (var b in new[] { ColorButton, DirButton })
        {
            b.Background = B(_p.Ctrl);
            b.BorderBrush = B(_p.CtrlBorder);
            b.Foreground = B(_p.Text);
        }
        ColorDot.Fill = B(temp.Hex);
        ColorSub.Text = temp.Name;
        ColorSub.Foreground = B(_p.Text2);
        DirSub.Text = dir;
        DirSub.Foreground = B(_p.Text2);
        DirFlip.ScaleX = s.Reverse ? -1 : 1;

        Halo.Fill = B(temp.Hex);
        Halo.Opacity = s.Light ? 0.24 : 0;
        Hub.Fill = B(s.Light ? temp.Hex : _p.Ctrl);

        string host = string.IsNullOrWhiteSpace(_settings.Host) ? "ventilo.local" : _settings.Host.Trim();
        if (_sendFailed) { ConnText.Text = $"Échec de l’envoi · {host} injoignable"; StatusDot.Fill = B(_p.Err); }
        else if (_eventsConnected && _synced) { ConnText.Text = $"{host} · synchronisé"; StatusDot.Fill = B(_p.Ok); }
        else if (_connected == true || _eventsConnected) { ConnText.Text = $"{host} · connecté"; StatusDot.Fill = B(_p.Ok); }
        else if (_connected == false) { ConnText.Text = $"{host} · injoignable"; StatusDot.Fill = B(_p.Err); }
        else { ConnText.Text = $"{host} · vérification…"; StatusDot.Fill = B(_p.Text3); }

        AssumedTitle.Text = _synced ? "Corriger l’état" : "État supposé";
        AssumedText.Text = _synced
            ? "Le boîtier suit la télécommande. Corrige seulement si le ventilateur a été commandé hors de sa portée. Rien n’est émis."
            : "Si la télécommande a été utilisée, corrige l’état ici. Rien n’est envoyé au ventilateur.";

        UpdateTray();
    }

    private void RefreshLastText()
    {
        if (_eventsConnected && _synced) { LastText.Text = "État synchronisé en direct"; return; }
        var t = S.LastCommandUtc;
        if (t is null) { LastText.Text = "Aucune commande envoyée"; return; }
        var d = DateTime.UtcNow - t.Value;
        string ago = d.TotalMinutes < 1 ? "à l’instant"
            : d.TotalHours < 1 ? $"il y a {(int)d.TotalMinutes} min"
            : d.TotalDays < 1 ? $"il y a {(int)d.TotalHours} h"
            : $"il y a {(int)d.TotalDays} j";
        LastText.Text = "Dernière commande · " + ago;
    }

    private void UpdateTray()
    {
        bool lightTaskbar = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            lightTaskbar = key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { }

        var accent = _p.Decor != DecorKind.None ? _p.Accent
            : _ui.GetColorValue(lightTaskbar ? UIColorType.AccentDark1 : UIColorType.AccentLight2);
        int size = Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, Native.GetDpiForSystem());
        var icon = IconRenderer.Render(size, S.On, S.Speed, lightTaskbar, accent.R, accent.G, accent.B);
        string name = string.IsNullOrWhiteSpace(_settings.Name) ? "Ventilateur" : _settings.Name;
        _tray.Update(icon, S.On ? $"{name} — Vitesse {S.Speed}" : $"{name} — Éteint");
    }

    // ───────────────────────── Animation ─────────────────────────

    private double TargetSpinRate() =>
        S.On ? 360 / (2.6 / S.Speed) * (S.Reverse ? -1 : 1) : 0;

    private void OnRendering(object? sender, object e)
    {
        double t = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(t - _lastFrame, 0.1);
        _lastFrame = t;

        // Inertie : l'hélice accélère en ~1 s, ralentit en ~2,5 s comme un vrai ventilateur
        double target = TargetSpinRate();
        double tau = Math.Abs(target) < Math.Abs(_spinRate) ? 2.5 : 1.0;
        _spinRate += (target - _spinRate) * (1 - Math.Exp(-dt / tau));
        _fanAngle = (_fanAngle + _spinRate * dt) % 360;
        FanRotate.Angle = _fanAngle;

        _decor.Update(t, S.On, S.Speed, 360, Root.ActualHeight);

        if (t - _lastTextRefresh > 15)
        {
            _lastTextRefresh = t;
            RefreshLastText();
        }
    }

    // ───────────────────────── Commandes ─────────────────────────

    /// <summary>Applique l'état tout de suite (l'UI répond sans attendre), annule si l'envoi échoue.</summary>
    private async Task SendAsync(string entity, Action<FanState> change)
    {
        var before = S.Clone();
        change(S);
        S.LastCommandUtc = DateTime.UtcNow;
        _sendFailed = false;
        RefreshState();
        if (_demo) return;

        bool ok = await _client.PressAsync(entity);
        if (!ok)
        {
            _settings.State = before;
            _sendFailed = true;
            _connected = false;
        }
        else
        {
            _connected = true;
        }
        _settings.Save();
        RefreshState();
    }

    private async void Speed_Click(object sender, RoutedEventArgs e)
    {
        int n = (int)((Button)sender).Tag;
        await SendAsync($"Vitesse {n}", s => { s.On = true; s.Speed = n; });
    }

    private async void Power_Click(object sender, RoutedEventArgs e)
    {
        // Allumer : on envoie une vitesse (commande absolue). Éteindre : seul Power le fait (bascule).
        if (S.On) await SendAsync("Power", s => s.On = false);
        else await SendAsync($"Vitesse {S.Speed}", s => s.On = true);
    }

    private async void Light_Click(object sender, RoutedEventArgs e) =>
        await SendAsync("Lumiere", s => s.Light = !s.Light);

    private async void Color_Click(object sender, RoutedEventArgs e) =>
        await SendAsync("Couleur lumiere", s => s.LightColor = (s.LightColor + 1) % 3);

    private async void Dir_Click(object sender, RoutedEventArgs e) =>
        await SendAsync("Sens rotation", s => s.Reverse = !s.Reverse);

    private async Task CheckConnectionAsync()
    {
        if (_sendFailed) return;
        _connected = null;
        RefreshState();
        _connected = await _client.PingAsync();
        RefreshState();
    }

    // ───────────────────────── Synchro d'état ─────────────────────────

    private void StartEvents()
    {
        _eventsCts?.Cancel();
        _eventsCts = new CancellationTokenSource();
        var ct = _eventsCts.Token;
        _ = Task.Run(() => _client.RunEventsAsync(
            (field, value) => DispatcherQueue.TryEnqueue(() => OnRemoteState(field, value)),
            connected => DispatcherQueue.TryEnqueue(() =>
            {
                _eventsConnected = connected;
                if (connected) { _connected = true; _sendFailed = false; }
                else _synced = false;
                RefreshState();
            }),
            ct));
    }

    /// <summary>État publié par le firmware : il fait foi (télécommande physique comprise).</summary>
    private void OnRemoteState(StateField field, int value)
    {
        _synced = true;
        var s = S;
        switch (field)
        {
            case StateField.On: s.On = value != 0; break;
            case StateField.Speed: if (value is >= 1 and <= 6) s.Speed = value; break;
            case StateField.Light: s.Light = value != 0; break;
            case StateField.LightColor: if (value is >= 0 and <= 2) s.LightColor = value; break;
            case StateField.Reverse: s.Reverse = value != 0; break;
        }
        RefreshState();
    }

    // ───────────────────────── Paramètres ─────────────────────────

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _loadingSettings = true;
        ThemeChoice.SelectedIndex = Math.Max(0, Array.IndexOf(Palette.Keys, _settings.Theme));
        NameBox.Text = _settings.Name;
        HostBox.Text = _settings.Host;
        StartupToggle.IsOn = Startup.IsEnabled;
        FixOn.IsOn = S.On;
        FixLight.IsOn = S.Light;
        FixColor.SelectedIndex = S.LightColor % 3;
        FixDir.SelectedIndex = S.Reverse ? 1 : 0;
        _loadingSettings = false;
        ShowPage(settings: true);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowPage(settings: false);

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || ThemeChoice.SelectedIndex < 0) return;
        _settings.Theme = Palette.Keys[ThemeChoice.SelectedIndex];
        _settings.Save();
        ApplyTheme();
    }

    private void Name_LostFocus(object sender, RoutedEventArgs e)
    {
        _settings.Name = NameBox.Text.Trim();
        _settings.Save();
        RefreshState();
    }

    private void Host_LostFocus(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (host.Length == 0 || host == _settings.Host) return;
        _settings.Host = host;
        _client.Host = host;
        _settings.Save();
        _sendFailed = false;
        _synced = false;
        _eventsConnected = false;
        StartEvents();
        _ = CheckConnectionAsync();
    }

    private void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        try { Startup.Set(StartupToggle.IsOn); }
        catch { StartupToggle.IsOn = Startup.IsEnabled; }
    }

    /// <summary>Correction manuelle de l'état : rien n'est émis vers le ventilateur.</summary>
    private async void FixState_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var s = S;
        var changes = new List<(StateField, int)>();
        if (s.On != FixOn.IsOn) changes.Add((StateField.On, FixOn.IsOn ? 1 : 0));
        if (s.Light != FixLight.IsOn) changes.Add((StateField.Light, FixLight.IsOn ? 1 : 0));
        if (FixColor.SelectedIndex >= 0 && s.LightColor != FixColor.SelectedIndex) changes.Add((StateField.LightColor, FixColor.SelectedIndex));
        bool rev = FixDir.SelectedIndex == 1;
        if (FixDir.SelectedIndex >= 0 && s.Reverse != rev) changes.Add((StateField.Reverse, rev ? 1 : 0));
        if (changes.Count == 0) return;

        foreach (var (field, value) in changes) OnLocalFix(field, value);
        _settings.Save();
        RefreshState();

        // Le firmware tient l'état : on le corrige aussi là-bas
        if (_synced)
            foreach (var (field, value) in changes)
                await _client.SetStateAsync(field, value);
    }

    private void OnLocalFix(StateField field, int value)
    {
        switch (field)
        {
            case StateField.On: S.On = value != 0; break;
            case StateField.Light: S.Light = value != 0; break;
            case StateField.LightColor: S.LightColor = value; break;
            case StateField.Reverse: S.Reverse = value != 0; break;
        }
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Quit();

    private void Quit()
    {
        _eventsCts?.Cancel();
        _settings.Save();
        _tray.Dispose();
        Environment.Exit(0);
    }
}
