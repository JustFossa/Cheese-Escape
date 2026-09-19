using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A table survivors can hide under. Added at runtime to every child of the scene's "Tables"
/// object, so no scene or prefab wiring. Occupancy is not stored here: the server owns it as
/// PlayerData.hiddenSpot, and this only asks (RoundManager.UseHidingSpotServerRpc).
/// </summary>
public class HidingSpot : MonoBehaviour, IInteractable, IConditionalPrompt
{
    private static readonly List<HidingSpot> spots = new List<HidingSpot>();

    public static int Count => spots.Count;

    public static bool TryGet(int index, out HidingSpot spot)
    {
        spot = index >= 0 && index < spots.Count ? spots[index] : null;
        return spot != null;
    }

    // ponytail: the index is sibling order under "Tables" - identical on every peer because it is
    // the same scene asset. Give the tables real NetworkObjects if spots ever spawn dynamically.
    public static void RegisterAll()
    {
        spots.Clear();

        GameObject root = GameObject.Find("Tables");
        if (root == null)
        {
            Debug.LogWarning("HidingSpot: no 'Tables' object in the scene - there is nowhere to hide.");
            return;
        }

        foreach (Transform table in root.transform)
        {
            HidingSpot spot = table.GetComponent<HidingSpot>();
            if (spot == null) spot = table.gameObject.AddComponent<HidingSpot>();
            spot.index = spots.Count;
            spots.Add(spot);
        }
    }

    // A spot on top of every table - the extra candidates for the shuffled cheese.
    public static IEnumerable<Vector3> TableTops()
    {
        foreach (HidingSpot s in spots) yield return s.Top;
    }

    private int index;
    private Bounds? cached;

    // Measured from the renderers rather than the pivot, so it works however the prefab is authored.
    private Bounds WorldBounds
    {
        get
        {
            if (cached.HasValue) return cached.Value;

            Bounds b = new Bounds(transform.position, Vector3.one);
            bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any)
            {
                foreach (Collider c in GetComponentsInChildren<Collider>())
                {
                    if (!any) { b = c.bounds; any = true; }
                    else b.Encapsulate(c.bounds);
                }
            }

            cached = b;
            return b;
        }
    }

    public Vector3 Top => new Vector3(WorldBounds.center.x, WorldBounds.max.y + 0.3f, WorldBounds.center.z);
    public float CameraWorldY => WorldBounds.min.y + 0.45f;
    public Vector3 HidePosition(float y) => new Vector3(WorldBounds.center.x, y, WorldBounds.center.z);

    private void Update()
    {
        // Leaving is a fresh E press, so the hold that got you in can't immediately kick you out.
        PlayerData me = PlayerData.Local;
        if (me != null && me.hiddenSpot.Value == index && Input.GetKeyDown(KeyCode.E) && RoundManager.Instance != null)
        {
            RoundManager.Instance.LeaveHidingSpotServerRpc();
        }
    }

    public float InteractionDuration => 1.5f;

    public string InteractionPrompt
    {
        get
        {
            PlayerData me = PlayerData.Local;
            return me != null && me.IsHunter ? "Hold to check under the table" : "Hold to hide";
        }
    }

    public bool ShowPrompt
    {
        get
        {
            PlayerData me = PlayerData.Local;
            if (me == null || RoundManager.Instance == null || RoundManager.Instance.state.Value != RoundState.Playing) return false;
            return me.IsHunter || me.CanAct;
        }
    }

    public bool CanInteract => ShowPrompt;

    public void OnInteractionStart() { }
    public void OnInteractionProgress(float progress) { }
    public void OnInteractionCancel() { }

    public void OnInteractionComplete()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.UseHidingSpotServerRpc(index);
    }
}
