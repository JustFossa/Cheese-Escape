using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Interaction UI that uses a slider approach similar to the stamina UI.
/// Setup requirements:
/// - InteractionPanel: Main GameObject containing all UI elements
/// - InteractionText: TextMeshPro component for displaying interaction prompts
/// - ProgressSlider: Slider component for showing interaction progress
/// - ProgressFill: Image component (slider's fill) for progress visualization
/// - ProgressText: Optional TextMeshPro component for percentage display
/// </summary>
public class InteractionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject interactionPanel;
    [SerializeField] private TextMeshProUGUI interactionText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Image progressFill;
    [SerializeField] private TextMeshProUGUI progressText;
    
    [Header("Settings")]
    [SerializeField] private string interactKeyDisplayName = "E";
    [SerializeField] private Color progressBarColor = Color.yellow;
    [SerializeField] private Color completedColor = Color.green;
    [SerializeField] private Color lockedColor = Color.red;
    
    [Header("Animation Settings")]
    [SerializeField] private bool enableFadeAnimation = true;
    [SerializeField] private float fadeSpeed = 5f;
    [SerializeField] private bool enablePulseEffect = true;
    [SerializeField] private float pulseSpeed = 2f;
    
    private Movement playerMovement;
    private CanvasGroup panelCanvasGroup;
    private bool isUIVisible = false;
    private float currentAlpha = 0f;
    
    void Start()
    {
        // Find the local player's movement component
        Movement[] movements = FindObjectsOfType<Movement>();
        foreach (Movement movement in movements)
        {
            if (movement.IsOwner)
            {
                playerMovement = movement;
                break;
            }
        }
        
        // Fallback to any movement if no owner found
        if (playerMovement == null && movements.Length > 0)
        {
            playerMovement = movements[0];
        }
        
        // Setup Canvas Group for smooth fading
        if (interactionPanel != null)
        {
            panelCanvasGroup = interactionPanel.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = interactionPanel.AddComponent<CanvasGroup>();
            }
        }
        
        // Make sure we have the UI elements
        if (interactionPanel == null)
        {
            Debug.LogWarning("InteractionUI: interactionPanel is not assigned!");
        }
        
        // Initialize progress slider
        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = 0f;
        }
        
        if (progressFill != null)
        {
            progressFill.color = progressBarColor;
        }
        
        interactionPanel.SetActive(false);
    }
    
    void Update()
    {
        if (playerMovement == null) return;
        
        IInteractable currentInteractable = playerMovement.CurrentInteractable;
        bool isInteracting = playerMovement.IsInteracting;
        float progress = playerMovement.InteractionProgress;
        


        // Handle UI visibility and animations
        HandleUIVisibility(currentInteractable);
        
        if (currentInteractable != null)
        {
            // Update interaction text
            UpdateInteractionText(currentInteractable, isInteracting);
            
            // Update progress bar
            UpdateProgressBar(progress, currentInteractable.CanInteract);
            
            // Update progress text
            UpdateProgressText(progress, isInteracting);
            
            // Handle pulse effect
            if (enablePulseEffect && !currentInteractable.CanInteract)
            {
                HandlePulseEffect();
            }
        }
    }
    
    private void HandleUIVisibility(IInteractable currentInteractable)
    {
        bool shouldShowUI = currentInteractable != null;
        
        interactionPanel.SetActive(shouldShowUI);
    }
    
    private void UpdateInteractionText(IInteractable interactable, bool isInteracting)
    {
        if (interactionText == null) return;
        
        if (!interactable.CanInteract)
        {
            // Show locked/unavailable message
            interactionText.text = interactable.InteractionPrompt;
            interactionText.color = lockedColor;
        }
        else if (isInteracting)
        {
            interactionText.text = $"Interacting... ({interactKeyDisplayName})";
            interactionText.color = completedColor;
        }
        else
        {
            interactionText.text = $"{interactable.InteractionPrompt} ({interactKeyDisplayName})";
            interactionText.color = Color.white;
        }
    }
    
    private void UpdateProgressBar(float progress, bool canInteract)
    {
        if (progressSlider == null) return;
        
        // Update slider value
        progressSlider.value = progress;
        
        // Set color based on state (similar to stamina UI approach)
        if (progressFill != null)
        {
            if (!canInteract)
            {
                progressFill.color = lockedColor;
            }
            else if (progress > 0.8f)
            {
                // Lerp between progress color and completed color for smooth transition
                float completionFactor = (progress - 0.8f) / 0.2f; // 0.8 to 1.0 mapped to 0 to 1
                progressFill.color = Color.Lerp(progressBarColor, completedColor, completionFactor);
            }
            else
            {
                progressFill.color = progressBarColor;
            }
        }
        
        // Show/hide progress slider based on interaction availability
        if (progressSlider.gameObject != null)
        {
            progressSlider.gameObject.SetActive(canInteract);
        }
    }
    
    private void UpdateProgressText(float progress, bool isInteracting)
    {
        if (progressText == null) return;
        
        if (isInteracting && progress > 0f)
        {
            progressText.text = $"{Mathf.RoundToInt(progress * 100)}%";
        }
        else
        {
            progressText.text = "";
        }
    }
    
    private void HandlePulseEffect()
    {
        if (interactionText == null) return;
        
        float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        Color textColor = interactionText.color;
        textColor.a = 0.5f + (pulse * 0.5f);
        interactionText.color = textColor;
    }

    
    // Public method to manually set key display name (useful for different input schemes)
    public void SetInteractKeyDisplayName(string keyName)
    {
        interactKeyDisplayName = keyName;
    }
    
    // Public method to update colors (useful for different themes)
    public void SetColors(Color normalColor, Color completedColor, Color lockedColor)
    {
        this.progressBarColor = normalColor;
        this.completedColor = completedColor;
        this.lockedColor = lockedColor;
    }
}