using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerManager : NetworkBehaviour
{
    public static PlayerManager Instance { get; private set; }
    
    [Header("Player Management")]
    public GameObject playerPrefab; // The player prefab that should have PlayerData component
    
    [SerializeField]
    private Dictionary<ulong, PlayerData> connectedPlayers = new Dictionary<ulong, PlayerData>();

    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        base.OnNetworkDespawn();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"PlayerManager: Client {clientId} connected to server");
        
        // In lobby mode, we don't spawn player objects immediately
        // Players will be spawned when the game actually starts
        // The PlayerData will be automatically created when the player object spawns
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected from server");
        
        // Remove from our tracking dictionary
        if (connectedPlayers.ContainsKey(clientId))
        {
            connectedPlayers.Remove(clientId);
        }
    }

    // Method to register a player (called by PlayerData when it spawns)
    public void RegisterPlayer(ulong clientId, PlayerData playerData)
    {
        if (IsServer)
        {
            connectedPlayers[clientId] = playerData;
            Debug.Log($"Registered player {clientId} with name: {playerData.PlayerName}");
            
            // Notify all clients about the new player
            NotifyPlayerJoinedClientRpc(clientId, playerData.PlayerName);
        }
    }

    // Method to unregister a player
    public void UnregisterPlayer(ulong clientId)
    {
        if (IsServer && connectedPlayers.ContainsKey(clientId))
        {
            string playerName = connectedPlayers[clientId].PlayerName;
            connectedPlayers.Remove(clientId);
            
            // Notify all clients about the player leaving
            NotifyPlayerLeftClientRpc(clientId, playerName);
        }
    }

    [ClientRpc]
    private void NotifyPlayerJoinedClientRpc(ulong clientId, string playerName)
    {
        Debug.Log($"Player {playerName} (ID: {clientId}) joined the game");
        
        // You can add UI updates here, like updating a player list
        // or showing a "Player joined" message
    }

    [ClientRpc]
    private void NotifyPlayerLeftClientRpc(ulong clientId, string playerName)
    {
        Debug.Log($"Player {playerName} (ID: {clientId}) left the game");
        
        // You can add UI updates here, like updating a player list
        // or showing a "Player left" message
    }

    // Public method to get all connected players (server only)
    public Dictionary<ulong, PlayerData> GetConnectedPlayers()
    {
        return IsServer ? connectedPlayers : null;
    }

    // Public method to get a specific player by client ID
    public PlayerData GetPlayer(ulong clientId)
    {
        return connectedPlayers.TryGetValue(clientId, out PlayerData player) ? player : null;
    }

    // Method to get all player names
    public List<string> GetAllPlayerNames()
    {
        List<string> names = new List<string>();
        foreach (var player in connectedPlayers.Values)
        {
            names.Add(player.PlayerName);
        }
        return names;
    }
}