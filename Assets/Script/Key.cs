using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Key : NetworkBehaviour
{
    [Header("Key Settings")]
    public int keyId = 0; // Unique identifier for this key
    public string keyName = "Key"; // Display name for this key
    public Color keyColor = Color.yellow; // Visual color for the key
    
    private NetworkVariable<bool> isCollected = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );
    
    public bool IsCollected => isCollected.Value;

    private void Start()
    {
        // Ensure this key has a unique ID if not set
        if (keyId == 0)
        {
            keyId = GetInstanceID(); // Use instance ID as fallback
        }
        
        // Subscribe to collection status changes
        isCollected.OnValueChanged += OnCollectionStatusChanged;
        
        // Setup visual appearance
        SetupVisualAppearance();
    }

    private void SetupVisualAppearance()
    {
        // Apply key color to renderer if available
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = keyColor;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only process on server and if not already collected
        if (!IsServer || isCollected.Value) return;
        
        // Check if it's a regular player (not hunter)
        PlayerData playerData = other.GetComponent<PlayerData>();
        if (playerData != null && !playerData.IsHunter)
        {
            CollectKey(playerData);
        }
        else if (playerData != null && playerData.IsHunter)
        {
            Debug.Log("Hunters cannot collect keys!");
        }
    }

    private void CollectKey(PlayerData player)
    {
        if (!IsServer) return;
        
        // Mark as collected
        isCollected.Value = true;
         // Despawn the key object for everyone
        StartCoroutine(DespawnAfterDelay(0.1f));
        // Add to player's inventory
        player.AddKeyToInventoryServerRpc(keyId, new FixedString64Bytes(keyName));
        
        // Notify all clients
        NotifyKeyCollectedClientRpc(player.OwnerClientId, keyName);
        
        Debug.Log($"Key '{keyName}' collected by {player.PlayerName}");
        
       
    }
    
    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }

    [ClientRpc]
    private void NotifyKeyCollectedClientRpc(ulong collectorClientId, string keyName)
    {
        // Notify all clients about key collection
        if (NetworkManager.Singleton.LocalClientId == collectorClientId)
        {
            Debug.Log($"You collected the {keyName}!");
            // Here you could trigger UI updates, sound effects, etc.
        }
        else
        {
            PlayerData collector = null;
            foreach (var player in FindObjectsOfType<PlayerData>())
            {
                if (player.OwnerClientId == collectorClientId)
                {
                    collector = player;
                    break;
                }
            }
            
            if (collector != null)
            {
                Debug.Log($"{collector.PlayerName} collected the {keyName}!");
            }
        }
    }

    private void OnCollectionStatusChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // Key was collected, hide it visually
            gameObject.SetActive(false);
        }
    }

    public KeyData GetKeyData()
    {
        return new KeyData
        {
            keyId = this.keyId,
            keyName = new FixedString64Bytes(this.keyName)
        };
    }

    public override void OnDestroy()
    {
        if (isCollected != null)
        {
            isCollected.OnValueChanged -= OnCollectionStatusChanged;
        }
        base.OnDestroy();
    }
}
