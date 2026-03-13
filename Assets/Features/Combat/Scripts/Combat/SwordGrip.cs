using UnityEngine;

/// <summary>
/// Manages weapon grip presets. Place on the same GameObject as the Animator
/// so animation events can call SetGrip(int) to switch between grip positions.
/// Define presets in the Inspector with an ID, local position, and local rotation.
/// </summary>
public class SwordGrip : MonoBehaviour
{
    [System.Serializable]
    public struct GripPreset
    {
        [Tooltip("ID referenced by animation events via SetGrip(int).")]
        public int id;
        public string label;
        public Vector3 localPosition;
        public Vector3 localRotation;
        [Tooltip("If enabled, this grip also applies local scale.")]
        public bool applyScale;
        public Vector3 localScale;
    }

    [Tooltip("The weapon transform to reposition (should be a child of a hand bone).")]
    public Transform weaponTransform;

    [Tooltip("Define grip presets here. Each has an ID, position offset, and rotation.")]
    public GripPreset[] grips;

    [Tooltip("Normalized transition speed (0 = instant, 1 = fastest smooth blend).")]
    [Range(0f, 1f)]
    public float transitionSpeed = 0f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetScale;
    private bool transitioning;

    void Awake()
    {
        if (weaponTransform != null)
        {
            targetPosition = weaponTransform.localPosition;
            targetRotation = weaponTransform.localRotation;
            targetScale = weaponTransform.localScale;
        }
    }

    void LateUpdate()
    {
        if (weaponTransform == null || !transitioning) return;

        if (transitionSpeed <= 0f)
        {
            weaponTransform.localPosition = targetPosition;
            weaponTransform.localRotation = targetRotation;
            weaponTransform.localScale = targetScale;
            transitioning = false;
            return;
        }

        float normalizedSpeed = Mathf.Clamp01(transitionSpeed);
        float t = 1f - Mathf.Pow(1f - normalizedSpeed, Time.deltaTime * 60f);
        weaponTransform.localPosition = Vector3.Lerp(weaponTransform.localPosition, targetPosition, t);
        weaponTransform.localRotation = Quaternion.Slerp(weaponTransform.localRotation, targetRotation, t);
        weaponTransform.localScale = Vector3.Lerp(weaponTransform.localScale, targetScale, t);

        if (Vector3.Distance(weaponTransform.localPosition, targetPosition) < 0.001f &&
            Vector3.Distance(weaponTransform.localScale, targetScale) < 0.001f)
        {
            weaponTransform.localPosition = targetPosition;
            weaponTransform.localRotation = targetRotation;
            weaponTransform.localScale = targetScale;
            transitioning = false;
        }
    }

    /// <summary>
    /// Called from animation events. Finds the preset matching the given ID
    /// and applies its position/rotation to the weapon transform.
    /// </summary>
    public void SetGrip(int gripId)
    {
        if (weaponTransform == null || grips == null) return;

        for (int i = 0; i < grips.Length; i++)
        {
            if (grips[i].id != gripId) continue;
            targetPosition = grips[i].localPosition;
            targetRotation = Quaternion.Euler(grips[i].localRotation);
            targetScale = grips[i].applyScale ? grips[i].localScale : weaponTransform.localScale;
            transitioning = true;
            return;
        }
    }
}
