using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using Autohand;

/// <summary>
/// Menu gesture: Index + Middle fingers both pinching Thumb simultaneously,
/// held briefly. Distinct from Meta system gesture (index+thumb only)
/// and existing custom quit gesture (middle+thumb, 2s, right hand).
///
/// Recommended setup:
///   - Attach to a GameObject that also has (or references) a MetaHandFingerGestureTracker
///   - Set restrictToOneHand = true to further avoid system gesture conflicts
///   - menuHandIsLeft picks which hand (DominantHandSetting keeps it on the
///     non-dominant hand so it never fights the extinguisher trigger)
///   - Wire OnMenuGestureTriggered in the Inspector
/// </summary>
public class MenuGestureDetector : MonoBehaviour
{
    [Header("References")]
    public MetaHandFingerGestureTracker fingerGestureTracker;

    [Header("Settings")]
    [Tooltip("Seconds both fingers must pinch before triggering. 0.3–0.5 feels natural.")]
    public float holdDuration = 0.35f;

    [Tooltip("Only listen to one hand (recommended — avoids system gesture zone)")]
    [FormerlySerializedAs("isLeftHandOnly")]
    public bool restrictToOneHand = true;

    [Tooltip("Which hand opens the menu when restrictToOneHand is on. " +
             "Should be the non-dominant (nozzle) hand.")]
    public bool menuHandIsLeft = true;

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
        // Optional: ignore the other hand entirely
        if (!IsMenuHand(hand)) return;

        if (IsIndexThumb(e)) _indexOnThumb = true;
        if (IsMiddleThumb(e)) _middleOnThumb = true;

        EvaluateGestureState();
    }

    void HandleTouchStop(MetaXRAutoHandTracking hand,
                         MetaHandFingerGestureTracker tracker,
                         FingerTouchEventArgs e)
    {
        if (!IsMenuHand(hand)) return;

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

    // ── hand selection ──────────────────────────────────────────
    /// <summary>Move the menu gesture to the other hand. Clears any half-made pinch.</summary>
    public void SetMenuHandLeft(bool left)
    {
        if (menuHandIsLeft == left) return;

        menuHandIsLeft = left;
        _indexOnThumb = false;
        _middleOnThumb = false;
        _gestureActive = false;
        _hasTriggered = false;
        Debug.Log($"[MenuGesture] Menu hand set to: {(left ? "Left" : "Right")}");
    }

    bool IsMenuHand(MetaXRAutoHandTracking hand) =>
        !restrictToOneHand || hand.hand.left == menuHandIsLeft;

    // ── pair helpers (order-independent) ───────────────────────
    static bool IsIndexThumb(FingerTouchEventArgs e) =>
        (e.finger1 == FingerEnum.index && e.finger2 == FingerEnum.thumb) ||
        (e.finger1 == FingerEnum.thumb && e.finger2 == FingerEnum.index);

    static bool IsMiddleThumb(FingerTouchEventArgs e) =>
        (e.finger1 == FingerEnum.middle && e.finger2 == FingerEnum.thumb) ||
        (e.finger1 == FingerEnum.thumb && e.finger2 == FingerEnum.middle);
}