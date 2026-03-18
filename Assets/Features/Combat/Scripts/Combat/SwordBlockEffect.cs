using UnityEngine;

/// <summary>
/// Enables the outline child renderer on the sword while the player is blocking.
/// Attach to the SwordOutline child GameObject (or any GameObject in the sword hierarchy).
/// Assign <see cref="outlineRenderer"/> in the Inspector to the MeshRenderer on the outline child.
/// </summary>
public class SwordBlockEffect : MonoBehaviour
{
    [Tooltip("MeshRenderer on the outline mesh child. Assign in the Inspector.")]
    public Renderer outlineRenderer;

    private PlayerController playerController;

    void Awake()
    {
        if (outlineRenderer != null)
            outlineRenderer.enabled = false;
    }

    void Update()
    {
        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();

        if (playerController == null || outlineRenderer == null) return;

        outlineRenderer.enabled = playerController.IsBlocking;
    }
}
