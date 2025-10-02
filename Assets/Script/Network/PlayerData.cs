using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[System.Serializable]
public struct KeyData : INetworkSerializable, System.IEquatable<KeyData>
{
    public int keyId;
    public FixedString64Bytes keyName;
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref keyId);
        serializer.SerializeValue(ref keyName);
    }
    
    public bool Equals(KeyData other)
    {
        return keyId == other.keyId && keyName.Equals(other.keyName);
    }
    
    public override bool Equals(object obj)
    {
        return obj is KeyData other && Equals(other);
    }
    
    public override int GetHashCode()
    {
        return System.HashCode.Combine(keyId, keyName.GetHashCode());
    }
}

public class PlayerData : NetworkBehaviour
{
    [Header("Player Information")]
    public NetworkVariable<FixedString64Bytes> playerName = new NetworkVariable<FixedString64Bytes>(
        "Player", 
        NetworkVariableReadPermission.Everyone
    );

    public NetworkVariable<bool> isHunter = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone
    );

    private NetworkList<KeyData> collectedKeys;

    public string PlayerName => playerName.Value.ToString();

    private void Awake()
    {
        collectedKeys = new NetworkList<KeyData>();
    }

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
        
        // Subscribe to hunter status changes
        isHunter.OnValueChanged += OnHunterStatusChanged;
        
        // Register with PlayerManager if it exists
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.RegisterPlayer(OwnerClientId, this);
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unregister from PlayerManager
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.UnregisterPlayer(OwnerClientId);
        }
        
        playerName.OnValueChanged -= OnPlayerNameChanged;
        isHunter.OnValueChanged -= OnHunterStatusChanged;
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
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetHunterStatusServerRpc(bool hunterStatus)
    {
        isHunter.Value = hunterStatus;
        Debug.Log($"Player {playerName.Value} hunter status set to: {hunterStatus}");
    }

    private void OnPlayerNameChanged(FixedString64Bytes oldName, FixedString64Bytes newName)
    {
        // You can add additional logic here when the name changes
        // For example, update UI elements, leaderboards, etc.
    }

    private void OnHunterStatusChanged(bool oldStatus, bool newStatus)
    {
        // You can add additional logic here when hunter status changes
        // For example, update UI, change player appearance, enable/disable hunter abilities, etc.
        Debug.Log($"Player {playerName.Value} hunter status changed from {oldStatus} to {newStatus}");
        
        if (newStatus)
        {
            Debug.Log($"{playerName.Value} is now the HUNTER!");
        }
        else
        {
            Debug.Log($"{playerName.Value} is no longer the hunter");
        }
        
        // Fix for hunter input issues: Reinitialize input system when hunter status changes
        if (IsOwner)
        {
            Movement movement = GetComponent<Movement>();
            if (movement != null)
            {
                // Wait a frame to ensure the status change is fully processed
                StartCoroutine(ReinitializeInputDelayed(movement));
            }
        }
    }
    
    private System.Collections.IEnumerator ReinitializeInputDelayed(Movement movement)
    {
        yield return null; // Wait one frame
        movement.ForceInputReinitialize();
        Debug.Log($"Reinitialized input system for {playerName.Value} after hunter status change");
    }

    // Public method to change player name (can be called from UI)
    public void ChangePlayerName(string newName)
    {
        if (IsOwner)
        {
            SetPlayerNameServerRpc(newName);
        }
    }

    // Public method to check if this player is the hunter
    public bool IsHunter => isHunter.Value;

    // Public method to set hunter status (server only)
    public void SetHunterStatus(bool hunterStatus)
    {
        if (IsServer)
        {
            isHunter.Value = hunterStatus;
            Debug.Log($"Player {playerName.Value} hunter status set to: {hunterStatus}");
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

    // Method to get the hunter player (useful for game logic)
    public static PlayerData GetHunter()
    {
        var playerDataList = FindObjectsOfType<PlayerData>();
        
        foreach (var player in playerDataList)
        {
            if (player.IsHunter)
            {
                return player;
            }
        }
        
        return null; // No hunter found
    }

    // Key Management Methods
    [ServerRpc(RequireOwnership = false)]
    public void AddKeyToInventoryServerRpc(int keyId, FixedString64Bytes keyName)
    {
        if (!IsServer) return;
        
        // Check if key is already collected
        foreach (var key in collectedKeys)
        {
            if (key.keyId == keyId)
            {
                Debug.Log($"Key {keyName} already in inventory!");
                return;
            }
        }
        
        // Add the key to inventory
        KeyData newKey = new KeyData
        {
            keyId = keyId,
            keyName = keyName
        };
        
        collectedKeys.Add(newKey);
        Debug.Log($"Added key {keyName} to {playerName.Value}'s inventory");
    }

    // Method to check if player has a specific key
    public bool HasKey(int keyId)
    {
        foreach (var key in collectedKeys)
        {
            if (key.keyId == keyId)
            {
                return true;
            }
        }
        return false;
    }

    // Method to get all collected keys
    public List<KeyData> GetCollectedKeys()
    {
        List<KeyData> keys = new List<KeyData>();
        foreach (var key in collectedKeys)
        {
            keys.Add(key);
        }
        return keys;
    }

    // Method to get number of keys collected
    public int GetKeyCount()
    {
        return collectedKeys.Count;
    }

    // Method to remove a key from inventory (for doors that consume keys)
    [ServerRpc(RequireOwnership = false)]
    public void RemoveKeyFromInventoryServerRpc(int keyId)
    {
        if (!IsServer) return;
        
        for (int i = 0; i < collectedKeys.Count; i++)
        {
            if (collectedKeys[i].keyId == keyId)
            {
                collectedKeys.RemoveAt(i);
                Debug.Log($"Removed key {keyId} from {playerName.Value}'s inventory");
                return;
            }
        }
    }

    // Method called when player reaches the exit (wins the game)
    public void ReachExit()
    {
        // Only non-hunters can win by reaching the exit
        if (IsHunter)
        {
            Debug.Log($"Hunter {PlayerName} reached exit but hunters cannot win this way");
            return;
        }

        Debug.Log($"Player {PlayerName} reached the exit and won the game!");

        // If this is the local player, handle their victory
        if (IsOwner)
        {
            Debug.Log("Local player won the game - returning to main menu");
            StartCoroutine(HandlePlayerVictory());
        }

        // Notify all players about the victory
        if (IsServer)
        {
            NotifyPlayerVictoryClientRpc(PlayerName);
        }
        else
        {
            NotifyPlayerVictoryServerRpc();
        }
    }

    // Server RPC to notify about player victory
    [ServerRpc(RequireOwnership = false)]
    private void NotifyPlayerVictoryServerRpc()
    {
        NotifyPlayerVictoryClientRpc(PlayerName);
    }

    // Client RPC to notify all players about the victory
    [ClientRpc]
    private void NotifyPlayerVictoryClientRpc(FixedString64Bytes winnerName)
    {
        Debug.Log($"Game Over! {winnerName} escaped and won the game!");
        
        // You can add UI elements here to show victory screen
        // For now, all players will return to main menu after a delay
        if (!IsOwner) // Non-winner players get a different treatment
        {
            StartCoroutine(HandleGameEndForOthers(winnerName.ToString()));
        }
    }

    // Coroutine to handle victory sequence for the winning player
    private System.Collections.IEnumerator HandlePlayerVictory()
    {
        // Unlock cursor
        Cursor.lockState = CursorLockMode.None;
        
        // Wait a moment to show victory
        yield return new WaitForSeconds(2f);
        
        // Shutdown network manager
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("Shutting down NetworkManager for victorious player");
            NetworkManager.Singleton.Shutdown();
        }
        
        // Wait for network shutdown
        yield return new WaitForSeconds(0.3f);
        
        // Load main menu
        Debug.Log("Loading main menu scene for victorious player");
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
    }

    // Coroutine to handle game end for other players
    private System.Collections.IEnumerator HandleGameEndForOthers(string winnerName)
    {
        // Wait a moment to process the victory message
        yield return new WaitForSeconds(3f);
        
        // Unlock cursor
        Cursor.lockState = CursorLockMode.None;
        
        // Shutdown network manager
        if (NetworkManager.Singleton != null)
        {
            Debug.Log($"Game ended - {winnerName} won. Shutting down NetworkManager");
            NetworkManager.Singleton.Shutdown();
        }
        
        // Wait for network shutdown
        yield return new WaitForSeconds(0.3f);
        
        // Load main menu
        Debug.Log("Loading main menu scene after game end");
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
    }
}