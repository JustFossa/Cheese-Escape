using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owner only. Once you are caught or have escaped you keep your body and camera; this just points
/// the camera at a teammate still in play. Click to cycle. It moves the camera rather than
/// reparenting it, so a target despawning mid-spectate can't drag the camera away with it.
/// </summary>
public class SpectatorCamera : MonoBehaviour
{
    private PlayerData me;
    private Movement movement;
    private readonly List<PlayerData> targets = new List<PlayerData>();
    private float nextScan;
    private int index;

    private void Start()
    {
        me = GetComponent<PlayerData>();
        movement = GetComponent<Movement>();
    }

    private void LateUpdate()
    {
        if (me == null || movement == null || movement.cameraTransform == null || !me.IsSpectating) return;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            targets.Clear();
            foreach (PlayerData p in FindObjectsOfType<PlayerData>())
            {
                if (p != me && (p.lifeState.Value == LifeState.Alive || p.lifeState.Value == LifeState.Downed))
                    targets.Add(p);
            }
        }

        if (Input.GetMouseButtonDown(0)) index++;
        if (Input.GetMouseButtonDown(1)) index--;
        if (targets.Count == 0) return;

        index = (index % targets.Count + targets.Count) % targets.Count;
        Transform t = targets[index] != null ? targets[index].transform : null;
        if (t == null) return;

        // Camera pitch isn't replicated, so follow the target's heading with a slight downward tilt.
        movement.cameraTransform.SetPositionAndRotation(
            t.position + Vector3.up * 0.6f,
            Quaternion.Euler(8f, t.eulerAngles.y, 0f));
    }
}
