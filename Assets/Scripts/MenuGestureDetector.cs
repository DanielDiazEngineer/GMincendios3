using UnityEngine;
using UnityEngine.Events;
using Autohand;

/// <summary>
/// Menu gesture: Index + Middle fingers both pinching Thumb simultaneously,
/// held briefly. Distinct from Meta system gesture (index+thumb only)
/// and existing custom quit gesture (middle+thumb, 2s, right hand).
///
/// Recommended setup:
///   - Attach to a GameObject that also has (or references) a MetaHandFingerGestureTracker
///   - Set isLeftHandOnly = true to further avoid system gesture conflicts
///   - Wire OnMenuGestureTriggered in the Inspector
/// </summary>
public class MenuGestureDetector : MonoBehaviour
{
    [Header("References")]
    public MetaHandFingerGestureTracker fingerGestureTracker;

    [Header("Settings")]
    [Tooltip("Seconds both fingers must pinch before triggering. 0.3–0.5 feels natural.")]
    public float holdDuration = 0.35f;

    [Tooltip("Restrict to left hand only (recommended — avoids system gesture zone)")]
    public bool isLeftHandOnly = true;

    [Header("Events")]
    public UnityEvent OnMenuGestureTriggered;

    // ── internal state ──────────────────────────────────────────
    bool _indexOnThumb = false;
    bool _middleOnThumb = false;
    bool _gestureActive = false;
    bool _hasTriggered = false;
    float _gestureStartTime;

    // ── lifecycle ───────────────────────────────────────────────
    void OnEnable()
    {
        if (fingerGestureTracker == null)
        {
            Debug.LogError("[MenuGesture] fingerGestureTracker is not assigned.", this);
            enabled = false;
            return;
        }
        fingerGestureTracker.OnFingerTouchStart += HandleTouchStart;
        fingerGestureTracker.OnFingerTouchStop += HandleTouchStop;
    }

    void OnDisable()
    {
        if (fingerGestureTracker == null) return;
        fingerGestureTracker.OnFingerTouchStart -= HandleTouchStart;
        fingerGestureTracker.OnFingerTouchStop -= HandleTouchStop;
    }

    // ── event handlers ──────────────────────────────────────────
    void HandleTouchStart(MetaXRAutoHandTracking hand,
                          MetaHandFingerGestureTracker tracker,
                          FingerTouchEventArgs e)
    {
        // Optional: ignore right hand entirely
        if (isLeftHandOnly && !hand.hand.left) return;

        if (IsIndexThumb(e)) _indexOnThumb = true;
        if (IsMiddleThumb(e)) _middleOnThumb = true;

        EvaluateGestureState();
    }

    void HandleTouchStop(MetaXRAutoHandTracking hand,
                         MetaHandFingerGestureTracker tracker,
                         FingerTouchEventArgs e)
    {
        if (isLeftHandOnly && !hand.hand.left) return;

        if (IsIndexThumb(e)) _indexOnThumb = false;
        if (IsMiddleThumb(e)) _middleOnThumb = false;

        EvaluateGestureState();
    }

    // ── core state machine ──────────────────────────────────────
    void EvaluateGestureState()
    {
        bool bothPinching = _indexOnThumb && _middleOnThumb;

        if (bothPinching && !_gestureActive)
        {
            _gestureActive = true;
            _hasTriggered = false;
            _gestureStartTime = Time.time;
            Debug.Log("[MenuGesture] Gesture started — hold to confirm.");
        }
        else if (!bothPinching && _gestureActive)
        {
            _gestureActive = false;

            if (!_hasTriggered)
                Debug.Log("[MenuGesture] Released before hold threshold — cancelled.");
        }
    }

    void Update()
    {
        if (!_gestureActive || _hasTriggered) return;

        if (Time.time - _gestureStartTime >= holdDuration)
        {
            _hasTriggered = true;
            Debug.Log("[MenuGesture] ✓ Menu gesture confirmed!");
            OnMenuGestureTriggered?.Invoke();
        }
    }

    // ── pair helpers (order-independent) ───────────────────────
    static bool IsIndexThumb(FingerTouchEventArgs e) =>
        (e.finger1 == FingerEnum.index && e.finger2 == FingerEnum.thumb) ||
        (e.finger1 == FingerEnum.thumb && e.finger2 == FingerEnum.index);

    static bool IsMiddleThumb(FingerTouchEventArgs e) =>
        (e.finger1 == FingerEnum.middle && e.finger2 == FingerEnum.thumb) ||
        (e.finger1 == FingerEnum.thumb && e.finger2 == FingerEnum.middle);
}