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

        Debug.Log("LobbyManager spawned on network");
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
            Debug.Log($"LobbyManager: Client {clientId} connected to lobby");
            UpdatePlayerCount();
            
            // If game has already started, we might want to handle late joiners differently
            if (isGameStarted.Value)
            {
                Debug.Log($"Game already started, but client {clientId} joined");
                // You can choose to either reject them or allow late joining
            }
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            Debug.Log($"LobbyManager: Client {clientId} disconnected from lobby");
            UpdatePlayerCount();
        }
    }

    private void UpdatePlayerCount()
    {
        if (IsServer)
        {
            connectedPlayersCount.Value = NetworkManager.Singleton.ConnectedClients.Count;
            Debug.Log($"Updated player count: {connectedPlayersCount.Value}");
        }
    }

    private void OnPlayerCountNetworkChanged(int oldCount, int newCount)
    {
        Debug.Log($"Player count changed from {oldCount} to {newCount}");
        OnPlayerCountChanged?.Invoke(newCount);
    }

    private void OnGameStartedNetworkChanged(bool oldValue, bool newValue)
    {
        Debug.Log($"Game started state changed from {oldValue} to {newValue}");
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
            Debug.LogWarning("Only the host can start the game!");
            return;
        }

        if (isGameStarted.Value)
        {
            Debug.LogWarning("Game is already started!");
            return;
        }

        if (connectedPlayersCount.Value < 1)
        {
            Debug.LogWarning("Not enough players to start the game!");
            return;
        }

        Debug.Log("Host is starting the game!");
        isGameStarted.Value = true;
    }

    // Method to load the game scene for all clients
    private void LoadGameScene()
    {
        if (IsServer)
        {
            Debug.Log($"Loading game scene: {gameSceneName}");
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