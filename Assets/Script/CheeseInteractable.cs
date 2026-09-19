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
    private Vector3 originalScale;
    private bool isInteracting = false;

    // Interface properties
    public float InteractionDuration => interactionDuration;
    public string InteractionPrompt => CarryingFull ? "Your hands are full - bank the cheese in a safe zone" : interactionPrompt;
    public bool CanInteract => canInteract && !isCollected.Value && !CarryingFull;

    // Picking cheese up means carrying it (up to RoundRules.CarryCap) until a safe zone banks it.
    private static bool CarryingFull
    {
        get
        {
            PlayerData me = PlayerData.Local;
            return me != null && !me.IsHunter && me.carriedCheese.Value >= RoundRules.CarryCap;
        }
    }

    private void Start()
    {
        // Store original transform for the bobbing / progress animations
        originalPosition = transform.position;
        originalScale = transform.localScale;

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
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isCollected.OnValueChanged += OnCollectionStatusChanged;

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

        // Slight scale increase as the hold progresses
        transform.localScale = originalScale * (1f + progress * 0.1f);
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

        // Always go through the request RPC so the server credits the client that actually
        // interacted. On the host this is just a local call.
        RequestCollectionServerRpc();
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

        // Reset visual state - OnInteractionProgress leaves the object scaled up
        transform.localScale = originalScale;
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

    private void CollectCheese(PlayerData player)
    {
        if (!IsServer || isCollected.Value) return;

        // Check if player is allowed to collect cheese
        if (player.IsHunter && !allowHunterToCollect)
        {
            Debug.Log($"Hunter {player.PlayerName} cannot collect cheese!");
            return;
        }

        // The server re-checks everything the client's prompt already gated: a request can be
        // late (round over, player downed) or forged.
        if (!player.CanAct || RoundManager.Instance == null || RoundManager.Instance.state.Value != RoundState.Playing) return;
        if (player.carriedCheese.Value >= RoundRules.CarryCap) return;

        // Carried, not counted: it only reaches the team total when a safe zone banks it
        // (SafeZones -> RoundManager.Deposit), and it is lost if the carrier is downed first.
        player.carriedCheese.Value = Mathf.Min(RoundRules.CarryCap, player.carriedCheese.Value + cheeseValue);

        // Everyone hears the pickup at the cheese's position - noise is how the hunter reads the map.
        PlayPickupClientRpc(transform.position);

        Debug.Log($"Cheese '{cheeseName}' (value: {cheeseValue}) collected by {player.PlayerName}");

        // Despawn after a short delay to allow sound to play
        StartCoroutine(DespawnAfterDelay(0.5f));

        // Mark collected last - OnValueChanged runs synchronously here on the server
        isCollected.Value = true;
    }

    [ClientRpc]
    private void PlayPickupClientRpc(Vector3 position)
    {
        ProximityAudio.PlayAt(position, ProximityAudio.Pickup);
    }

    // RoundManager shuffles cheese to new spots each round; keep the bob centred on the new home.
    public void MoveTo(Vector3 position)
    {
        transform.position = position;
        originalPosition = position;
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
        if (!newValue) return;

        // OnNetworkSpawn can run before Start, so resolve the renderer lazily.
        if (objectRenderer == null) objectRenderer = GetComponent<Renderer>();

        // Cheese was collected, hide it visually
        if (objectRenderer != null)
        {
            objectRenderer.enabled = false;
        }

        // Disable interaction
        canInteract = false;
        isInteracting = false;

        if (highlightEffect != null)
        {
            highlightEffect.SetActive(false);
        }
        isHighlighted = false;

        // Stop all animations
        rotateWhenIdle = false;
        bobUpAndDown = false;
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

    // Visual debugging
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = highlightColor;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
