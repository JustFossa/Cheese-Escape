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

    // Server-written, read by everyone. Nothing despawns on a catch any more: a caught survivor
    // goes Downed, then Caught (spectating) - the round decides when anyone leaves the scene.
    public NetworkVariable<LifeState> lifeState = new NetworkVariable<LifeState>(LifeState.Alive);
    public NetworkVariable<int> hiddenSpot = new NetworkVariable<int>(-1);
    public NetworkVariable<double> bleedoutEnd = new NetworkVariable<double>(0);

    // Chosen in the lobby, copied here by the server at spawn. Use ActivePerk, which ignores hunters.
    public NetworkVariable<Perk> perk = new NetworkVariable<Perk>(Perk.None);
    // Cheese picked up but not yet banked in a safe zone. Lost if this survivor is downed.
    public NetworkVariable<int> carriedCheese = new NetworkVariable<int>(0);
    // Server time until which the hunter can see this survivor through walls (alarm, trap).
    public NetworkVariable<double> revealedUntil = new NetworkVariable<double>(0);
    // Server time until which a trap keeps this survivor slow.
    public NetworkVariable<double> slowedUntil = new NetworkVariable<double>(0);

    private NetworkList<KeyData> collectedKeys;
    private Collider[] bodyColliders;
    private bool[] colliderWasTrigger;
    private Vector3 preHidePosition;

    // The PlayerData this machine controls, or null in the menus / before spawn.
    public static PlayerData Local
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            return po != null ? po.GetComponent<PlayerData>() : null;
        }
    }

    public string PlayerName => playerName.Value.ToString();
    public bool IsHidden => hiddenSpot.Value >= 0;
    public bool CanAct => lifeState.Value == LifeState.Alive && !IsHidden;
    public bool IsSpectating => lifeState.Value == LifeState.Caught || lifeState.Value == LifeState.Escaped;
    public Perk ActivePerk => IsHunter ? Perk.None : perk.Value;
    public bool IsRevealed => revealedUntil.Value > ServerNow;
    public bool IsSlowed => slowedUntil.Value > ServerNow;

    private static double ServerNow => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0.0;

    private void Awake()
    {
        collectedKeys = new NetworkList<KeyData>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        bodyColliders = GetComponentsInChildren<Collider>(true);
        colliderWasTrigger = new bool[bodyColliders.Length];
        for (int i = 0; i < bodyColliders.Length; i++) colliderWasTrigger[i] = bodyColliders[i].isTrigger;

        lifeState.OnValueChanged += OnLifeStateChanged;
        hiddenSpot.OnValueChanged += OnHiddenSpotChanged;
        ApplyBody();
        if (IsOwner && isHunter.Value) EnsureHunterSense();
        if (IsServer && RoundManager.Instance != null) perk.Value = RoundManager.Instance.PerkOf(OwnerClientId);

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
        lifeState.OnValueChanged -= OnLifeStateChanged;
        hiddenSpot.OnValueChanged -= OnHiddenSpotChanged;
        base.OnNetworkDespawn();
    }

    private void OnLifeStateChanged(LifeState oldState, LifeState newState) => ApplyBody();

    // Runs on every peer. Downed bodies stay hittable by the interaction ray (so a teammate can
    // find them to revive) but stop blocking anyone; caught/escaped/hidden bodies are gone.
    private void ApplyBody()
    {
        LifeState s = lifeState.Value;
        bool solid = s == LifeState.Alive && !IsHidden;
        bool downed = s == LifeState.Downed;

        for (int i = 0; i < bodyColliders.Length; i++)
        {
            if (bodyColliders[i] == null) continue;
            bodyColliders[i].enabled = solid || downed;
            bodyColliders[i].isTrigger = downed || colliderWasTrigger[i];
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = !solid;

        // The owner's own model is already hidden by Movement so they don't see their body.
        Movement m = GetComponent<Movement>();
        if (m != null && m.playerModel != null && !IsOwner)
        {
            m.playerModel.SetActive(!IsSpectating && !IsHidden);
        }
    }

    // Only the owner moves - the transform is owner-authoritative (ClientNetworkTransform).
    private void OnHiddenSpotChanged(int oldSpot, int newSpot)
    {
        ApplyBody();
        if (!IsOwner) return;

        Movement m = GetComponent<Movement>();
        if (m == null) return;

        if (newSpot >= 0 && HidingSpot.TryGet(newSpot, out HidingSpot spot))
        {
            preHidePosition = transform.position;
            m.HideAt(spot.HidePosition(transform.position.y), spot.CameraWorldY);
        }
        else if (oldSpot >= 0)
        {
            m.Teleport(preHidePosition);
        }
    }

    // Server only. Caught survivors go down first so a teammate can revive them; bleeding out
    // (RoundManager) is what actually takes them out of the round.
    public void Down(double bleedoutSeconds)
    {
        if (!IsServer || IsHunter || lifeState.Value != LifeState.Alive || IsHidden) return;
        if (RoundManager.Instance != null && RoundManager.Instance.state.Value == RoundState.Ending) return;

        // A carrier who goes down loses what they were carrying - that is the risk of carrying.
        if (carriedCheese.Value > 0)
        {
            if (RoundManager.Instance != null) RoundManager.Instance.LoseCheese(carriedCheese.Value);
            carriedCheese.Value = 0;
        }

        bleedoutEnd.Value = NetworkManager.ServerTime.Time + bleedoutSeconds;
        lifeState.Value = LifeState.Downed;
    }

    private void EnsureHunterSense()
    {
        if (GetComponent<HunterSense>() == null) gameObject.AddComponent<HunterSense>();
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
            if (IsOwner) EnsureHunterSense();
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

    // From 6 players there are two hunters, so "the hunter" is not one object any more.
    public static List<PlayerData> GetHunters()
    {
        List<PlayerData> hunters = new List<PlayerData>();
        foreach (PlayerData player in FindObjectsOfType<PlayerData>())
        {
            if (player.IsHunter) hunters.Add(player);
        }
        return hunters;
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

    // Called by GameEnd, which is server-gated - physics triggers fire on every peer.
    // Escaping only takes THIS survivor out of play; the round keeps going for everyone else and
    // RoundManager ends it once nobody is left (it used to shut the whole session down).
    public void ReachExit()
    {
        if (!IsServer || IsHunter || !CanAct) return;

        Debug.Log($"Player {PlayerName} reached the exit and escaped!");
        lifeState.Value = LifeState.Escaped;
    }
}
