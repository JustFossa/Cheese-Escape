using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Door : NetworkBehaviour
{
    [Header("Door Settings")]
    public int requiredKeyId = 1; // ID of the key required to open this door
    public string doorName = "Door"; // Display name for this door
    public bool consumeKey = false; // Whether opening the door consumes the key
    public bool allowHunterToPass = false; // Whether hunters can pass through without a key
    
    [Header("Door Animation")]
    public bool useAnimation = true;
    public float animationDuration = 1f;
    public Vector3 openPosition = Vector3.zero; // Local position when open
    public Vector3 openRotation = Vector3.zero; // Local rotation when open
    
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );
    
    private Vector3 closedPosition;
    private Vector3 closedRotation;
    private bool isAnimating = false;
    
    public bool IsOpen => isOpen.Value;

    private void Start()
    {
        // Store original position and rotation
        closedPosition = transform.localPosition;
        closedRotation = transform.localEulerAngles;
        
        // Subscribe to door state changes
        isOpen.OnValueChanged += OnDoorStateChanged;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only process on server
        if (!IsServer) return;
        
        // Don't process if door is already open
        if (isOpen.Value) return;
        
        // Check if it's a player
        PlayerData playerData = other.GetComponent<PlayerData>();
        if (playerData == null) return;
        
        // Handle based on player type
        if (playerData.IsHunter)
        {
            HandleHunterAtDoor(playerData);
        }
        else
        {
            HandleRegularPlayerAtDoor(playerData);
        }
    }

    private void HandleHunterAtDoor(PlayerData hunter)
    {
        if (allowHunterToPass)
        {
            OpenDoor(hunter);
            NotifyDoorActionClientRpc(hunter.OwnerClientId, $"Hunter {hunter.PlayerName} forced the door open!", true);
        }
        else
        {
            NotifyDoorActionClientRpc(hunter.OwnerClientId, $"The {doorName} is locked. Hunters cannot open doors.", false);
        }
    }

    private void HandleRegularPlayerAtDoor(PlayerData player)
    {
        if (player.HasKey(requiredKeyId))
        {
            OpenDoor(player);
            
            // Remove key if it should be consumed
            if (consumeKey)
            {
                player.RemoveKeyFromInventoryServerRpc(requiredKeyId);
                NotifyDoorActionClientRpc(player.OwnerClientId, $"You used your key to open the {doorName}!", true);
            }
            else
            {
                NotifyDoorActionClientRpc(player.OwnerClientId, $"You opened the {doorName} with your key!", true);
            }
        }
        else
        {
            NotifyDoorActionClientRpc(player.OwnerClientId, $"You need a key to open the {doorName}.", false);
        }
    }

    private void OpenDoor(PlayerData player)
    {
        if (!IsServer) return;
        
        isOpen.Value = true;
        Debug.Log($"{doorName} opened by {player.PlayerName}");
        
        // Notify all clients about the door opening
        NotifyDoorOpenedClientRpc(player.OwnerClientId);
    }

    [ClientRpc]
    private void NotifyDoorActionClientRpc(ulong playerClientId, string message, bool success)
    {
        // Show message to the specific player
        if (NetworkManager.Singleton.LocalClientId == playerClientId)
        {
            if (success)
            {
                Debug.Log($"<color=green>{message}</color>");
            }
            else
            {
                Debug.Log($"<color=red>{message}</color>");
            }
            // Here you could trigger UI notifications, sound effects, etc.
        }
    }

    [ClientRpc]
    private void NotifyDoorOpenedClientRpc(ulong openerClientId)
    {
        PlayerData opener = null;
        foreach (var player in FindObjectsOfType<PlayerData>())
        {
            if (player.OwnerClientId == openerClientId)
            {
                opener = player;
                break;
            }
        }
        
        if (opener != null)
        {
            Debug.Log($"{doorName} was opened by {opener.PlayerName}!");
        }
    }

    private void OnDoorStateChanged(bool oldValue, bool newValue)
    {
        if (newValue && !oldValue)
        {
            // Door opened
            StartCoroutine(AnimateDoor(true));
        }
        else if (!newValue && oldValue)
        {
            // Door closed
            StartCoroutine(AnimateDoor(false));
        }
    }

    private IEnumerator AnimateDoor(bool opening)
    {
        if (isAnimating) yield break;
        
        isAnimating = true;
        
        Vector3 startPos = transform.localPosition;
        Vector3 startRot = transform.localEulerAngles;
        Vector3 targetPos = opening ? openPosition : closedPosition;
        Vector3 targetRot = opening ? openRotation : closedRotation;
        
        if (useAnimation)
        {
            float elapsedTime = 0f;
            
            while (elapsedTime < animationDuration)
            {
                elapsedTime += Time.deltaTime;
                float t = elapsedTime / animationDuration;
                
                // Smooth animation curve
                t = Mathf.SmoothStep(0f, 1f, t);
                
                transform.localPosition = Vector3.Lerp(startPos, targetPos, t);
                transform.localEulerAngles = Vector3.Lerp(startRot, targetRot, t);
                
                yield return null;
            }
        }
        
        // Ensure final position is exact
        transform.localPosition = targetPos;
        transform.localEulerAngles = targetRot;
        
        isAnimating = false;
    }

    // Public method to close the door (if needed for game mechanics)
    [ServerRpc(RequireOwnership = false)]
    public void CloseDoorServerRpc()
    {
        if (IsServer)
        {
            isOpen.Value = false;
        }
    }

    // Public method to check if a specific player can open this door
    public bool CanPlayerOpenDoor(PlayerData player)
    {
        if (player == null) return false;
        
        if (player.IsHunter)
        {
            return allowHunterToPass;
        }
        else
        {
            return player.HasKey(requiredKeyId);
        }
    }

    public override void OnDestroy()
    {
        if (isOpen != null)
        {
            isOpen.OnValueChanged -= OnDoorStateChanged;
        }
        base.OnDestroy();
    }

    // Visual debugging
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position, Vector3.one);
        
        if (Application.isPlaying && isOpen.Value)
        {
            Gizmos.color = Color.green;
        }
        else
        {
            Gizmos.color = Color.red;
        }
        
        Vector3 doorCenter = transform.position + transform.TransformDirection(openPosition) * 0.5f;
        Gizmos.DrawWireSphere(doorCenter, 0.5f);
    }
}
