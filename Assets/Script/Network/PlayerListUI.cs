using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public class PlayerListUI : NetworkBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI playerListText;
    public GameObject playerListPanel;
    
    [Header("Settings")]
    public bool showPlayerList = true;
    public float updateInterval = 1f; // Update player list every second
    
    private float updateTimer = 0f;

    void Start()
    {
        // Subscribe to PlayerManager events if available
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.OnLobbyPlayerJoined += OnLobbyPlayerJoined;
            PlayerManager.Instance.OnLobbyPlayerLeft += OnLobbyPlayerLeft;
        }
    }
    
    public override void OnDestroy()
    {
        // Unsubscribe from PlayerManager events
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.OnLobbyPlayerJoined -= OnLobbyPlayerJoined;
            PlayerManager.Instance.OnLobbyPlayerLeft -= OnLobbyPlayerLeft;
        }
        base.OnDestroy();
    }
    
    void Update()
    {
        if (!showPlayerList || playerListText == null) return;
        
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            UpdatePlayerList();
            updateTimer = 0f;
        }
    }
    
    private void OnLobbyPlayerJoined(ulong clientId, string playerName)
    {
        UpdatePlayerList();
    }
    
    private void OnLobbyPlayerLeft(ulong clientId, string playerName)
    {
        UpdatePlayerList();
    }

    private void UpdatePlayerList()
    {
        if (PlayerManager.Instance == null) return;
        List<string> playerNames = new List<string>();
        
        // Get all LobbyPlayer components in the scene for lobby display
        LobbyPlayer[] allLobbyPlayers = FindObjectsOfType<LobbyPlayer>();
        
        if (allLobbyPlayers.Length > 0)
        {
            // We're in lobby - show lobby players
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
                
                playerNames.Add(playerInfo);
            }
        }
        else
        {
            // We're in game - show regular players
            PlayerData[] allPlayers = FindObjectsOfType<PlayerData>();
            
            foreach (PlayerData player in allPlayers)
            {
                string playerInfo = $"• {player.PlayerName}";
                
                // Add additional info if this is the local player
                if (player.IsOwner)
                {
                    playerInfo += " (You)";
                }
                
                playerNames.Add(playerInfo);
            }
        }
        
        // Update the UI text
        if (playerNames.Count > 0)
        {
            playerListText.text = "Connected Players:\n" + string.Join("\n", playerNames);
        }
        else
        {
            playerListText.text = "No players connected";
        }
        
        // Show/hide the panel based on whether there are players
        if (playerListPanel != null)
        {
            playerListPanel.SetActive(playerNames.Count > 0);
        }
    }

    // Method to toggle the player list visibility
    public void TogglePlayerList()
    {
        showPlayerList = !showPlayerList;
        
        if (playerListPanel != null)
        {
            playerListPanel.SetActive(showPlayerList);
        }
    }

    // Method to manually refresh the player list
    public void RefreshPlayerList()
    {
        UpdatePlayerList();
    }
}