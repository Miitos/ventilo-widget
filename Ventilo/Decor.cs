using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Ventilo;

internal enum DecorKind { None, Sakura, Autumn, Winter, Summer }

/// <summary>
/// Décor des thèmes de saison : un motif dans le coin haut-droit (branche, lune…)
/// et des particules qui suivent le vent du ventilateur. Tout reste derrière le contenu.
/// </summary>
internal sealed class Decor
{
    private const string MapleLeaf =
        "M10,0 L12,5 L16,3 L15,8 L20,9 L16,12 L17,16 L12,14 L10,20 L8,14 L3,16 L4,12 L0,9 L5,8 L4,3 L8,5 Z";
    private const string Crescent = "M0,-22 A22,22 0 1 0 0,22 A28,28 0 0 1 0,-22 Z";

    private readonly Canvas _art;
    private readonly RotateTransform _sway;
    private readonly Canvas _particlesLayer;
    private readonly List<Particle> _particles = new();
    private readonly List<(UIElement El, double Phase)> _stars = new();
    private DecorKind _kind = DecorKind.None;
    private string _particleKey = "";

    private sealed class Particle
    {
        public required FrameworkElement El;
        public required CompositeTransform T;
        public double X0, Y0, Dur, Phase, Dx, Rot, Amp, MaxOpacity = 0.9;
        public bool Resting;
    }

    public Decor(Canvas art, RotateTransform sway, Canvas particles)
    {
        _art = art;
        _sway = sway;
        _particlesLayer = particles;
    }

    public DecorKind Kind => _kind;

    public void SetKind(DecorKind kind)
    {
        if (kind == _kind) return;
        _kind = kind;
        _art.Children.Clear();
        _stars.Clear();
        _sway.Angle = 0;
        _particleKey = "";
        switch (kind)
        {
            case DecorKind.Sakura: BuildBranch(Palette.C(0xFF5A3A42)); AddBlossoms(); break;
            case DecorKind.Autumn: BuildBranch(Palette.C(0xFF5C3A21)); AddMapleLeaves(); break;
            case DecorKind.Winter: BuildBranch(Palette.C(0xFF7A8A9C)); AddSnowCaps(); break;
            case DecorKind.Summer: AddMoonAndStars(); break;
        }
    }

    // ───────────── Motifs du coin (repère 230×178, réduit en XAML) ─────────────

    private static readonly (double X, double Y, double S, double R)[] Spots =
    {
        (54, 106, 1.15, 0), (116, 10, 1, 0), (160, 124, 1.1, 0), (140, 32, 1.2, 20), (104, 84, 1, -15),
        (178, 36, 0.95, 35), (146, 104, 0.85, 0), (196, 66, 1.05, 10), (74, 96, 0.8, 40), (214, 30, 0.9, 0),
    };

    private void BuildBranch(Color wood)
    {
        AddPath("M232,20 C196,24 162,40 128,72 C104,94 84,102 54,106", wood, 5);
        AddPath("M162,40 C150,22 136,14 116,10", wood, 2.6);
        AddPath("M128,72 C130,94 142,112 160,124", wood, 2.6);
        AddPath("M200,24 C206,40 204,54 196,66", wood, 2);
    }

    private void AddPath(string data, Color stroke, double thickness)
    {
        _art.Children.Add(new Path
        {
            Data = Geo(data),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
    }

    private void AddBlossoms()
    {
        var petal = new[] { 0xFFF9CBD7u, 0xFFF7BFCFu, 0xFFF9CBD7u, 0xFFF5B4C6u, 0xFFF9CBD7u };
        foreach (var s in Spots)
        {
            var c = new Canvas();
            for (int i = 0; i < 5; i++)
            {
                var e = new Ellipse
                {
                    Width = 8.8, Height = 12.4,
                    Fill = new SolidColorBrush(Palette.C(petal[i])),
                    RenderTransform = new RotateTransform { Angle = i * 72, CenterX = 4.4, CenterY = 12.2 },
                };
                Canvas.SetLeft(e, -4.4);
                Canvas.SetTop(e, -12.2);
                c.Children.Add(e);
            }
            c.Children.Add(Dot(0, 0, 2.4, Palette.C(0xFFD94A7B)));
            Place(c, s.X, s.Y, s.S, s.R);
        }
        foreach (var (x, y) in new[] { (128.0, 20.0), (92, 100), (170, 112), (206, 50) })
            _art.Children.Add(Dot(x, y, 2.8, Palette.C(0xFFE77FA0)));
    }

    private void AddMapleLeaves()
    {
        var colors = new[] { 0xFFE2552Bu, 0xFFF2994Au, 0xFFF7C548u, 0xFFC0392Bu, 0xFFE67E22u };
        int i = 0;
        foreach (var s in Spots)
        {
            var leaf = new Path { Data = Geo(MapleLeaf), Fill = new SolidColorBrush(Palette.C(colors[i++ % colors.Length])) };
            var c = new Canvas();
            Canvas.SetLeft(leaf, -10);
            Canvas.SetTop(leaf, -10);
            c.Children.Add(leaf);
            Place(c, s.X, s.Y, s.S * 0.85, s.R + i * 23);
        }
    }

    private void AddSnowCaps()
    {
        var snow = Palette.C(0xFFF4F8FF);
        // Neige posée sur le dessus des branches
        AddPath("M226,15 C194,19 162,34 128,65 C106,85 86,94 58,99", snow, 2.2);
        AddPath("M158,35 C148,20 136,12 118,7", snow, 1.3);
        // Petits amas de neige le long des rameaux
        foreach (var (x, y, w) in new[] { (140.0, 50.0, 10.0), (100, 84, 9), (184, 26, 8), (130, 18, 7), (70, 98, 8) })
            _art.Children.Add(new Ellipse
            {
                Width = w, Height = w * 0.55, Fill = new SolidColorBrush(snow), Opacity = 0.95,
                RenderTransform = new TranslateTransform { X = x - w / 2, Y = y - w * 0.4 },
            });
    }

    private void AddMoonAndStars()
    {
        var moon = new Path { Data = Geo(Crescent), Fill = new SolidColorBrush(Palette.C(0xFFFFF3C4)) };
        Place(moon, 186, 44, 1, -20);
        var rnd = new Random(7);
        foreach (var (x, y) in new[] { (120.0, 22.0), (150, 70), (90, 48), (212, 96), (60, 18), (172, 118), (134, 104) })
        {
            var star = Dot(x, y, 1.2 + rnd.NextDouble(), Palette.C(0xFFFFFBE6));
            _art.Children.Add(star);
            _stars.Add((star, rnd.NextDouble() * 6.28));
        }
    }

    private void Place(UIElement el, double x, double y, double scale, double rot)
    {
        el.RenderTransform = new CompositeTransform { TranslateX = x, TranslateY = y, ScaleX = scale, ScaleY = scale, Rotation = rot };
        _art.Children.Add(el);
    }

    private static Ellipse Dot(double cx, double cy, double r, Color c)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(c) };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    private static Geometry Geo(string data) =>
        (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    // ───────────── Particules ─────────────

    private static double Rnd(int i, int k)
    {
        double x = Math.Sin(i * 12.9898 + k * 78.233) * 43758.5453;
        return x - Math.Floor(x);
    }

    /// <summary>À appeler à chaque image tant que le panneau est visible.</summary>
    public void Update(double t, bool on, int speed, double width, double height)
    {
        if (_kind == DecorKind.None) return;

        string key = $"{_kind}-{on}-{speed}";
        if (key != _particleKey)
        {
            _particleKey = key;
            BuildParticles(on, speed, width, height);
        }

        if (on && _kind != DecorKind.Summer)
        {
            double period = 7 - speed * 0.8;
            double amp = _kind == DecorKind.Winter ? 0.6 : 1.2;
            _sway.Angle = Math.Sin(t * 2 * Math.PI / period) * amp;
        }

        foreach (var (el, phase) in _stars)
            el.Opacity = 0.45 + 0.55 * Math.Pow(Math.Sin(t * 1.3 + phase) * 0.5 + 0.5, 2);

        double wind = on ? speed : 0;
        foreach (var p in _particles)
        {
            if (p.Resting) continue;
            if (_kind == DecorKind.Summer)
            {
                // Lucioles : errent, clignotent, et dérivent avec le vent
                double drift = wind * 9 * t;
                double x = p.X0 - drift + Math.Sin(t * 0.7 + p.Phase) * 26;
                x = ((x % (width + 40)) + width + 40) % (width + 40) - 20;
                p.T.TranslateX = x;
                p.T.TranslateY = p.Y0 + Math.Cos(t * 0.5 + p.Phase * 1.7) * 18;
                p.El.Opacity = 0.15 + 0.85 * Math.Pow(Math.Max(0, Math.Sin(t * 1.6 + p.Phase)), 2);
                continue;
            }

            double k = ((t + p.Phase) / p.Dur) % 1;
            p.T.TranslateX = p.X0 + p.Dx * k + Math.Sin(k * 2 * Math.PI + p.Phase) * p.Amp;
            p.T.TranslateY = -14 + k * (height + 30);
            p.T.Rotation = p.Rot * k;
            double fade = k < 0.08 ? k / 0.08 : k > 0.92 ? (1 - k) / 0.08 : 1;
            p.El.Opacity = fade * p.MaxOpacity;
        }
    }

    private void BuildParticles(bool on, int s, double width, double height)
    {
        _particles.Clear();
        _particlesLayer.Children.Clear();
        double bottom = height - 58;

        switch (_kind)
        {
            case DecorKind.Sakura:
                if (on)
                    for (int i = 0; i < 5 + s * 3; i++)
                        AddFalling(i, Petal(i, 7 + Rnd(i, 1) * 6), s, amp: 16, maxOpacity: 0.9);
                else
                    Rest(new[] { (40.0, 0.9, 20.0), (92, 1.1, -30), (210, 0.8, 60), (300, 1, 10), (255, 0.7, -50) },
                        (i, sc) => Petal(i, 10 * sc), bottom, 0.7);
                break;

            case DecorKind.Autumn:
                if (on)
                    for (int i = 0; i < 3 + s * 2; i++)
                        AddFalling(i, Leaf(i, 10 + Rnd(i, 1) * 6), s, amp: 24, maxOpacity: 0.85, rotBoost: 1.6);
                else
                    Rest(new[] { (36.0, 0.8, 30.0), (120, 0.9, -40), (230, 0.7, 75), (296, 0.85, 10) },
                        (i, sc) => Leaf(i, 14 * sc), bottom - 4, 0.65);
                break;

            case DecorKind.Winter:
            {
                // Éteint : la neige tombe droit et lentement. Allumé : elle part avec le vent.
                int count = on ? 14 + s * 4 : 14;
                for (int i = 0; i < count; i++)
                {
                    double size = 2.5 + Rnd(i, 1) * 3.5;
                    var flake = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Palette.C(0xFFF4F8FF)) };
                    double dur = on ? 9 - s * 0.9 + Rnd(i, 2) * 3 : 12 + Rnd(i, 2) * 5;
                    var p = NewParticle(flake);
                    p.Dur = dur;
                    p.Phase = Rnd(i, 3) * dur;
                    p.X0 = (on ? 40 : 0) + Rnd(i, 4) * (on ? 340 : width);
                    p.Dx = on ? -(20 + s * 30 + Rnd(i, 5) * 50) : 0;
                    p.Amp = on ? 10 : 6;
                    p.MaxOpacity = 0.55 + Rnd(i, 6) * 0.35;
                }
                break;
            }

            case DecorKind.Summer:
            {
                int count = on ? 7 + s : 7;
                for (int i = 0; i < count; i++)
                {
                    var glow = new Grid { Width = 12, Height = 12 };
                    glow.Children.Add(new Ellipse { Fill = new SolidColorBrush(Palette.C(0x55D4F06B)) });
                    glow.Children.Add(new Ellipse
                    {
                        Width = 4, Height = 4, Fill = new SolidColorBrush(Palette.C(0xFFF4FFB0)),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    });
                    var p = NewParticle(glow);
                    p.X0 = Rnd(i, 1) * width;
                    // Plutôt dans la moitié basse, loin des titres
                    p.Y0 = height * 0.35 + Rnd(i, 2) * height * 0.55;
                    p.Phase = Rnd(i, 3) * 20;
                }
                break;
            }
        }
    }

    private void AddFalling(int i, FrameworkElement el, int s, double amp, double maxOpacity, double rotBoost = 1)
    {
        var p = NewParticle(el);
        p.Dur = 8.5 - s * 0.9 + Rnd(i, 2) * 2.5;
        p.Phase = Rnd(i, 3) * p.Dur;
        p.X0 = 60 + Rnd(i, 4) * 300;
        p.Dx = -(30 + s * 28 + Rnd(i, 5) * 70);
        p.Rot = (300 + Rnd(i, 6) * 500) * rotBoost;
        p.Amp = amp;
        p.MaxOpacity = maxOpacity;
    }

    private void Rest((double X, double S, double R)[] spots, Func<int, double, FrameworkElement> make, double y, double opacity)
    {
        for (int i = 0; i < spots.Length; i++)
        {
            var p = NewParticle(make(i, spots[i].S));
            p.Resting = true;
            p.T.TranslateX = spots[i].X;
            p.T.TranslateY = y;
            p.T.Rotation = spots[i].R;
            p.El.Opacity = opacity;
        }
    }

    private Particle NewParticle(FrameworkElement el)
    {
        var t = new CompositeTransform();
        el.RenderTransform = t;
        el.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        el.Opacity = 0;
        _particlesLayer.Children.Add(el);
        var p = new Particle { El = el, T = t };
        _particles.Add(p);
        return p;
    }

    private static FrameworkElement Petal(int i, double size)
    {
        var colors = new[] { 0xFFF9CBD7u, 0xFFF5B4C6u, 0xFFFBD9E2u, 0xFFF7BFCFu };
        return new Border
        {
            Width = size,
            Height = size * 0.72,
            CornerRadius = new CornerRadius(0, size * 0.72, 0, size * 0.72),
            Background = new SolidColorBrush(Palette.C(colors[i % colors.Length])),
        };
    }

    private static FrameworkElement Leaf(int i, double size)
    {
        var colors = new[] { 0xFFE2552Bu, 0xFFF2994Au, 0xFFF7C548u, 0xFFC0392Bu, 0xFFE67E22u };
        return new Viewbox
        {
            Width = size, Height = size,
            Child = new Path { Data = Geo(MapleLeaf), Fill = new SolidColorBrush(Palette.C(colors[i % colors.Length])) },
        };
    }
}
