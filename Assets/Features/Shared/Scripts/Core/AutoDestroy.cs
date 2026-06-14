using UnityEngine;

/// <summary>
/// Destroys the GameObject after a set lifetime.
/// If lifetime is left at -1 and a ParticleSystem is present, the duration
/// is read automatically (main.duration + max startLifetime), so VFX prefabs
/// clean themselves up after playing once without any manual configuration.
/// </summary>
public class AutoDestroy : MonoBehaviour
{
    [Tooltip("Time in seconds before the object destroys itself. " +
             "-1 = auto-detect from ParticleSystem on this GameObject.")]
    public float lifetime = -1f;

    void Start()
    {
        float duration = lifetime;

        if (duration < 0f)
        {
            var ps = GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                duration = main.duration + main.startLifetime.constantMax;
            }
        }

        if (duration >= 0f)
            Destroy(gameObject, duration);
    }
}
