using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace VentiloTray;

internal sealed record Palette(
    ElementTheme Theme, DecorKind Decor,
    Color Tint, Color Text, Color Text2, Color Text3,
    Color Ctrl, Color CtrlBorder, Color Bar,
    Color Accent, Color OnAccent,
    Color Ring, Color RingFill, Color Blade, Color Divider, Color Footer,
    Color Ok, Color Err)
{
    /// <summary>Clés enregistrées dans les réglages, dans l'ordre du sélecteur de thème.</summary>
    public static readonly string[] Keys = { "System", "Dark", "Light", "Sakura", "Autumn", "Winter", "Summer" };
    public static readonly string[] Labels = { "Système", "Sombre", "Clair", "Sakura", "Automne", "Hiver", "Nuit d’été" };

    public static Color C(uint argb) =>
        ColorHelper.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static Color OnColor(Color bg) =>
        (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255 > 0.55 ? C(0xFF000000) : C(0xFFFFFFFF);

    public static bool SystemIsDark(UISettings ui) =>
        ui.GetColorValue(UIColorType.Background).R < 128;

    /// <summary>Thème sombre décoré : même structure, seules les teintes changent.</summary>
    private static Palette Seasonal(DecorKind decor, uint tint, uint light, uint accent, uint onAccent, uint ringFill) =>
        new(ElementTheme.Dark, decor,
            Tint: C(tint), Text: C(0xFFFFFFFF), Text2: C(0xCC000000 | light), Text3: C(0xA3000000 | light),
            Ctrl: C(0x14000000 | light), CtrlBorder: C(0x1F000000 | light), Bar: C(0x24000000 | light),
            Accent: C(accent), OnAccent: C(onAccent),
            Ring: C(0x2E000000 | light), RingFill: C(ringFill), Blade: C(0xE0000000 | light),
            Divider: C(0x1A000000 | light), Footer: C(0x38000000),
            Ok: C(0xFF6CCB5F), Err: C(0xFFFF99A4));

    public static Palette Resolve(string theme, UISettings ui)
    {
        if (theme == "System") theme = SystemIsDark(ui) ? "Dark" : "Light";
        switch (theme)
        {
            case "Sakura": return Seasonal(DecorKind.Sakura, 0x9E2A1724, 0xFFF0F5, 0xFFF48FB1, 0xFF2B0A17, 0x99241620);
            case "Autumn": return Seasonal(DecorKind.Autumn, 0x9E2B1A10, 0xFFF1E4, 0xFFF2994A, 0xFF2A1300, 0x99261810);
            case "Winter": return Seasonal(DecorKind.Winter, 0x9E122233, 0xEAF4FF, 0xFF8FD3FF, 0xFF00203A, 0x99101C2A);
            case "Summer": return Seasonal(DecorKind.Summer, 0xA60B1626, 0xEFF6E0, 0xFFD4F06B, 0xFF1C2600, 0x990A1420);
            case "Light":
            {
                var accent = ui.GetColorValue(UIColorType.AccentDark1);
                return new Palette(ElementTheme.Light, DecorKind.None,
                    Tint: C(0x66F6F6F6), Text: C(0xFF1A1A1A), Text2: C(0xAD000000), Text3: C(0x94000000),
                    Ctrl: C(0xB3FFFFFF), CtrlBorder: C(0x12000000), Bar: C(0x17000000),
                    Accent: accent, OnAccent: OnColor(accent),
                    Ring: C(0x1F000000), RingFill: C(0x00000000), Blade: C(0xB8000000), Divider: C(0x0F000000), Footer: C(0x66FFFFFF),
                    Ok: C(0xFF107C10), Err: C(0xFFC42B1C));
            }
            default:
            {
                var accent = ui.GetColorValue(UIColorType.AccentLight2);
                return new Palette(ElementTheme.Dark, DecorKind.None,
                    Tint: C(0x66202020), Text: C(0xFFFFFFFF), Text2: C(0xC7FFFFFF), Text3: C(0x99FFFFFF),
                    Ctrl: C(0x0FFFFFFF), CtrlBorder: C(0x14FFFFFF), Bar: C(0x1FFFFFFF),
                    Accent: accent, OnAccent: OnColor(accent),
                    Ring: C(0x24FFFFFF), RingFill: C(0x00000000), Blade: C(0xDBFFFFFF), Divider: C(0x12FFFFFF), Footer: C(0x2E000000),
                    Ok: C(0xFF6CCB5F), Err: C(0xFFFF99A4));
            }
        }
    }
}
