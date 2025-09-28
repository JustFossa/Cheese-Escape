using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI lobbyStatusText;
    public TextMeshProUGUI playerListText;
    public Button startGameButton;
    public Button leaveLobbyButton;
    public GameObject hostControls; // Panel that should only be visible to host


    [Header("Settings")]
    public float updateInterval = 0.5f; // How often to update the UI

    private LobbyManager lobbyManager;
    private PlayerListUI playerListUI;
    private float updateTimer = 0f;

    void Start()
    {
        // Find the LobbyManager in the scene
        StartCoroutine(WaitForLobbyManager());

        // Set up button listeners
        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameClicked);

        if (leaveLobbyButton != null)
            leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);

        // Find PlayerListUI if it exists
        playerListUI = FindObjectOfType<PlayerListUI>();

        print("PlayerListUI: " + (playerListUI != null ? "Found" : "Not Found"));

        // Will be set properly when LobbyManager is available

    }

    private System.Collections.IEnumerator WaitForLobbyManager()
    {
        // Wait for LobbyManager to be available
        while (LobbyManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }

        lobbyManager = LobbyManager.Instance;
        
        // Subscribe to lobby events
        lobbyManager.OnPlayerCountChanged += OnPlayerCountChanged;
        lobbyManager.OnGameStateChanged += OnGameStateChanged;
        
        // Initial UI update
        UpdateUI();
    }

    void Update()
    {
        if (lobbyManager == null) return;
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            UpdateUI();
            updateTimer = 0f;
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (lobbyManager != null)
        {
            lobbyManager.OnPlayerCountChanged -= OnPlayerCountChanged;
            lobbyManager.OnGameStateChanged -= OnGameStateChanged;
        }
    }

    private void UpdateUI()
    {
        if (lobbyManager == null) return;

        // Update lobby status text
        if (lobbyStatusText != null)
        {
            lobbyStatusText.text = lobbyManager.GetLobbyStatus();
        }

        // Update host controls visibility
        bool isHost = lobbyManager.IsHost;
        if (hostControls != null)
            hostControls.SetActive(isHost);
        
        
        // Update start game button
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = lobbyManager.CanStartGame;
            
            // Update button text based on state
            TextMeshProUGUI buttonText = startGameButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                if (lobbyManager.IsGameStarted)
                {
                    buttonText.text = "Starting...";
                }
                else if (lobbyManager.ConnectedPlayersCount < 1)
                {
                    buttonText.text = "Need Players";
                }
                else
                {
                    buttonText.text = "Start Game";
                }
            }
        }

        // Update player list if we don't have a separate PlayerListUI
        if (playerListText != null && playerListUI == null)
        {
            print("Updating player list text in LobbyUI");
            UpdatePlayerListText();
        }
    }

    private void UpdatePlayerListText()
    {
        if (playerListText == null) return;

        // Get all LobbyPlayer components in the scene
        LobbyPlayer[] allLobbyPlayers = FindObjectsOfType<LobbyPlayer>();
        
        if (allLobbyPlayers.Length > 0)
        {
            System.Text.StringBuilder playerList = new System.Text.StringBuilder();
            playerList.AppendLine("Players in Lobby:");
            
            foreach (LobbyPlayer player in allLobbyPlayers)
            {
                string playerInfo = $"• {player.PlayerName}";
                
                // Add additional info
                if (player.IsOwner)
                {
                    playerInfo += " (You)";
                }
                
                // Add host indicator
                if (player.OwnerClientId == 0 || (NetworkManager.Singleton != null && 
                    player.OwnerClientId == NetworkManager.Singleton.LocalClientId && 
                    NetworkManager.Singleton.IsHost))
                {
                    playerInfo += " [Host]";
                }
                
                playerList.AppendLine(playerInfo);
            }
            
            playerListText.text = playerList.ToString();
        }
        else
        {
            playerListText.text = "No players in lobby";
        }
    }

    private void OnPlayerCountChanged(int newCount)
    {
        UpdateUI();
    }

    private void OnGameStateChanged(bool gameStarted)
    {
        UpdateUI();
        
        if (gameStarted)
        {
            // Show loading message or transition effect
            if (lobbyStatusText != null)
            {
                lobbyStatusText.text = "Loading Game...";
            }
        }
    }

    private void OnStartGameClicked()
    {
        if (lobbyManager != null && lobbyManager.CanStartGame)
        {
            print("Host clicked Start Game - initiating game start sequence");
            lobbyManager.StartGameServerRpc();
            
            // Update UI immediately to show that the process has started
            if (lobbyStatusText != null)
            {
                lobbyStatusText.text = "Starting Game...";
            }
        }
        else
        {
            print("Cannot start game - conditions not met");
        }
    }

    private void OnLeaveLobbyClicked()
    {
        if (lobbyManager != null)
        {
            // Check if this is the host
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                // Host is leaving - use LobbyManager to handle host disconnect  
                lobbyManager.HostLeaveLobby();
            }
            else
            {
                // Regular client leaving
                lobbyManager.LeaveLobby();
            }
        }
        else
        {
            // Fallback if LobbyManager is not available
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
            SceneManager.LoadScene("MainMenuScene");
        }
    }


    // Public method to manually refresh the UI
    public void RefreshUI()
    {
        UpdateUI();
    }
}