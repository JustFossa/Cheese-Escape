using UnityEngine;

public class InteractableObject : MonoBehaviour, IInteractable
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionDuration = 2f;
    [SerializeField] private string interactionPrompt = "Hold E to interact";
    [SerializeField] private bool canInteract = true;
    
    [Header("Visual Feedback")]
    [SerializeField] private GameObject highlightEffect;
    [SerializeField] private Color highlightColor = Color.yellow;
    
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip interactionStartSound;
    [SerializeField] private AudioClip interactionCompleteSound;
    [SerializeField] private AudioClip interactionCancelSound;
    
    private Renderer objectRenderer;
    private Color originalColor;
    private bool isHighlighted = false;
    
    public float InteractionDuration => interactionDuration;
    public string InteractionPrompt => interactionPrompt;
    public bool CanInteract => canInteract;
    
    void Start()
    {
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            originalColor = objectRenderer.material.color;
        }
        
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }
    
    public void OnInteractionStart()
    {
        Debug.Log($"Started interacting with {gameObject.name}");
        
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
        // You can add visual/audio feedback for progress here
        // For example, changing the highlight intensity based on progress
        if (objectRenderer != null)
        {
            Color lerpedColor = Color.Lerp(originalColor, highlightColor, progress);
            objectRenderer.material.color = lerpedColor;
        }
    }
    
    public void OnInteractionComplete()
    {
        Debug.Log($"Interaction with {gameObject.name} completed!");
        
        // Audio feedback
        if (audioSource != null && interactionCompleteSound != null)
        {
            audioSource.PlayOneShot(interactionCompleteSound);
        }
        
        // Reset visual state
        SetHighlight(false);
        
        // Add your interaction logic here
        // For example: open door, pick up item, activate mechanism, etc.
        PerformInteraction();
    }
    
    public void OnInteractionCancel()
    {
        Debug.Log($"Interaction with {gameObject.name} cancelled");
        
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
    }
    
    private void PerformInteraction()
    {
        // Override this method in derived classes or add your specific interaction logic here
        // Examples:
        // - Destroy the object
        // - Change object state
        // - Trigger an event
        // - Open a door
        // - Collect an item
        
        // For demonstration, let's just disable the object
        gameObject.SetActive(false);
    }
    
    // Optional: Make the object interactable/non-interactable from other scripts
    public void SetInteractable(bool interactable)
    {
        canInteract = interactable;
    }
}