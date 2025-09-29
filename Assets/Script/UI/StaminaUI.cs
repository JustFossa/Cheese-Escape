using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class StaminaUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Slider staminaSlider;
    public Image staminaFill;
    public CanvasGroup staminaCanvasGroup;
    
    [Header("UI Settings")]
    public Color fullStaminaColor = Color.green;
    public Color lowStaminaColor = Color.red;
    public Color mediumStaminaColor = Color.yellow;
    public float lowStaminaThreshold = 0.3f;
    public float mediumStaminaThreshold = 0.6f;
    public float fadeSpeed = 2f;
    public float visibilityDuration = 3f; // How long UI stays visible after stamina change
    
    private Movement playerMovement;
    private float lastStaminaValue = -1f;
    private float lastVisibilityTime;
    private bool shouldBeVisible = false;

    void Start()
    {
        // Find the local player's movement component
        FindLocalPlayerMovement();
        
        // Initialize UI
        if (staminaSlider != null)
        {
            staminaSlider.minValue = 0f;
            staminaSlider.maxValue = 100f;
        }
        
        // Start with UI hidden
        if (staminaCanvasGroup != null)
        {
            staminaCanvasGroup.alpha = 0f;
        }
    }
    
    void FindLocalPlayerMovement()
    {
        // Find all Movement components in the scene
        Movement[] allMovements = FindObjectsOfType<Movement>();
        
        foreach (Movement movement in allMovements)
        {
            // Check if this is the local player
            NetworkBehaviour networkBehaviour = movement.GetComponent<NetworkBehaviour>();
            if (networkBehaviour != null && networkBehaviour.IsOwner)
            {
                playerMovement = movement;
                break;
            }
        }
        
        // If we couldn't find by NetworkBehaviour, try another approach
        if (playerMovement == null)
        {
            // Look for a movement component on a GameObject with a specific tag or name
            GameObject localPlayer = GameObject.FindWithTag("Player");
            if (localPlayer != null)
            {
                playerMovement = localPlayer.GetComponent<Movement>();
            }
        }
    }

    void Update()
    {
        // If we haven't found the player movement yet, keep trying
        if (playerMovement == null)
        {
            FindLocalPlayerMovement();
            return;
        }
        
        // Get current stamina from the movement script
        float currentStamina = playerMovement.CurrentStamina;
        float maxStamina = playerMovement.maxStamina;
        bool isSprinting = playerMovement.IsSprinting;
        
        // Update UI if stamina changed or player is sprinting
        if (Mathf.Abs(currentStamina - lastStaminaValue) > 0.1f || isSprinting)
        {
            UpdateStaminaUI(currentStamina, maxStamina);
            lastStaminaValue = currentStamina;
            shouldBeVisible = true;
            lastVisibilityTime = Time.time;
        }
        
        // Handle UI visibility
        HandleUIVisibility();
    }
    
    void UpdateStaminaUI(float currentStamina, float maxStamina)
    {
        if (staminaSlider != null)
        {
            staminaSlider.value = currentStamina;
            staminaSlider.maxValue = maxStamina;
        }
        
        // Update color based on stamina level
        if (staminaFill != null)
        {
            float staminaPercentage = currentStamina / maxStamina;
            
            if (staminaPercentage <= lowStaminaThreshold)
            {
                staminaFill.color = lowStaminaColor;
            }
            else if (staminaPercentage <= mediumStaminaThreshold)
            {
                staminaFill.color = Color.Lerp(lowStaminaColor, mediumStaminaColor, 
                    (staminaPercentage - lowStaminaThreshold) / (mediumStaminaThreshold - lowStaminaThreshold));
            }
            else
            {
                staminaFill.color = Color.Lerp(mediumStaminaColor, fullStaminaColor, 
                    (staminaPercentage - mediumStaminaThreshold) / (1f - mediumStaminaThreshold));
            }
        }
    }
    
    void HandleUIVisibility()
    {
        if (staminaCanvasGroup == null) return;
        
        // Determine if UI should be visible
        bool showUI = shouldBeVisible && (Time.time - lastVisibilityTime < visibilityDuration || 
                     (playerMovement != null && playerMovement.IsSprinting) ||
                     (playerMovement != null && playerMovement.CurrentStamina < playerMovement.maxStamina));
        
        // Fade UI in/out
        float targetAlpha = showUI ? 1f : 0f;
        staminaCanvasGroup.alpha = Mathf.MoveTowards(staminaCanvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        
        // Hide completely when fully faded out
        if (staminaCanvasGroup.alpha <= 0.01f)
        {
            shouldBeVisible = false;
        }
    }
}