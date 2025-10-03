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
            
            // For the host, manually trigger lobby player spawning if we're in lobby
            // This ensures the host gets a lobby player even if OnClientConnected wasn't called
            if (LobbyManager.Instance != null && !LobbyManager.Instance.IsGameStarted)
            {
                // Check if host already has a lobby player spawned
                ulong hostClientId = NetworkManager.Singleton.LocalClientId;
                if (!lobbyPlayers.ContainsKey(hostClientId))
                {
                    print($"PlayerManager spawned on server - ensuring host (client {hostClientId}) has lobby player");
                    StartCoroutine(EnsureHostLobbyPlayerSpawned(hostClientId));
                }
            }
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
            // Check if this client already has a lobby player to avoid duplicates
            if (lobbyPlayers.ContainsKey(clientId))
            {
                print($"Client {clientId} already has a lobby player, skipping spawn");
                return;
            }
            
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
        
        // Final check to prevent duplicate spawning
        if (lobbyPlayers.ContainsKey(clientId))
        {
            print($"Attempted to spawn lobby player for client {clientId} but one already exists");
            return;
        }
        
        print($"Spawning lobby player for client {clientId}");
        
        // Instantiate the lobby player prefab
        GameObject lobbyPlayerObj = Instantiate(lobbyPlayerPrefab);
        NetworkObject networkObject = lobbyPlayerObj.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Spawn as player object for this client
            networkObject.SpawnAsPlayerObject(clientId);
            print($"Successfully spawned lobby player NetworkObject for client {clientId}");
            
            // Note: Don't register here - let the LobbyPlayer register itself when it spawns
            // This prevents double registration
        }
        else
        {
            print($"Error: Lobby player prefab for client {clientId} doesn't have NetworkObject component");
            Destroy(lobbyPlayerObj);
        }
    }
    
    // Coroutine to delay lobby player spawning for host to avoid race conditions
    private IEnumerator SpawnLobbyPlayerDelayed(ulong clientId)
    {
        // Wait a frame to ensure the host network setup is complete
        yield return null;
        
        // Wait a bit more to make sure everything is initialized
        yield return new WaitForSeconds(0.2f);
        
        // Double-check that we don't already have a lobby player for this client
        if (lobbyPlayers.ContainsKey(clientId))
        {
            print($"Client {clientId} already has a lobby player after delay, skipping spawn");
            yield break;
        }
        
        // Ensure we're still in lobby state
        if (LobbyManager.Instance != null && !LobbyManager.Instance.IsGameStarted)
        {
            // Now spawn the lobby player
            SpawnLobbyPlayer(clientId);
        }
        else
        {
            print($"No longer in lobby state, skipping delayed spawn for client {clientId}");
        }
    }
    
    // Coroutine to ensure the host gets a lobby player during PlayerManager initialization
    private IEnumerator EnsureHostLobbyPlayerSpawned(ulong hostClientId)
    {
        // Wait a bit to ensure everything is initialized
        yield return new WaitForSeconds(0.3f);
        
        // Check again if host still needs a lobby player
        if (!lobbyPlayers.ContainsKey(hostClientId) && 
            LobbyManager.Instance != null && 
            !LobbyManager.Instance.IsGameStarted)
        {
            print($"Host (client {hostClientId}) still needs lobby player - spawning now");
            SpawnLobbyPlayer(hostClientId);
        }
        else
        {
            print($"Host (client {hostClientId}) already has lobby player or game has started");
        }
    }
    
    // Method to register a lobby player (called by LobbyPlayer when it spawns)
    public void RegisterLobbyPlayer(ulong clientId, LobbyPlayer lobbyPlayer)
    {
        if (IsServer && lobbyPlayer != null)
        {
            // Check if this client already has a registered lobby player
            if (lobbyPlayers.ContainsKey(clientId))
            {
                print($"Warning: Client {clientId} already has a registered lobby player ({lobbyPlayers[clientId].PlayerName}), replacing with new one ({lobbyPlayer.PlayerName})");
            }
            
            lobbyPlayers[clientId] = lobbyPlayer;
            lobbyPlayerCount.Value = lobbyPlayers.Count;

            // Notify all clients about the new lobby player
            OnLobbyPlayerJoined?.Invoke(clientId, lobbyPlayer.PlayerName);
            NotifyLobbyPlayerJoinedClientRpc(clientId, lobbyPlayer.PlayerName);
            print($"Successfully registered lobby player: {lobbyPlayer.PlayerName} (Client {clientId}). Total lobby players: {lobbyPlayers.Count}");
        }
        else if (!IsServer)
        {
            print($"Cannot register lobby player {lobbyPlayer?.PlayerName} - not server");
        }
        else
        {
            print("Cannot register lobby player - lobbyPlayer is null");
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
            print($"Unregistered lobby player: {playerName} (Client {clientId}). Remaining lobby players: {lobbyPlayers.Count}");
        }
        else if (!IsServer)
        {
            print($"Cannot unregister lobby player for client {clientId} - not server");
        }
        else
        {
            print($"Cannot unregister lobby player for client {clientId} - not found in registry");
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

    // Method to get the current hunter player
    public PlayerData GetHunter()
    {
        foreach (var player in connectedPlayers.Values)
        {
            if (player != null && player.IsHunter)
            {
                return player;
            }
        }
        return null;
    }

    // Method to get the hunter's client ID
    public ulong? GetHunterClientId()
    {
        foreach (var kvp in connectedPlayers)
        {
            if (kvp.Value != null && kvp.Value.IsHunter)
            {
                return kvp.Key;
            }
        }
        return null;
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

        var hunter =(ulong)0;
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
        Vector3 spawnPosition = GetSpawnPosition(clientId, isHunter);
        
        // Instantiate the game player prefab
        GameObject gamePlayerObj = Instantiate(isHunter ? hunterPrefab : playerPrefab, spawnPosition, Quaternion.identity);
        NetworkObject networkObject = gamePlayerObj.GetComponent<NetworkObject>();
        
        if (networkObject != null)
        {
            // Verify essential components exist before spawning
            if (isHunter)
            {
                if (!ValidateHunterPrefabComponents(gamePlayerObj))
                {
                    Debug.LogError($"Hunter prefab is missing essential components! Spawning failed for client {clientId}");
                    Destroy(gamePlayerObj);
                    return;
                }
            }
            
            // Spawn as player object for this client
            networkObject.SpawnAsPlayerObject(clientId);
            print($"Spawned game player for client {clientId} at position {spawnPosition}");
            
            // If this is the hunter, set the hunter status after a brief delay to ensure PlayerData is registered
            if (isHunter)
            {
                StartCoroutine(SetHunterStatusAfterSpawn(clientId));
            }
            
            // The PlayerData component will register itself when it spawns
        }
        else
        {
            print($"Error: Player prefab for client {clientId} doesn't have NetworkObject component");
            Destroy(gamePlayerObj);
        }
    }

    // Coroutine to set hunter status after player spawn
    private IEnumerator SetHunterStatusAfterSpawn(ulong clientId)
    {
        // Wait a brief moment to ensure PlayerData has registered itself
        yield return new WaitForSeconds(0.1f);
        
        // Try to get the PlayerData for this client
        PlayerData playerData = GetPlayer(clientId);
        int attempts = 0;
        
        // Keep trying for a few seconds if PlayerData isn't registered yet
        while (playerData == null && attempts < 50) // 5 seconds max (50 * 0.1s)
        {
            yield return new WaitForSeconds(0.1f);
            playerData = GetPlayer(clientId);
            attempts++;
        }
        
        if (playerData != null)
        {
            // Set the hunter status
            playerData.SetHunterStatusServerRpc(true);
            print($"Successfully set hunter status for client {clientId}");
            
            // Additional fix: Ensure hunter's movement component is properly initialized
            yield return new WaitForSeconds(0.2f); // Wait a bit longer for network sync
            EnsureHunterComponentsInitialized(clientId, playerData);
            
            // Send client RPC to ensure input is properly initialized on the client
            EnsureHunterInputInitializedClientRpc(clientId);
        }
        else
        {
            print($"Failed to set hunter status for client {clientId} - PlayerData not found after waiting");
        }
    }
    
    // Additional method to ensure hunter components are properly initialized
    private void EnsureHunterComponentsInitialized(ulong clientId, PlayerData playerData)
    {
        if (playerData == null) return;
        
        // Get the Movement component
        Movement movement = playerData.GetComponent<Movement>();
        if (movement != null)
        {
            print($"Ensuring hunter {clientId} movement component is initialized");
            
            // Force reinitialize input system for the hunter
            // This will be called on the server, but the method checks IsOwner internally
            movement.ForceInputReinitialize();
            
            // Also ensure the PlayerInput component exists and is enabled
            UnityEngine.InputSystem.PlayerInput playerInput = movement.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null)
            {
                print($"Hunter {clientId} PlayerInput found - ensuring it's enabled");
                playerInput.enabled = true;
            }
            else
            {
                Debug.LogError($"Hunter {clientId} is missing PlayerInput component! This will cause movement issues.");
            }
        }
        else
        {
            Debug.LogError($"Hunter {clientId} is missing Movement component!");
        }
    }
    
    // Client RPC to ensure hunter input is properly initialized on the client side
    [ClientRpc]
    private void EnsureHunterInputInitializedClientRpc(ulong targetClientId)
    {
        // Only process this on the target client
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        
        print($"Received hunter input initialization RPC for client {targetClientId}");
        
        // Find the local player's movement component
        Movement[] movements = FindObjectsOfType<Movement>();
        foreach (Movement movement in movements)
        {
            if (movement.IsOwner)
            {
                print($"Found local movement component, forcing input reinitialization for hunter {targetClientId}");
                
                // Use a coroutine to ensure proper timing
                StartCoroutine(DelayedHunterInputInit(movement));
                break;
            }
        }
    }
    
    // Coroutine to handle delayed hunter input initialization
    private IEnumerator DelayedHunterInputInit(Movement movement)
    {
        // Wait a moment to ensure everything is ready
        yield return new WaitForSeconds(0.3f);
        
        if (movement != null)
        {
            print("Executing delayed hunter input initialization");
            movement.ForceInputReinitialize();
            
            // Double-check after another moment
            yield return new WaitForSeconds(0.5f);
            
            // Verify PlayerInput component is working
            UnityEngine.InputSystem.PlayerInput playerInput = movement.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null && playerInput.enabled)
            {
                print("Hunter input system verified as working");
            }
            else
            {
                Debug.LogWarning("Hunter input system may still have issues - attempting final fix");
                movement.ForceInputReinitialize();
            }
        }
    }
    
    // Method to validate that the hunter prefab has all necessary components
    private bool ValidateHunterPrefabComponents(GameObject hunterObj)
    {
        bool isValid = true;
        
        // Check for PlayerData component
        if (hunterObj.GetComponent<PlayerData>() == null)
        {
            Debug.LogError("Hunter prefab is missing PlayerData component!");
            isValid = false;
        }
        
        // Check for Movement component
        if (hunterObj.GetComponent<Movement>() == null)
        {
            Debug.LogError("Hunter prefab is missing Movement component!");
            isValid = false;
        }
        
        // Check for PlayerInput component
        if (hunterObj.GetComponent<UnityEngine.InputSystem.PlayerInput>() == null)
        {
            Debug.LogError("Hunter prefab is missing PlayerInput component!");
            isValid = false;
        }
        
        // Check for NetworkObject component
        if (hunterObj.GetComponent<NetworkObject>() == null)
        {
            Debug.LogError("Hunter prefab is missing NetworkObject component!");
            isValid = false;
        }
        
        // Check for Rigidbody component
        if (hunterObj.GetComponent<Rigidbody>() == null)
        {
            Debug.LogError("Hunter prefab is missing Rigidbody component!");
            isValid = false;
        }
        
        if (isValid)
        {
            print("Hunter prefab component validation passed");
        }
        
        return isValid;
    }

    // Method to calculate spawn position for players
    private Vector3 GetSpawnPosition(ulong clientId, bool isHunter = false)
    {

        if (isHunter)
        {
            return new Vector3(-26.55f, 1, -59.71f);
        }
        var spawnPoints = new Vector3[]
        {
            new Vector3(8.73f, 1, -2.7f),
            new Vector3(10.58f, 1, -2.7f),
            new Vector3(12.64f, 1, -2.7f),
            new Vector3(14.29f, 1, -2.7f),
            new Vector3(16.14f, 1, -2.7f),
            new Vector3(17.4f, 1, -2.7f),
        };

        int index = (int)(clientId % (ulong)spawnPoints.Length);
        return spawnPoints[index];
    }
    
    // Public method to fix hunter movement issues at runtime
    [ServerRpc(RequireOwnership = false)]
    public void FixHunterMovementServerRpc(ulong clientId)
    {
        if (!IsServer) return;
        
        print($"Attempting to fix hunter movement for client {clientId}");
        
        PlayerData playerData = GetPlayer(clientId);
        if (playerData != null && playerData.IsHunter)
        {
            // Force reinitialization of hunter components
            EnsureHunterComponentsInitialized(clientId, playerData);
            
            // Send client RPC for client-side fixes
            EnsureHunterInputInitializedClientRpc(clientId);
            
            print($"Hunter movement fix initiated for client {clientId}");
        }
        else
        {
            print($"Client {clientId} is not a hunter or not found");
        }
    }
    
    // Public method that can be called from UI or other systems to fix hunter issues
    public void RequestHunterMovementFix()
    {
        if (NetworkManager.Singleton.IsClient)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            FixHunterMovementServerRpc(localClientId);
        }
    }
}