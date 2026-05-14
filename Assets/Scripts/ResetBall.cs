using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Autohand;

/// <summary>
/// Floating "reset" ball. A hand entering the trigger volume fires
/// <see cref="onBallActivated"/> (intended to open the confirmation panel).
/// Re-entry is locked out while the panel is open. Call <see cref="OnLevelCleared"/>
/// to start the gentle "pinch me" pulse (scale + opacity + emission), and
/// <see cref="OnPanelClosed"/> from either panel button to release the lockout.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ResetBall : MonoBehaviour
{
    [Header("Activation")]
    [Tooltip("Fires when an AutoHand Hand enters the trigger volume while the panel is closed. Wire your panel's Show() here.")]
    [SerializeField] private UnityEvent onBallActivated;

    [Header("Pulse Timing")]
    [Tooltip("Pulse rate in Hz. ~1Hz = one breath per second.")]
    [SerializeField] private float pulseFrequencyHz = 0.9f;
    [Tooltip("Seconds to ease into the pulse instead of snapping to it.")]
    [SerializeField] private float pulseRampSeconds = 0.5f;

    [Header("Pulse Scale")]
    [Tooltip("Scale multiplier at the bottom of the sine.")]
    [SerializeField] private float pulseMinScale = 1.0f;
    [Tooltip("Scale multiplier at the top of the sine.")]
    [SerializeField] private float pulseMaxScale = 1.25f;

    [Header("Pulse Material (Built-in Pipeline)")]
    [Tooltip("Renderer used for opacity / emission pulse. Auto-assigned via GetComponentInChildren on Awake if left null.")]
    [SerializeField] private Renderer ballRenderer;
    [Tooltip("Alpha at the bottom of the pulse. Material must be transparent for this to be visible.")]
    [Range(0f, 1f)] [SerializeField] private float pulseMinAlpha = 0.57f;
    [Tooltip("Alpha at the top of the pulse.")]
    [Range(0f, 1f)] [SerializeField] private float pulseMaxAlpha = 1.0f;
    [Tooltip("Emission color added on top of the material's base emission at the peak of the pulse. Set to black to disable. Requires a shader with an _EmissionColor property (Standard, Legacy/Self-Illumin/*).")]
    [ColorUsage(true, true)] [SerializeField] private Color pulseEmissionPeak = new Color(1f, 0.7f, 0.3f, 1f);

    private const string COLOR_PROP = "_Color";
    private const string EMISSION_PROP = "_EmissionColor";

    private Vector3 _baseScale;
    private bool _panelOpen;
    private bool _isPulsing;
    private Coroutine _pulseCo;

    private Material _matInstance;
    private Color _baseColor;
    private Color _baseEmissionColor;
    private bool _hasColorProperty;
    private bool _hasEmissionProperty;

    public bool IsPanelOpen => _panelOpen;

    private void Awake()
    {
        _baseScale = transform.localScale;

        var col = GetComponent<Collider>();
        if (!col.isTrigger)
            Debug.LogWarning($"{nameof(ResetBall)} on '{name}': collider should be marked isTrigger.", this);

        if (ballRenderer == null) ballRenderer = GetComponentInChildren<Renderer>();
        if (ballRenderer != null)
        {
            // .material creates an instance unique to this renderer — safe to mutate
            _matInstance = ballRenderer.material;
            _hasColorProperty = _matInstance.HasProperty(COLOR_PROP);
            _hasEmissionProperty = _matInstance.HasProperty(EMISSION_PROP);
            if (_hasColorProperty) _baseColor = _matInstance.GetColor(COLOR_PROP);
            if (_hasEmissionProperty)
            {
                _baseEmissionColor = _matInstance.GetColor(EMISSION_PROP);
                // Make sure emission actually renders on shaders that gate it behind a keyword (Standard).
                _matInstance.EnableKeyword("_EMISSION");
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_panelOpen) return;

        var hand = other.GetComponentInParent<Hand>();
        if (hand == null) return;

        _panelOpen = true;
        onBallActivated?.Invoke();
    }

    /// <summary>
    /// Call from BOTH panel buttons (Restart and Cancel) and from the auto-close timer
    /// to release the re-entry lockout.
    /// </summary>
    public void OnPanelClosed()
    {
        _panelOpen = false;
    }

    /// <summary>
    /// Call when all desk fires have been extinguished to start the "pinch me" pulse.
    /// </summary>
    public void OnLevelCleared()
    {
        if (_isPulsing) return;
        _isPulsing = true;
        _pulseCo = StartCoroutine(PulseRoutine());
    }

    /// <summary>
    /// Returns the ball to baseline scale and material state, stops the pulse.
    /// Mostly useful for editor testing — full scene reload handles this at runtime.
    /// </summary>
    public void ResetVisualState()
    {
        if (_pulseCo != null) StopCoroutine(_pulseCo);
        _pulseCo = null;
        _isPulsing = false;
        transform.localScale = _baseScale;
        RestoreMaterial();
    }

    private IEnumerator PulseRoutine()
    {
        float t = 0f;
        float ramp = 0f;
        const float TWO_PI = Mathf.PI * 2f;

        while (_isPulsing)
        {
            t += Time.deltaTime;

            ramp = pulseRampSeconds > 0f
                ? Mathf.Min(1f, ramp + Time.deltaTime / pulseRampSeconds)
                : 1f;

            float sine01 = (Mathf.Sin(t * pulseFrequencyHz * TWO_PI) + 1f) * 0.5f;

            // Scale
            float scaleTarget = Mathf.Lerp(pulseMinScale, pulseMaxScale, sine01);
            float scale = Mathf.Lerp(1f, scaleTarget, ramp);
            transform.localScale = _baseScale * scale;

            // Material (opacity + emission)
            if (_matInstance != null)
            {
                if (_hasColorProperty)
                {
                    Color c = _baseColor;
                    float pulsedAlpha = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, sine01);
                    c.a = Mathf.Lerp(_baseColor.a, pulsedAlpha, ramp);
                    _matInstance.SetColor(COLOR_PROP, c);
                }
                if (_hasEmissionProperty)
                {
                    Color contribution = pulseEmissionPeak * (sine01 * ramp);
                    _matInstance.SetColor(EMISSION_PROP, _baseEmissionColor + contribution);
                }
            }

            yield return null;
        }

        // Safety restore in case _isPulsing was toggled externally mid-loop
        transform.localScale = _baseScale;
        RestoreMaterial();
    }

    private void RestoreMaterial()
    {
        if (_matInstance == null) return;
        if (_hasColorProperty) _matInstance.SetColor(COLOR_PROP, _baseColor);
        if (_hasEmissionProperty) _matInstance.SetColor(EMISSION_PROP, _baseEmissionColor);
    }

    private void OnDestroy()
    {
        // The .material call in Awake created an instance — clean it up
        if (_matInstance != null) Destroy(_matInstance);
    }
}
