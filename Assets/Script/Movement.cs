using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Movement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpForce = 10f;
    public float mouseSensitivity = 2f;
    
    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckDistance = 0.4f;
    public LayerMask groundLayerMask = 1; // Default layer
    
    [Header("View Bobbing")]
    public float bobAmount = 0.05f;
    public float bobSpeed = 14f;
    public bool enableViewBobbing = true;
    
    [Header("Physics Settings")]
    public float linearDrag = 1f;
    public float angularDrag = 10f;
    public float playerMass = 1f;
    public float collisionDamping = 0.5f;


    private Rigidbody rb;
    private bool isGrounded;
    private Vector3 moveDirection;
    public Transform cameraTransform;
    private float xRotation = 0f;
    
    // Input System variables
    private PlayerInput playerInput;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction lookAction;
    private Vector2 moveInput;
    private Vector2 lookInput;

    public GameObject playerModel;
    
    // View bobbing variables
    private Vector3 cameraOriginalPosition;
    private float bobTimer = 0f;
    private bool isMoving = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Setup camera and input after network spawn
        SetupPlayerComponents();
        
    }
    
    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        // Configure Rigidbody to prevent unwanted spinning
        if (rb != null)
        {
            // Freeze rotation on X and Z axes to prevent spinning from collisions
            rb.freezeRotation = true;
            
            // Set drag values to reduce sliding and spinning
            rb.drag = linearDrag; // Linear drag to reduce sliding
            rb.angularDrag = angularDrag; // Angular drag to reduce spinning (though rotation is frozen)
            
            // Set mass for more stable physics
            rb.mass = playerMass;
        }
        
        // Setup physics material for all colliders to prevent bouncing and reduce friction
        SetupPhysicsMaterial();
        
        // Create ground check point if it doesn't exist (for all players)
        if (groundCheck == null)
        {
            GameObject groundCheckObj = new GameObject("GroundCheck");
            groundCheckObj.transform.SetParent(transform);
            groundCheckObj.transform.localPosition = new Vector3(0, -1f, 0);
            groundCheck = groundCheckObj.transform;
        }
        
        if(IsOwner && playerModel != null)
        {
            // Disable the player model for the local player to prevent seeing own body
            playerModel.SetActive(false);
        }


        // If network isn't spawned yet, setup components anyway
            if (!IsSpawned)
            {
                SetupPlayerComponents();
            }
    }
    
    private void SetupPlayerComponents()
    {
        // Only setup input and camera for the local player (owner)
        if (IsOwner)
        {
            // Get PlayerInput component and setup input actions
            playerInput = GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = true;
                moveAction = playerInput.actions["Move"];
                jumpAction = playerInput.actions["Jump"];
                lookAction = playerInput.actions["Look"];
            }
            
            // Lock cursor to center of screen
            Cursor.lockState = CursorLockMode.Locked;
            
            // Disable any existing main cameras in the scene (to prevent conflicts)
            DisableSceneCamera();
            
            // Enable the camera for the local player
            if (cameraTransform != null)
            {
                Camera playerCamera = cameraTransform.GetComponent<Camera>();
                if (playerCamera != null)
                {
                    playerCamera.enabled = true;
                    print($"Enabled camera for local player {OwnerClientId}");
                    
                    // Also enable AudioListener if present
                    AudioListener audioListener = cameraTransform.GetComponent<AudioListener>();
                    if (audioListener != null)
                    {
                        audioListener.enabled = true;
                        print($"Enabled audio listener for local player {OwnerClientId}");
                    }
                }
                
                // Store the original camera position for view bobbing
                cameraOriginalPosition = cameraTransform.localPosition;
            }
        }
        else
        {
            // Disable camera and audio listener for remote players
            if (cameraTransform != null)
            {
                Camera playerCamera = cameraTransform.GetComponent<Camera>();
                if (playerCamera != null)
                {
                    playerCamera.enabled = false;
                    print($"Disabled camera for remote player {OwnerClientId}");
                }
                
                AudioListener audioListener = cameraTransform.GetComponent<AudioListener>();
                if (audioListener != null)
                {
                    audioListener.enabled = false;
                    print($"Disabled audio listener for remote player {OwnerClientId}");
                }
            }
            
            // Disable PlayerInput for remote players
            playerInput = GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = false;
            }
        }
    }
    
    public override void OnNetworkDespawn()
    {
        // Release cursor lock when the local player is destroyed
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        
        base.OnNetworkDespawn();
    }
    
    public override void OnDestroy()
    {
        // Also release cursor lock on destroy
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        
        base.OnDestroy();
    }
    
    private void DisableSceneCamera()
    {
        // Find and disable any main camera in the scene that's not part of a player
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.transform != cameraTransform)
        {
            mainCamera.enabled = false;
            print("Disabled scene main camera to prevent conflicts");
            
            // Also disable its AudioListener if present
            AudioListener sceneAudioListener = mainCamera.GetComponent<AudioListener>();
            if (sceneAudioListener != null)
            {
                sceneAudioListener.enabled = false;
                print("Disabled scene audio listener");
            }
        }
    }
    
    private void SetupPhysicsMaterial()
    {
        // Get all colliders on this GameObject and its children
        Collider[] colliders = GetComponentsInChildren<Collider>();
        
        foreach (Collider col in colliders)
        {
            // Try to load the physics material we created
            PhysicMaterial playerPhysics = Resources.Load<PhysicMaterial>("PlayerPhysics");
            
            // If we can't load it, create one with the right settings
            if (playerPhysics == null)
            {
                playerPhysics = new PhysicMaterial("PlayerPhysics");
                playerPhysics.dynamicFriction = 0.3f;
                playerPhysics.staticFriction = 0.3f;
                playerPhysics.bounciness = 0f; // No bouncing
                playerPhysics.frictionCombine = PhysicMaterialCombine.Average;
                playerPhysics.bounceCombine = PhysicMaterialCombine.Minimum;
            }
            
            col.material = playerPhysics;
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Only process input if this is the local player
        if (!IsOwner || cameraTransform == null) return;
        
        // Calculate movement direction relative to camera
        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        
        // Remove Y component to keep movement on ground plane
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();
        
        moveDirection = (forward * moveInput.y + right * moveInput.x).normalized;
        
        // Check if grounded using raycast
        isGrounded = Physics.Raycast(groundCheck.position, Vector3.down, groundCheckDistance, groundLayerMask);
        
        // Handle mouse look
        HandleMouseLook();
        
        // Handle view bobbing
        HandleViewBobbing();
    }
    
    void FixedUpdate()
    {
        // Only move if this is the local player
        if (!IsOwner) return;
        
        // Handle movement
        Move();
    }
    
    void Move()
    {
        // Use AddForce for more natural physics interaction instead of directly setting velocity
        Vector3 targetVelocity = moveDirection * moveSpeed;
        Vector3 currentVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        Vector3 velocityDifference = targetVelocity - currentVelocity;
        
        // Apply force to reach target velocity, but don't exceed it
        if (velocityDifference.magnitude > 0.1f)
        {
            // Use ForceMode.VelocityChange for immediate response, but scaled down for smoothness
            rb.AddForce(velocityDifference * 10f, ForceMode.Force);
        }
        
        // Ensure we don't exceed max speed
        Vector3 horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        if (horizontalVelocity.magnitude > moveSpeed)
        {
            horizontalVelocity = horizontalVelocity.normalized * moveSpeed;
            rb.velocity = new Vector3(horizontalVelocity.x, rb.velocity.y, horizontalVelocity.z);
        }
        
        // Always ensure no unwanted rotation
        rb.angularVelocity = Vector3.zero;
    }
    
    void Jump()
    {
        // Apply jump force
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }
    
    void HandleMouseLook()
    {
        // Get mouse input from Input System
        float mouseX = lookInput.x * mouseSensitivity;
        float mouseY = lookInput.y * mouseSensitivity;
        
        // Rotate the player body horizontally
        transform.Rotate(Vector3.up * mouseX);
        
        // Rotate the camera vertically
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 65f);
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
    
    void HandleViewBobbing()
    {
        if (!enableViewBobbing || cameraTransform == null) return;
        
        // Check if the player is moving and grounded
        isMoving = moveInput.magnitude > 0.1f && isGrounded;
        
        if (isMoving)
        {
            // Increment bob timer
            bobTimer += Time.deltaTime * bobSpeed;
            
            // Calculate bobbing offset using sine waves
            float yBob = Mathf.Sin(bobTimer) * bobAmount;
            float xBob = Mathf.Cos(bobTimer * 0.5f) * bobAmount * 0.5f; // Horizontal sway, less intense
            
            // Apply bobbing to camera position
            Vector3 bobOffset = new Vector3(xBob, yBob, 0f);
            cameraTransform.localPosition = cameraOriginalPosition + bobOffset;
        }
        else
        {
            // Smoothly return to original position when not moving
            cameraTransform.localPosition = Vector3.Lerp(
                cameraTransform.localPosition, 
                cameraOriginalPosition, 
                Time.deltaTime * bobSpeed * 2f
            );
            
            // Reset bob timer gradually when not moving
            bobTimer = Mathf.Lerp(bobTimer, 0f, Time.deltaTime * 2f);
        }
    }
    
    // Input System callback for Jump action
    public void OnJump(InputValue value)
    {
        if (!IsOwner) return;
        
        if (value.isPressed && isGrounded)
        {
            Jump();
        }
    }
    
    // Input System callback for Move action
    public void OnMove(InputValue value)
    {
        if (!IsOwner) return;
        moveInput = value.Get<Vector2>();
    }
    
    // Input System callback for Look action
    public void OnLook(InputValue value)
    {
        if (!IsOwner) return;
        lookInput = value.Get<Vector2>();
    }
    
    // Handle collisions with other players to prevent spinning
    void OnCollisionEnter(Collision collision)
    {
        // Check if we collided with another player
        if (collision.gameObject.GetComponent<Movement>() != null)
        {
            // Stop any unwanted rotation immediately
            if (rb != null)
            {
                rb.angularVelocity = Vector3.zero;
                
                // Reduce the collision impact by dampening the velocity
                Vector3 currentVelocity = rb.velocity;
                currentVelocity.x *= collisionDamping; // Reduce horizontal velocity
                currentVelocity.z *= collisionDamping; // Reduce horizontal velocity
                rb.velocity = currentVelocity;
            }
        }
    }
    
    void OnCollisionStay(Collision collision)
    {
        // Continuously prevent spinning while in contact with another player
        if (collision.gameObject.GetComponent<Movement>() != null)
        {
            if (rb != null)
            {
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
    
    // Visual debugging for ground check
    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, 0.1f);
            Gizmos.DrawLine(groundCheck.position, groundCheck.position + Vector3.down * groundCheckDistance);
        }
    }
}
