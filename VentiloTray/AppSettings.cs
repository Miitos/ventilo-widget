using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace VentiloTray;

/// <summary>État supposé du ventilateur : l'API ne renvoie rien, on retient ce qu'on a envoyé.</summary>
internal sealed class FanState
{
    public bool On { get; set; }
    public int Speed { get; set; } = 3;
    public bool Light { get; set; }
    public int LightColor { get; set; }
    public bool Reverse { get; set; }
    public DateTime? LastCommandUtc { get; set; }

    public FanState Clone() => (FanState)MemberwiseClone();
}

internal sealed class AppSettings
{
    public string Theme { get; set; } = "System"; // System | Dark | Light | Sakura
    public string Host { get; set; } = "ventilo.local";
    public string Name { get; set; } = "Ventilateur";
    public FanState State { get; set; } = new();

    /// <summary>Réglages en mémoire seulement (mode démo) : jamais écrits sur disque.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Transient { get; set; }

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VentiloTray");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // fichier corrompu : on repart des valeurs par défaut
        }
        return new();
    }

    public void Save()
    {
        if (Transient) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}

internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VentiloTray";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue(ValueName, false);
    }
}
