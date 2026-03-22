using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Extinguisher spray logic for a REAL physical extinguisher tracked via OVR controller.
    /// AutoHand dependency has been removed entirely.
    ///
    /// This component is now DUMB about input — all state flags (_isGrabbed, _isPinRemoved,
    /// _isTriggerHeld) are SET EXTERNALLY by RealExtinguisherTracker. This script only
    /// evaluates spray conditions, runs the raycast, and drives VFX/Audio.
    ///
    /// Setup on Extinguisher GO:
    ///   ├─ RealExtinguisherTracker   ← drives the pose + sets state flags
    ///   ├─ ExtinguisherBehavior      ← this script (spray logic only)
    ///   └─ NozzleOrigin              ← child; RealExtinguisherTracker.nozzleAttachment
    ///        ├─ SprayParticles (ParticleSystem)
    ///        └─ SprayLine     (LineRenderer, optional)
    ///
    /// Events:
    ///   OnPinRemoved    — first frame _isPinRemoved becomes true
    ///   OnSprayStarted  — spray activates
    ///   OnSprayStopped  — spray deactivates
    /// </summary>
    [RequireComponent(typeof(RealExtinguisherTracker))]
    public class ExtinguisherBehavior : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════
        // INSPECTOR
        // ═══════════════════════════════════════════════════════════════

        [Header("Nozzle & Raycast")]
        [Tooltip("Driven automatically by RealExtinguisherTracker. " +
                 "Assign here too so the raycast origin is always available.")]
        public Transform nozzleOrigin;

        [SerializeField] private float maxSprayDistance = 5f;
        [SerializeField] private LayerMask fireLayer = ~0;
        [SerializeField] private float sprayRadius = 0.1f;

        [Header("Spray VFX")]
        [SerializeField] private ParticleSystem sprayVFX;
        public LineRenderer sprayLine;

        [Header("Spray Audio")]
        [SerializeField] private AudioSource sprayAudio;
        [SerializeField] private AudioClip pinPullSound;

        [Header("Gas Gauge (optional)")]
        [SerializeField] private Transform gaugeRotator;
        [SerializeField] private float gaugeSpeed = 1f;
        [SerializeField] private float gaugeFactor = 1f;

        [Header("Events")]
        public UnityEvent OnPinRemoved;
        public UnityEvent OnSprayStarted;
        public UnityEvent OnSprayStopped;

        [Header("Grab Requirement")]
        [Tooltip("Uncheck when using a real physical extinguisher — no virtual grab needed.")]
        [SerializeField] private bool requireGrab = true;

        [Tooltip("Gate controlled externally — set to true once intro is complete.")]
        [HideInInspector] public bool shootingEnabled = false;

        [Header("UI Hint (optional)")]
        [SerializeField] private UnityEngine.UI.Text hintText;

        [HideInInspector] public bool _controllerTriggerHeld;

        // ═══════════════════════════════════════════════════════════════
        // STATE — written by RealExtinguisherTracker, read here
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Set by RealExtinguisherTracker when controller is connected/disconnected.</summary>
        [HideInInspector] public bool _isGrabbed;

        /// <summary>Set by RealExtinguisherTracker when pin button is pressed (or forced).</summary>
        [HideInInspector] public bool _isPinRemoved;

        /// <summary>Set by RealExtinguisherTracker from index trigger axis.</summary>
        [HideInInspector] public bool _isTriggerHeld;

        // Internal
        public bool _isSpraying { get; private set; }
        private bool _pinWasRemoved = false;
        private FireBehavior _currentTarget;

        public bool IsGrabbed => _isGrabbed;
        public bool IsPinRemoved => _isPinRemoved;
        public bool IsSpraying => _isSpraying;

        // ═══════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        private void Start()
        {
            if (nozzleOrigin == null)
            {
                var nozzle = transform.Find("NozzleOrigin");
                nozzleOrigin = nozzle != null ? nozzle : transform;
                if (nozzle == null)
                    Debug.LogWarning("[Extinguisher] No NozzleOrigin child found. Using transform root.");
            }

            SetSprayActive(false);
            UpdateHint();
        }

        private void Update()
        {
            // ── Pin removal edge detection ───────────────────────────────────
            if (_isPinRemoved && !_pinWasRemoved)
            {
                _pinWasRemoved = true;

                if (pinPullSound != null)
                    AudioSource.PlayClipAtPoint(pinPullSound, transform.position);

                OnPinRemoved?.Invoke();
                Debug.Log("[Extinguisher] Pin removed!");
                UpdateHint();
            }

            // ── Spray state machine ──────────────────────────────────────────
            //  bool shouldSpray = shootingEnabled && (!requireGrab || _isGrabbed) && _isPinRemoved && _isTriggerHeld;

            bool shouldSpray = shootingEnabled && (!requireGrab || _isGrabbed) && _isPinRemoved
                   && (_isTriggerHeld || _controllerTriggerHeld);




            if (shouldSpray && !_isSpraying)
                StartSpray();
            else if (!shouldSpray && _isSpraying)
                StopSpray();

            // ── Per-frame spray logic ────────────────────────────────────────
            if (_isSpraying)
            {
                DoSprayRaycast();
                UpdateGauge();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SPRAY
        // ═══════════════════════════════════════════════════════════════

        private void StartSpray()
        {
            _isSpraying = true;
            SetSprayActive(true);
            OnSprayStarted?.Invoke();
            Debug.Log("[Extinguisher] Spraying!");
        }

        private void StopSpray()
        {
            if (!_isSpraying) return;
            _isSpraying = false;
            _currentTarget = null;
            SetSprayActive(false);
            OnSprayStopped?.Invoke();
        }

        private void DoSprayRaycast()
        {
            var origin = nozzleOrigin.position;
            var direction = nozzleOrigin.forward;

            if (sprayLine != null)
            {
                sprayLine.SetPosition(0, origin);
                sprayLine.SetPosition(1, origin + direction * maxSprayDistance);
            }

            if (Physics.SphereCast(origin, sprayRadius, direction, out var hit,
                    maxSprayDistance, fireLayer, QueryTriggerInteraction.Collide))
            {
                if (sprayLine != null)
                    sprayLine.SetPosition(1, hit.point);

                Debug.DrawLine(origin, hit.point, Color.cyan);

                var fire = hit.collider.GetComponentInParent<FireBehavior>();
                if (fire != null && !fire.IsExtinguished)
                {
                    fire.ApplyExtinguisher();
                    _currentTarget = fire;
                }
                else
                {
                    _currentTarget = null;
                }
            }
            else
            {
                _currentTarget = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GAUGE / VFX / AUDIO
        // ═══════════════════════════════════════════════════════════════

        private void UpdateGauge()
        {
            if (gaugeRotator == null) return;
            gaugeRotator.localRotation *= Quaternion.Euler(0f, Time.deltaTime * gaugeSpeed * gaugeFactor, 0f);
        }

        private void SetSprayActive(bool active)
        {
            if (sprayVFX != null)
            {
                if (active) sprayVFX.Play();
                else sprayVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (sprayLine != null)
                sprayLine.enabled = active;

            if (sprayAudio != null)
            {
                if (active && !sprayAudio.isPlaying) sprayAudio.Play();
                else if (!active) sprayAudio.Stop();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HINTS
        // ═══════════════════════════════════════════════════════════════

        private void UpdateHint()
        {
            if (hintText == null) return;

            if (!_isGrabbed)
                hintText.text = "Toma el extintor";
            else if (!_isPinRemoved)
                hintText.text = "Presiona A para quitar el seguro";
            else
                hintText.text = "Apunta a la base del fuego\ny aprieta el gatillo";
        }

        // ═══════════════════════════════════════════════════════════════
        // RESET
        // ═══════════════════════════════════════════════════════════════

        public void ResetExtinguisher()
        {
            _isPinRemoved = false;
            _pinWasRemoved = false;
            _isGrabbed = false;
            _isTriggerHeld = false;
            StopSpray();
            UpdateHint();
        }

        // ═══════════════════════════════════════════════════════════════
        // GIZMOS
        // ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (nozzleOrigin == null) return;

            Gizmos.color = _isSpraying ? Color.cyan
                         : _isPinRemoved ? Color.yellow
                         : Color.gray;
            Gizmos.DrawRay(nozzleOrigin.position, nozzleOrigin.forward * maxSprayDistance);

            if (_currentTarget != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_currentTarget.transform.position, 0.2f);
            }
        }
#endif
    }
}
