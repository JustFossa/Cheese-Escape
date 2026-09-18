using Unity.Netcode;
using UnityEngine;

public class GameEnd : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        // Physics runs on every peer, so without this gate every client would fire
        // its own victory RPC and the scene would be loaded once per client.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        PlayerData playerData = other.GetComponent<PlayerData>();
        if (playerData != null && !playerData.IsHunter)
        {
            // Trigger game end for this player
            playerData.ReachExit();
        }
    }
}
