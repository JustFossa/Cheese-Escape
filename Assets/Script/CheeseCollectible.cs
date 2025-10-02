using UnityEngine;

public class CheeseCollectible : MonoBehaviour, IInteractable
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionDuration = 1f;
    [SerializeField] private string interactionPrompt = "Hold E to collect cheese";
    [SerializeField] private bool canInteract = true;
    [SerializeField] private int cheeseValue = 1; // How much cheese this item gives
    
    [Header("Visual Feedback")]
    [SerializeField] private GameObject highlightEffect;
    [SerializeField] private Color highlightColor = Color.yellow;
    [SerializeField] private bool bobUpAndDown = true;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float bobHeight = 0.2f;
    
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip interactionStartSound;
    [SerializeField] private AudioClip interactionCompleteSound;
    [SerializeField] private AudioClip interactionCancelSound;

    private Renderer objectRenderer;
    private Color originalColor;
    private bool isHighlighted = false;
    private Vector3 originalPosition;
    private UIHandler uiHandler;
    
    // Interface properties
    public float InteractionDuration => interactionDuration;
    public string InteractionPrompt => interactionPrompt;
    public bool CanInteract => canInteract;
    
    void Start()
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
        
        // Find the UIHandler in the scene
        uiHandler = FindObjectOfType<UIHandler>();
        if (uiHandler == null)
        {
            Debug.LogWarning("CheeseCollectible: UIHandler not found in scene! Cheese count will not be updated.");
        }
    }
    
    void Update()
    {
        // Bobbing animation when not being interacted with
        if (bobUpAndDown && !isHighlighted)
        {
            float newY = originalPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = new Vector3(originalPosition.x, newY, originalPosition.z);
        }
    }
    
    public void OnInteractionStart()
    {
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
        Debug.Log($"Cheese collected from {gameObject.name}! Value: {cheeseValue}");
        
        // Audio feedback
        if (audioSource != null && interactionCompleteSound != null)
        {
            audioSource.PlayOneShot(interactionCompleteSound);
        }
        
        // Update cheese count in UI
        if (uiHandler != null)
        {
            uiHandler.AddCheese(cheeseValue);
        }
        else
        {
            Debug.LogWarning("UIHandler not found! Cannot update cheese count.");
        }
        
        // Disable the cheese object after collection
        StartCoroutine(DestroyAfterSound());
    }
    
    public void OnInteractionCancel()
    {
        Debug.Log($"Cheese collection from {gameObject.name} cancelled");
        
        // Audio feedback
        if (audioSource != null && interactionCancelSound != null)
        {
            audioSource.PlayOneShot(interactionCancelSound);
        }
        
        // Reset visual state
        SetHighlight(false);
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
    
    private System.Collections.IEnumerator DestroyAfterSound()
    {
        // Wait a bit for the sound to play
        if (audioSource != null && interactionCompleteSound != null)
        {
            yield return new WaitForSeconds(interactionCompleteSound.length);
        }
        else
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        // Disable the game object
        gameObject.SetActive(false);
    }
    
    // Public method to set cheese value (useful for different cheese types)
    public void SetCheeseValue(int value)
    {
        cheeseValue = value;
    }
    
    // Public method to make the cheese interactable/non-interactable
    public void SetInteractable(bool interactable)
    {
        canInteract = interactable;
    }
}