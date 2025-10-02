using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Cheese collectible that players can pick up to increase their cheese count
/// This script should be attached to cheese GameObjects in the game scene
/// </summary>
public class Cheese : NetworkBehaviour
{
    [Header("Cheese Settings")]
    [SerializeField] private int cheeseValue = 1; // How much cheese this gives when collected
    [SerializeField] private string cheeseName = "Cheese Piece";
    [SerializeField] private bool allowHunterToCollect = false; // Whether hunters can collect cheese
    
    [Header("Visual Settings")]
    [SerializeField] private Color cheeseColor = Color.yellow;
    [SerializeField] private bool rotateObject = true;
    [SerializeField] private float rotationSpeed = 90f; // Degrees per second
    
    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip collectSound;
    
    private NetworkVariable<bool> isCollected = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );
    
    private Renderer objectRenderer;
    
    public bool IsCollected => isCollected.Value;
    
    private void Start()
    {
        // Setup visual appearance
        SetupVisualAppearance();
        
        // Subscribe to collection status changes
        isCollected.OnValueChanged += OnCollectionStatusChanged;
        
        // Setup audio
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }
    
    private void Update()
    {
        // Rotate the cheese piece for visual appeal
        if (rotateObject && !isCollected.Value)
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
    }
    
    private void SetupVisualAppearance()
    {
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            objectRenderer.material.color = cheeseColor;
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Only process on server and if not already collected
        if (!IsServer || isCollected.Value) return;
        
        // Check if it's a player
        PlayerData playerData = other.GetComponent<PlayerData>();
        if (playerData != null)
        {
            // Check if player is allowed to collect cheese
            if (playerData.IsHunter && !allowHunterToCollect)
            {
                Debug.Log($"Hunter {playerData.PlayerName} cannot collect cheese!");
                return;
            }
            
            CollectCheese(playerData);
        }
    }
    
    private void CollectCheese(PlayerData player)
    {
        if (!IsServer) return;
        
        // Mark as collected
        isCollected.Value = true;
        
        // Play collection sound
        if (collectSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(collectSound);
        }
        
        // Notify all clients about collection
        NotifyCheeseCollectedClientRpc(player.OwnerClientId, cheeseValue, cheeseName);
        
        Debug.Log($"Cheese '{cheeseName}' (value: {cheeseValue}) collected by {player.PlayerName}");
        
        // Despawn after a short delay to allow sound to play
        StartCoroutine(DespawnAfterDelay(0.5f));
    }
    
    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
    
    [ClientRpc]
    private void NotifyCheeseCollectedClientRpc(ulong collectorClientId, int value, string cheeseName)
    {
        // Update UI for the collector
        GameUI.Instance.AddCheese(value);
       
    }
    
    private void OnCollectionStatusChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // Cheese was collected, hide it visually
            if (objectRenderer != null)
            {
                objectRenderer.enabled = false;
            }
            
            // Disable collider to prevent multiple collections
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }
            
            // Stop rotation
            rotateObject = false;
        }
    }
    
    // Public methods for customization
    
    /// <summary>
    /// Set the value of this cheese piece
    /// </summary>
    /// <param name="value">New cheese value</param>
    public void SetCheeseValue(int value)
    {
        cheeseValue = Mathf.Max(1, value);
    }
    
    /// <summary>
    /// Set whether hunters can collect this cheese
    /// </summary>
    /// <param name="allowed">True if hunters can collect, false otherwise</param>
    public void SetHunterCollectionAllowed(bool allowed)
    {
        allowHunterToCollect = allowed;
    }
    
    public override void OnDestroy()
    {
        if (isCollected != null)
        {
            isCollected.OnValueChanged -= OnCollectionStatusChanged;
        }
        base.OnDestroy();
    }
    
    // Visual debugging
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = cheeseColor;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        
        if (Application.isPlaying && isCollected.Value)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
        }
    }
}