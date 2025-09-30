using UnityEngine;

public interface IInteractable
{
    /// <summary>
    /// The duration in seconds that the player needs to hold E to interact
    /// </summary>
    float InteractionDuration { get; }
    
    /// <summary>
    /// The display name of the interactable object
    /// </summary>
    string InteractionPrompt { get; }
    
    /// <summary>
    /// Called when the player starts looking at this interactable
    /// </summary>
    void OnInteractionStart();
    
    /// <summary>
    /// Called every frame while the player is holding E and looking at this interactable
    /// </summary>
    /// <param name="progress">Progress from 0 to 1 (0 = just started, 1 = completed)</param>
    void OnInteractionProgress(float progress);
    
    /// <summary>
    /// Called when the interaction is completed (held for full duration)
    /// </summary>
    void OnInteractionComplete();
    
    /// <summary>
    /// Called when the player stops looking at this interactable or releases E
    /// </summary>
    void OnInteractionCancel();
    
    /// <summary>
    /// Whether this object can currently be interacted with
    /// </summary>
    bool CanInteract { get; }
}