using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerManager : NetworkBehaviour
{
    public static PlayerManager Instance { get; private set; }
    
    [Header("Player Management")]
    public GameObject playerPrefab; // The player prefab that should have PlayerData component
    public GameObject lobbyPlayerPrefab; // The lobby player prefab for lobby scene

    public GameObject hunterPrefab;

    [SerializeField]
    private Dictionary<ulong, PlayerData> connectedPlayers = new Dictionary<ulong, PlayerData>();
    
    [SerializeField]
    private Dictionary<ulong, LobbyPlayer> lobbyPlayers = new Dictionary<ulong, LobbyPlayer>();
    
    // Network variable to track lobby player names for all clients
    private NetworkVariable<int> lobbyPlayerCount = new NetworkVariable<int>(
        0, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );
    
    // Events for UI updates
    public System.Action<ulong, string> OnLobbyPlayerJoined;
    public System.Action<ulong, string> OnLobbyPlayerLeft;

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
        // Check if we're in lobby scene by checking if LobbyManager exists
        if (LobbyManager.Instance != null && !LobbyManager.Instance.IsGameStarted)
        {
            // We're in lobby - spawn lobby player
            // For the host (client ID 0), delay spawning slightly to avoid race conditions
            if (clientId == 0 && NetworkManager.Singleton.IsHost)
            {
                StartCoroutine(SpawnLobbyPlayerDelayed(clientId));
            }
            else
            {
                // For regular clients, spawn immediately
                SpawnLobbyPlayer(clientId);
            }
        }
        else
        {
            // We're in game or game has started - spawn the actual player prefab
            // This happens when someone joins mid-game or when transitioning from lobby
            SpawnGamePlayer(clientId);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Remove from our tracking dictionaries
        if (connectedPlayers.ContainsKey(clientId))
        {
            connectedPlayers.Remove(clientId);
        }
        
        if (lobbyPlayers.ContainsKey(clientId))
        {
            string playerName = lobbyPlayers[clientId].PlayerName;
            
            // Despawn the lobby player object
            if (lobbyPlayers[clientId] != null && lobbyPlayers[clientId].NetworkObject != null)
            {
                lobbyPlayers[clientId].NetworkObject.Despawn();
            }
            
            lobbyPlayers.Remove(clientId);
            lobbyPlayerCount.Value = lobbyPlayers.Count;
            
            // Notify about lobby player leaving
            OnLobbyPlayerLeft?.Invoke(clientId, playerName);
            NotifyLobbyPlayerLeftClientRpc(clientId, playerName);
        }
    }

    // Method to register a player (called by PlayerData when it spawns)
    public void RegisterPlayer(ulong clientId, PlayerData playerData)
    {
        if (IsServer && playerData != null)
        {
            connectedPlayers[clientId] = playerData;
            
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
        // You can add UI updates here, like updating a player list
        // or showing a "Player joined" message
    }

    [ClientRpc]
    private void NotifyPlayerLeftClientRpc(ulong clientId, string playerName)
    {
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
    
    // Method to spawn lobby player when client connects
    private void SpawnLobbyPlayer(ulong clientId)
    {
        if (!IsServer || lobbyPlayerPrefab == null) return;
        
        // Instantiate the lobby player prefab
        GameObject lobbyPlayerObj = Instantiate(lobbyPlayerPrefab);
        NetworkObject networkObject = lobbyPlayerObj.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Spawn as player object for this client
            networkObject.SpawnAsPlayerObject(clientId);
            
            // Note: Don't register here - let the LobbyPlayer register itself when it spawns
            // This prevents double registration
        }
        else
        {
            Destroy(lobbyPlayerObj);
        }
    }
    
    // Coroutine to delay lobby player spawning for host to avoid race conditions
    private IEnumerator SpawnLobbyPlayerDelayed(ulong clientId)
    {
        // Wait a frame to ensure the host network setup is complete
        yield return null;
        
        // Wait a bit more to make sure everything is initialized
        yield return new WaitForSeconds(0.1f);
        
        // Now spawn the lobby player
        SpawnLobbyPlayer(clientId);
    }
    
    // Method to register a lobby player (called by LobbyPlayer when it spawns)
    public void RegisterLobbyPlayer(ulong clientId, LobbyPlayer lobbyPlayer)
    {
        if (IsServer && lobbyPlayer != null)
        {
            lobbyPlayers[clientId] = lobbyPlayer;
            lobbyPlayerCount.Value = lobbyPlayers.Count;

            // Notify all clients about the new lobby player
            OnLobbyPlayerJoined?.Invoke(clientId, lobbyPlayer.PlayerName);
            NotifyLobbyPlayerJoinedClientRpc(clientId, lobbyPlayer.PlayerName);
            print("Registered lobby player: " + lobbyPlayer.PlayerName);
        }
    }
    
    // Method to unregister a lobby player
    public void UnregisterLobbyPlayer(ulong clientId)
    {
        if (IsServer && lobbyPlayers.ContainsKey(clientId))
        {
            string playerName = lobbyPlayers[clientId].PlayerName;
            lobbyPlayers.Remove(clientId);
            lobbyPlayerCount.Value = lobbyPlayers.Count;

            // Notify all clients about the lobby player leaving
            OnLobbyPlayerLeft?.Invoke(clientId, playerName);
            NotifyLobbyPlayerLeftClientRpc(clientId, playerName);
            print("Unregistered lobby player: " + playerName);
        }
    }
    
    [ClientRpc]
    private void NotifyLobbyPlayerJoinedClientRpc(ulong clientId, string playerName)
    {
        OnLobbyPlayerJoined?.Invoke(clientId, playerName);
    }
    
    [ClientRpc]
    private void NotifyLobbyPlayerLeftClientRpc(ulong clientId, string playerName)
    {
        OnLobbyPlayerLeft?.Invoke(clientId, playerName);
    }
    
    // Public method to get all lobby players (for lobby UI)
    public Dictionary<ulong, LobbyPlayer> GetLobbyPlayers()
    {
        return IsServer ? lobbyPlayers : null;
    }
    
    // Method to get all lobby player names
    public List<string> GetAllLobbyPlayerNames()
    {
        List<string> names = new List<string>();
        foreach (var lobbyPlayer in lobbyPlayers.Values)
        {
            if (lobbyPlayer != null)
            {
                names.Add(lobbyPlayer.PlayerName);
            }
        }
        return names;
    }
    
    // Method to get lobby player count
    public int GetLobbyPlayerCount()
    {
        return lobbyPlayerCount.Value;
    }
    
    // Method to cleanup lobby players when transitioning to game
    public void CleanupLobbyPlayers()
    {
        if (!IsServer) return;
        
        print($"Cleaning up {lobbyPlayers.Count} lobby players...");
        
        // Store player names before cleanup for potential use
        Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();
        foreach (var kvp in lobbyPlayers)
        {
            if (kvp.Value != null)
            {
                playerNames[kvp.Key] = kvp.Value.PlayerName;
            }
        }
        
        // Despawn all lobby player objects
        var lobbyPlayersToRemove = new List<ulong>(lobbyPlayers.Keys);
        foreach (var clientId in lobbyPlayersToRemove)
        {
            if (lobbyPlayers.ContainsKey(clientId) && lobbyPlayers[clientId] != null)
            {
                var lobbyPlayer = lobbyPlayers[clientId];
                if (lobbyPlayer.NetworkObject != null)
                {
                    lobbyPlayer.NetworkObject.Despawn();
                }
                lobbyPlayers.Remove(clientId);
            }
        }
        
        // Reset lobby player count
        lobbyPlayerCount.Value = 0;
        
        print($"Lobby players cleanup completed. Player names preserved: {string.Join(", ", playerNames.Values)}");
    }
    
    // Method to spawn game players for all connected clients when transitioning from lobby to game
    public void SpawnGamePlayersForAllClients()
    {
        if (!IsServer || playerPrefab == null) 
        {
            print("Cannot spawn game players - not server or playerPrefab is null");
            return;
        }
        
        print($"Spawning game players for {NetworkManager.Singleton.ConnectedClientsIds.Count} connected clients...");
        
        var hunter = (ulong)Random.Range(0, NetworkManager.Singleton.ConnectedClientsIds.Count);
        print("Selected hunter index: " + hunter);

        // Get all connected clients
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == hunter)
            {
                SpawnGamePlayer(clientId, true);
            } else SpawnGamePlayer(clientId);
            
        }
        
        print("Game player spawning process completed");
    }
    
    // Method to spawn a game player for a specific client
    private void SpawnGamePlayer(ulong clientId, bool isHunter = false)
    {
        if (!IsServer || playerPrefab == null || hunterPrefab == null) return;
        
        // Skip if player already exists
        if (connectedPlayers.ContainsKey(clientId))
        {
            print($"Game player for client {clientId} already exists, skipping spawn");
            return;
        }
        
        print($"Spawning game player for client {clientId} (IsHunter: {isHunter})");
        // Calculate spawn position
        Vector3 spawnPosition = GetSpawnPosition(clientId);
        
        // Instantiate the game player prefab
        GameObject gamePlayerObj = Instantiate(isHunter ? hunterPrefab : playerPrefab, spawnPosition, Quaternion.identity);
        NetworkObject networkObject = gamePlayerObj.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Spawn as player object for this client
            networkObject.SpawnAsPlayerObject(clientId);
            print($"Spawned game player for client {clientId} at position {spawnPosition}");
            
            // The PlayerData component will register itself when it spawns
        }
        else
        {
            print($"Error: Player prefab for client {clientId} doesn't have NetworkObject component");
            Destroy(gamePlayerObj);
        }
    }

    // Method to calculate spawn position for players
    private Vector3 GetSpawnPosition(ulong clientId)
    {
        var spawnPoints = new Vector3[]
        {
            new Vector3(0, 1, 0),
            new Vector3(5, 1, 5),
            new Vector3(-5, 1, -5),
            new Vector3(5, 1, -5),
            new Vector3(-5, 1, 5),
            new Vector3(10, 1, 0),
            new Vector3(-10, 1, 0),
            new Vector3(0, 1, 10),
            new Vector3(0, 1, -10)
        };

        int index = (int)(clientId % (ulong)spawnPoints.Length);
        return spawnPoints[index];
    }
}