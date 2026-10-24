using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace DamageMeter;

/// <summary>
/// The overlay: one panel with a line per player (DPS over the last few seconds, total, share of the party's
/// damage), and in detailed mode a line per spell under each player. F3 shows or hides it, F4 switches between
/// compact and detailed.
/// </summary>
public class Overlay : MonoBehaviour
{
    public Overlay(IntPtr ptr) : base(ptr) { }

    private const float Width = 330f;

    private GameObject canvasGo, panel;
    private RectTransform panelRt, textRt;
    private TextMeshProUGUI text;
    private float nextRefresh, nextErrorLog;
    private string lastText = "";

    private static readonly Dictionary<string, string> nameCache = new();
    private static readonly Dictionary<ulong, string> playerNames = new();
    private static readonly Dictionary<ulong, Color> playerColors = new();

    private static Color PlayerColor(ulong id, Color fallback) => playerColors.TryGetValue(id, out var c) ? c : fallback;
    private static float nextNameScan;

    private void Update()
    {
        try
        {
            if (Pressed(Plugin.ToggleKey.Value)) Plugin.Visible.Value = !Plugin.Visible.Value;
            if (Pressed(Plugin.DetailKey.Value)) Plugin.Detailed.Value = !Plugin.Detailed.Value;
            if (Pressed(Plugin.ResetKey.Value)) Tracker.Reset();

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;

            Tracker.HostTick();

            bool show = Plugin.Enabled.Value && Plugin.Visible.Value && (Tracker.Playing || (!Tracker.InRun && Tracker.HaveData && Plugin.KeepAfterRun.Value));
            if (show && !Plugin.ShowWhenSolo.Value && IsSolo()) show = false;
            if (!show) { if (panel != null && panel.activeSelf) panel.SetActive(false); return; }

            if (!Build()) return;
            string s = Compose();
            if (s != lastText)
            {
                lastText = s;
                text.text = s;
                float h = 0f;
                try { h = text.GetPreferredValues(s, Width - 20f, 0f).y; } catch { }
                if (h <= 0f) h = s.Split('\n').Length * 18f;
                panelRt.sizeDelta = new Vector2(Width, h + 16f);
                textRt.sizeDelta = new Vector2(Width - 20f, h + 4f);
            }
            Place();
            if (!panel.activeSelf) panel.SetActive(true);
        }
        catch (Exception ex)
        {
            if (Time.unscaledTime < nextErrorLog) return;
            nextErrorLog = Time.unscaledTime + 10f;
            Plugin.Log.LogError($"Overlay: {ex}");
        }
    }

    private static bool IsSolo()
    {
        try { var nm = NetworkManager.Singleton; return nm == null || !nm.IsListening || (nm.IsServer && Tracker.ClientCount(nm) <= 1); }
        catch { return true; }
    }

    private static bool Pressed(string keyName)
    {
        var kb = Keyboard.current;
        if (kb == null || string.IsNullOrEmpty(keyName)) return false;
        if (!Enum.TryParse(keyName, true, out Key key) || key == Key.None) return false;
        var control = kb[key];
        return control != null && control.wasPressedThisFrame;
    }

    // ---------- drawing ----------

    private bool Build()
    {
        if (panel != null && !panel.WasCollected && panel != null) return true;
        TMP_FontAsset font = null;
        try
        {
            var any = UObject.FindObjectOfType<TextMeshProUGUI>();
            if (any != null) font = any.font;
            if (font == null) font = TMP_Settings.defaultFontAsset;
        }
        catch { }
        if (font == null) return false;

        canvasGo = new GameObject("DamageMeter_Canvas");
        DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        panel = new GameObject("Panel");
        panelRt = panel.AddComponent<RectTransform>();
        panel.transform.SetParent(canvasGo.transform, false);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.04f, 0.12f, Mathf.Clamp01(Plugin.BackgroundOpacity.Value));
        bg.raycastTarget = false;

        var tgo = new GameObject("Text");
        textRt = tgo.AddComponent<RectTransform>();
        tgo.transform.SetParent(panel.transform, false);
        text = tgo.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = 15f;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = true;
        text.raycastTarget = false;
        text.color = Color.white;
        textRt.anchorMin = textRt.anchorMax = textRt.pivot = new Vector2(0.5f, 0.5f);
        textRt.anchoredPosition = Vector2.zero;
        lastText = "";
        return true;
    }

    private void Place()
    {
        float scale = Mathf.Clamp(Plugin.Scale.Value, 0.5f, 2.5f);
        panelRt.localScale = new Vector3(scale, scale, 1f);
        Vector2 a = Plugin.Anchor.Value switch
        {
            "TopLeft" => new Vector2(0f, 1f),
            "TopRight" => new Vector2(1f, 1f),
            "Right" => new Vector2(1f, 0.5f),
            "BottomLeft" => new Vector2(0f, 0f),
            "BottomRight" => new Vector2(1f, 0f),
            _ => new Vector2(0f, 0.5f), // Left
        };
        panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = a;
        float x = a.x < 0.5f ? Plugin.OffsetX.Value : -Plugin.OffsetX.Value;
        float y = a.y > 0.5f ? -Plugin.OffsetY.Value : a.y < 0.5f ? Plugin.OffsetY.Value : Plugin.OffsetY.Value;
        panelRt.anchoredPosition = new Vector2(x, y);
    }

    private string Compose()
    {
        var rows = Tracker.Rows;
        var sb = new StringBuilder();
        sb.Append("<b>DAMAGE</b><size=80%><color=#B8B0D0>  ");
        sb.Append(TimeText(Tracker.GameRunSeconds > 0f ? Tracker.GameRunSeconds : Tracker.RunSeconds)).Append("</color></size>");
        sb.Append("<pos=50%><size=80%><color=#B8B0D0>DPS<pos=70%>TOTAL<pos=89%>%</color></size>");

        if (rows.Count == 0)
        {
            bool client = false;
            try { var nm = NetworkManager.Singleton; client = nm != null && nm.IsListening && !nm.IsServer; } catch { }
            sb.Append("\n<size=85%><color=#B8B0D0>");
            sb.Append(client && Time.unscaledTime - Tracker.RunStartedUnscaled > 15f
                ? "No numbers from the host yet. The host needs DamageMeter too."
                : "No damage yet.");
            sb.Append("</color></size>");
            return sb.ToString();
        }

        double party = 0;
        foreach (var r in rows) party += r.Total;
        bool detailed = Plugin.Detailed.Value;
        int maxSpells = Mathf.Clamp(Plugin.MaxSpellsPerPlayer.Value, 1, 20);

        foreach (var r in rows)
        {
            string col = ColorUtility.ToHtmlStringRGB(Brighten(PlayerColor(r.Id, r.Color)));
            sb.Append('\n');
            sb.Append("<color=#").Append(col).Append('>').Append(Trim(PlayerName(r.Id), 16)).Append("</color>");
            sb.Append("<pos=50%>").Append(Short(r.Dps));
            sb.Append("<pos=70%>").Append(Short(r.Total));
            sb.Append("<pos=89%>").Append(party > 0 ? Math.Round(100.0 * r.Total / party).ToString(CultureInfo.InvariantCulture) : "0");

            if (!detailed) continue;
            int shown = 0;
            double rest = 0;
            foreach (var s in r.Sources)
            {
                if (shown >= maxSpells) { rest += s.Value; continue; }
                shown++;
                sb.Append("\n<size=82%><color=#D8D2EA>   ").Append(Trim(SourceName(s.Key), 20)).Append("</color>");
                sb.Append("<pos=70%>").Append(Short(s.Value));
                sb.Append("<pos=89%>").Append(r.Total > 0 ? Math.Round(100.0 * s.Value / r.Total).ToString(CultureInfo.InvariantCulture) : "0");
                sb.Append("</size>");
            }
            if (rest > 0)
                sb.Append("\n<size=82%><color=#B8B0D0>   other<pos=70%>").Append(Short(rest)).Append("</color></size>");
        }
        return sb.ToString();
    }

    private static Color Brighten(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Min(s, 0.75f), Mathf.Max(v, 0.85f));
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    private static string TimeText(float seconds)
    {
        int t = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{t / 60}:{t % 60:00}";
    }

    internal static string Short(double v)
    {
        var c = CultureInfo.InvariantCulture;
        if (v < 1000) return Math.Round(v).ToString(c);
        if (v < 10_000) return (v / 1000).ToString("0.0", c) + "k";
        if (v < 1_000_000) return Math.Round(v / 1000).ToString(c) + "k";
        if (v < 10_000_000) return (v / 1_000_000).ToString("0.00", c) + "M";
        if (v < 1_000_000_000) return (v / 1_000_000).ToString("0.0", c) + "M";
        return (v / 1_000_000_000).ToString("0.00", c) + "B";
    }

    // ---------- names ----------

    private static string PlayerName(ulong id)
    {
        if (Time.unscaledTime >= nextNameScan || !playerNames.ContainsKey(id))
        {
            nextNameScan = Time.unscaledTime + 5f;
            try
            {
                foreach (var p in UObject.FindObjectsOfType<GameplayPlayerIdentity>())
                {
                    if (p == null || !p.IsSpawned) continue;
                    string n = null;
                    try { n = p.GetDisplayName(); } catch { }
                    if (!string.IsNullOrWhiteSpace(n)) playerNames[p.OwnerClientId] = n;
                    try { var c = p.PlayerColor; if (c.a > 0.01f) playerColors[p.OwnerClientId] = c; } catch { }
                }
            }
            catch { }
        }
        return playerNames.TryGetValue(id, out var name) ? name : $"Player {id + 1}";
    }

    private static string SourceName(int key) => key switch
    {
        Sources.InfusedTornado => "Infused tornado",
        Sources.Explosions => "Explosions",
        Sources.Other => "Other",
        _ => SpellName(key),
    };

    private static string SpellName(int spell)
    {
        string id;
        try { id = ((SpellType)spell).ToString(); } catch { id = spell.ToString(CultureInfo.InvariantCulture); }
        string key = "SpellName_" + id;
        if (nameCache.TryGetValue(key, out var cached)) return cached;
        string result = null;
        try
        {
            var db = LocalizationSettings.StringDatabase;
            if (db != null) result = db.GetLocalizedString(key);
        }
        catch { }
        bool ok = !string.IsNullOrWhiteSpace(result) && result != key && !result.Contains("No translation found");
        if (ok) { nameCache[key] = result; return result; }
        return Pretty(id);
    }

    private static string Pretty(string id)
    {
        if (id == "Smg") return "SMG";
        var sb = new StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            if (i > 0 && char.IsUpper(id[i]) && !char.IsUpper(id[i - 1])) sb.Append(' ');
            sb.Append(id[i]);
        }
        return sb.ToString();
    }
}
