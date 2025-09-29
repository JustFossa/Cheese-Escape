using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SafeZones : NetworkBehaviour
{
    [Header("Safe Zone Settings")]

    private Collider safeZoneCollider;
    public Collider hunterBlockerCollider;
    
    void Start()
    {
        // Initially disable the hunter blocker - it should only activate when hunter tries to enter
        if (hunterBlockerCollider != null)
        {
            hunterBlockerCollider.enabled = false;
            print("Hunter blocker collider initially disabled");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        print("Trigger Entered by: " + other.gameObject.name);
        PlayerData playerData = other.gameObject.GetComponent<PlayerData>();
        
        if (playerData != null)
        {
            print($"Player {playerData.PlayerName} entered safe zone. IsHunter: {playerData.isHunter.Value}");
            
            if (playerData.isHunter.Value)
            {
                // Block hunter from entering safe zone
                hunterBlockerCollider.enabled = true;
                print("Hunter blocked from entering safe zone");
            }
            else
            {
                // Allow non-hunters to enter safe zone
                hunterBlockerCollider.enabled = false;
                print("Non-hunter allowed in safe zone");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        PlayerData playerData = other.gameObject.GetComponent<PlayerData>();
        
        if (playerData != null)
        {
            print($"Player {playerData.PlayerName} exited safe zone. IsHunter: {playerData.isHunter.Value}");
            
            // When someone exits the safe zone, re-enable the blocker to prevent hunter entry
            hunterBlockerCollider.enabled = true;
            print("Hunter blocker re-enabled after player exit");
        }
    }
}
