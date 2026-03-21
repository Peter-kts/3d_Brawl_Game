using UnityEngine;

/// <summary>
/// Enables the outline child renderer on the sword while the player is blocking,
/// and flashes the weapon renderer white during the block active window.
/// Attach to the SwordOutline child GameObject (or any GameObject in the sword hierarchy).
/// </summary>
public class SwordBlockEffect : MonoBehaviour
{
    [Tooltip("MeshRenderer on the outline mesh child. Assign in the Inspector.")]
    public Renderer outlineRenderer;

    [Header("Block Active Window Flash")]
    [Tooltip("Renderer on the main weapon mesh to tint white during the block active window. If null, no flash is applied.")]
    public Renderer weaponRenderer;

    [Tooltip("Color applied to the weapon during the block active window.")]
    public Color activeWindowColor = Color.white;

    private PlayerController playerController;
    private MaterialPropertyBlock propBlock;
    private bool wasWindowActive;

    // Property name used by both URP (_BaseColor) and Standard (_Color).
    private static readonly int PropBaseColor  = Shader.PropertyToID("_BaseColor");
    private static readonly int PropColor      = Shader.PropertyToID("_Color");

    void Awake()
    {
        if (outlineRenderer != null)
            outlineRenderer.enabled = false;
        propBlock = new MaterialPropertyBlock();
    }

    void OnDisable()
    {
        if (weaponRenderer != null && wasWindowActive)
        {
            propBlock.Clear();
            weaponRenderer.SetPropertyBlock(propBlock);
            wasWindowActive = false;
        }
        if (outlineRenderer != null)
            outlineRenderer.enabled = false;
    }

    void Update()
    {
        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();

        if (playerController == null) return;

        if (outlineRenderer != null)
            outlineRenderer.enabled = playerController.IsBlockWindowActive;

        bool windowActive = playerController.IsBlockWindowActive;
        if (weaponRenderer != null && windowActive != wasWindowActive)
        {
            if (windowActive)
            {
                weaponRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor(PropBaseColor, activeWindowColor);
                propBlock.SetColor(PropColor,     activeWindowColor);
                weaponRenderer.SetPropertyBlock(propBlock);
            }
            else
            {
                // Clear the override so the material's original color is restored.
                propBlock.Clear();
                weaponRenderer.SetPropertyBlock(propBlock);
            }
            wasWindowActive = windowActive;
        }
    }
}
