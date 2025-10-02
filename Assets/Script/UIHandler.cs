using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.SceneManagement;

using Unity.Netcode;
public class UIHandler : MonoBehaviour, IPointerClickHandler
{
    [Header("Menu UI Elements")]
    public TMPro.TMP_InputField ipInputField;
    public TMPro.TMP_InputField playerNameInputField;
    
    [Header("Game UI Elements")]
    public TMPro.TextMeshProUGUI keysCollectedText;
    public TMPro.TextMeshProUGUI cheeseCountText;
    public GameObject gameUIPanel; // Panel containing game UI elements
    
    private bool isConnecting = false;
    private PlayerData localPlayerData;
    private int cheeseCount = 0; // Placeholder for cheese system

    public void OnPointerClick(PointerEventData eventData)
    {
        Button btn = eventData.pointerPress.GetComponent<Button>();

        if (btn != null)
        {
            switch (btn.name)
            {
                case "Host":
                    SceneManager.LoadScene("HostGameScene");
                    break;
                case "HostGame":
                    StartHost();
                    break;
                case "Join":
                    SceneManager.LoadScene("JoinGameScene");
                    break;
                case "Quit":
                    Application.Quit();
                    break;
                case "Back":
                    SceneManager.LoadScene("MainMenuScene");
                    break;
                case "JoinGame":
                    JoinGame();
                    break;
            }
        }
    }

    void Start()
    {
        if (ipInputField != null)
        {
            ipInputField.onValueChanged.AddListener(delegate { PlayerPrefs.SetString("ServerIP", ipInputField.text); PlayerPrefs.Save(); });
            // Load saved IP
            ipInputField.text = PlayerPrefs.GetString("ServerIP", "127.0.0.1");
        }
        if (playerNameInputField != null)
        {
            playerNameInputField.onValueChanged.AddListener(delegate { PlayerPrefs.SetString("PlayerName", playerNameInputField.text); PlayerPrefs.Save(); });
            // Load saved player name
            playerNameInputField.text = PlayerPrefs.GetString("PlayerName", "Player");
        }
        
        // Subscribe to network events for better error handling
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        
        // Initialize game UI
        InitializeGameUI();
        
        // Subscribe to scene change events
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    void OnDestroy()
    {
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        // Unsubscribe from scene events
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Reinitialize UI when scene changes
        InitializeGameUI();
    }
    
    void Update()
    {
        // Update game UI if we're in a game scene
        if (IsInGameScene())
        {
            UpdateGameUI();
        }
    }
    
    private bool IsInGameScene()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        return currentScene == "GameScene" || currentScene == "Game" || currentScene.Contains("Game");
    }
    
    private void InitializeGameUI()
    {
        // Show/hide game UI based on current scene
        if (gameUIPanel != null)
        {
            gameUIPanel.SetActive(IsInGameScene());
        }
        
        // Initialize UI text if in game scene
        if (IsInGameScene())
        {
            FindLocalPlayer();
            UpdateGameUI();
        }
    }
    
    private void FindLocalPlayer()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            // Find the local player's PlayerData
            PlayerData[] allPlayers = FindObjectsOfType<PlayerData>();
            foreach (PlayerData player in allPlayers)
            {
                if (player.IsOwner)
                {
                    localPlayerData = player;
                    break;
                }
            }
        }
    }
    
    private void UpdateGameUI()
    {
        // Update keys collected display
        UpdateKeysDisplay();
        
        // Update cheese count display
        UpdateCheeseDisplay();
    }
    
    private void UpdateKeysDisplay()
    {
        if (keysCollectedText == null) return;
        
        if (localPlayerData != null)
        {
            var collectedKeys = localPlayerData.GetCollectedKeys();
            int keyCount = collectedKeys.Count;
            
            if (keyCount == 0)
            {
                keysCollectedText.text = "Keys: None";
            }
            else
            {
                string keyNames = "";
                for (int i = 0; i < collectedKeys.Count; i++)
                {
                    keyNames += collectedKeys[i].keyName.ToString();
                    if (i < collectedKeys.Count - 1)
                    {
                        keyNames += ", ";
                    }
                }
                keysCollectedText.text = $"Keys ({keyCount}): {keyNames}";
            }
        }
        else
        {
            // Try to find local player again if not found
            FindLocalPlayer();
            keysCollectedText.text = "Keys: Loading...";
        }
    }
    
    private void UpdateCheeseDisplay()
    {
        if (cheeseCountText == null) return;
        
        // For now, display placeholder cheese count
        // This can be expanded when a cheese collection system is implemented
        cheeseCountText.text = $"Cheese: {cheeseCount}";
        
        // TODO: Replace with actual cheese collection system
        // Example: cheeseCount = localPlayerData.GetCheeseCount();
    }
    
    // Public method to update cheese count (to be called by cheese collection system)
    public void AddCheese(int amount = 1)
    {
        cheeseCount += amount;
        UpdateCheeseDisplay();
        
        // Also update GameUI if it exists for compatibility
        if (GameUI.Instance != null)
        {
            GameUI.Instance.AddCheese(amount);
        }
    }
    
    // Public method to reset cheese count
    public void ResetCheese()
    {
        cheeseCount = 0;
        UpdateCheeseDisplay();
        
        // Also reset GameUI if it exists for compatibility
        if (GameUI.Instance != null)
        {
            GameUI.Instance.ResetCheese();
        }
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        // If this is the local client disconnecting unexpectedly
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.LogWarning("Local client was disconnected from server");
            
            // Don't show error if we're already in main menu or if this is an intentional disconnect
            if (SceneManager.GetActiveScene().name != "MainMenuScene")
            {
                ShowConnectionError("Disconnected from server");
            }
        }
    }


    public void StartHost()
    {
        if (isConnecting)
        {
            Debug.LogWarning("Connection already in progress");
            return;
        }
        
        isConnecting = true;
        Debug.Log("Starting host server...");
        
        try
        {
            NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData("127.0.0.1", (ushort)7777, "0.0.0.0");
            NetworkManager.Singleton.StartHost();

            // Start coroutine to load LobbyScene after host starts
            StartCoroutine(LoadLobbyAfterHostStart());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to start host: {e.Message}");
            ShowConnectionError("Failed to start host server");
            isConnecting = false;
        }
    }

    public void JoinGame()
    {
        if (isConnecting)
        {
            Debug.LogWarning("Connection already in progress");
            return;
        }
        
        string ip = PlayerPrefs.GetString("ServerIP", "127.0.0.1");
        
        // Validate IP input
        if (string.IsNullOrEmpty(ip))
        {
            Debug.LogError("No server IP specified!");
            ShowConnectionError("Please enter a server IP address");
            return;
        }
        
        isConnecting = true;
        Debug.Log($"Attempting to join server at {ip}:7777");
        
        try
        {
            NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData(ip, (ushort)7777);
            NetworkManager.Singleton.StartClient();
            
            // Start coroutine to load LobbyScene after client connects
            StartCoroutine(LoadLobbyAfterClientConnect());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to start client: {e.Message}");
            ShowConnectionError("Failed to connect to server");
            isConnecting = false;
        }
    }



    private IEnumerator LoadLobbyAfterHostStart()
    {
        Debug.Log("Waiting for host to start...");
        
        // Wait a frame to ensure the host has started
        yield return null;
        
        float timeout = 10f;
        float elapsed = 0f;
        
        // Wait until the NetworkManager is listening (host is active)
        while (!NetworkManager.Singleton.IsListening && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        if (NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsHost)
        {
            Debug.Log("Host started successfully, loading lobby scene...");
            
            // Additional safety check - wait a moment for stability
            yield return new WaitForSeconds(0.2f);
            
            isConnecting = false;
            SceneManager.LoadScene("LobbyScene");
        }
        else
        {
            Debug.LogError("Failed to start host within timeout period");
            ShowConnectionError("Failed to start host server");
            
            // Cleanup failed host attempt
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            
            isConnecting = false;
        }
    }

    private IEnumerator LoadLobbyAfterClientConnect()
    {
        Debug.Log("Waiting for client to connect...");
        
        // Wait a frame to ensure the client connection attempt has started
        yield return null;
        
        // Wait until the client is connected
        float timeout = 15f; // 15 second timeout (increased from 10)
        float elapsed = 0f;
        
        while (!NetworkManager.Singleton.IsConnectedClient && elapsed < timeout)
        {
            // Check if connection failed
            if (!NetworkManager.Singleton.IsClient)
            {
                Debug.LogError("Client connection failed or was rejected");
                ShowConnectionError("Connection failed or was rejected by server");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        if (NetworkManager.Singleton.IsConnectedClient)
        {
            Debug.Log("Client connected successfully, loading lobby scene...");
            
            // Additional safety check - wait a bit more to ensure stable connection
            yield return new WaitForSeconds(0.5f);
            
            // Check if still connected before loading scene
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                isConnecting = false;
                SceneManager.LoadScene("LobbyScene");
            }
            else
            {
                Debug.LogError("Lost connection before loading lobby scene");
                ShowConnectionError("Lost connection to server");
                isConnecting = false;
            }
        }
        else if (elapsed >= timeout)
        {
            Debug.LogError("Connection timeout exceeded");
            ShowConnectionError("Connection timeout - server may be unreachable");
            
            // Cleanup failed connection attempt
            if (NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }
            
            isConnecting = false;
        }
    }
    
    private void ShowConnectionError(string message)
    {
        Debug.LogError($"Connection Error: {message}");
        
        // Here you could show a UI popup or notification
        // For now, we'll just log the error
        // You can expand this to show UI dialogs later
        
        // Optionally, you could add a simple UI text component to show errors
        // Or use Unity's built-in Debug.LogError which appears in the console
    }

}
