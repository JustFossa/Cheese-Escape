using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("Lobby Settings")]
    public string gameSceneName = "GameScene";
    public int maxPlayers = 8;

    [Header("Network Variables")]
    private NetworkVariable<bool> isGameStarted = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<int> connectedPlayersCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Events for UI updates
    public System.Action<int> OnPlayerCountChanged;
    public System.Action<bool> OnGameStateChanged;

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
            
            // Initialize player count
            UpdatePlayerCount();
        }

        // Subscribe to network variable changes
        connectedPlayersCount.OnValueChanged += OnPlayerCountNetworkChanged;
        isGameStarted.OnValueChanged += OnGameStartedNetworkChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        connectedPlayersCount.OnValueChanged -= OnPlayerCountNetworkChanged;
        isGameStarted.OnValueChanged -= OnGameStartedNetworkChanged;

        base.OnNetworkDespawn();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            UpdatePlayerCount();
            
            // If game has already started, we might want to handle late joiners differently
            if (isGameStarted.Value)
            {
                // You can choose to either reject them or allow late joining
            }
            
            // Note: PlayerManager will handle spawning LobbyPlayer objects automatically
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            UpdatePlayerCount();
        }
    }

    private void UpdatePlayerCount()
    {
        if (IsServer)
        {
            connectedPlayersCount.Value = NetworkManager.Singleton.ConnectedClients.Count;
        }
    }

    private void OnPlayerCountNetworkChanged(int oldCount, int newCount)
    {
        OnPlayerCountChanged?.Invoke(newCount);
    }

    private void OnGameStartedNetworkChanged(bool oldValue, bool newValue)
    {
        OnGameStateChanged?.Invoke(newValue);
        
        if (newValue)
        {
            // Game has started, load the game scene
            LoadGameScene();
        }
    }

    // Public method for host to start the game
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc(ServerRpcParams rpcParams = default)
    {
        // Only allow the host (server) to start the game
        if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (isGameStarted.Value)
        {
            return;
        }

        if (connectedPlayersCount.Value < 1)
        {
            return;
        }

        // Start the game transition process
        StartCoroutine(StartGameTransition());
    }
    
    // Coroutine to handle the game start transition
    private System.Collections.IEnumerator StartGameTransition()
    {
        print("Starting game transition...");
        
        // Set game started flag
        isGameStarted.Value = true;
        
        // Wait a moment for UI updates
        yield return new WaitForSeconds(0.5f);
        
        // Cleanup lobby players and spawn game players
        if (PlayerManager.Instance != null)
        {
            // Clean up lobby players first
            PlayerManager.Instance.CleanupLobbyPlayers();
            
            // Wait a moment for cleanup
            yield return new WaitForSeconds(0.2f);
            
            // Spawn game players for all connected clients
            PlayerManager.Instance.SpawnGamePlayersForAllClients();
            print("Game players spawned for all clients");
        }
        else
        {
            print("Warning: PlayerManager instance not found!");
        }
        
        // Wait a moment for spawning to complete
        yield return new WaitForSeconds(0.5f);
        
        // Load the game scene
        LoadGameScene();
    }

    // Method to load the game scene for all clients
    private void LoadGameScene()
    {
        if (IsServer)
        {
            print($"Loading game scene: {gameSceneName}");
            
            // Use NetworkManager's scene management to load the scene for all clients
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
    }

    // Public properties to access network variables
    public int ConnectedPlayersCount => connectedPlayersCount.Value;
    public bool IsGameStarted => isGameStarted.Value;
    new public bool IsHost => NetworkManager.Singleton.IsServer;
    public bool CanStartGame => IsHost && ConnectedPlayersCount >= 1 && !IsGameStarted;

    // Method to leave the lobby (return to main menu)
    public void LeaveLobby()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Return to main menu
        SceneManager.LoadScene("MainMenuScene");
    }
    
    // Method for host to leave lobby and disconnect all clients
    public void HostLeaveLobby()
    {
        if (IsServer)
        {
            // Notify all clients that host is leaving
            NotifyClientsHostLeavingClientRpc();
            
            // Start coroutine to shutdown server after a brief delay
            StartCoroutine(ShutdownServerDelayed());
        }
        else
        {
            // Fallback to regular leave if not server
            LeaveLobby();
        }
    }
    
    [ClientRpc]
    private void NotifyClientsHostLeavingClientRpc()
    {
        // If this is not the host (i.e., a regular client), disconnect them
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsHost)
        {
            // Disconnect the client after a brief delay
            StartCoroutine(DisconnectClientDelayed());
        }
    }
    
    private System.Collections.IEnumerator ShutdownServerDelayed()
    {
        // Wait a moment to ensure the ClientRpc is sent to all clients
        yield return new WaitForSeconds(0.5f);
        
        // Now shutdown the server
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Return host to main menu
        SceneManager.LoadScene("MainMenuScene");
    }
    
    private System.Collections.IEnumerator DisconnectClientDelayed()
    {
        // Wait a moment before disconnecting
        yield return new WaitForSeconds(1f);
        
        // Shutdown client connection
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        // Return client to main menu
        SceneManager.LoadScene("MainMenuScene");
    }

    // Method to get lobby status for UI
    public string GetLobbyStatus()
    {
        if (IsGameStarted)
        {
            return "Game Starting...";
        }
        else if (IsHost)
        {
            return $"Lobby ({ConnectedPlayersCount}/{maxPlayers}) - You are the Host";
        }
        else
        {
            return $"Lobby ({ConnectedPlayersCount}/{maxPlayers}) - Waiting for Host";
        }
    }
}