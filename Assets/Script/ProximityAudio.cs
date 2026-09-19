using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Audio as information: you hear other players' footsteps in 3D, and as a survivor your own
/// heartbeat speeds up as the hunter closes. Added to every player body at spawn.
///
/// No RPCs - transforms already replicate, so each client works out how fast a remote player is
/// moving from their own position deltas.
///
/// ponytail: the clips are synthesized placeholders. Drop real files at
/// Resources/Audio/footstep, heartbeat and pickup (any audio format) and they replace these.
/// </summary>
public class ProximityAudio : MonoBehaviour
{
    private const float HearingRange = 30f;
    private const float HeartbeatRange = 25f;

    private static AudioClip step, beat, pickup, alarm, clatter, snap;
    public static AudioClip Step => Clip(ref step, "footstep", 0.12f, t => (Random.value * 2f - 1f) * Mathf.Exp(-t * 40f) * 0.6f);
    public static AudioClip Beat => Clip(ref beat, "heartbeat", 0.6f, t => Thump(t) + 0.7f * Thump(t - 0.22f));
    public static AudioClip Pickup => Clip(ref pickup, "pickup", 0.3f, t => Mathf.Sin(2f * Mathf.PI * (880f + 3520f * t) * t) * Mathf.Exp(-t * 10f) * 0.5f);
    // Door-open alarm: two tones alternating every 0.2s. Drop Resources/Audio/alarm|clatter|snap to replace.
    public static AudioClip Alarm => Clip(ref alarm, "alarm", 1.2f, t => Mathf.Sin(2f * Mathf.PI * (Mathf.Repeat(t, 0.4f) < 0.2f ? 700f : 950f) * t) * 0.45f);
    // A thrown crumb hitting the floor: a loud, rattly noise burst.
    public static AudioClip Clatter => Clip(ref clatter, "clatter", 0.5f, t => (Random.value * 2f - 1f) * Mathf.Exp(-t * 9f) * (0.6f + 0.4f * Mathf.Sin(t * 90f)));
    // A trap going off: one sharp click.
    public static AudioClip Snap => Clip(ref snap, "snap", 0.18f, t => (Random.value * 2f - 1f) * Mathf.Exp(-t * 45f) * 0.9f);

    private static float Thump(float t) => t < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 18f);

    private static AudioClip Clip(ref AudioClip cache, string name, float seconds, Func<float, float> synth)
    {
        if (cache != null) return cache;

        cache = Resources.Load<AudioClip>("Audio/" + name);
        if (cache == null)
        {
            const int rate = 22050;
            float[] data = new float[Mathf.CeilToInt(seconds * rate)];
            for (int i = 0; i < data.Length; i++) data[i] = synth(i / (float)rate);
            cache = AudioClip.Create(name, data.Length, 1, rate, false);
            cache.SetData(data, 0);
        }
        return cache;
    }

    // A one-off 3D sound that anyone in range can hear - a cheese being picked up, for instance.
    public static void PlayAt(Vector3 position, AudioClip clip, float maxDistance = 40f)
    {
        GameObject go = new GameObject("OneShot");
        go.transform.position = position;
        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 2f;
        s.maxDistance = maxDistance;
        s.Play();
        Destroy(go, clip.length + 0.1f);
    }

    // A sound for the whole map - the alarm - rather than one placed in the room.
    public static void PlayGlobal(AudioClip clip)
    {
        GameObject go = new GameObject("GlobalOneShot");
        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.spatialBlend = 0f;
        s.Play();
        Destroy(go, clip.length + 0.1f);
    }

    private PlayerData me;
    private List<PlayerData> hunters = new List<PlayerData>();
    private AudioSource source;
    private Vector3 last;
    private float stepClock, beatClock, nextHunterSearch;

    private void Start()
    {
        me = GetComponent<PlayerData>();
        last = transform.position;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        if (me.IsOwner)
        {
            source.spatialBlend = 0f; // your own heartbeat is in your head, not in the room
        }
        else
        {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = HearingRange;
        }
    }

    private void Update()
    {
        Vector3 delta = transform.position - last;
        delta.y = 0f;
        last = transform.position;

        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        if (speed > 40f) speed = 0f; // a teleport into or out of a hiding spot, not running

        if (me.IsOwner) Heartbeat();
        else Footsteps(speed);
    }

    private void Footsteps(float speed)
    {
        if (!me.CanAct || speed < 2f)
        {
            stepClock = 0f;
            return;
        }

        stepClock -= Time.deltaTime;
        if (stepClock > 0f) return;

        float run = Mathf.Clamp01(speed / 20f);
        stepClock = Mathf.Lerp(0.5f, 0.25f, run);
        source.pitch = Random.Range(0.9f, 1.1f);
        source.PlayOneShot(Step, Mathf.Lerp(0.4f, 1f, run));
    }

    private void Heartbeat()
    {
        if (me.IsHunter || !me.CanAct) return;

        // Re-read once a second: hunters are assigned just after spawn, and there can be two.
        if (Time.time >= nextHunterSearch)
        {
            nextHunterSearch = Time.time + 1f;
            hunters = PlayerData.GetHunters();
        }

        float distance = float.MaxValue;
        foreach (PlayerData h in hunters)
        {
            if (h != null) distance = Mathf.Min(distance, Vector3.Distance(transform.position, h.transform.position));
        }
        if (distance > HeartbeatRange) return;

        beatClock -= Time.deltaTime;
        if (beatClock > 0f) return;

        float closeness = 1f - distance / HeartbeatRange;
        beatClock = Mathf.Lerp(1.1f, 0.5f, closeness);
        source.PlayOneShot(Beat, Mathf.Lerp(0.25f, 1f, closeness));
    }
}
