using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

/// <summary>
/// Owns the round: start, timer, win conditions, results, and the trip back to the lobby with the
/// connection intact. Lives on the "Game Manager" object next to PlayerManager (which is
/// DontDestroyOnLoad), so it survives Lobby -> Game -> Lobby.
///
/// Server-authoritative. The server polls live PlayerData rather than being told about each catch
/// or escape, so a survivor disconnecting mid-round can't leave a stale counter that never ends it.
/// </summary>
public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }

    public const float BleedoutSeconds = 45f;

    // Abilities. The client mirrors the cooldowns for its HUD; the server is what enforces them.
    public const float CrumbCooldown = 12f;
    public const float TrapCooldown = 20f;
    private const float CrumbMaxRange = 30f;
    private const float TrapLifetime = 45f;
    private const float TrapRadius = 1.5f;
    private const int MaxTraps = 3;
    private const float AlarmRevealSeconds = 5f;
    private const float TrapRevealSeconds = 10f;
    private const float TrapSlowSeconds = 4f;

    [SerializeField] private float roundDuration = 300f;
    [SerializeField] private float resultsDuration = 8f;
    // ponytail: 6 of the 9 cheese in GameScene - leaves route choice and slack for cheese the
    // hunter camps. tools/check_balance.py asserts this against the scene; raise both together.
    [SerializeField] private int cheeseNeededForDoor = 6;

    public NetworkVariable<RoundState> state = new NetworkVariable<RoundState>(RoundState.Waiting);
    public NetworkVariable<RoundOutcome> outcome = new NetworkVariable<RoundOutcome>(RoundOutcome.None);
    public NetworkVariable<int> cheeseCollected = new NetworkVariable<int>(0);
    public NetworkVariable<int> survivorsAlive = new NetworkVariable<int>(0);
    public NetworkVariable<int> survivorsEscaped = new NetworkVariable<int>(0);
    public NetworkVariable<int> survivorsTotal = new NetworkVariable<int>(0);
    // Written once at round start; clients derive the countdown locally instead of receiving ticks.
    public NetworkVariable<double> roundEndTime = new NetworkVariable<double>(0);
    // Cheese destroyed with a downed carrier. The door's requirement shrinks by this much so a
    // round can never be left needing more cheese than exists.
    public NetworkVariable<int> cheeseLost = new NetworkVariable<int>(0);
    // Running match table, rebuilt by the server at the end of every round. Plain text on purpose:
    // clients only ever draw it.
    public NetworkVariable<FixedString4096Bytes> scoreText = new NetworkVariable<FixedString4096Bytes>();

    private NetworkList<Vector3> cheeseSpots;
    // Hunter snares. Positions are replicated so a hunter's HUD can draw them; expiry is server-only.
    private NetworkList<Vector3> traps;
    private readonly List<double> trapExpiry = new List<double>();
    private bool cheeseApplied;
    private bool returningToLobby;
    private bool alarmed;
    private float nextTick;
    private RuntimeHud hud;

    // Server-only match state. Lives as long as the session, so scores and the hunter rotation
    // carry from round to round without anyone reconnecting.
    private readonly Dictionary<ulong, int> points = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, int> hunts = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();
    private readonly Dictionary<ulong, Perk> perks = new Dictionary<ulong, Perk>();
    private readonly Dictionary<ulong, float> nextCrumb = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, float> nextTrap = new Dictionary<ulong, float>();

    public int CheeseCollected => cheeseCollected.Value;
    // cheeseSpots.Count is how many cheese this round has, so lost cheese can never make the door
    // unreachable. 0 before the round has placed any.
    public int CheeseNeeded => Mathf.Min(cheeseNeededForDoor, Mathf.Max(0, cheeseSpots.Count - cheeseLost.Value));
    public float ResultsDuration => resultsDuration;
    public float SecondsLeft => Mathf.Max(0f, (float)(roundEndTime.Value - NetworkManager.ServerTime.Time));
    public string ScoreText => scoreText.Value.ToString();
    public int TrapCount => traps.Count;
    public Vector3 TrapAt(int i) => traps[i];

    // 0..1, derived on every peer from values that are already replicated - no extra variable.
    public float Rage => state.Value == RoundState.Playing
        ? RoundRules.Rage(cheeseCollected.Value, CheeseNeeded, roundDuration - SecondsLeft, roundDuration)
        : 0f;

    private string GameSceneName => LobbyManager.Instance != null ? LobbyManager.Instance.gameSceneName : "GameScene";
    private const string LobbySceneName = "LobbyScene";

    private void Awake()
    {
        cheeseSpots = new NetworkList<Vector3>();
        traps = new NetworkList<Vector3>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;

        SceneManager.sceneLoaded += OnSceneLoaded;
        cheeseSpots.OnListChanged += OnCheeseSpotsChanged;
        hud = new GameObject("RoundHud").AddComponent<RuntimeHud>();

        if (IsServer)
        {
            NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;

            // This object is DontDestroyOnLoad, so its variables outlive a session. Quitting
            // mid-round and re-hosting would otherwise open a lobby showing the old round's timer.
            state.Value = RoundState.Waiting;
            outcome.Value = RoundOutcome.None;
            cheeseCollected.Value = 0;
            cheeseLost.Value = 0;
            cheeseSpots.Clear();
            ClearTraps();

            // A new session is a new match.
            scoreText.Value = default;
            points.Clear();
            hunts.Clear();
            names.Clear();
            perks.Clear();
        }
    }

    public override void OnNetworkDespawn()
    {
        // A results countdown still running when the session ends must not reach for a
        // NetworkManager that is already gone.
        StopAllCoroutines();
        returningToLobby = false;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        cheeseSpots.OnListChanged -= OnCheeseSpotsChanged;
        if (NetworkManager != null && NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
        if (hud != null) Destroy(hud.gameObject);
        if (Instance == this) Instance = null;
        base.OnNetworkDespawn();
    }

    private void OnCheeseSpotsChanged(NetworkListEvent<Vector3> e) => cheeseApplied = false;

    // Every peer, when its own copy of the scene is up.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == GameSceneName) HidingSpot.RegisterAll();
    }

    // Server only: fires once every client has finished loading.
    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (sceneName == GameSceneName)
        {
            StartRound();
        }
        else if (sceneName == LobbySceneName && returningToLobby)
        {
            // The first trip into the lobby is spawned by PlayerManager itself; this is the return.
            returningToLobby = false;
            PlayerManager.Instance.SpawnLobbyPlayersForAllClients();
            state.Value = RoundState.Waiting;
        }
    }

    // ---- round flow (server) ----

    private void StartRound()
    {
        PickCheeseSpots();
        cheeseCollected.Value = 0;
        cheeseLost.Value = 0;
        alarmed = false;
        nextCrumb.Clear();
        nextTrap.Clear();
        ClearTraps();
        outcome.Value = RoundOutcome.None;
        roundEndTime.Value = NetworkManager.ServerTime.Time + roundDuration;
        Tick();
        state.Value = RoundState.Playing;
    }

    private void Update()
    {
        ApplyCheeseSpots();

        if (!IsServer || state.Value != RoundState.Playing) return;

        // Every frame, not on the 4 Hz tick: a sprinting survivor covers ~5 units between ticks and
        // would hop straight over a 1.5-unit trap.
        UpdateTraps();

        if (Time.time < nextTick) return;
        nextTick = Time.time + 0.25f;

        Tick();
        bool timeUp = NetworkManager.ServerTime.Time >= roundEndTime.Value;
        RoundOutcome result = RoundRules.Evaluate(survivorsAlive.Value, survivorsEscaped.Value, survivorsTotal.Value, timeUp);
        if (result != RoundOutcome.None) EndRound(result);
    }

    // Bleed out the downed, then recount from live state.
    private void Tick()
    {
        Dictionary<ulong, PlayerData> players = PlayerManager.Instance != null ? PlayerManager.Instance.GetConnectedPlayers() : null;
        if (players == null) return;

        int total = 0, alive = 0, escaped = 0;
        double now = NetworkManager.ServerTime.Time;
        foreach (PlayerData p in players.Values)
        {
            if (p == null || p.IsHunter) continue;

            if (p.lifeState.Value == LifeState.Downed && now >= p.bleedoutEnd.Value)
            {
                p.lifeState.Value = LifeState.Caught;
            }

            total++;
            LifeState s = p.lifeState.Value;
            if (s == LifeState.Alive || s == LifeState.Downed) alive++;
            else if (s == LifeState.Escaped) escaped++;
        }

        survivorsTotal.Value = total;
        survivorsAlive.Value = alive;
        survivorsEscaped.Value = escaped;
    }

    private void EndRound(RoundOutcome result)
    {
        ScoreRound();
        outcome.Value = result;
        state.Value = RoundState.Ending;
        StartCoroutine(ReturnToLobby());
    }

    // Survivors are paid for how they finished; every hunter shares the credit for who was taken out.
    // Runs while the players are still spawned, so their final life state is readable.
    private void ScoreRound()
    {
        Dictionary<ulong, PlayerData> players = PlayerManager.Instance != null ? PlayerManager.Instance.GetConnectedPlayers() : null;
        if (players == null) return;

        int caught = 0;
        foreach (PlayerData p in players.Values)
        {
            if (p == null || p.IsHunter) continue;
            if (p.lifeState.Value == LifeState.Caught || p.lifeState.Value == LifeState.Downed) caught++;
        }

        foreach (KeyValuePair<ulong, PlayerData> kv in players)
        {
            PlayerData p = kv.Value;
            if (p == null) continue;

            int gain = p.IsHunter ? RoundRules.HunterPoints(caught) : RoundRules.SurvivorPoints(p.lifeState.Value);
            points[kv.Key] = (points.TryGetValue(kv.Key, out int have) ? have : 0) + gain;
            names[kv.Key] = p.PlayerName;
        }

        scoreText.Value = BuildScoreText();
    }

    // Only players still connected, best first. Names are capped at 20 characters, so 8 rows (<= 90
    // UTF-8 bytes each) always fit in the FixedString4096Bytes - an overflow there would throw.
    private string BuildScoreText()
    {
        List<KeyValuePair<ulong, int>> rows = new List<KeyValuePair<ulong, int>>();
        foreach (KeyValuePair<ulong, int> kv in points)
        {
            if (NetworkManager.ConnectedClients.ContainsKey(kv.Key)) rows.Add(kv);
        }
        rows.Sort((a, b) => b.Value.CompareTo(a.Value));

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            string name = names.TryGetValue(rows[i].Key, out string n) ? n : "Player " + rows[i].Key;
            if (name.Length > 20) name = name.Substring(0, 20);
            if (i > 0) sb.Append('\n');
            sb.Append(i + 1).Append(". ").Append(name).Append("  ").Append(rows[i].Value);
        }
        return sb.ToString();
    }

    // No Shutdown() here: the session stays up, so the next round is one click, not a re-host.
    private IEnumerator ReturnToLobby()
    {
        yield return new WaitForSeconds(resultsDuration);

        PlayerManager.Instance.DespawnGamePlayers();
        LobbyManager.Instance.ResetForNewRound();
        cheeseSpots.Clear();
        ClearTraps();
        returningToLobby = true;
        NetworkManager.SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
    }

    // ---- cheese (item 03: one replicated counter; item 08: shuffled spawns) ----

    // Called with cheese that has been banked in a safe zone (see Deposit), not with a pickup.
    public void AddCheese(int amount)
    {
        if (!IsServer || state.Value != RoundState.Playing) return;

        cheeseCollected.Value += amount;

        if (!alarmed && CheeseNeeded > 0 && cheeseCollected.Value >= CheeseNeeded)
        {
            alarmed = true;
            Alarm();
        }
    }

    // A survivor standing in a safe zone banks whatever they carry.
    public void Deposit(PlayerData p)
    {
        if (!IsServer || state.Value != RoundState.Playing || p == null || p.carriedCheese.Value <= 0) return;

        int n = p.carriedCheese.Value;
        p.carriedCheese.Value = 0;
        AddCheese(n);
    }

    public void LoseCheese(int amount)
    {
        if (IsServer && amount > 0) cheeseLost.Value += amount;
    }

    // The door just opened for everyone: the hunter sees every survivor for a few seconds.
    private void Alarm()
    {
        double until = NetworkManager.ServerTime.Time + AlarmRevealSeconds;
        foreach (PlayerData p in PlayerManager.Instance.GetConnectedPlayers().Values)
        {
            if (p == null || p.IsHunter) continue;
            if (p.lifeState.Value == LifeState.Alive || p.lifeState.Value == LifeState.Downed) p.revealedUntil.Value = until;
        }
        AlarmClientRpc();
    }

    [ClientRpc]
    private void AlarmClientRpc()
    {
        ProximityAudio.PlayGlobal(ProximityAudio.Alarm);
        if (RuntimeHud.Instance != null) RuntimeHud.Instance.ShowBanner("ALARM - the cheese door is open!", 4f);
    }

    // Candidate pool = every authored cheese spot plus a spot on top of every table (~20 points,
    // all already in-bounds). Only the chosen positions are replicated; every peer moves its own
    // local cheese to them, so the cheese prefab needs no NetworkTransform.
    // ponytail: keys are NOT shuffled - each key gates a door, and a key landing behind its own
    // door soft-locks the round. Needs a reachability check before it is safe to randomize.
    private void PickCheeseSpots()
    {
        cheeseSpots.Clear();

        CheeseInteractable[] cheese = FindObjectsOfType<CheeseInteractable>();
        List<Vector3> pool = new List<Vector3>();
        foreach (CheeseInteractable c in cheese) pool.Add(c.transform.position);
        pool.AddRange(HidingSpot.TableTops());

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        for (int i = 0; i < cheese.Length && i < pool.Count; i++) cheeseSpots.Add(pool[i]);
    }

    // Every peer. Cheese are matched to spots by NetworkObjectId, which is the same everywhere.
    private void ApplyCheeseSpots()
    {
        if (cheeseApplied || cheeseSpots.Count == 0 || SceneManager.GetActiveScene().name != GameSceneName) return;

        CheeseInteractable[] cheese = FindObjectsOfType<CheeseInteractable>();
        if (cheese.Length == 0) return; // scene objects not up yet on this peer - retry next frame

        Array.Sort(cheese, (a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
        for (int i = 0; i < cheese.Length && i < cheeseSpots.Count; i++) cheese[i].MoveTo(cheeseSpots[i]);
        cheeseApplied = true;
    }

    // ---- who hunts, and perks ----

    // The least-hunted clients, so the role rotates round to round. Server only; PlayerManager calls it.
    public List<ulong> PickHunters(IReadOnlyList<ulong> clients)
    {
        List<ulong> picked = RoundRules.PickHunters(
            clients,
            c => hunts.TryGetValue(c, out int n) ? n : 0,
            RoundRules.HunterCount(clients.Count),
            n => Random.Range(0, n));

        foreach (ulong c in picked) hunts[c] = (hunts.TryGetValue(c, out int have) ? have : 0) + 1;
        return picked;
    }

    public Perk PerkOf(ulong clientId) => perks.TryGetValue(clientId, out Perk p) ? p : Perk.None;

    // Lobby only: a perk is locked in once the round starts.
    [ServerRpc(RequireOwnership = false)]
    public void SetPerkServerRpc(Perk perk, ServerRpcParams rpcParams = default)
    {
        if (state.Value != RoundState.Waiting || !Enum.IsDefined(typeof(Perk), perk)) return;
        perks[rpcParams.Receive.SenderClientId] = perk;
    }

    // ---- crumbs and traps ----

    // A survivor throws a crumb: everyone hears the clatter at the target, and the hunter gets a "?".
    [ServerRpc(RequireOwnership = false)]
    public void ThrowCrumbServerRpc(Vector3 target, ServerRpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        PlayerData who = PlayerOf(id);
        if (who == null || who.IsHunter || !who.CanAct || state.Value != RoundState.Playing) return;
        if (nextCrumb.TryGetValue(id, out float ready) && Time.time < ready) return;
        if ((target - who.transform.position).sqrMagnitude > CrumbMaxRange * CrumbMaxRange) return;

        nextCrumb[id] = Time.time + CrumbCooldown;
        NoiseClientRpc(target);
    }

    [ClientRpc]
    private void NoiseClientRpc(Vector3 position)
    {
        ProximityAudio.PlayAt(position, ProximityAudio.Clatter, 50f);

        PlayerData me = PlayerData.Local;
        if (me != null && me.IsHunter && RuntimeHud.Instance != null) RuntimeHud.Instance.AddMarker(position, "?", 3f);
    }

    // The hunter lays a hidden snare where they stand.
    [ServerRpc(RequireOwnership = false)]
    public void PlaceTrapServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        PlayerData who = PlayerOf(id);
        if (who == null || !who.IsHunter || state.Value != RoundState.Playing) return;
        if (nextTrap.TryGetValue(id, out float ready) && Time.time < ready) return;

        nextTrap[id] = Time.time + TrapCooldown;
        if (traps.Count >= MaxTraps) RemoveTrap(0); // the oldest makes room

        traps.Add(who.transform.position);
        trapExpiry.Add(NetworkManager.ServerTime.Time + TrapLifetime);
    }

    // Server, every frame. A survivor who steps on a snare is revealed and slowed; the snare is spent.
    private void UpdateTraps()
    {
        if (traps.Count == 0) return;

        double now = NetworkManager.ServerTime.Time;
        Dictionary<ulong, PlayerData> players = PlayerManager.Instance.GetConnectedPlayers();

        for (int i = traps.Count - 1; i >= 0; i--)
        {
            if (now >= trapExpiry[i]) { RemoveTrap(i); continue; }

            Vector3 at = traps[i];
            foreach (PlayerData p in players.Values)
            {
                if (p == null || p.IsHunter || !p.CanAct) continue;

                Vector3 d = p.transform.position - at;
                d.y = 0f;
                if (d.sqrMagnitude > TrapRadius * TrapRadius) continue;

                p.revealedUntil.Value = now + TrapRevealSeconds;
                p.slowedUntil.Value = now + TrapSlowSeconds;
                SnapClientRpc(at, p.OwnerClientId);
                RemoveTrap(i);
                break;
            }
        }
    }

    private void RemoveTrap(int i)
    {
        traps.RemoveAt(i);
        trapExpiry.RemoveAt(i);
    }

    private void ClearTraps()
    {
        traps.Clear();
        trapExpiry.Clear();
    }

    [ClientRpc]
    private void SnapClientRpc(Vector3 position, ulong victimClientId)
    {
        ProximityAudio.PlayAt(position, ProximityAudio.Snap, 40f);

        if (NetworkManager.Singleton.LocalClientId == victimClientId && RuntimeHud.Instance != null)
        {
            RuntimeHud.Instance.ShowBanner("Trap! The hunter can see you", 3f);
        }
    }

    // ---- hiding & reviving (server-validated; clients only ask) ----

    private PlayerData PlayerOf(ulong clientId)
    {
        return NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient c) && c.PlayerObject != null
            ? c.PlayerObject.GetComponent<PlayerData>()
            : null;
    }

    // Survivors hide in a free spot; the hunter "checks" a spot and flushes out whoever is in it.
    [ServerRpc(RequireOwnership = false)]
    public void UseHidingSpotServerRpc(int spot, ServerRpcParams rpcParams = default)
    {
        PlayerData who = PlayerOf(rpcParams.Receive.SenderClientId);
        if (who == null || state.Value != RoundState.Playing || spot < 0 || spot >= HidingSpot.Count) return;

        PlayerData occupant = null;
        foreach (PlayerData p in PlayerManager.Instance.GetConnectedPlayers().Values)
        {
            if (p != null && p.hiddenSpot.Value == spot) { occupant = p; break; }
        }

        if (who.IsHunter)
        {
            if (occupant != null) occupant.hiddenSpot.Value = -1;
        }
        else if (who.CanAct && occupant == null)
        {
            who.hiddenSpot.Value = spot;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void LeaveHidingSpotServerRpc(ServerRpcParams rpcParams = default)
    {
        PlayerData who = PlayerOf(rpcParams.Receive.SenderClientId);
        if (who != null && who.IsHidden) who.hiddenSpot.Value = -1;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestReviveServerRpc(ulong targetNetworkObjectId, ServerRpcParams rpcParams = default)
    {
        PlayerData who = PlayerOf(rpcParams.Receive.SenderClientId);
        if (who == null || who.IsHunter || !who.CanAct || state.Value != RoundState.Playing) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject obj)) return;
        PlayerData target = obj.GetComponent<PlayerData>();
        if (target == null || target == who || target.lifeState.Value != LifeState.Downed) return;

        // The client's raycast got them here; this just refuses a request from across the map.
        if ((target.transform.position - who.transform.position).sqrMagnitude > 8f * 8f) return;

        target.lifeState.Value = LifeState.Alive;
    }
}
