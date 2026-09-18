using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Key : NetworkBehaviour
{
    [Header("Key Settings")]
    public int keyId = 0; // Unique identifier for this key - must match a Door's requiredKeyId
    public string keyName = "Key"; // Display name for this key
    public Color keyColor = Color.yellow; // Visual color for the key

    private NetworkVariable<bool> isCollected = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Renderer objectRenderer;

    public bool IsCollected => isCollected.Value;

    private void Start()
    {
        // Setup visual appearance
        SetupVisualAppearance();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isCollected.OnValueChanged += OnCollectionStatusChanged;

        // Already collected before we joined - apply the hidden state immediately
        if (isCollected.Value)
        {
            OnCollectionStatusChanged(false, true);
        }
    }

    public override void OnNetworkDespawn()
    {
        isCollected.OnValueChanged -= OnCollectionStatusChanged;
        base.OnNetworkDespawn();
    }

    private void SetupVisualAppearance()
    {
        // Apply key color to renderer if available
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            objectRenderer.material.color = keyColor;
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

        // Add to player's inventory
        player.AddKeyToInventoryServerRpc(keyId, new FixedString64Bytes(keyName));

        // Notify all clients
        NotifyKeyCollectedClientRpc(player.OwnerClientId, keyName);

        Debug.Log($"Key '{keyName}' collected by {player.PlayerName}");

        // Start the despawn BEFORE flipping the flag. OnValueChanged fires synchronously on the
        // server, and the old code deactivated the GameObject there - which made StartCoroutine
        // throw and left the key spawned forever.
        StartCoroutine(DespawnAfterDelay(0.1f));

        isCollected.Value = true;
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
            return;
        }

        foreach (var player in FindObjectsOfType<PlayerData>())
        {
            if (player.OwnerClientId == collectorClientId)
            {
                Debug.Log($"{player.PlayerName} collected the {keyName}!");
                break;
            }
        }
    }

    private void OnCollectionStatusChanged(bool oldValue, bool newValue)
    {
        if (!newValue) return;

        // OnNetworkSpawn can run before Start, so resolve the renderer lazily.
        if (objectRenderer == null) objectRenderer = GetComponent<Renderer>();

        // Hide visually without deactivating the GameObject, so coroutines keep running.
        if (objectRenderer != null)
        {
            objectRenderer.enabled = false;
        }

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
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
}
