using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameEnd : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        PlayerData playerData = other.GetComponent<PlayerData>();
        if (playerData != null && !playerData.IsHunter)
        {
            // Trigger game end for this player
            playerData.ReachExit();
        }
    }
}
