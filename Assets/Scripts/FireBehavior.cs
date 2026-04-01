using UnityEngine;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Attached to each fire gameplay object (box, barrel, desk zone).
    /// Tracks extinguisher spray accumulation and extinguishes when threshold is met.
    ///
    /// Barrel setup: place this component on a child GO above the barrel rim.
    /// The barrel mesh collider blocks side shots naturally — no layer tricks needed.
    ///
    /// Desk zone setup: DeskFireBehavior sets SweepGateOpen each frame.
    /// Progress only accumulates when that gate is open (nozzle moving laterally).
    /// </summary>
    public class FireBehavior : MonoBehaviour
    {
        [Header("Extinguishing")]
        [Tooltip("Seconds of continuous spray needed to extinguish.")]
        [SerializeField] private float timeToExtinguish = 3f;

        [Tooltip("Progress decay per second when not being sprayed. 0 = no decay.")]
        [SerializeField] private float decayRate = 0.5f;

        [Header("Fire Target Layer")]
        [Tooltip("If > 0, the fire's detection collider is moved to this layer. " +
                 "Leave at 0 unless you have a specific reason to separate layers.")]
        [SerializeField] private int fireTargetLayer = 0;

        [Header("Visual Feedback")]
        [SerializeField] public ParticleSystem fireParticles;
        [SerializeField] private Light fireLight;
        [Header("Halo")]
        [SerializeField] private GameObject halo;

        [Header("Audio")]
        [SerializeField] private AudioClip extinguishSound;

        // ── State ──────────────────────────────────────────────────────────────

        private FireTrainingController _controller;
        private float _extinguishProgress = 0f;
        private bool _isBeingSprayed = false;
        private bool _extinguished = false;

        private float _initialParticleRate;
        private float _initialLightIntensity;

        /// <summary>
        /// Set by DeskFireBehavior each frame. When false, spray accumulation is paused.
        /// Defaults to true — non-desk fires are always open.
        /// </summary>
        [HideInInspector] public bool SweepGateOpen = true;

        /// <summary>Used by DeskFireBehavior to count zone completion exactly once.</summary>
        [HideInInspector] public bool ReportedToDesk = false;

        public float ExtinguishPercent => Mathf.Clamp01(_extinguishProgress / timeToExtinguish);
        public bool IsExtinguished => _extinguished;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Start()
        {
            AutoSetup();
        }

        /// <summary>Called by FireTrainingController when spawning at runtime.</summary>
        public void Init(FireTrainingController controller)
        {
            _controller = controller;
            AutoSetup();
        }

        private void AutoSetup()
        {
            if (fireParticles == null)
                fireParticles = GetComponentInChildren<ParticleSystem>();

            if (fireLight == null)
                fireLight = GetComponentInChildren<Light>();

            if (fireParticles != null)
            {
                var emission = fireParticles.emission;
                _initialParticleRate = emission.rateOverTime.constant;
            }

            if (fireLight != null)
                _initialLightIntensity = fireLight.intensity;

            EnsureCollider();
        }

        private void EnsureCollider()
        {
            var col = GetComponentInChildren<Collider>();
            if (col == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.3f, 0.4f, 0.3f);
                box.center = new Vector3(0f, 0.2f, 0f);
                col = box;
                Debug.Log($"[FireBehavior] Auto-added BoxCollider on '{name}'.");
            }

            if (fireTargetLayer > 0)
                col.gameObject.layer = fireTargetLayer;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_extinguished) return;

            if (_isBeingSprayed && SweepGateOpen)
            {
                _extinguishProgress += Time.deltaTime;

                if (_extinguishProgress >= timeToExtinguish)
                {
                    Extinguish();
                    return;
                }
            }
            else if (decayRate > 0f && _extinguishProgress > 0f)
            {
                _extinguishProgress -= decayRate * Time.deltaTime;
                _extinguishProgress = Mathf.Max(0f, _extinguishProgress);
            }

            _isBeingSprayed = false;

            UpdateVisualFeedback();
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Call every frame the extinguisher spray is hitting this fire.</summary>
        public void ApplyExtinguisher()
        {
            if (_extinguished) return;
            _isBeingSprayed = true;
        }

        // ── Extinguish ─────────────────────────────────────────────────────────

        private void Extinguish()
        {
            _extinguished = true;
            if (halo != null) halo.SetActive(false);

            if (fireParticles != null)
            {
                var emission = fireParticles.emission;
                emission.rateOverTime = 0f;
                fireParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (fireLight != null)
                fireLight.intensity = 0f;

            if (extinguishSound != null)
                AudioSource.PlayClipAtPoint(extinguishSound, transform.position);

            Debug.Log($"[FireBehavior] '{name}' extinguished!");

            _controller?.ReportFireExtinguished(gameObject);
        }

        // ── Visual Feedback ────────────────────────────────────────────────────

        private void UpdateVisualFeedback()
        {
            float t = 1f - ExtinguishPercent;

            if (fireParticles != null)
            {
                var emission = fireParticles.emission;
                emission.rateOverTime = _initialParticleRate * t;
            }

            if (fireLight != null)
                fireLight.intensity = _initialLightIntensity * t;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.Lerp(Color.red, Color.green, ExtinguishPercent);
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            if (!SweepGateOpen)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
                Gizmos.DrawWireSphere(transform.position, 0.2f);
            }
        }
#endif
    }
}
