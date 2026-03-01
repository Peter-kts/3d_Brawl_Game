using UnityEngine;

/// <summary>Add to the same GameObject as the main Camera. Call RequestShake from anywhere to trigger a screen shake (e.g. when damage is applied).</summary>
[DefaultExecutionOrder(100)]
public class ScreenShake : MonoBehaviour
{
    [Tooltip("Default intensity when RequestShake() is called with no args.")]
    public float defaultIntensity = 0.08f;
    [Tooltip("Default duration in seconds.")]
    public float defaultDuration = 0.12f;

    private Vector3 _lastOffset;
    private float _shakeUntil;
    private float _currentIntensity;

    /// <summary>Request a screen shake. Uses default intensity and duration if not specified. No-op if no ScreenShake on main camera.</summary>
    public static void RequestShake(float intensity = -1f, float duration = -1f)
    {
        if (Camera.main == null) return;
        var shaker = Camera.main.GetComponent<ScreenShake>();
        if (shaker == null) return;
        shaker.Shake(intensity >= 0 ? intensity : shaker.defaultIntensity, duration >= 0 ? duration : shaker.defaultDuration);
    }

    public void Shake(float intensity, float duration)
    {
        _currentIntensity = intensity;
        _shakeUntil = Mathf.Max(_shakeUntil, Time.time + duration);
    }

    void LateUpdate()
    {
        transform.position -= _lastOffset;
        _lastOffset = Vector3.zero;

        if (Time.time < _shakeUntil)
        {
            float t = (_shakeUntil - Time.time) / 0.12f;
            float strength = _currentIntensity * Mathf.Clamp01(t);
            _lastOffset = UnityEngine.Random.insideUnitSphere * strength;
            transform.position += _lastOffset;
        }
    }
}
