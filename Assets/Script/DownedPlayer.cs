using UnityEngine;

/// <summary>
/// Added to every player body at spawn. Only means anything while that player is Downed: a
/// healthy survivor looking at them holds E to revive. The server validates the request.
/// </summary>
public class DownedPlayer : MonoBehaviour, IInteractable, IConditionalPrompt
{
    private PlayerData target;

    private void Awake() => target = GetComponent<PlayerData>();

    // The reviver's perk sets how long the hold takes: a Medic needs 3s, everyone else 5s.
    public float InteractionDuration
    {
        get
        {
            PlayerData local = PlayerData.Local;
            return RoundRules.ReviveSeconds(local != null ? local.ActivePerk : Perk.None);
        }
    }
    public string InteractionPrompt => $"Hold to revive {target.PlayerName}";

    public bool ShowPrompt
    {
        get
        {
            if (target == null || target.IsOwner || target.lifeState.Value != LifeState.Downed) return false;
            PlayerData local = PlayerData.Local;
            return local != null && !local.IsHunter && local.CanAct;
        }
    }

    public bool CanInteract => ShowPrompt;

    public void OnInteractionStart() { }
    public void OnInteractionProgress(float progress) { }
    public void OnInteractionCancel() { }

    public void OnInteractionComplete()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.RequestReviveServerRpc(target.NetworkObjectId);
    }
}
