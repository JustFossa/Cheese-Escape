using Unity.Netcode;
using UnityEngine;
using TMPro;
using Unity.Collections;

/// <summary>
/// Simple lobby player representation that doesn't require full player spawning
/// This is useful for showing players in the lobby before the actual game starts
/// </summary>
public class LobbyPlayer : NetworkBehaviour
{
    [Header("Lobby Player Info")]
    private NetworkVariable<FixedString64Bytes> lobbyPlayerName = new NetworkVariable<FixedString64Bytes>(
        "", 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    [Header("UI References (Optional)")]
    public TextMeshProUGUI playerNameText;

    public string PlayerName => lobbyPlayerName.Value.ToString();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();


        if (IsOwner)
        {
            // Get the player name from PlayerPrefs
            string savedName = PlayerPrefs.GetString("PlayerName", "Player");
        
            if (string.IsNullOrEmpty(savedName))
            {
                savedName = "Player " + OwnerClientId;
            }

            // Set the lobby player name
            SetLobbyPlayerNameServerRpc(savedName);
        }

        // Subscribe to name changes
        lobbyPlayerName.OnValueChanged += OnNameChanged;



        // Register with PlayerManager if it exists
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.RegisterLobbyPlayer(OwnerClientId, this);
            print("LobbyPlayer spawned with name: " + PlayerName);
        }
        
        // Update UI
        UpdateNameDisplay();
    }

    public override void OnNetworkDespawn()
    {
        // Unregister from PlayerManager
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.UnregisterLobbyPlayer(OwnerClientId);
        }
        
        lobbyPlayerName.OnValueChanged -= OnNameChanged;
        base.OnNetworkDespawn();
    }

    [ServerRpc]
    private void SetLobbyPlayerNameServerRpc(string newName)
    {
        // Validate the name
        if (string.IsNullOrEmpty(newName) || newName.Length > 32)
        {
            newName = "Player " + OwnerClientId;
        }
        
        lobbyPlayerName.Value = newName;
    }

    private void OnNameChanged(FixedString64Bytes oldName, FixedString64Bytes newName)
    {
        UpdateNameDisplay();
    }

    private void UpdateNameDisplay()
    {
        if (playerNameText != null)
        {
            string displayName = PlayerName;
            
            // Add host indicator
            if (OwnerClientId == 0 || (NetworkManager.Singleton != null && OwnerClientId == NetworkManager.Singleton.LocalClientId && NetworkManager.Singleton.IsHost))
            {
                displayName += " [Host]";
            }
            
            // Add "you" indicator
            if (IsOwner)
            {
                displayName += " (You)";
            }
            
            playerNameText.text = displayName;
        }
    }

    // Static method to get all lobby players
    public static LobbyPlayer[] GetAllLobbyPlayers()
    {
        return FindObjectsOfType<LobbyPlayer>();
    }

    // Static method to get all lobby player names
    public static string[] GetAllLobbyPlayerNames()
    {
        LobbyPlayer[] lobbyPlayers = GetAllLobbyPlayers();
        string[] names = new string[lobbyPlayers.Length];
        
        for (int i = 0; i < lobbyPlayers.Length; i++)
        {
            names[i] = lobbyPlayers[i].PlayerName;
        }
        
        return names;
    }
}