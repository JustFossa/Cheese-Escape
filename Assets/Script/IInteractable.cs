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

/// <summary>
/// Optional. For interactables that sit on things you can look at all the time (a player's body,
/// a table) but only mean something in some situations. While ShowPrompt is false the interaction
/// ray ignores the object, so it doesn't flash a locked prompt at a healthy teammate.
/// </summary>
public interface IConditionalPrompt
{
    bool ShowPrompt { get; }
}