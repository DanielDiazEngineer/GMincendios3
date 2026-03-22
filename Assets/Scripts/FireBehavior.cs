using UnityEngine;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Attached to each fire gameplay object.
    /// Tracks how long the extinguisher ray has been aimed at the fire's base.
    /// When enough time accumulates, the fire is extinguished.
    ///
    /// Setup on Fire Prefab:
    ///   - Particle System (flames)
    ///   - A Collider at the BASE of the fire (BoxCollider or CapsuleCollider)
    ///     tagged "FireBase" — this is the raycast target
    ///   - This script (auto-added by FireTrainingController if missing)
    ///
    /// The extinguisher raycasts and calls ApplyExtinguisher() each frame it hits.
    /// </summary>
    public class FireBehavior : MonoBehaviour
    {
        [Header("Extinguishing Settings")]
        [Tooltip("Seconds of continuous spray needed to extinguish this fire.")]
        [SerializeField] private float timeToExtinguish = 3f;

        [Tooltip("If spray stops, how fast the progress decays (seconds per second). 0 = no decay.")]
        [SerializeField] private float decayRate = 0.5f;

        [Header("Visual Feedback")]
        [Tooltip("Optional: particle system to shrink as fire is being extinguished.")]
        [SerializeField] public ParticleSystem fireParticles;
        [Tooltip("Optional: light to dim as fire is extinguished.")]
        [SerializeField] private Light fireLight;

        [Header("Audio")]
        [SerializeField] private AudioClip extinguishSound;

        // ─── State ─────────────────────────────────────────────────────

        private FireTrainingController _controller;
        private float _extinguishProgress = 0f; // 0 → timeToExtinguish
        private bool _isBeingSprayed = false;
        private bool _extinguished = false;

        // Cache initial values for scaling feedback
        private float _initialParticleRate = 6.4f;//TDO REMOVE HERE
        private float _initialLightIntensity;

        public float ExtinguishPercent => Mathf.Clamp01(_extinguishProgress / timeToExtinguish);
        public bool IsExtinguished => _extinguished;

        // ─── Init ──────────────────────────────────────────────────────

        public void Start() ///TODO REMOVE AN LET CONTROLLED DO
        {
            if (fireParticles == null)
                fireParticles = GetComponentInChildren<ParticleSystem>();
        }

        public void Init(FireTrainingController controller)
        {
            _controller = controller;

            // Auto-find particle system if not assigned
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

            // Ensure we have a collider for raycast hits
            var col = GetComponentInChildren<Collider>();
            if (col == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.3f, 0.4f, 0.3f); // reasonable fire base size
                box.center = new Vector3(0f, 0.2f, 0f);    // centered at base
                Debug.Log("[FireBehavior] Auto-added BoxCollider for raycast targeting.");
            }
        }

        // ─── Update ────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_extinguished) return;

            if (_isBeingSprayed)
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
                // Decay progress when not being sprayed
                _extinguishProgress -= decayRate * Time.deltaTime;
                _extinguishProgress = Mathf.Max(0f, _extinguishProgress);
            }

            // Reset spray flag — ExtinguisherBehavior must call ApplyExtinguisher() each frame
            _isBeingSprayed = false;

            UpdateVisualFeedback();
        }

        // ─── Public API (called by ExtinguisherBehavior) ───────────────

        /// <summary>
        /// Call this every frame the extinguisher ray is hitting this fire.
        /// </summary>
        public void ApplyExtinguisher()
        {
            if (_extinguished) return;
            _isBeingSprayed = true;
        }

        // ─── Extinguish ────────────────────────────────────────────────

        private void Extinguish()
        {
            _extinguished = true;

            // Stop particles
            if (fireParticles != null)
            {
                var emission = fireParticles.emission;
                emission.rateOverTime = 0f;
                fireParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            // Kill light
            if (fireLight != null)
                fireLight.intensity = 0f;

            // Play sound
            if (extinguishSound != null)
                AudioSource.PlayClipAtPoint(extinguishSound, transform.position);

            Debug.Log("[FireBehavior] Fire extinguished!");

            // Notify controller (which will Destroy this GO)
            //TODO RESTORE FUNCTIONALLITY
            //  _controller.ReportFireExtinguished(gameObject);
        }

        // ─── Visual Feedback ───────────────────────────────────────────

        private void UpdateVisualFeedback()
        {
            float t = 1f - ExtinguishPercent; // 1 = full fire, 0 = almost out

            // Shrink particle emission
            if (fireParticles != null)
            {
                var emission = fireParticles.emission;
                emission.rateOverTime = _initialParticleRate * t;
            }

            // Dim light
            if (fireLight != null)
                fireLight.intensity = _initialLightIntensity * t;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Show extinguish progress in editor
            Gizmos.color = Color.Lerp(Color.red, Color.green, ExtinguishPercent);
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
#endif
    }
}
