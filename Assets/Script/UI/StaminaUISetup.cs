using UnityEngine;
using UnityEngine.UI;

public class StaminaUISetup : MonoBehaviour
{
    [Header("Setup Instructions")]
    [TextArea(5, 10)]
    public string instructions = @"To set up the Stamina UI:

1. Create a Canvas (if you don't have one)
2. Create a new UI > Slider as a child of the Canvas
3. Position the slider at the bottom of the screen
4. Set the slider's Fill Area > Fill to use a nice stamina color (green/blue)
5. Add a CanvasGroup component to the slider
6. Attach the StaminaUI script to the slider
7. Assign the Slider component, Fill Image, and CanvasGroup to the script
8. Adjust colors and settings as desired";

    [Header("Auto Setup")]
    public bool autoSetupOnStart = false;
    
    void Start()
    {
        if (autoSetupOnStart)
        {
            AutoSetupStaminaUI();
        }
    }
    
    [ContextMenu("Auto Setup Stamina UI")]
    public void AutoSetupStaminaUI()
    {
        // Find or create canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        // Create stamina slider
        GameObject sliderObj = new GameObject("StaminaSlider");
        sliderObj.transform.SetParent(canvas.transform, false);
        
        // Add RectTransform and position it
        RectTransform rectTransform = sliderObj.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.1f, 0.05f);
        rectTransform.anchorMax = new Vector2(0.4f, 0.1f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        // Add Slider component
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.value = 100f;
        
        // Create Background
        GameObject background = new GameObject("Background");
        background.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRect = background.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        slider.targetGraphic = bgImage;
        
        // Create Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;
        
        // Create Fill
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = Color.green;
        
        slider.fillRect = fillRect;
        
        // Add CanvasGroup for fading
        CanvasGroup canvasGroup = sliderObj.AddComponent<CanvasGroup>();
        
        // Add StaminaUI script
        StaminaUI staminaUI = sliderObj.AddComponent<StaminaUI>();
        staminaUI.staminaSlider = slider;
        staminaUI.staminaFill = fillImage;
        staminaUI.staminaCanvasGroup = canvasGroup;
        
        Debug.Log("Stamina UI has been automatically set up!");
    }
}