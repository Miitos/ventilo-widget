using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ventilo;

/// <summary>Champ d'état publié par le firmware (entités « Etat … », voir ventilo-controller/ventilo.yaml).</summary>
internal enum StateField { On, Speed, Light, LightColor, Reverse }

/// <summary>Client de l'API ESPHome (voir ventilo-controller/API.md).</summary>
internal sealed class FanClient
{
    // L'ESP32 ignore un appel reçu pendant une émission : on espace les commandes.
    private static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(450);

    // La résolution mDNS de ventilo.local peut prendre ~3 s à froid
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly HttpClient _events = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastSend = DateTime.MinValue;

    public string Host { get; set; } = "ventilo.local";

    private string BaseUrl
    {
        get
        {
            var h = Host.Trim().TrimEnd('/');
            return h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || h.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? h : "http://" + h;
        }
    }

    public async Task<bool> PressAsync(string entity)
    {
        await _gate.WaitAsync();
        try
        {
            var wait = _lastSend + MinSpacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            using var resp = await _http.PostAsync($"{BaseUrl}/button/{Uri.EscapeDataString(entity)}/press", null);
            _lastSend = DateTime.UtcNow;
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Le boîtier répond-il ? N'importe quelle réponse HTTP compte.</summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            using var resp = await _http.GetAsync(BaseUrl + "/", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ───────────── Synchro d'état ─────────────

    /// <summary>Corrige l'état tenu par le firmware. Rien n'est émis vers le ventilateur.</summary>
    public async Task<bool> SetStateAsync(StateField field, int value)
    {
        string path = field switch
        {
            StateField.On => Switch("Etat marche", value != 0),
            StateField.Light => Switch("Etat lumiere", value != 0),
            StateField.Reverse => Switch("Etat sens inverse", value != 0),
            StateField.Speed => $"/number/{Uri.EscapeDataString("Etat vitesse")}/set?value={value}",
            _ => $"/number/{Uri.EscapeDataString("Etat couleur")}/set?value={value}",
        };
        try
        {
            using var resp = await _http.PostAsync(BaseUrl + path, null);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }

        static string Switch(string name, bool on) => $"/switch/{Uri.EscapeDataString(name)}/{(on ? "turn_on" : "turn_off")}";
    }

    /// <summary>
    /// Écoute le flux SSE /events jusqu'à annulation, en se reconnectant tout seul.
    /// À la connexion, ESPHome renvoie l'état de toutes les entités.
    /// </summary>
    public async Task RunEventsAsync(Action<StateField, int> onState, Action<bool> onConnected, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var resp = await _events.GetAsync(BaseUrl + "/events", HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                onConnected(true);
                delay = TimeSpan.FromSeconds(2);

                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? evt = null;
                while (true)
                {
                    // ESPHome envoie un « ping » régulier : un long silence = connexion morte
                    using var lineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lineCts.CancelAfter(TimeSpan.FromSeconds(45));
                    var line = await reader.ReadLineAsync(lineCts.Token);
                    if (line is null) break;
                    if (line.StartsWith("event:")) evt = line[6..].Trim();
                    else if (line.StartsWith("data:") && evt == "state") Dispatch(line[5..], onState);
                    else if (line.Length == 0) evt = null;
                }
            }
            catch when (!ct.IsCancellationRequested)
            {
            }
            catch
            {
                return;
            }

            onConnected(false);
            try { await Task.Delay(delay, ct); } catch { return; }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    private static void Dispatch(string json, Action<StateField, int> onState)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string key = Normalize(Str(root, "id") + " " + Str(root, "name"));
            StateField? field =
                key.Contains("etatsensinverse") ? StateField.Reverse :
                key.Contains("etatmarche") ? StateField.On :
                key.Contains("etatvitesse") ? StateField.Speed :
                key.Contains("etatlumiere") ? StateField.Light :
                key.Contains("etatcouleur") ? StateField.LightColor : null;
            if (field is null) return;

            var v = root.TryGetProperty("value", out var val) ? val
                : root.TryGetProperty("state", out var st) ? st : default;
            int? value = v.ValueKind switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.Number => (int)Math.Round(v.GetDouble()),
                JsonValueKind.String => ParseString(v.GetString()),
                _ => null,
            };
            if (value is int n) onState(field.Value, n);
        }
        catch
        {
            // événement illisible : ignoré
        }
    }

    private static int? ParseString(string? s)
    {
        if (s is null) return null;
        if (s.Equals("ON", StringComparison.OrdinalIgnoreCase)) return 1;
        if (s.Equals("OFF", StringComparison.OrdinalIgnoreCase)) return 0;
        var num = s.Split(' ')[0];
        return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;
    }

    private static string Str(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
