using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A survivor's active abilities, added to the local player's body at spawn and idle while that
/// player is the hunter (whose abilities live in HunterSense).
///   F - throw a crumb: a noise the hunter is drawn to (server-enforced cooldown).
///   Q - Scout perk only: glimpse where the hunters are for a few seconds.
///
/// ponytail: the Scout glimpse is drawn only while the hunter is in front of the camera; add edge
/// arrows if playtesting says a behind-you hunter is too easy to miss. Its cooldown is client-side.
/// </summary>
public class SurvivorAbilities : MonoBehaviour
{
    private const float ThrowRange = 25f;
    private const float ScoutCooldown = 40f;
    private const float ScoutSeconds = 3f;

    private PlayerData me;
    private Movement movement;
    private float crumbReadyAt;
    private float scoutReadyAt;

    public float CrumbReadyIn => Mathf.Max(0f, crumbReadyAt - Time.time);
    public float ScoutReadyIn => Mathf.Max(0f, scoutReadyAt - Time.time);

    private void Start()
    {
        me = GetComponent<PlayerData>();
        movement = GetComponent<Movement>();
    }

    private void Update()
    {
        if (me == null || me.IsHunter || !me.CanAct) return;

        RoundManager r = RoundManager.Instance;
        if (r == null || r.state.Value != RoundState.Playing) return;

        if (Input.GetKeyDown(KeyCode.F) && CrumbReadyIn <= 0f) Throw(r);
        if (me.ActivePerk == Perk.Scout && Input.GetKeyDown(KeyCode.Q) && ScoutReadyIn <= 0f) Scout();
    }

    private void Throw(RoundManager r)
    {
        Transform cam = movement != null ? movement.cameraTransform : null;
        if (cam == null) return;

        // Start a unit ahead of the camera so the ray can't hit this player's own capsule.
        Vector3 origin = cam.position + cam.forward;
        Vector3 target = cam.position + cam.forward * ThrowRange;
        if (Physics.Raycast(origin, cam.forward, out RaycastHit hit, ThrowRange, ~0, QueryTriggerInteraction.Ignore))
        {
            target = hit.point - cam.forward * 0.2f;
        }

        crumbReadyAt = Time.time + RoundManager.CrumbCooldown;
        r.ThrowCrumbServerRpc(target);
    }

    private void Scout()
    {
        scoutReadyAt = Time.time + ScoutCooldown;

        List<Vector3> points = new List<Vector3>();
        foreach (PlayerData hunter in PlayerData.GetHunters()) points.Add(hunter.transform.position);

        if (RuntimeHud.Instance != null) RuntimeHud.Instance.ShowBlips(points, ScoutSeconds);
    }
}
