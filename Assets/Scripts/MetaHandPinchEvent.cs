using UnityEngine;
using UnityEngine.Events;

namespace Autohand {
    /// <summary>
    /// Fires a UnityEvent on a single finger-touch gesture (one-shot at touch start).
    /// Configure for thumb+index in the inspector to use as a standard pinch event.
    /// Drop on the same GameObject as a MetaHandFingerGestureTracker, or assign one.
    /// Includes a cooldown to debounce contact jitter at the touch boundary —
    /// common with rookie hand tracking.
    /// </summary>
    public class MetaHandPinchEvent : MonoBehaviour {
        [Header("Tracker")]
        [Tooltip("Hand gesture tracker to listen to. Assign per-hand.")]
        [SerializeField] private MetaHandFingerGestureTracker gestureTracker;

        [Header("Gesture")]
        [Tooltip("Primary finger of the touch. Set to Thumb for a standard pinch.")]
        [SerializeField] private FingerEnum finger1;
        [Tooltip("Secondary finger of the touch. Set to Index for a standard pinch.")]
        [SerializeField] private FingerEnum finger2;

        [Header("Behavior")]
        [Tooltip("Minimum seconds between fires. Debounces fingertip jitter at the contact boundary. Keep short for responsive feel.")]
        [SerializeField] private float cooldownSeconds = 0.4f;

        [Header("Event")]
        [Tooltip("Invoked once when the configured finger touch begins.")]
        public UnityEvent onPinch;

        private float _lastFireTime = -999f;

        /// <summary>
        /// Runtime gate. Set true to temporarily ignore pinches (e.g. during a panel transition).
        /// </summary>
        public bool Disabled { get; set; }

        private void OnEnable() {
            if (gestureTracker == null) {
                Debug.LogWarning($"{nameof(MetaHandPinchEvent)} on '{name}' has no gestureTracker assigned.", this);
                return;
            }
            gestureTracker.OnFingerTouchStart += HandleFingerTouchStart;
        }

        private void OnDisable() {
            if (gestureTracker != null)
                gestureTracker.OnFingerTouchStart -= HandleFingerTouchStart;
        }

        private void HandleFingerTouchStart(MetaXRAutoHandTracking hand, MetaHandFingerGestureTracker tracker, FingerTouchEventArgs e) {
            if (Disabled) return;
            if (e.finger1 != finger1 || e.finger2 != finger2) return;
            if (Time.time - _lastFireTime < cooldownSeconds) return;

            _lastFireTime = Time.time;
            onPinch?.Invoke();
        }
    }
}
