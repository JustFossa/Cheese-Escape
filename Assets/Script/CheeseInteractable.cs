using UnityEngine;
using Unity.Netcode;

/// <summary>
/// A cheese collectible that requires manual interaction (hold E) and works with the network system
/// This combines the manual interaction of InteractableObject with the network features of Cheese
/// </summary>
public class CheeseInteractable : NetworkBehaviour, IInteractable
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionDuration = 2f;
    [SerializeField] private string interactionPrompt = "Hold E to collect cheese";
    [SerializeField] private bool canInteract = true;
    
    [Header("Cheese Settings")]
    [SerializeField] private int cheeseValue = 1;
    [SerializeField] private string cheeseName = "Cheese Piece";
    [SerializeField] private bool allowHunterToCollect = false;
    
    [Header("Visual Feedback")]
    [SerializeField] private GameObject highlightEffect;
    [SerializeField] private Color highlightColor = Color.yellow;
    [SerializeField] private Color originalColor = Color.white;
    [SerializeField] private bool rotateWhenIdle = true;
    [SerializeField] private float rotationSpeed = 90f;
    [SerializeField] private bool bobUpAndDown = true;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float bobHeight = 0.2f;
    
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip interactionStartSound;
    [SerializeField] private AudioClip interactionCompleteSound;
    [SerializeField] private AudioClip interactionCancelSound;

    private NetworkVariable<bool> isCollected = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    private Renderer objectRenderer;
    private bool isHighlighted = false;
    private Vector3 originalPosition;
    private bool isInteracting = false;
    
    // Interface properties
    public float InteractionDuration => interactionDuration;
    public string InteractionPrompt => interactionPrompt;
    public bool CanInteract => canInteract && !isCollected.Value;
    
    private void Start()
    {
        // Store original position for bobbing animation
        originalPosition = transform.position;
        
        // Get renderer for visual feedback
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            originalColor = objectRenderer.material.color;
        }
        
        // Find audio source if not assigned
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        
        // Subscribe to collection status changes
        isCollected.OnValueChanged += OnCollectionStatusChanged;
    }
    
    private void Update()
    {
        if (isCollected.Value) return;
        
        // Bobbing animation when not being interacted with
        if (bobUpAndDown && !isHighlighted)
        {
            float newY = originalPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = new Vector3(originalPosition.x, newY, originalPosition.z);
        }
        
        // Rotation when idle
        if (rotateWhenIdle && !isInteracting)
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
    }
    
    public void OnInteractionStart()
    {
        if (isCollected.Value) return;
        
        isInteracting = true;
        Debug.Log($"Started collecting cheese from {gameObject.name}");
        
        // Visual feedback
        SetHighlight(true);
        
        // Audio feedback
        if (audioSource != null && interactionStartSound != null)
        {
            audioSource.PlayOneShot(interactionStartSound);
        }
    }
    
    public void OnInteractionProgress(float progress)
    {
        if (isCollected.Value) return;
        
        // Visual feedback for progress - change color based on completion
        if (objectRenderer != null)
        {
            Color lerpedColor = Color.Lerp(originalColor, highlightColor, progress);
            objectRenderer.material.color = lerpedColor;
        }
        
        // Optional: Scale the object slightly based on progress
        float scale = 1f + (progress * 0.1f); // Slight scale increase
        transform.localScale = Vector3.one * scale;
    }
    
    public void OnInteractionComplete()
    {
        if (isCollected.Value) return;
        
        Debug.Log($"Cheese collection from {gameObject.name} completed! Value: {cheeseValue}");
        
        // Audio feedback
        if (audioSource != null && interactionCompleteSound != null)
        {
            audioSource.PlayOneShot(interactionCompleteSound);
        }
        
        // Perform the collection (server-side)
        if (IsServer)
        {
            CollectCheeseServerRpc();
        }
        else if (IsClient)
        {
            // Request collection from server
            RequestCollectionServerRpc();
        }
    }
    
    public void OnInteractionCancel()
    {
        if (isCollected.Value) return;
        
        isInteracting = false;
        Debug.Log($"Cheese collection from {gameObject.name} cancelled");
        
        // Audio feedback
        if (audioSource != null && interactionCancelSound != null)
        {
            audioSource.PlayOneShot(interactionCancelSound);
        }
        
        // Reset visual state
        SetHighlight(false);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void RequestCollectionServerRpc(ServerRpcParams rpcParams = default)
    {
        if (isCollected.Value) return;
        
        // Find the player who requested collection
        PlayerData playerData = GetPlayerFromClientId(rpcParams.Receive.SenderClientId);
        if (playerData != null)
        {
            CollectCheese(playerData);
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void CollectCheeseServerRpc()
    {
        if (isCollected.Value) return;
        
        // Find any local player for testing purposes
        PlayerData[] players = FindObjectsOfType<PlayerData>();
        if (players.Length > 0)
        {
            CollectCheese(players[0]);
        }
    }
    
    private void CollectCheese(PlayerData player)
    {
        if (!IsServer || isCollected.Value) return;
        
        // Check if player is allowed to collect cheese
        if (player.IsHunter && !allowHunterToCollect)
        {
            Debug.Log($"Hunter {player.PlayerName} cannot collect cheese!");
            return;
        }
        
        // Mark as collected
        isCollected.Value = true;
        
        // Notify all clients about collection
        NotifyCheeseCollectedClientRpc(player.OwnerClientId, cheeseValue, cheeseName);
        
        Debug.Log($"Cheese '{cheeseName}' (value: {cheeseValue}) collected by {player.PlayerName}");
        
        // Despawn after a short delay to allow sound to play
        StartCoroutine(DespawnAfterDelay(0.5f));
    }
    
    [ClientRpc]
    private void NotifyCheeseCollectedClientRpc(ulong collectorClientId, int value, string cheeseName)
    {
        // Update UI for all clients (the UI will determine if it's the local player)
        if (GameUI.Instance != null)
        {
            GameUI.Instance.AddCheese(value);
        }
        
        Debug.Log($"Cheese '{cheeseName}' collected! Value: {value}");
    }
    
    private PlayerData GetPlayerFromClientId(ulong clientId)
    {
        PlayerData[] players = FindObjectsOfType<PlayerData>();
        foreach (PlayerData player in players)
        {
            if (player.OwnerClientId == clientId)
            {
                return player;
            }
        }
        return null;
    }
    
    private System.Collections.IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
    
    private void SetHighlight(bool highlight)
    {
        isHighlighted = highlight;
        
        if (objectRenderer != null)
        {
            objectRenderer.material.color = highlight ? highlightColor : originalColor;
        }
        
        if (highlightEffect != null)
        {
            highlightEffect.SetActive(highlight);
        }
        
        // Stop bobbing when highlighted
        if (highlight)
        {
            transform.position = originalPosition;
        }
    }
    
    private void OnCollectionStatusChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // Cheese was collected, hide it visually
            if (objectRenderer != null)
            {
                objectRenderer.enabled = false;
            }
            
            // Disable interaction
            canInteract = false;
            isInteracting = false;
            
            // Reset visual state
            SetHighlight(false);
            
            // Stop all animations
            rotateWhenIdle = false;
            bobUpAndDown = false;
        }
    }
    
    // Public methods for customization
    public void SetCheeseValue(int value)
    {
        cheeseValue = Mathf.Max(1, value);
    }
    
    public void SetHunterCollectionAllowed(bool allowed)
    {
        allowHunterToCollect = allowed;
    }
    
    public void SetInteractable(bool interactable)
    {
        canInteract = interactable;
    }
    
    public override void OnDestroy()
    {
        if (isCollected != null)
        {
            isCollected.OnValueChanged -= OnCollectionStatusChanged;
        }
        base.OnDestroy();
    }
    
    // Visual debugging
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = highlightColor;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        
        if (Application.isPlaying && isCollected.Value)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
        }
    }
}