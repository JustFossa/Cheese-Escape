using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hunter's abilities. Q pulses the position of every survivor who has moved recently (standing
/// still or hiding is the counter-play); its cooldown shortens as the hunter's rage builds. F lays a
/// hidden snare. Added to the hunter's own body at runtime.
///
/// ponytail: the Q ping is computed on the hunter's client from replicated transforms - no RPC, and
/// its cooldown is not server-enforced; move it into a ServerRpc if a modified client ever matters.
/// The trap IS server-enforced (RoundManager.PlaceTrapServerRpc).
/// </summary>
public class HunterSense : MonoBehaviour
{
    private const float RecentlyMoved = 2f;   // seconds
    private const float BlipTime = 3f;

    private struct Track { public Vector3 pos; public float movedAt; }

    private readonly Dictionary<PlayerData, Track> seen = new Dictionary<PlayerData, Track>();
    private PlayerData[] players = new PlayerData[0];
    private float nextScan;
    private float readyAt;
    private float trapReadyAt;

    public float ReadyIn => Mathf.Max(0f, readyAt - Time.time);
    public float TrapReadyIn => Mathf.Max(0f, trapReadyAt - Time.time);

    private void Update()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            players = FindObjectsOfType<PlayerData>();
        }

        foreach (PlayerData p in players)
        {
            if (p == null || p.IsHunter) continue;

            seen.TryGetValue(p, out Track t);
            if ((p.transform.position - t.pos).sqrMagnitude > 0.01f) t.movedAt = Time.time;
            t.pos = p.transform.position;
            seen[p] = t;
        }

        if (Input.GetKeyDown(KeyCode.Q) && ReadyIn <= 0f) Ping();
        if (Input.GetKeyDown(KeyCode.F) && TrapReadyIn <= 0f) PlaceTrap();
    }

    private void PlaceTrap()
    {
        RoundManager r = RoundManager.Instance;
        if (r == null || r.state.Value != RoundState.Playing) return;

        trapReadyAt = Time.time + RoundManager.TrapCooldown;
        r.PlaceTrapServerRpc();
    }

    private void Ping()
    {
        float rage = RoundManager.Instance != null ? RoundManager.Instance.Rage : 0f;
        readyAt = Time.time + RoundRules.SenseCooldown(rage);

        List<Vector3> blips = new List<Vector3>();
        foreach (KeyValuePair<PlayerData, Track> kv in seen)
        {
            PlayerData p = kv.Key;
            if (p == null || !p.CanAct) continue;
            if (Time.time - kv.Value.movedAt <= RecentlyMoved) blips.Add(p.transform.position);
        }

        if (RuntimeHud.Instance != null) RuntimeHud.Instance.ShowBlips(blips, BlipTime);
    }
}
