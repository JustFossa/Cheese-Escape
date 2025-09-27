using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerData : NetworkBehaviour
{
    [Header("Player Information")]
    public NetworkVariable<FixedString64Bytes> playerName = new NetworkVariable<FixedString64Bytes>(
        "Player", 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );

    public string PlayerName => playerName.Value.ToString();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsOwner)
        {
            // Get the player name from PlayerPrefs when the player spawns
            string savedName = PlayerPrefs.GetString("PlayerName", "Player");
            if (string.IsNullOrEmpty(savedName))
            {
                savedName = "Player " + OwnerClientId;
            }
            
            // Set the player name on the server
            SetPlayerNameServerRpc(savedName);
        }
        
        // Subscribe to name changes to update UI or other systems
        playerName.OnValueChanged += OnPlayerNameChanged;
        
        // Register with PlayerManager if it exists
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.RegisterPlayer(OwnerClientId, this);
        }
        
        Debug.Log($"Player {OwnerClientId} spawned with name: {PlayerName}");
    }

    public override void OnNetworkDespawn()
    {
        // Unregister from PlayerManager
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.UnregisterPlayer(OwnerClientId);
        }
        
        playerName.OnValueChanged -= OnPlayerNameChanged;
        base.OnNetworkDespawn();
    }

    [ServerRpc]
    private void SetPlayerNameServerRpc(string newName)
    {
        // Validate the name (optional)
        if (string.IsNullOrEmpty(newName) || newName.Length > 32)
        {
            newName = "Player " + OwnerClientId;
        }
        
        playerName.Value = newName;
        Debug.Log($"Server: Player {OwnerClientId} name set to: {newName}");
    }

    private void OnPlayerNameChanged(FixedString64Bytes oldName, FixedString64Bytes newName)
    {
        Debug.Log($"Player {OwnerClientId} name changed from '{oldName}' to '{newName}'");
        
        // You can add additional logic here when the name changes
        // For example, update UI elements, leaderboards, etc.
    }

    // Public method to change player name (can be called from UI)
    public void ChangePlayerName(string newName)
    {
        if (IsOwner)
        {
            SetPlayerNameServerRpc(newName);
        }
    }

    // Method to get all player names (useful for displaying player list)
    public static string[] GetAllPlayerNames()
    {
        var playerDataList = FindObjectsOfType<PlayerData>();
        string[] names = new string[playerDataList.Length];
        
        for (int i = 0; i < playerDataList.Length; i++)
        {
            names[i] = playerDataList[i].PlayerName;
        }
        
        return names;
    }
}