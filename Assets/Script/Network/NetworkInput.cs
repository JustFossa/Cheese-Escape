using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkInput : NetworkBehaviour
{

    PlayerInput m_PlayerInput;
    void Awake()
    {
        m_PlayerInput = GetComponent<PlayerInput>();
        m_PlayerInput.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        m_PlayerInput.enabled = IsOwner;
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        m_PlayerInput.enabled = false;
        base.OnNetworkDespawn();
    }
}
