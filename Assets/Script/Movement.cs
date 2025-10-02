using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Movement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float mouseSensitivity = 2f;

    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float staminaRegenRate = 20f; // Stamina regenerated per second
    public float sprintStaminaDrain = 25f; // Stamina drained per second while sprinting
    public float minStaminaToSprint = 10f; // Minimum stamina required to start sprinting

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

    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioClip sprintStartSound;
    public AudioClip sprintStopSound;
    public AudioClip lowStaminaSound;

    [Header("Interaction Settings")]
    public float interactionRange = 3f;
    public LayerMask interactableLayerMask = -1; // All layers by default
    public KeyCode interactKey = KeyCode.E;


    private Rigidbody rb;
    private bool isGrounded;
    private Vector3 moveDirection;
    public Transform cameraTransform;
    private float xRotation = 0f;

    // Input System variables
    private PlayerInput playerInput;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction sprintAction;
    private InputAction interactAction;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool isSprintPressed;
    private bool isInteractPressed;

    // Stamina variables
    private float currentStamina;
    private bool isSprinting = false;
    private bool canSprint = true;

    // Public properties for UI access
    public float CurrentStamina => currentStamina;
    public bool IsSprinting => isSprinting;

    public GameObject playerModel;

    // View bobbing variables
    private Vector3 cameraOriginalPosition;
    private float bobTimer = 0f;
    private bool isMoving = false;

    // Interaction variables
    private IInteractable currentInteractable;
    private float interactionTimer = 0f;
    private bool isInteracting = false;

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

        // Initialize stamina
        currentStamina = maxStamina;

        // Configure Rigidbody to prevent unwanted spinning
        if (rb != null)
        {
            rb.freezeRotation = true; // Prevent rotation from physics
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // Better collision detection

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

        if (IsOwner && playerModel != null)
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
                // Force enable and reinitialize the input system
                playerInput.enabled = false; // Disable first
                playerInput.enabled = true;  // Then re-enable to force refresh

                // Wait a frame and setup actions to ensure proper initialization
                StartCoroutine(SetupInputActionsDelayed());
            }
            else
            {
                Debug.LogError($"PlayerInput component not found on {gameObject.name}! Input will not work properly.", this);
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


        }
    }

    private IEnumerator SetupInputActionsDelayed()
    {
        // Wait a frame to ensure PlayerInput is fully initialized
        yield return null;

        if (playerInput != null && playerInput.actions != null)
        {
            // Setup input actions with error handling
            try
            {
                moveAction = playerInput.actions["Move"];
                lookAction = playerInput.actions["Look"];
                sprintAction = playerInput.actions["Sprint"];

                // Try to get interaction action if it exists
                try
                {
                    interactAction = playerInput.actions["Interact"];
                }
                catch
                {
                    interactAction = null;
                    Debug.Log("Interact action not found in Input Actions, using manual key detection");
                }

                // Enable all actions
                if (moveAction != null) moveAction.Enable();
                if (lookAction != null) lookAction.Enable();
                if (sprintAction != null) sprintAction.Enable();
                if (interactAction != null) interactAction.Enable();

                Debug.Log($"Input actions successfully initialized for player {OwnerClientId}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to setup input actions: {e.Message}", this);

                // Fallback: try to reinitialize PlayerInput component after a longer delay
                StartCoroutine(RetryInputSetupDelayed());
            }
        }
        else
        {
            Debug.LogWarning("PlayerInput or actions are null, retrying input setup...");
            yield return new WaitForSeconds(0.5f);
            RetryInputSetup();
        }
    }

    private IEnumerator RetryInputSetupDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        RetryInputSetup();
    }

    private void RetryInputSetup()
    {
        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInput>();
        }

        if (playerInput != null)
        {
            playerInput.enabled = false;
            playerInput.enabled = true;
            StartCoroutine(SetupInputActionsDelayed());
        }
        else
        {
            Debug.LogError("PlayerInput component is missing! Player input will not work.");
        }
    }

    // Public method to force input system reinitialization (can be called when hunter status changes)
    public void ForceInputReinitialize()
    {
        if (IsOwner)
        {
            Debug.Log($"Force reinitializing input for player {OwnerClientId}");
            SetupPlayerComponents();
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

        // Check if input actions are null and reinitialize if needed
        if (moveAction == null || lookAction == null || sprintAction == null)
        {
            Debug.LogWarning($"Input actions are null for player {OwnerClientId}, attempting to reinitialize...");
            ForceInputReinitialize();
            return; // Skip this frame while reinitializing
        }

        // Handle stamina system
        HandleStamina();

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

        // Handle object interaction
        HandleInteraction();
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
        // Determine current movement speed based on sprinting
        float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;

        // Use AddForce for more natural physics interaction instead of directly setting velocity
        Vector3 targetVelocity = moveDirection * currentSpeed;
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
        if (horizontalVelocity.magnitude > currentSpeed)
        {
            horizontalVelocity = horizontalVelocity.normalized * currentSpeed;
            rb.velocity = new Vector3(horizontalVelocity.x, rb.velocity.y, horizontalVelocity.z);
        }

        // Always ensure no unwanted rotation
        rb.angularVelocity = Vector3.zero;
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
            // Adjust bobbing intensity and speed based on sprinting
            float currentBobSpeed = isSprinting ? bobSpeed * 1.5f : bobSpeed;
            float currentBobAmount = isSprinting ? bobAmount * 1.3f : bobAmount;

            // Increment bob timer
            bobTimer += Time.deltaTime * currentBobSpeed;

            // Calculate bobbing offset using sine waves
            float yBob = Mathf.Sin(bobTimer) * currentBobAmount;
            float xBob = Mathf.Cos(bobTimer * 0.5f) * currentBobAmount * 0.5f; // Horizontal sway, less intense

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

    void HandleStamina()
    {
        // Store previous sprinting state for audio feedback
        bool wasSprinting = isSprinting;

        // Manual check for sprint input as backup (in case callback fails)
        bool manualSprintCheck = false;
        if (sprintAction != null)
        {
            manualSprintCheck = sprintAction.IsPressed();
        }

        // Use manual check if it differs from callback value
        if (manualSprintCheck != isSprintPressed)
        {

            isSprintPressed = manualSprintCheck;
        }

        // Check if player is trying to sprint
        bool wantsToSprint = isSprintPressed && moveInput.magnitude > 0.1f && isGrounded;


        // Determine if we can/should sprint
        if (wantsToSprint && currentStamina >= minStaminaToSprint && canSprint)
        {
            // Start sprinting if not already sprinting
            if (!isSprinting)
            {
                isSprinting = true;
                Debug.Log("Started sprinting");
                PlaySprintStartSound();
            }

            // Only drain stamina if we're actually sprinting
            if (isSprinting)
            {
                currentStamina -= sprintStaminaDrain * Time.deltaTime;
                currentStamina = Mathf.Max(0f, currentStamina);

                // If stamina runs out, stop sprinting and prevent immediate restart
                if (currentStamina <= 0f)
                {
                    isSprinting = false;
                    canSprint = false;
                    Debug.Log("Stamina exhausted - stopped sprinting");
                    PlayLowStaminaSound();
                }
            }
        }
        else
        {
            // Stop sprinting immediately when not wanting to sprint
            if (isSprinting)
            {
                isSprinting = false;
                Debug.Log("Stopped sprinting - conditions not met");
                PlaySprintStopSound();
            }

            // Regenerate stamina when not sprinting
            if (currentStamina < maxStamina)
            {
                currentStamina += staminaRegenRate * Time.deltaTime;
                currentStamina = Mathf.Min(maxStamina, currentStamina);
            }

            // Allow sprinting again when stamina is above minimum threshold
            if (currentStamina >= minStaminaToSprint)
            {
                canSprint = true;
            }
        }

        // Failsafe: Reset sprint state if Left Shift is not actually pressed
        if (isSprinting && !UnityEngine.Input.GetKey(KeyCode.LeftShift))
        {
            Debug.Log("Failsafe: Force stopping sprint - Left Shift not detected");
            isSprinting = false;
            isSprintPressed = false;
            PlaySprintStopSound();
        }
    }

    void PlaySprintStartSound()
    {
        if (audioSource != null && sprintStartSound != null)
        {
            audioSource.PlayOneShot(sprintStartSound);
        }
    }

    void PlaySprintStopSound()
    {
        if (audioSource != null && sprintStopSound != null)
        {
            audioSource.PlayOneShot(sprintStopSound);
        }
    }

    void PlayLowStaminaSound()
    {
        if (audioSource != null && lowStaminaSound != null)
        {
            audioSource.PlayOneShot(lowStaminaSound);
        }
    }

    // Input System callback for Move action
    public void OnMove(InputValue value)
    {
        if (!IsOwner) return;
        Vector2 newMoveInput = value.Get<Vector2>();

        // Debug logging for hunters to track input issues
        PlayerData playerData = GetComponent<PlayerData>();
        if (playerData != null && playerData.IsHunter && newMoveInput.magnitude > 0.1f)
        {
            Debug.Log($"Hunter {OwnerClientId} move input: {newMoveInput}");
        }

        moveInput = newMoveInput;
    }

    // Input System callback for Look action
    public void OnLook(InputValue value)
    {
        if (!IsOwner) return;
        lookInput = value.Get<Vector2>();
    }

    // Input System callback for Sprint action
    public void OnSprint(InputValue value)
    {
        if (!IsOwner) return;
        isSprintPressed = value.isPressed;

        // Debug logging for hunters
        PlayerData playerData = GetComponent<PlayerData>();
        if (playerData != null && playerData.IsHunter)
        {
            Debug.Log($"Hunter {OwnerClientId} sprint input: {value.isPressed}");
        }
    }

    // Input System callback for Interact action (if it exists in Input Actions)
    public void OnInteract(InputValue value)
    {
        if (!IsOwner) return;
        isInteractPressed = value.isPressed;
    }

    // Handle collisions with other players to prevent spinning
    void OnCollisionEnter(Collision collision)
    {
        // Log collision for debugging, especially for hunters
        PlayerData playerData = GetComponent<PlayerData>();
        if (playerData != null && playerData.IsHunter && collision.gameObject.GetComponent<Movement>() != null)
        {
            // Check if hunter is facing the player
            Vector3 directionToPlayer = (collision.transform.position - transform.position).normalized;
            Vector3 hunterForward = transform.forward;
            
            // Calculate the angle between hunter's forward direction and direction to player
            float angle = Vector3.Angle(hunterForward, directionToPlayer);
            
            // Only allow catch if hunter is facing the player (within 45 degrees)
            if (angle <= 45f)
            {
                // Get the caught player's data before destroying
                Movement caughtPlayerMovement = collision.gameObject.GetComponent<Movement>();
                PlayerData caughtPlayerData = collision.gameObject.GetComponent<PlayerData>();
                
                if (caughtPlayerMovement != null && caughtPlayerData != null)
                {
                    // If this is the local player who got caught, handle their elimination
                    if (caughtPlayerMovement.IsOwner)
                    {
                        Debug.Log($"Local player {caughtPlayerData.PlayerName} was caught by hunter - returning to main menu");
                        
                        // Start coroutine to handle elimination sequence
                        StartCoroutine(HandlePlayerElimination());
                    }
                    
                    Debug.Log($"Hunter {OwnerClientId} caught {caughtPlayerData.PlayerName} while facing them (angle: {angle:F1}°)");
                }
                
                Destroy(collision.gameObject);
            }
            else
            {
                Debug.Log($"Hunter {OwnerClientId} collided with {collision.gameObject.name} but wasn't facing them (angle: {angle:F1}°)");
            }
        }
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

    void HandleInteraction()
    {
        // Get interaction input (try Input Actions first, fallback to KeyCode)
        bool interactInput = false;
        if (interactAction != null)
        {
            interactInput = interactAction.IsPressed();
        }
        else
        {
            interactInput = Input.GetKey(interactKey);
        }

        // If we have manual input, override the callback value
        if (interactInput != isInteractPressed)
        {
            isInteractPressed = interactInput;
        }

        // Raycast from camera to detect interactable objects
        IInteractable detectedInteractable = GetInteractableInView();

        // Handle interactable object changes
        if (detectedInteractable != currentInteractable)
        {
            // Cancel interaction with previous object
            if (currentInteractable != null && isInteracting)
            {
                currentInteractable.OnInteractionCancel();
                isInteracting = false;
                interactionTimer = 0f;
            }

            currentInteractable = detectedInteractable;
        }

        // Handle interaction logic
        if (currentInteractable != null && currentInteractable.CanInteract)
        {
            if (isInteractPressed)
            {
                if (!isInteracting)
                {
                    // Start interaction
                    isInteracting = true;
                    interactionTimer = 0f;
                    currentInteractable.OnInteractionStart();
                }

                // Update interaction progress
                interactionTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(interactionTimer / currentInteractable.InteractionDuration);
                currentInteractable.OnInteractionProgress(progress);

                // Check if interaction is complete
                if (interactionTimer >= currentInteractable.InteractionDuration)
                {
                    currentInteractable.OnInteractionComplete();
                    isInteracting = false;
                    interactionTimer = 0f;
                }
            }
            else if (isInteracting)
            {
                // Cancel interaction if E is released
                currentInteractable.OnInteractionCancel();
                isInteracting = false;
                interactionTimer = 0f;
            }
        }
        else if (isInteracting)
        {
            // Cancel interaction if object is no longer interactable or in view
            if (currentInteractable != null)
            {
                currentInteractable.OnInteractionCancel();
            }
            isInteracting = false;
            interactionTimer = 0f;
        }
    }

    IInteractable GetInteractableInView()
    {
        // Cast multiple rays accounting for camera rotation to create a small cone of detection
        Vector3 cameraPos = cameraTransform.position;
        Vector3 cameraForward = cameraTransform.forward;
        Vector3 cameraRight = cameraTransform.right;
        Vector3 cameraUp = cameraTransform.up;

        // Define the rays to cast (center + small offsets for rotation tolerance)
        Vector3[] rayDirections = new Vector3[]
        {
            cameraForward, // Center ray
            (cameraForward + cameraRight * 0.1f).normalized, // Slightly right
            (cameraForward - cameraRight * 0.1f).normalized, // Slightly left
            (cameraForward + cameraUp * 0.1f).normalized, // Slightly up
            (cameraForward - cameraUp * 0.1f).normalized, // Slightly down
        };

        IInteractable closestInteractable = null;
        float closestDistance = float.MaxValue;

        // Cast rays in multiple directions to account for rotation
        foreach (Vector3 direction in rayDirections)
        {
            Ray ray = new Ray(cameraPos, direction);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, interactionRange, interactableLayerMask))
            {
                // Check if the hit object has an IInteractable component
                IInteractable interactable = hit.collider.GetComponent<IInteractable>();

                if (interactable != null && hit.distance < closestDistance)
                {
                    closestInteractable = interactable;
                    closestDistance = hit.distance;

                }

            }
        }

        return closestInteractable;
    }

    // Public properties for UI access
    public IInteractable CurrentInteractable => currentInteractable;
    public bool IsInteracting => isInteracting;
    public float InteractionProgress => currentInteractable != null ?
        Mathf.Clamp01(interactionTimer / currentInteractable.InteractionDuration) : 0f;

    // Coroutine to handle player elimination sequence
    private System.Collections.IEnumerator HandlePlayerElimination()
    {
        // Unlock cursor first
        Cursor.lockState = CursorLockMode.None;
        
        // Wait a brief moment for any network cleanup
        yield return new WaitForSeconds(0.5f);
        
        // Shutdown the network manager
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("Shutting down NetworkManager for eliminated player");
            NetworkManager.Singleton.Shutdown();
        }
        
        // Wait a moment for network shutdown to complete
        yield return new WaitForSeconds(0.3f);
        
        // Load the main menu scene
        Debug.Log("Loading main menu scene for eliminated player");
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
    }

    // Visual debugging for ground check and interaction ray
    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, 0.1f);
            Gizmos.DrawLine(groundCheck.position, groundCheck.position + Vector3.down * groundCheckDistance);
        }

        // Draw interaction ray
        if (cameraTransform != null)
        {
            Gizmos.color = currentInteractable != null ? Color.green : Color.blue;
            Gizmos.DrawRay(cameraTransform.position, cameraTransform.forward * interactionRange);
        }
    }
    

}
