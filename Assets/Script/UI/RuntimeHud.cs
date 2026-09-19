using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Round timer, survivor counts, per-player status, results and match table, the hunter's markers
/// (pings, revealed survivors, traps, noises), banners, and the lobby perk picker.
/// Created by RoundManager at runtime and drawn with IMGUI, so there is no Canvas, font or prefab
/// to wire up in the Editor.
/// ponytail: IMGUI is plain. Replace with a Canvas when the game gets real UI art.
/// </summary>
public class RuntimeHud : MonoBehaviour
{
    public static RuntimeHud Instance { get; private set; }

    private struct Marker { public Vector3 pos; public string glyph; public float until; }

    private const string LobbySceneName = "LobbyScene";
    private static readonly string[] PerkNames = { "None", "Sprinter", "Medic", "Scout" };
    private static readonly string[] PerkHints =
    {
        "No perk",
        "Sprinter: sprint longer and recover faster",
        "Medic: revive teammates in 3s instead of 5s",
        "Scout: press Q to glimpse where the hunter is",
    };

    private readonly List<Marker> markers = new List<Marker>();
    private string banner;
    private float bannerUntil;

    private PlayerData[] players = new PlayerData[0];
    private float nextScan;
    private RoundState lastState;
    private float endingSince;

    private Perk chosen;
    private bool pushed;

    private GUIStyle big, mid, small, button;

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        chosen = (Perk)Mathf.Clamp(PlayerPrefs.GetInt("Perk", 0), 0, PerkNames.Length - 1);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Kept for the pings: a "!" a little above each point.
    public void ShowBlips(List<Vector3> points, float seconds)
    {
        foreach (Vector3 p in points) AddMarker(p + Vector3.up, "!", seconds);
    }

    public void AddMarker(Vector3 position, string glyph, float seconds)
    {
        markers.Add(new Marker { pos = position, glyph = glyph, until = Time.time + seconds });
    }

    public void ShowBanner(string text, float seconds)
    {
        banner = text;
        bannerUntil = Time.time + seconds;
    }

    private void Update()
    {
        RoundManager r = RoundManager.Instance;
        if (r == null) return;

        RoundState state = r.state.Value;
        if (state == RoundState.Ending && lastState != RoundState.Ending) endingSince = Time.time;
        lastState = state;

        markers.RemoveAll(m => Time.time > m.until);

        if (state != RoundState.Waiting && Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            players = FindObjectsOfType<PlayerData>();
        }

        PushPerk(r);
    }

    private static bool InLobby(RoundManager r) =>
        SceneManager.GetActiveScene().name == LobbySceneName
        && r.state.Value == RoundState.Waiting
        && !(LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted);

    // Tell the server which perk we want each time we land in the lobby (and whenever we change it).
    private void PushPerk(RoundManager r)
    {
        if (!InLobby(r)) { pushed = false; return; }
        if (pushed || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        r.SetPerkServerRpc(chosen);
        pushed = true;
    }

    private void OnGUI()
    {
        RoundManager r = RoundManager.Instance;
        if (r == null) return;

        BuildStyles();
        float w = Screen.width, h = Screen.height;

        if (r.state.Value == RoundState.Waiting)
        {
            if (InLobby(r)) DrawPerkPicker(w, h);
            return;
        }

        if (r.state.Value == RoundState.Ending)
        {
            DrawResults(r, w, h);
            return;
        }

        int t = Mathf.CeilToInt(r.SecondsLeft);
        Text(new Rect(0, h * 0.02f, w, h * 0.08f), $"{t / 60}:{t % 60:00}", big, t <= 30 ? Color.red : Color.white);
        Text(new Rect(0, h * 0.10f, w, h * 0.04f),
            $"Survivors {r.survivorsAlive.Value}/{r.survivorsTotal.Value}    Escaped {r.survivorsEscaped.Value}", small, Color.white);

        if (banner != null && Time.time < bannerUntil) Text(new Rect(0, h * 0.18f, w, h * 0.08f), banner, mid, Color.red);

        DrawStatus(r, w, h);
        DrawMarkers(r);
    }

    private void DrawStatus(RoundManager r, float w, float h)
    {
        PlayerData me = PlayerData.Local;
        if (me == null) return;

        string line = null;
        Color color = Color.white;

        if (me.lifeState.Value == LifeState.Downed)
        {
            int left = Mathf.CeilToInt((float)(me.bleedoutEnd.Value - NetworkManager.Singleton.ServerTime.Time));
            line = $"DOWNED - bleeding out in {Mathf.Max(0, left)}s. A teammate can hold E to revive you.";
            color = Color.red;
        }
        else if (me.IsSpectating)
        {
            line = me.lifeState.Value == LifeState.Escaped ? "You escaped! Spectating - click to switch player" : "You were caught. Spectating - click to switch player";
        }
        else if (me.IsHidden)
        {
            line = "Hiding - press E to come out";
        }
        else if (me.IsHunter)
        {
            HunterSense sense = me.GetComponent<HunterSense>();
            if (sense != null)
            {
                string q = sense.ReadyIn <= 0f ? "Q sense" : $"Q {Mathf.CeilToInt(sense.ReadyIn)}s";
                string f = sense.TrapReadyIn <= 0f ? "F trap" : $"F {Mathf.CeilToInt(sense.TrapReadyIn)}s";
                line = $"{q}    {f}    Rage {Mathf.RoundToInt(r.Rage * 100f)}%";
            }
        }
        else
        {
            List<string> parts = new List<string>();
            int carried = me.carriedCheese.Value;
            if (carried > 0) parts.Add($"Carrying {carried}/{RoundRules.CarryCap} - bank it in a safe zone");

            SurvivorAbilities a = me.GetComponent<SurvivorAbilities>();
            if (a != null)
            {
                parts.Add(a.CrumbReadyIn <= 0f ? "F crumb" : $"F {Mathf.CeilToInt(a.CrumbReadyIn)}s");
                if (me.ActivePerk == Perk.Scout) parts.Add(a.ScoutReadyIn <= 0f ? "Q scout" : $"Q {Mathf.CeilToInt(a.ScoutReadyIn)}s");
            }
            if (me.IsSlowed) parts.Add("Slowed!");
            if (parts.Count > 0) line = string.Join("    ", parts);
        }

        if (line != null) Text(new Rect(0, h * 0.86f, w, h * 0.05f), line, mid, color);
    }

    private Camera LocalCamera()
    {
        PlayerData me = PlayerData.Local;
        Movement mv = me != null ? me.GetComponent<Movement>() : null;
        return mv != null && mv.cameraTransform != null ? mv.cameraTransform.GetComponent<Camera>() : null;
    }

    private void DrawMarkers(RoundManager r)
    {
        Camera cam = LocalCamera();
        if (cam == null) return;

        foreach (Marker m in markers) Glyph(cam, m.pos, m.glyph, Color.red);

        // Only a hunter sees revealed survivors and their own snares.
        PlayerData me = PlayerData.Local;
        if (me == null || !me.IsHunter) return;

        foreach (PlayerData p in players)
        {
            if (p == null || p.IsHunter || !p.IsRevealed) continue;
            if (p.lifeState.Value == LifeState.Caught || p.lifeState.Value == LifeState.Escaped) continue;
            Glyph(cam, p.transform.position + Vector3.up, "!", Color.red);
        }

        for (int i = 0; i < r.TrapCount; i++) Glyph(cam, r.TrapAt(i), "x", Color.yellow);
    }

    private void Glyph(Camera cam, Vector3 world, string glyph, Color color)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        if (s.z <= 0f) return; // behind the camera
        Text(new Rect(s.x - 40f, Screen.height - s.y - 40f, 80f, 80f), glyph, big, color);
    }

    private void DrawPerkPicker(float w, float h)
    {
        float bw = w * 0.09f, bh = h * 0.06f, gap = w * 0.008f;
        float block = PerkNames.Length * bw + (PerkNames.Length - 1) * gap;
        float x0 = w * 0.02f;

        Text(new Rect(x0, h * 0.76f, block, h * 0.04f), "Survivor perk", small, Color.white);
        for (int i = 0; i < PerkNames.Length; i++)
        {
            GUI.color = (int)chosen == i ? Color.green : Color.white;
            if (GUI.Button(new Rect(x0 + i * (bw + gap), h * 0.81f, bw, bh), PerkNames[i], button))
            {
                chosen = (Perk)i;
                PlayerPrefs.SetInt("Perk", i);
                pushed = false;
            }
        }
        GUI.color = Color.white;
        Text(new Rect(x0, h * 0.88f, block * 1.6f, h * 0.04f), PerkHints[(int)chosen], small, Color.white);
    }

    private void DrawResults(RoundManager r, float w, float h)
    {
        string headline;
        switch (r.outcome.Value)
        {
            case RoundOutcome.SurvivorsEscaped: headline = "SURVIVORS ESCAPED"; break;
            case RoundOutcome.HunterCaughtAll: headline = "THE HUNTER CAUGHT EVERYONE"; break;
            default: headline = "TIME'S UP - THE HUNTER WINS"; break;
        }

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        Text(new Rect(0, h * 0.15f, w, h * 0.1f), headline, big, Color.white);

        // Left: how this round ended for each player. Right: the running match table.
        float y = h * 0.32f;
        foreach (PlayerData p in players)
        {
            if (p == null) continue;
            string status = p.IsHunter ? "Hunter"
                : p.lifeState.Value == LifeState.Escaped ? "Escaped"
                : p.lifeState.Value == LifeState.Caught ? "Caught"
                : p.lifeState.Value == LifeState.Downed ? "Downed" : "Survived";
            Text(new Rect(0, y, w * 0.5f, h * 0.05f), $"{p.PlayerName} - {status}", mid, p.lifeState.Value == LifeState.Escaped ? Color.green : Color.white);
            y += h * 0.05f;
        }

        string table = r.ScoreText;
        if (!string.IsNullOrEmpty(table))
        {
            float ty = h * 0.32f;
            Text(new Rect(w * 0.5f, ty, w * 0.5f, h * 0.05f), "Match scores", mid, Color.yellow);
            foreach (string row in table.Split('\n'))
            {
                ty += h * 0.05f;
                Text(new Rect(w * 0.5f, ty, w * 0.5f, h * 0.05f), row, mid, Color.white);
            }
        }

        int back = Mathf.CeilToInt(Mathf.Max(0f, r.ResultsDuration - (Time.time - endingSince)));
        Text(new Rect(0, h * 0.85f, w, h * 0.05f), $"Back to the lobby in {back}s", small, Color.white);
    }

    private void BuildStyles()
    {
        if (big != null) return;
        big = Style(Screen.height / 12);
        mid = Style(Screen.height / 28);
        small = Style(Screen.height / 38);
        button = new GUIStyle(GUI.skin.button) { fontSize = Screen.height / 40 };
    }

    private static GUIStyle Style(int size) =>
        new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

    private static void Text(Rect r, string s, GUIStyle style, Color color)
    {
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), s, style);
        style.normal.textColor = color;
        GUI.Label(r, s, style);
    }
}
