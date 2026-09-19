using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SafeZones : NetworkBehaviour
{
    [Header("Safe Zone Settings")]
    public Collider hunterBlockerCollider;

    [Header("Occupancy Timer")]
    public float maxCharge = 20f;     // seconds a survivor can shelter before the zone stops blocking the hunter
    public float rechargeRate = 0.5f; // charge regained per second while the zone is empty

    // ponytail: zone-level charge, not per-player. Go per-player if two survivors sharing a zone
    // (and draining it at the same rate as one) turns out to feel unfair.
    private NetworkVariable<float> charge = new NetworkVariable<float>(20f);
    private readonly HashSet<PlayerData> sheltering = new HashSet<PlayerData>(); // server only
    private bool hunterNear; // every peer: physics triggers fire everywhere

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer) charge.Value = maxCharge;
    }

    void Start()
    {
        // Initially disable the hunter blocker - it only activates while a hunter is nearby
        if (hunterBlockerCollider != null)
        {
            hunterBlockerCollider.enabled = false;
        }
    }

    void Update()
    {
        // A drained zone simply stops blocking, so camping in one can no longer stall a round.
        if (hunterBlockerCollider != null)
        {
            hunterBlockerCollider.enabled = hunterNear && charge.Value > 0f;
        }

        if (!IsServer) return;

        // OnTriggerExit isn't reliable for a collider that gets disabled (caught, hiding, escaped).
        sheltering.RemoveWhere(p => p == null || !p.CanAct);

        // A safe zone is also where cheese is banked. Checked every frame rather than on entry, so
        // cheese picked up while already inside still counts.
        if (RoundManager.Instance != null)
        {
            foreach (PlayerData p in sheltering)
            {
                if (p.carriedCheese.Value > 0) RoundManager.Instance.Deposit(p);
            }
        }

        if (sheltering.Count > 0)
        {
            charge.Value = Mathf.Max(0f, charge.Value - Time.deltaTime);
        }
        else if (!hunterNear)
        {
            // Not while the hunter is inside: re-enabling the blocker on top of them would trap them.
            charge.Value = Mathf.Min(maxCharge, charge.Value + Time.deltaTime * rechargeRate);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        Track(other, true);
    }

    void OnTriggerExit(Collider other)
    {
        Track(other, false);
    }

    // Hunters toggle the blocker; survivors (server-side only) drain the charge. Reacting to
    // non-hunters for the blocker used to disable it on entry, letting the hunter walk straight in.
    private void Track(Collider other, bool inside)
    {
        PlayerData playerData = other.GetComponent<PlayerData>();
        if (playerData == null) return;

        if (playerData.IsHunter)
        {
            hunterNear = inside;
            print($"Hunter {playerData.PlayerName} {(inside ? "blocked from" : "left")} safe zone");
        }
        else if (IsServer)
        {
            if (inside) sheltering.Add(playerData);
            else sheltering.Remove(playerData);
        }
    }
}
