using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Handles in-game UI elements like key collection display and cheese counter
/// This script should be attached to a GameObject in the game scene
/// </summary>
public class GameUI : MonoBehaviour
{
    [Header("Game UI Elements")]
    [SerializeField] private TextMeshProUGUI keysCollectedText;
    [SerializeField] private TextMeshProUGUI cheeseCountText;
    [SerializeField] private GameObject gameUIPanel;
    
    [Header("Display Settings")]
    [SerializeField] private bool showKeyNames = true;
    [SerializeField] private bool showKeyCount = true;
    [SerializeField] private string noKeysMessage = "Keys: None";
    [SerializeField] private string cheesePrefix = "Cheese: ";
    
    [Header("Update Settings")]
    [SerializeField] private float updateInterval = 0.5f; // Update UI every 0.5 seconds instead of every frame
    
    private PlayerData localPlayerData;
    private float lastUpdateTime = 0f;

    [Header("Cheese Door")]
    public GameObject cheeseDoor;
    private bool cheeseDoorOpened = false;

    // Static instance for easy access from other scripts
    public static GameUI Instance { get; private set; }
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    private void Start()
    {
        // Initialize UI
        InitializeUI();
        
        // Find local player
        FindLocalPlayer();
        
        // Initial UI update
        UpdateUI();
    }
    
    private void Update()
    {
        // Update UI at intervals instead of every frame for performance
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateUI();
            lastUpdateTime = Time.time;
        }
    }
    
    private void InitializeUI()
    {
        // Show game UI panel if it exists
        if (gameUIPanel != null)
        {
            gameUIPanel.SetActive(true);
        }
        
        // Initialize text components with default values
        if (keysCollectedText != null)
        {
            keysCollectedText.text = noKeysMessage;
        }
        
        if (cheeseCountText != null)
        {
            cheeseCountText.text = cheesePrefix + "0";
        }
    }
    
    private void FindLocalPlayer()
    {
        // Only find local player if we don't have one or if it's null
        if (localPlayerData != null) return;
        
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            // Find the local player's PlayerData
            PlayerData[] allPlayers = FindObjectsOfType<PlayerData>();
            foreach (PlayerData player in allPlayers)
            {
                if (player.IsOwner)
                {
                    localPlayerData = player;
                    Debug.Log($"GameUI: Found local player {player.PlayerName}");
                    break;
                }
            }
        }
    }
    
    private void UpdateUI()
    {
        // Try to find local player if we don't have one
        if (localPlayerData == null)
        {
            FindLocalPlayer();
        }
        
        // Update both displays
        UpdateKeysDisplay();
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
                keysCollectedText.text = noKeysMessage;
            }
            else
            {
                string displayText = "";
                
                if (showKeyCount)
                {
                    displayText = $"Keys ({keyCount})";
                }
                else
                {
                    displayText = "Keys";
                }
                
                if (showKeyNames && keyCount > 0)
                {
                    displayText += ": ";
                    List<string> keyNames = new List<string>();
                    
                    foreach (var key in collectedKeys)
                    {
                        keyNames.Add(key.keyName.ToString());
                    }
                    
                    displayText += string.Join(", ", keyNames);
                }
                else if (showKeyCount)
                {
                    // Just show count without names
                    // displayText already contains the count
                }
                
                keysCollectedText.text = displayText;
            }
        }
        else
        {
            keysCollectedText.text = "Keys: Loading...";
        }
    }
    
    // The count is one replicated number on RoundManager, so every client opens the door off the
    // same value. It used to be a per-client int bumped by a ClientRpc - in sync by luck.
    private void UpdateCheeseDisplay()
    {
        RoundManager round = RoundManager.Instance;
        int count = round != null ? round.CheeseCollected : 0;
        int needed = round != null ? round.CheeseNeeded : 0;

        if (cheeseCountText != null)
        {
            cheeseCountText.text = needed > 0 ? $"{cheesePrefix}{count}/{needed}" : cheesePrefix + count;
        }

        if (!cheeseDoorOpened && needed > 0 && count >= needed)
        {
            cheeseDoorOpened = true;
            if (cheeseDoor != null)
            {
                Destroy(cheeseDoor);
            }
        }
    }

    public int GetCheeseCount()
    {
        return RoundManager.Instance != null ? RoundManager.Instance.CheeseCollected : 0;
    }

    // Public methods for UI customization
    
    /// <summary>
    /// Force an immediate UI update
    /// </summary>
    public void ForceUpdateUI()
    {
        UpdateUI();
    }
    
    /// <summary>
    /// Set whether to show key names in the display
    /// </summary>
    /// <param name="show">True to show key names, false to hide them</param>
    public void SetShowKeyNames(bool show)
    {
        showKeyNames = show;
        UpdateKeysDisplay();
    }
    
    /// <summary>
    /// Set whether to show key count in the display
    /// </summary>
    /// <param name="show">True to show key count, false to hide it</param>
    public void SetShowKeyCount(bool show)
    {
        showKeyCount = show;
        UpdateKeysDisplay();
    }
    
    // Debug methods
    
    /// <summary>
    /// Get debug information about the current UI state
    /// </summary>
    /// <returns>Debug info string</returns>
    public string GetDebugInfo()
    {
        string info = "GameUI Debug Info:\n";
        info += $"Local Player Found: {localPlayerData != null}\n";
        info += $"Cheese Count: {GetCheeseCount()}\n";
        
        if (localPlayerData != null)
        {
            info += $"Player Name: {localPlayerData.PlayerName}\n";
            info += $"Keys Collected: {localPlayerData.GetKeyCount()}\n";
        }
        
        return info;
    }
    
    private void OnDestroy()
    {
        // Clean up singleton reference
        if (Instance == this)
        {
            Instance = null;
        }
    }
}