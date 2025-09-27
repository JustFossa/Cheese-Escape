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
    
    // View bobbing variables
    private Vector3 cameraOriginalPosition;
    private float bobTimer = 0f;
    private bool isMoving = false;
    
    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        // Get PlayerInput component and setup input actions
        playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            moveAction = playerInput.actions["Move"];
            jumpAction = playerInput.actions["Jump"];
            lookAction = playerInput.actions["Look"];
        }
        
        // Lock cursor to center of screen
        Cursor.lockState = CursorLockMode.Locked;
        
        // Create ground check point if it doesn't exist
        if (groundCheck == null)
        {
            GameObject groundCheckObj = new GameObject("GroundCheck");
            groundCheckObj.transform.SetParent(transform);
            groundCheckObj.transform.localPosition = new Vector3(0, -1f, 0);
            groundCheck = groundCheckObj.transform;
        }
        
        // Store the original camera position for view bobbing
        if (cameraTransform != null)
        {
            cameraOriginalPosition = cameraTransform.localPosition;
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Only process input if this is the local player
        if (!IsOwner) return;
        
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
        // Apply horizontal movement while preserving Y velocity
        Vector3 velocity = moveDirection * moveSpeed;
        velocity.y = rb.velocity.y;
        rb.velocity = velocity;
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
