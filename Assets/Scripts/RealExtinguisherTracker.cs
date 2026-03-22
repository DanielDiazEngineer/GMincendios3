using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Tracks a physical extinguisher via a strapped Quest controller.
    /// Spray trigger = AutoHand right-hand grip (4 fingers closed) + hand proximity to controller.
    /// Index trigger is NOT used.
    ///
    /// ── DROPOUT FIX ────────────────────────────────────────────────────────
    /// Quest 3 simultaneous H&C mode requires the cameras to visually confirm
    /// the hand on the controller before reporting its pose. During dropout,
    /// OVRInput returns exactly Vector3.zero. A pose cache holds the last valid
    /// position so the object doesn't teleport to the room origin.
    ///
    /// ── OVR MANAGER (confirmed correct in your inspector) ──────────────────
    ///   Hand Tracking Support              : Controllers And Hands
    ///   Simultaneous H&C Enabled           : ✓
    ///   Launch Simultaneous H&C On Startup : ✓
    ///   Controller Driven Hand Poses Type  : None
    ///
    /// ── OVR CONTROLLER HELPER FIX ──────────────────────────────────────────
    ///   Controller field was "None" — set it to RTouch.
    ///   This only affects the visual mesh, not our pose data.
    ///
    /// ── HIERARCHY ──────────────────────────────────────────────────────────
    ///   [Anywhere OUTSIDE OVRCameraRig hand anchors]
    ///   ExtinguisherRoot
    ///     ├─ RealExtinguisherTracker
    ///     ├─ ExtinguisherBehavior
    ///     └─ NozzleOrigin   ← assign as nozzleAttachment
    ///          ├─ SprayParticles
    ///          └─ SprayLine
    /// </summary>
    public class RealExtinguisherTracker : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════
        // INSPECTOR
        // ═══════════════════════════════════════════════════════════════

        [Header("OVR References")]
        [Tooltip("OVRCameraRig > TrackingSpace. Auto-found if empty.")]
        [SerializeField] private Transform trackingSpace;

        [Tooltip("The controller strapped to the extinguisher.")]
        [SerializeField] private OVRInput.Controller controllerHand = OVRInput.Controller.RTouch;

        [Header("Nozzle Attachment")]
        [Tooltip("Child transform driven to controller pose + offset every frame.")]
        [SerializeField] private Transform nozzleAttachment;

        [Tooltip("Offset from controller grip origin to real nozzle tip (controller-local).")]
        [SerializeField] private Vector3 nozzleLocalOffset = new Vector3(0f, -0.05f, 0.15f);

        [Tooltip("Rotation offset so nozzleAttachment.forward = real spray direction.")]
        [SerializeField] private Vector3 nozzleLocalRotationEuler = Vector3.zero;

        [Header("Pose Cache  (survives simultaneous H&C dropout)")]
        [Tooltip("Hold last valid pose when controller goes dark instead of snapping to origin.")]
        [SerializeField] private bool usePoseCache = true;

        [Tooltip("Seconds before cached pose is considered too stale. 0 = keep forever.")]
        [SerializeField] private float poseCacheMaxAge = 10f;

        [Tooltip("Disable all dropout protection. Only use if controller is always visible.")]
        [SerializeField] private bool alwaysActive = false;

        [Header("Spray Trigger — AutoHand Grab + Proximity")]
        [Tooltip("AutoHand Hand on the right hand. Its gripAmount drives the spray trigger.")]
        //  [SerializeField] private Autohand.Hand rightHand;

        [SerializeField] private OVRHand ovrRightHand; // drag in OVRCameraRig > RightHandAnchor > OVRHandPrefab

        [Tooltip("gripAmount (0–1) required to count as grabbing. ~0.7 = 4 fingers mostly closed.")]
        [SerializeField] private float grabThreshold = 0.7f;

        [Tooltip("How close (meters) the hand must be to the controller before grab triggers spray. " +
                 "Prevents firing from across the room. 0.15–0.25m is a comfortable grip radius.")]
        [SerializeField] private float grabProximityRadius = 0.2f;

        [Header("Pin Removal")]
        [Tooltip("Button to remove the pin (simulates pulling the real safety pin).")]
        [SerializeField] private OVRInput.Button pinRemoveButton = OVRInput.Button.One;
        [SerializeField] private bool requirePinRemoval = true;

        [Header("Linked Extinguisher Behavior")]
        [SerializeField] private ExtinguisherBehavior extinguisher;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private bool logDropouts = true;

        [Header("Events")]
        public UnityEvent OnControllerActive;
        public UnityEvent OnControllerDropout;


        // ═══════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════

        private Vector3 _cachedNozzlePos;
        private Quaternion _cachedNozzleRot = Quaternion.identity;
        private float _cacheAge = 0f;
        private bool _hasCachedPose = false;
        private bool _wasActive = false;
        private bool _wasHandNearby = false;
        private bool _pinRemoved = false;
        private Quaternion _nozzleLocalRotOffset;

        public bool IsControllerActive { get; private set; }
        public bool HasValidPose =>
            _hasCachedPose && (poseCacheMaxAge <= 0f || _cacheAge < poseCacheMaxAge);

        // ═══════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        private void Awake()
        {
            _nozzleLocalRotOffset = Quaternion.Euler(nozzleLocalRotationEuler);

            if (trackingSpace == null)
            {
                var rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null)
                {
                    trackingSpace = rig.trackingSpace;
                    Debug.Log("[RealExtTracker] Auto-found OVRCameraRig.trackingSpace.");
                }
                else
                    Debug.LogError("[RealExtTracker] No OVRCameraRig found. Assign trackingSpace manually.");
            }

            if (nozzleAttachment == null)
            {
                var go = new GameObject("NozzleAttachment_Auto");
                go.transform.SetParent(transform, false);
                nozzleAttachment = go.transform;
                Debug.LogWarning("[RealExtTracker] nozzleAttachment not assigned — auto-created child.");
            }

            if (extinguisher == null)
                extinguisher = GetComponent<ExtinguisherBehavior>();

            if (!requirePinRemoval)
                _pinRemoved = true;
        }

        private void OnEnable()
        {
            if (extinguisher != null)
            {
                extinguisher._isGrabbed = true;
                extinguisher._isPinRemoved = _pinRemoved;
            }
        }

        private void Update()
        {
            UpdateControllerPose();
            UpdateInputState();
        }

        // ═══════════════════════════════════════════════════════════════
        // POSE TRACKING
        // ═══════════════════════════════════════════════════════════════

        private void UpdateControllerPose()
        {
            if (trackingSpace == null) return;

            bool poseValid = false;
            bool connected = alwaysActive || OVRInput.IsControllerConnected(controllerHand);

            if (connected)
            {
                Vector3 localPos = OVRInput.GetLocalControllerPosition(controllerHand);
                Quaternion localRot = OVRInput.GetLocalControllerRotation(controllerHand);

                // Zero-guard: OS returns exactly (0,0,0) during H&C dropout.
                // A real room-scale position is never at the exact floor-center origin.
                bool positionIsReal = alwaysActive || (localPos.sqrMagnitude > 0.0001f);

                if (positionIsReal)
                {
                    Vector3 worldPos = trackingSpace.TransformPoint(localPos);
                    Quaternion worldRot = trackingSpace.rotation * localRot;

                    _cachedNozzlePos = worldPos + worldRot * nozzleLocalOffset;
                    _cachedNozzleRot = worldRot * _nozzleLocalRotOffset;

                    _hasCachedPose = true;
                    _cacheAge = 0f;
                    poseValid = true;
                }
            }

            if (!poseValid)
                _cacheAge += Time.deltaTime;

            // Edge events
            IsControllerActive = poseValid;
            if (poseValid != _wasActive)
            {
                if (poseValid)
                {
                    if (logDropouts) Debug.Log("[RealExtTracker] Controller pose ACTIVE.");
                    OnControllerActive?.Invoke();
                    if (extinguisher != null) extinguisher._isGrabbed = true;
                }
                else
                {
                    if (logDropouts) Debug.LogWarning(
                        $"[RealExtTracker] Controller DROPOUT. " +
                        $"Holding cached pose: {usePoseCache && HasValidPose} ({_cacheAge:F1}s)");
                    OnControllerDropout?.Invoke();
                }
                _wasActive = poseValid;
            }

            // Apply pose (live or cached)
            bool effectivelyTracked = poseValid || (usePoseCache && HasValidPose);
            if (effectivelyTracked)
            {
                nozzleAttachment.SetPositionAndRotation(_cachedNozzlePos, _cachedNozzleRot);
                if (extinguisher?.nozzleOrigin != null)
                    extinguisher.nozzleOrigin.SetPositionAndRotation(_cachedNozzlePos, _cachedNozzleRot);
            }

            if (!effectivelyTracked && extinguisher != null)
                extinguisher._isTriggerHeld = false;
        }

        // ═══════════════════════════════════════════════════════════════
        // INPUT — AutoHand grip + proximity (NO index trigger)
        // ═══════════════════════════════════════════════════════════════

        private void UpdateInputState()
        {
            if (extinguisher == null) return;

            // Pin removal — OVR button, fires once
            if (!_pinRemoved && requirePinRemoval)
            {
                if (OVRInput.GetDown(pinRemoveButton, controllerHand))
                {
                    _pinRemoved = true;
                    extinguisher._isPinRemoved = true;
                    Debug.Log("[RealExtTracker] Pin removed!");
                }
            }

            // Spray trigger — requires BOTH conditions:
            //   1. Right hand is within grabProximityRadius of the controller
            //   2. Hand's gripAmount >= grabThreshold (4 fingers closed)
            if (IsControllerActive || alwaysActive)
            {
                bool handNearby = IsHandNearController();
                //bool handGrabbing = rightHand != null && rightHand.gripAmount >= grabThreshold;
                bool handGrabbing = GetHandClosedness() >= grabThreshold;
                Debug.Log("$$$$closed hand");
                extinguisher._isTriggerHeld = handNearby && handGrabbing;
            }
            else if (!HasValidPose)
            {
                // Cache fully expired — cut spray
                extinguisher._isTriggerHeld = false;
            }
            // Cache valid but pose dropped: hold last trigger state (brief occlusion mid-spray)
        }

        /// <summary>
        /// Returns true when the right hand palm is within grabProximityRadius of
        /// the controller's current world position (grip origin, not nozzle tip).
        /// </summary>
        /// 
        /// 
        private float GetHandClosedness()
        {
            if (ovrRightHand == null) return 0f;
            if (!ovrRightHand.IsTracked) return 0f;

            float index = ovrRightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            float middle = ovrRightHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
            float ring = ovrRightHand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
            //Debug.Log($"{index}  {middle}  {ring}");
            return (index + middle + ring) / 3f;
        }

        private bool IsHandNearController()
        {
            if (ovrRightHand == null || trackingSpace == null) return false;

            // Use the raw controller position for distance — hand wraps around
            // the body of the extinguisher, not the nozzle tip.
            Vector3 controllerWorldPos = trackingSpace.TransformPoint(
                OVRInput.GetLocalControllerPosition(controllerHand));

            float dist = Vector3.Distance(ovrRightHand.transform.position, controllerWorldPos);
            bool nearby = dist <= grabProximityRadius;

            // Log only on state change to avoid spam
            if (logDropouts && nearby != _wasHandNearby)
            {
                Debug.Log($"[RealExtTracker] Hand {(nearby ? "ENTERED" : "LEFT")} grab zone " +
                          $"(dist: {dist:F3}m, radius: {grabProximityRadius}m)");
                _wasHandNearby = nearby;
            }

            return nearby;
        }

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════

        public void ForceRemovePin()
        {
            _pinRemoved = true;
            if (extinguisher != null) extinguisher._isPinRemoved = true;
        }

        public void ResetPin()
        {
            _pinRemoved = !requirePinRemoval;
            if (extinguisher != null) extinguisher._isPinRemoved = _pinRemoved;
        }

        public void RefreshNozzleRotationOffset()
        {
            _nozzleLocalRotOffset = Quaternion.Euler(nozzleLocalRotationEuler);
        }

        // ═══════════════════════════════════════════════════════════════
        // GIZMOS
        // ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos) return;

            if (Application.isPlaying)
            {
                // Nozzle: green = live | yellow = cached | red = lost
                Gizmos.color = IsControllerActive ? Color.green
                             : HasValidPose ? Color.yellow
                             : Color.red;
                Gizmos.DrawSphere(nozzleAttachment.position, 0.018f);
                Gizmos.DrawRay(nozzleAttachment.position, nozzleAttachment.forward * 0.12f);

                // Proximity sphere around controller grip origin
                if (IsControllerActive && trackingSpace != null)
                {
                    Vector3 ctrlPos = trackingSpace.TransformPoint(
                        OVRInput.GetLocalControllerPosition(controllerHand));

                    Gizmos.color = _wasHandNearby
                        ? new Color(0f, 1f, 0f, 0.15f)
                        : new Color(1f, 1f, 0f, 0.08f);
                    Gizmos.DrawWireSphere(ctrlPos, grabProximityRadius);
                }
            }
            else
            {
                // Edit-mode: nozzle offset preview
                Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
                Gizmos.DrawSphere(transform.TransformPoint(nozzleLocalOffset), 0.018f);

                // Proximity sphere preview at object root
                Gizmos.color = new Color(1f, 1f, 0f, 0.08f);
                Gizmos.DrawWireSphere(transform.position, grabProximityRadius);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos || !Application.isPlaying) return;
            string status = IsControllerActive ? "LIVE"
                          : HasValidPose ? $"CACHED ({_cacheAge:F1}s)"
                          : "LOST";
            UnityEditor.Handles.Label(nozzleAttachment.position + Vector3.up * 0.06f, status);
        }
#endif
    }
}
