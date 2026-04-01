using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Extinguisher spray logic. Input state (_isGrabbed, _isPinRemoved, _isTriggerHeld)
    /// is set externally by HandTrackingExtinguisherTracker.
    ///
    /// Exposes LastSprayHitPoint / HasSprayHit for DeskFireBehavior sweep detection.
    /// </summary>
    public class ExtinguisherBehavior : MonoBehaviour
    {
        [Header("Nozzle & Raycast")]
        public Transform nozzleOrigin;

        [SerializeField] private float maxSprayDistance = 5f;

        [Tooltip("Cast against all layers — environment geometry blocks naturally. " +
                 "Only FireBehavior hits trigger ApplyExtinguisher().")]
        [SerializeField] private LayerMask fireLayer = ~0;

        [SerializeField] private float sprayRadius = 0.1f;

        [Header("Spray VFX")]
        [SerializeField] private ParticleSystem sprayVFX;
        public LineRenderer sprayLine;

        [Header("Spray Audio")]
        [SerializeField] private AudioSource sprayAudio;
        [SerializeField] private AudioClip pinPullSound;

        [Header("Gauge (optional)")]
        [SerializeField] private Transform gaugeRotator;
        [SerializeField] private float gaugeSpeed = 1f;
        [SerializeField] private float gaugeFactor = 1f;

        [Header("Events")]
        public UnityEvent OnPinRemoved;
        public UnityEvent OnSprayStarted;
        public UnityEvent OnSprayStopped;

        [Header("Grab Requirement")]
        [Tooltip("Uncheck when using hand-tracking — no virtual grab needed.")]
        [SerializeField] private bool requireGrab = false;

        [Tooltip("Set true by leveldemonew.CompleteIntro() after the intro sequence.")]
        [HideInInspector] public bool shootingEnabled = false;

        [Header("UI Hint (optional)")]
        [SerializeField] private UnityEngine.UI.Text hintText;

        [HideInInspector] public bool _controllerTriggerHeld;

        // ── State — written by tracker, read here ──────────────────────────────

        [HideInInspector] public bool _isGrabbed;
        [HideInInspector] public bool _isPinRemoved;
        [HideInInspector] public bool _isTriggerHeld;

        public bool _isSpraying { get; private set; }
        private bool _pinWasRemoved = false;
        private FireBehavior _currentTarget;

        /// <summary>World-space point where the SphereCast last hit a fire collider.</summary>
        public Vector3 LastSprayHitPoint { get; private set; }

        /// <summary>True when spraying AND the cast hit a fire collider this frame.</summary>
        public bool HasSprayHit { get; private set; }

        public bool IsGrabbed => _isGrabbed;
        public bool IsPinRemoved => _isPinRemoved;
        public bool IsSpraying => _isSpraying;

        public bool IsAimingAtFire { get; private set; }

        public float CurrentTargetProgress => 
    _currentTarget != null ? _currentTarget.ExtinguishPercent : 0f;
public bool HasTarget => _currentTarget != null && !_currentTarget.IsExtinguished;

        // ── Lifecycle ──────────────────────────────────────────────────────────

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
            HasSprayHit = false;

            // ── Pin removal ────────────────────────────────────────────────────
            if (_isPinRemoved && !_pinWasRemoved)
            {
                _pinWasRemoved = true;

                if (pinPullSound != null)
                    AudioSource.PlayClipAtPoint(pinPullSound, transform.position);

                OnPinRemoved?.Invoke();
                Debug.Log("[Extinguisher] Pin removed!");
                UpdateHint();
            }

            IsAimingAtFire = false;
            if (nozzleOrigin != null && Physics.SphereCast(
                    nozzleOrigin.position, sprayRadius, nozzleOrigin.forward,
                    out var aimHit, maxSprayDistance, fireLayer, QueryTriggerInteraction.Collide))
            {
                IsAimingAtFire = aimHit.collider.GetComponentInParent<FireBehavior>() != null;
            }

            // ── Spray state machine ────────────────────────────────────────────
            bool shouldSpray = shootingEnabled
                && (!requireGrab || _isGrabbed)
                && _isPinRemoved
                && (_isTriggerHeld || _controllerTriggerHeld);

            if (shouldSpray && !_isSpraying)
                StartSpray();
            else if (!shouldSpray && _isSpraying)
                StopSpray();

            if (_isSpraying)
            {
                DoSprayRaycast();
                UpdateGauge();
            }
        }

        // ── Spray ──────────────────────────────────────────────────────────────

        private void StartSpray()
        {
            _isSpraying = true;
            SetSprayActive(true);
            OnSprayStarted?.Invoke();
           // Debug.Log("[Extinguisher] Spraying!");
        }

        private void StopSpray()
        {
            if (!_isSpraying) return;
            _isSpraying = false;
            _currentTarget = null;
            HasSprayHit = false;
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
                    HasSprayHit = true;
                    LastSprayHitPoint = hit.point;
                    fire.ApplyExtinguisher();
                    _currentTarget = fire;
                }
                else
                {
                    // Hit environment geometry (barrel wall, desk surface, etc.) — blocked
                    _currentTarget = null;
                }
            }
            else
            {
                _currentTarget = null;
            }
        }

        // ── Gauge / VFX / Audio ────────────────────────────────────────────────

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

        // ── Hints ──────────────────────────────────────────────────────────────

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

        // ── Reset ──────────────────────────────────────────────────────────────

        public void ResetExtinguisher()
        {
            _isPinRemoved = false;
            _pinWasRemoved = false;
            _isGrabbed = false;
            _isTriggerHeld = false;
            StopSpray();
            UpdateHint();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (nozzleOrigin == null) return;

            Gizmos.color = _isSpraying ? Color.cyan
                         : _isPinRemoved ? Color.yellow
                         : Color.gray;
            Gizmos.DrawRay(nozzleOrigin.position, nozzleOrigin.forward * maxSprayDistance);

            if (HasSprayHit)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(LastSprayHitPoint, sprayRadius);
            }
        }
#endif
    }
}
