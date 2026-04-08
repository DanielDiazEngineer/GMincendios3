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
    ///
    /// Multi-PS setup: assign all particle systems to allFireParticles[].
    /// If left empty, AutoSetup will find all PS children automatically.
    /// Each PS can be faded independently via the Fade Behaviour checkboxes.
    /// Recommended for texture-sheet fires: fadeEmissionRate = false,
    /// fadeStartSize = false, fadeLifetime = true.
    /// </summary>
    public class FireBehavior : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Extinguishing")]
        [Tooltip("Seconds of continuous spray needed to extinguish.")]
        [SerializeField] private float timeToExtinguish = 3f;

        [Tooltip("Progress decay per second when not being sprayed. 0 = no decay.")]
        [SerializeField] private float decayRate = 0.5f;

        [Header("Fire Target Layer")]
        [Tooltip("If > 0, the fire's detection collider is moved to this layer. " +
                 "Leave at 0 unless you have a specific reason to separate layers.")]
        [SerializeField] private int fireTargetLayer = 0;

        [Header("Visual Feedback — Particle Systems")]
        [Tooltip("All particle systems that make up this fire. " +
                 "Leave empty to auto-discover all PS children on Start.")]
        [SerializeField] private ParticleSystem[] allFireParticles;

        [Tooltip("Optional point light that dims as fire is suppressed.")]
        [SerializeField] private Light fireLight;

        [Header("Fade Behaviour")]
        [Tooltip("Scale emission rate down as fire is suppressed. " +
                 "Disable for large sparse-emitter fires — looks unnatural.")]
        [SerializeField] private bool fadeEmissionRate = false;

        [Tooltip("Scale startSize down as fire is suppressed. " +
                 "Causes particles to shrink from their centre — use fadeLifetime instead for bottom-anchored feel.")]
        [SerializeField] private bool fadeStartSize = false;

        [Tooltip("Scale startLifetime (and startSpeed) down as fire is suppressed. " +
                 "Particles don't travel as high → fire height drops while base stays anchored. " +
                 "Best option for texture-sheet fires.")]
        [SerializeField] private bool fadeLifetime = true;

        [Tooltip("Minimum size multiplier when fully suppressed (0 = off, 1 = no change).")]
        [Range(0f, 1f)]
        [SerializeField] private float minSizePercent = 0.3f;

        [Tooltip("Minimum lifetime multiplier when fully suppressed (0 = off, 1 = no change).")]
        [Range(0f, 1f)]
        [SerializeField] private float minLifetimePercent = 0.2f;

        [Header("Halo")]
        [SerializeField] private GameObject halo;

        [Header("Audio")]
        [SerializeField] private AudioClip extinguishSound;

        // ── State ──────────────────────────────────────────────────────────────

        private FireTrainingController _controller;
        private float _extinguishProgress = 0f;
        private bool _isBeingSprayed = false;
        private bool _extinguished = false;

        // Per-PS initial values captured on AutoSetup
        private float[] _initialParticleRates;
        private float[] _initialStartSizes;
        private float[] _initialLifetimes;
        private float[] _initialSpeeds;

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
            // Discover particle systems if not assigned
            if (allFireParticles == null || allFireParticles.Length == 0)
                allFireParticles = GetComponentsInChildren<ParticleSystem>();

            if (fireLight == null)
                fireLight = GetComponentInChildren<Light>();

            // Capture initial values per PS
            int count = allFireParticles.Length;
            _initialParticleRates = new float[count];
            _initialStartSizes    = new float[count];
            _initialLifetimes     = new float[count];
            _initialSpeeds        = new float[count];

            for (int i = 0; i < count; i++)
            {
                if (allFireParticles[i] == null) continue;

                var em   = allFireParticles[i].emission;
                var main = allFireParticles[i].main;

                _initialParticleRates[i] = em.rateOverTime.constant;
                _initialStartSizes[i]    = main.startSize.constant;
                _initialLifetimes[i]     = main.startLifetime.constant;
                _initialSpeeds[i]        = main.startSpeed.constant;
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
                box.size   = new Vector3(0.3f, 0.4f, 0.3f);
                box.center = new Vector3(0f,   0.2f, 0f);
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
                _extinguishProgress  = Mathf.Max(0f, _extinguishProgress);
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

            if (halo != null)
                halo.SetActive(false);

            // Hard-stop all particle systems
            foreach (var ps in allFireParticles)
            {
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTime = 0f;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
            // t: 1.0 = fire at full strength, 0.0 = nearly extinguished
            float t = 1f - ExtinguishPercent;

            for (int i = 0; i < allFireParticles.Length; i++)
            {
                if (allFireParticles[i] == null) continue;

                var em   = allFireParticles[i].emission;
                var main = allFireParticles[i].main;

                if (fadeEmissionRate)
                    em.rateOverTime = _initialParticleRates[i] * t;

                if (fadeStartSize)
                    main.startSize = _initialStartSizes[i] * Mathf.Lerp(minSizePercent, 1f, t);

                if (fadeLifetime)
                {
                    // Shorter lifetime = particles don't travel as high = fire shrinks from top,
                    // base stays anchored at emitter position. Pairs well with texture-sheet animation.
                    float lifeT = Mathf.Lerp(minLifetimePercent, 1f, t);
                    main.startLifetime = _initialLifetimes[i] * lifeT;
                    main.startSpeed    = _initialSpeeds[i]    * lifeT;
                }
            }

            if (fireLight != null)
                fireLight.intensity = _initialLightIntensity * t;
        }

        // ── Editor Gizmos ──────────────────────────────────────────────────────

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
