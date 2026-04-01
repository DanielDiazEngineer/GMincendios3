using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Hand-tracking extinguisher tracker. No controller required.
    /// User holds the real physical extinguisher with both hands:
    ///   Grabber hand (dominant) = on the handle → spray trigger
    ///   Nozzle hand             = on the hose   → aim direction
    ///
    /// SPRAY IS BINARY — all conditions must be true simultaneously or
    /// nothing activates. No partial states, no grace periods.
    ///
    /// ── AIM ──────────────────────────────────────────────────────────
    /// Both tracked  → direction = vector from grabber hand → nozzle hand
    /// Nozzle only   → nozzle palm forward
    /// Either lost   → spray stops immediately, direction freezes
    ///
    /// ── TRIGGER ──────────────────────────────────────────────────────
    /// Average curl of grabber hand (index + middle + ring) >= grabThreshold
    /// Optional proximity check (disabled by default — measure first).
    ///
    /// ── OVR MANAGER SETTINGS ─────────────────────────────────────────
    ///   Hand Tracking Support              : Hands Only
    ///   Hand Tracking Frequency            : High (Fast Motion Mode)
    ///   Simultaneous H&C Enabled           : OFF
    ///   Launch Simultaneous H&C On Startup : OFF
    ///   Controller Driven Hand Poses Type  : None
    ///
    /// ── INSPECTOR WIRING ─────────────────────────────────────────────
    ///   ovrHandRight     → RightHandAnchor > OVRHandPrefab
    ///   ovrHandLeft      → LeftHandAnchor  > OVRHandPrefab
    ///   nozzleAttachment → NozzleOrigin child transform
    ///   extinguisher     → ExtinguisherBehavior on this GO
    /// </summary>
    public class HandTrackingExtinguisherTracker : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════
        // AIM MODE
        // ═══════════════════════════════════════════════════════════════

        public enum AimMode
        {
            /// <summary>
            /// Direction = vector from grabber hand → nozzle hand.
            /// Best for rigid extinguishers with no flexible hose.
            /// </summary>
            BothHands,

            /// <summary>
            /// Direction = nozzle hand palm forward.
            /// Best for flexible hose — user aims with the nozzle hand directly.
            /// </summary>
            NozzleHandOnly
        }

        // ═══════════════════════════════════════════════════════════════
        // INSPECTOR
        // ═══════════════════════════════════════════════════════════════

        [Header("OVR Hand References")]
        [SerializeField] private OVRHand ovrHandRight;
        [SerializeField] private OVRHand ovrHandLeft;

        [SerializeField] private OVRSkeleton ovrSkeletonRight;  // ADD
        [SerializeField] private OVRSkeleton ovrSkeletonLeft;   // ADD
        [SerializeField] private float thumbCurlThreshold = 0.86f;  //thumb0, thumb 1 (button rpess could be .4)
        [SerializeField] private float minFingerBendForThumb = 0.26f; // set to 0 to ignore



        [Header("Hand Roles")]
        [Tooltip("True  = right hand on handle (trigger), left hand on nozzle.\n" +
                 "False = left hand on handle (trigger), right hand on nozzle.")]
        [SerializeField] private bool dominantHandIsRight = true;

        [Header("Aim Mode")]
        [Tooltip("BothHands    = direction from grabber → nozzle hand. Use for rigid extinguisher.\n" +
                 "NozzleHandOnly = nozzle hand palm forward. Use for flexible hose.")]
        [SerializeField] private AimMode aimMode = AimMode.BothHands;

        [Header("Nozzle Attachment")]
        [Tooltip("Child transform driven to the computed nozzle pose every frame.")]
        [SerializeField] private Transform nozzleAttachment;

        [Tooltip("Local offset from nozzle-hand wrist origin to real hose tip.")]
        [SerializeField] private Vector3 nozzleHandLocalOffset = new Vector3(0f, 0f, 0.15f);

        [Header("Spray Trigger")]
        [Tooltip("Average curl of index+middle+ring on the grabber hand. " +
                 "0=open, 1=fist. Start around 0.6 and tune on device.")]
        [SerializeField] private float grabThreshold = 0.6f;

        [Header("Optional Hand Proximity Check")]
        [Tooltip("Also require hands to be within maxHandDistance. " +
                 "Leave OFF until you measure the real prop distance.")]
        [SerializeField] private bool requireHandProximity = false;
        [SerializeField] private float maxHandDistance = 0.6f;

        [Header("Aim Smoothing")]
        [Tooltip("Higher = more responsive, lower = smoother. 20 is a good start.")]
        [SerializeField] private float aimSmoothSpeed = 20f;

        [Header("Pin Removal")]
        [Tooltip("Uncheck to skip pin mechanic entirely during development.")]
        [SerializeField] private bool requirePinRemoval = false;

        [Header("Linked Extinguisher Behavior")]
        [SerializeField] private ExtinguisherBehavior extinguisher;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private bool logStateChanges = true;

        [Header("Events")]
        public UnityEvent OnBothHandsTracked;
        public UnityEvent OnTrackingLost;

        // ═══════════════════════════════════════════════════════════════
        // STATE
        // ═══════════════════════════════════════════════════════════════

        private Vector3 _smoothedAimDirection = Vector3.forward;
        private Vector3 _frozenAimDirection = Vector3.forward;
        private bool _wasTracking = false;

        public bool GrabberHandTracked { get; private set; }
        public bool NozzleHandTracked { get; private set; }
        public bool BothHandsTracked => GrabberHandTracked && NozzleHandTracked;
        public float GrabberClosedness { get; private set; }

        private OVRHand GrabberHand => dominantHandIsRight ? ovrHandRight : ovrHandLeft;
        private OVRHand NozzleHand => dominantHandIsRight ? ovrHandLeft : ovrHandRight;

        // ═══════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (nozzleAttachment == null)
            {
                var go = new GameObject("NozzleAttachment_Auto");
                go.transform.SetParent(transform, false);
                nozzleAttachment = go.transform;
                Debug.LogWarning("[HandExtTracker] nozzleAttachment not assigned — auto-created child.");
            }

            if (extinguisher == null)
                extinguisher = GetComponent<ExtinguisherBehavior>();
        }

        private void OnEnable()
        {
            if (extinguisher != null)
            {
                extinguisher._isGrabbed = true;
                extinguisher._isPinRemoved = !requirePinRemoval;
                extinguisher._isTriggerHeld = false;
            }
        }

        private void Update()
        {
            UpdateTrackingState();
            UpdateAimDirection();
            UpdateSprayTrigger();
        }

        // ═══════════════════════════════════════════════════════════════
        // TRACKING STATE
        // ═══════════════════════════════════════════════════════════════

        private void UpdateTrackingState()
        {
            GrabberHandTracked = GrabberHand != null
                && GrabberHand.IsTracked
                && GrabberHand.HandConfidence == OVRHand.TrackingConfidence.High;

            NozzleHandTracked = NozzleHand != null
                && NozzleHand.IsTracked
                && NozzleHand.HandConfidence == OVRHand.TrackingConfidence.High;

            // In NozzleHandOnly mode, only the nozzle hand is needed for aim.
            // Spray still requires grabber tracked (for trigger reading).
            bool effectivelyTracked = aimMode == AimMode.NozzleHandOnly
                ? NozzleHandTracked   // aim works with one hand
                : BothHandsTracked;   // aim requires both

            if (effectivelyTracked != _wasTracking)
            {
                if (effectivelyTracked)
                {
                    if (logStateChanges) Debug.Log("[HandExtTracker] Tracking ready.");
                    OnBothHandsTracked?.Invoke();
                }
                else
                {
                    if (logStateChanges) Debug.LogWarning("[HandExtTracker] Tracking lost.");
                    OnTrackingLost?.Invoke();
                }
                _wasTracking = effectivelyTracked;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // AIM DIRECTION
        // ═══════════════════════════════════════════════════════════════

        private void UpdateAimDirection()
        {
            Vector3 targetDirection;

            switch (aimMode)
            {
                case AimMode.BothHands:
                    targetDirection = GetBothHandsDirection();
                    break;

                case AimMode.NozzleHandOnly:
                    targetDirection = GetNozzleHandDirection();
                    break;

                default:
                    targetDirection = _frozenAimDirection;
                    break;
            }

            // If direction came back as zero (tracking lost / degenerate) freeze and bail
            if (targetDirection.sqrMagnitude < 0.001f)
            {
                ApplyNozzlePose(nozzleAttachment.position, _frozenAimDirection);
                return;
            }

            _smoothedAimDirection = Vector3.Slerp(
                _smoothedAimDirection, targetDirection, Time.deltaTime * aimSmoothSpeed);

            _frozenAimDirection = _smoothedAimDirection;

            Vector3 nozzleWorldPos = NozzleHandTracked
                ? NozzleHand.transform.position +
                  NozzleHand.transform.TransformDirection(nozzleHandLocalOffset)
                : nozzleAttachment.position;

            ApplyNozzlePose(nozzleWorldPos, _smoothedAimDirection);
        }

        /// <summary>
        /// Rigid hose: direction = grabber hand wrist → nozzle hand tip.
        /// Requires both hands tracked. Returns zero vector if not available.
        /// </summary>
        private Vector3 GetBothHandsDirection()
        {
            if (!BothHandsTracked) return Vector3.zero;

            Vector3 grabberPos = GrabberHand.transform.position;
            Vector3 nozzlePos = NozzleHand.transform.position +
                                 NozzleHand.transform.TransformDirection(nozzleHandLocalOffset);

            return (nozzlePos - grabberPos).normalized;
        }

        /// <summary>
        /// Flexible hose: direction = where the nozzle hand palm is pointing.
        /// Only requires nozzle hand tracked. Returns zero vector if not available.
        /// </summary>
        private Vector3 GetNozzleHandDirection()
        {
            if (!NozzleHandTracked) return Vector3.zero;
            return NozzleHand.transform.forward;
        }

        private void ApplyNozzlePose(Vector3 position, Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f) return;

            Quaternion rot = Quaternion.LookRotation(direction, Vector3.up);
            nozzleAttachment.SetPositionAndRotation(position, rot);

            if (extinguisher?.nozzleOrigin != null)
                extinguisher.nozzleOrigin.SetPositionAndRotation(position, rot);
        }

        // ═══════════════════════════════════════════════════════════════
        // SPRAY TRIGGER — all conditions or nothing
        // ═══════════════════════════════════════════════════════════════

        private void UpdateSprayTrigger()
        {
            if (extinguisher == null) return;

            // Condition 1: both hands must be tracked
            if (!BothHandsTracked)
            {
                extinguisher._isTriggerHeld = false;
                return;
            }

            // Condition 2: grabber hand must be sufficiently closed
            // GrabberClosedness = GetHandClosedness(GrabberHand);
            // bool handClosed = GrabberClosedness >= grabThreshold;
            bool handClosed = IsThumbPressingDown(GrabberHand);

            // Condition 3 (optional): hands must be close enough to each other
            bool proximityOk = true;
            if (requireHandProximity)
            {
                float dist = Vector3.Distance(
                    GrabberHand.transform.position,
                    NozzleHand.transform.position);
                proximityOk = dist <= maxHandDistance;
            }

            // All conditions must be true simultaneously — no partial activation
            extinguisher._isTriggerHeld = handClosed && proximityOk;
        }

        // ═══════════════════════════════════════════════════════════════
        // HAND CLOSEDNESS — OVRHand finger curl
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns 0–1 average curl of index, middle, and ring fingers.
        /// Thumb excluded — users often leave thumb out when gripping a handle.
        /// Returns 0 if hand is not tracked or confidence is low.
        /// </summary>
        /// 
        /// //This is deprecated since it requeires the thumb to close anyway??
        private float GetHandClosednessDeprecated(OVRHand hand)
        {
            if (hand == null || !hand.IsTracked) return 0f;
            if (hand.HandConfidence != OVRHand.TrackingConfidence.High) return 0f;

            float index = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            float middle = hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
            float ring = hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);

            return (index + middle + ring) / 3f;
        }


        private float GetHandClosedness(OVRHand hand)
        {
            if (hand == null || !hand.IsTracked) return 0f;
            if (hand.HandConfidence != OVRHand.TrackingConfidence.High) return 0f;

            OVRSkeleton skeleton = ovrSkeletonRight;//TODO DEBUG(hand == ovrHandRight) ? ovrSkeletonRight : ovrSkeletonLeft;

            if (skeleton == null || !skeleton.IsInitialized)
            {
                // Fallback to pinch if no skeleton assigned
                float i = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                float m = hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
                float r = hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
                return (i + m + r) / 3f;
            }

            // Read proximal bone curl for index, middle, ring
            // Proximal phalanx bend is the primary signal for "gripping"
            // Z rotation on these bones maps to finger curl in OVR skeleton
            float curl = GetBoneCurl(skeleton, OVRSkeleton.BoneId.Hand_Index2)
                       + GetBoneCurl(skeleton, OVRSkeleton.BoneId.Hand_Middle1)
                       + GetBoneCurl(skeleton, OVRSkeleton.BoneId.Hand_Ring1);

            // Debug.Log($" {GetBoneCurl(skeleton, OVRSkeleton.BoneId.Hand_Index2)} {GetBoneCurl(skeleton, OVRSkeleton.BoneId.Hand_Middle1)}  {GetBoneCurl(skeleton, OVRSkeleton.BoneId.Hand_Ring1)} ");

            return Mathf.Clamp01(curl / 3f);
        }

        private float GetBoneCurlpredepacted(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId)
        {
            var bones = skeleton.Bones;
            foreach (var bone in bones)
            {
                if (bone.Id != boneId) continue;

                // Local Z rotation of the proximal phalanx.
                // ~0 = finger straight, ~90+ degrees = finger curled into palm.
                // Normalize to 0-1 over a 90 degree range.
                float angle = bone.Transform.localRotation.eulerAngles.z;

                // Euler angles wrap at 360 — remap from Unity's 0-360 to -180/+180
                if (angle > 180f) angle -= 360f;

                return Mathf.Clamp01(Mathf.Abs(angle) / 90f);
            }
            return 0f;
        }

        private float GetBoneCurl(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId)
        {
            var bones = skeleton.Bones;
            foreach (var bone in bones)
            {
                if (bone.Id != boneId) continue;

                Vector3 euler = bone.Transform.localRotation.eulerAngles;

                // Remap all axes from 0-360 to -180/+180
                float x = euler.x > 180f ? euler.x - 360f : euler.x;
                float y = euler.y > 180f ? euler.y - 360f : euler.y;
                float z = euler.z > 180f ? euler.z - 360f : euler.z;

                // Log all three to find which axis actually moves on curl
                //  Debug.Log($"[Bone {boneId}] X:{x:F1} Y:{y:F1} Z:{z:F1}");

                // Return max across all axes temporarily — so you see a response
                // regardless of which axis is correct
                float best = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z));
                return Mathf.Clamp01(best / 90f);
            }
            return 0f;
        }

        private float GetBoneCurlThumb(OVRSkeleton skeleton, OVRSkeleton.BoneId boneId)
        {
            var bones = skeleton.Bones;
            foreach (var bone in bones)
            {
                if (bone.Id != boneId) continue;

                Vector3 euler = bone.Transform.localRotation.eulerAngles;

                // Remap all axes from 0-360 to -180/+180
                float x = euler.x > 180f ? euler.x - 360f : euler.x;
                float y = euler.y > 180f ? euler.y - 360f : euler.y;
                float z = euler.z > 180f ? euler.z - 360f : euler.z;

                // Log all three to find which axis actually moves on curl
               // Debug.Log($"[Bone {boneId}] X:{x:F1} Y:{y:F1} Z:{z:F1}");

                // Return max across all axes temporarily — so you see a response
                // regardless of which axis is correct
                float best = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y), Mathf.Abs(z));
                return Mathf.Clamp01(best / 90f);
            }
            return 0f;
        }

        private float GetThumbCurl(OVRHand hand)
        {
            if (hand == null || !hand.IsTracked) return 0f;
            if (hand.HandConfidence != OVRHand.TrackingConfidence.High) return 0f;

            OVRSkeleton skeleton = (hand == ovrHandRight) ? ovrSkeletonRight : ovrSkeletonLeft;
            if (skeleton == null || !skeleton.IsInitialized) return 0f;

            // Hand_ThumbTip0 = thumb proximal — first joint that bends downward
            return GetBoneCurlThumb(skeleton, OVRSkeleton.BoneId.Hand_Thumb0);
        }

        private bool IsThumbPressingDown(OVRHand hand)
        {
            float thumb = GetThumbCurl(hand);
            float fingers = GetHandClosedness(hand); // reuse existing

            bool thumbDown = thumb >= thumbCurlThreshold;
            bool fingersEngaged = minFingerBendForThumb <= 0f || fingers >= minFingerBendForThumb;

            return thumbDown && fingersEngaged;
        }



        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Switch aim mode at runtime — call from settings menu.
        /// </summary>
        public void SetAimMode(AimMode mode)
        {
            aimMode = mode;
            if (logStateChanges)
                Debug.Log($"[HandExtTracker] Aim mode set to: {mode}");
        }

        /// <summary>
        /// Convenience overload for UI dropdowns / buttons that pass an int.
        /// 0 = BothHands, 1 = NozzleHandOnly.
        /// </summary>
        public void SetAimMode(int modeIndex)
        {
            SetAimMode((AimMode)modeIndex);
        }

        /// <summary>
        /// Swap grabber and nozzle hand roles at runtime.
        /// Call from a settings screen or on session start based on user preference.
        /// </summary>
        public void SetDominantHand(bool rightIsDominant)
        {
            dominantHandIsRight = rightIsDominant;
            if (logStateChanges)
                Debug.Log($"[HandExtTracker] Dominant hand set to: {(rightIsDominant ? "Right" : "Left")}");
        }

        // ═══════════════════════════════════════════════════════════════
        // GIZMOS
        // ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos || !Application.isPlaying) return;

            // Nozzle position and aim direction
            Gizmos.color = BothHandsTracked
                ? (extinguisher != null && extinguisher._isTriggerHeld ? Color.cyan : Color.green)
                : Color.red;

            Gizmos.DrawSphere(nozzleAttachment.position, 0.018f);
            Gizmos.DrawRay(nozzleAttachment.position, _smoothedAimDirection * 0.3f);

            // Hand-to-hand vector when both tracked
            if (BothHandsTracked)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
                Gizmos.DrawLine(
                    GrabberHand.transform.position,
                    NozzleHand.transform.position);

                // Proximity sphere if enabled
                if (requireHandProximity)
                {
                    Gizmos.color = new Color(1f, 1f, 0f, 0.08f);
                    Gizmos.DrawWireSphere(GrabberHand.transform.position, maxHandDistance);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos || !Application.isPlaying) return;

            string grabberState = GrabberHandTracked
                ? $"Grabber: {GrabberClosedness:F2} {(extinguisher != null && extinguisher._isTriggerHeld ? "SPRAYING" : "")}"
                : "Grabber: NOT TRACKED";

            string nozzleState = NozzleHandTracked ? "Nozzle: OK" : "Nozzle: NOT TRACKED";

            UnityEditor.Handles.Label(
                nozzleAttachment.position + Vector3.up * 0.08f,
                $"{grabberState}\n{nozzleState}");
        }
#endif
    }
}
