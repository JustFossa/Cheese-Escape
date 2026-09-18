using Unity.Netcode;
using UnityEngine;

public class SafeZones : NetworkBehaviour
{
    [Header("Safe Zone Settings")]
    public Collider hunterBlockerCollider;

    void Start()
    {
        // Initially disable the hunter blocker - it only activates while a hunter is nearby
        if (hunterBlockerCollider != null)
        {
            hunterBlockerCollider.enabled = false;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        SetBlockerForHunter(other, true);
    }

    void OnTriggerExit(Collider other)
    {
        SetBlockerForHunter(other, false);
    }

    // Only hunters may toggle the blocker. Reacting to non-hunters used to disable the
    // blocker on entry (letting the hunter walk straight in) and re-enable it on any exit.
    private void SetBlockerForHunter(Collider other, bool enable)
    {
        if (hunterBlockerCollider == null) return;

        PlayerData playerData = other.GetComponent<PlayerData>();
        if (playerData == null || !playerData.IsHunter) return;

        hunterBlockerCollider.enabled = enable;
        print($"Hunter {playerData.PlayerName} {(enable ? "blocked from" : "left")} safe zone");
    }
}
