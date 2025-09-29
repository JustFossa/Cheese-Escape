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
}