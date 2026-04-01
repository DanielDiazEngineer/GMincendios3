using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Desk fire: spreads across a surface and requires a sweeping motion to extinguish.
    ///
    /// ── HOW IT WORKS ──────────────────────────────────────────────────────────
    /// The desk fire is made up of N child "zones" (left → right), each with its
    /// own FireBehavior and a collider on the FireTarget layer. The ExtinguisherBehavior
    /// SphereCast hits whichever zone collider the nozzle points at.
    ///
    /// Progress in a zone only accumulates while the spray hit point is moving
    /// laterally (along the desk's local X axis) above minSweepSpeed. This prevents
    /// the player from parking on one spot and waiting.
    ///
    /// All zones must be extinguished → OnDeskFullyExtinguished fires.
    ///
    /// ── SETUP ─────────────────────────────────────────────────────────────────
    /// 1. Create a parent GO for the desk fire (this script here).
    /// 2. Add N child GOs (e.g. DeskZone_L, DeskZone_C, DeskZone_R) — set their
    ///    layer to "FireTarget" (layer 11 or whichever you chose).
    ///    Give each a FireBehavior + BoxCollider sized to cover one third of the desk.
    /// 3. Assign zones[] in the inspector (or tick autoFindChildZones).
    /// 4. Assign extinguisher reference.
    /// 5. Wire OnDeskFullyExtinguished → your HUD / controller.
    ///
    /// ── DESK LAYOUT (top-down) ────────────────────────────────────────────────
    ///   [Zone 0 LEFT] [Zone 1 CENTER] [Zone 2 RIGHT]
    ///   ←──────────── desk local X+ ──────────────→
    /// </summary>
    public class DeskFireBehavior : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Zones")]
        [Tooltip("FireBehaviors across desk width, left-to-right. " +
                 "Each needs a collider on the FireTarget layer.")]
        [SerializeField] private FireBehavior[] zones;

        [Tooltip("If true, auto-discovers all FireBehavior children on Start.")]
        [SerializeField] private bool autoFindChildZones = true;

        [Header("Sweep Requirement")]
        [Tooltip("Zone progress only accumulates while the spray hit point is moving. " +
                 "Prevents stationary hosing.")]
        [SerializeField] private bool requireSweepMotion = true;

        [Tooltip("Minimum lateral speed (m/s on desk surface) required for progress. " +
                 "0.15–0.4 m/s feels natural.")]
        [SerializeField] private float minSweepSpeed = 0.25f;

        [Tooltip("Axis of the desk that defines 'sweeping'. " +
                 "Defaults to local X (desk width). Change to Z for depth.")]
        [SerializeField] private Vector3 sweepAxisLocal = Vector3.right;

        [Header("Extinguisher Reference")]
        [SerializeField] private ExtinguisherBehavior extinguisher;

        [Header("Visual Feedback (optional)")]
        [Tooltip("Light that dims as fires go out.")]
        [SerializeField] private Light deskLight;

        [Header("Halo")]
        [SerializeField] private GameObject deskHalo;

        [Header("Events")]
        public UnityEvent OnDeskFullyExtinguished;
        /// <summary>Fires with count of remaining active zones.</summary>
        public UnityEvent<int> OnZoneExtinguished;

        [Header("Audio")]
        [SerializeField] private AudioClip deskExtinguishSound;

        // ── State ──────────────────────────────────────────────────────────────

        private int _activeZoneCount;
        private bool _done = false;

        // Sweep tracking
        private Vector3 _lastHitPoint;
        private bool _hadHitLastFrame = false;

        // Per-zone sweep gate: zone index → is currently allowed to accumulate?
        private readonly Dictionary<int, bool> _zoneSweepGate = new();

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Start()
        {
            if (autoFindChildZones)
                zones = GetComponentsInChildren<FireBehavior>();

            if (zones == null || zones.Length == 0)
            {
                Debug.LogWarning("[DeskFire] No zones found. Assign them or tick autoFindChildZones.");
                enabled = false;
                return;
            }

            _activeZoneCount = zones.Length;
            for (int i = 0; i < zones.Length; i++)
                _zoneSweepGate[i] = true; // open by default; gated each frame below

            Debug.Log($"[DeskFire] Initialized with {zones.Length} zone(s). Sweep required: {requireSweepMotion}");
        }

        private void Update()
        {
            if (_done) return;

            // ── Compute sweep speed this frame ─────────────────────────────────
            bool sweepOk = true; // default: allow progress

            if (requireSweepMotion && extinguisher != null && extinguisher.HasSprayHit)
            {
                Vector3 hitWorld = extinguisher.LastSprayHitPoint;

                if (_hadHitLastFrame)
                {
                    // Project movement onto the desk's sweep axis (world space)
                    Vector3 sweepAxisWorld = transform.TransformDirection(sweepAxisLocal).normalized;
                    Vector3 delta = hitWorld - _lastHitPoint;
                    float lateralSpeed = Vector3.Dot(delta, sweepAxisWorld) / Time.deltaTime;
                    lateralSpeed = Mathf.Abs(lateralSpeed);

                    sweepOk = lateralSpeed >= minSweepSpeed;
                }
                else
                {
                    sweepOk = false; // first frame of contact — don't grant progress yet
                }

                _lastHitPoint = hitWorld;
                _hadHitLastFrame = true;
            }
            else
            {
                _hadHitLastFrame = false;
            }

            // ── Gate each zone's progress ──────────────────────────────────────
            // FireBehavior.ApplyExtinguisher() is already called by the SphereCast.
            // We block progress by pausing/resuming the FireBehavior (via its enabled flag).
            // This avoids duplicating accumulation logic.
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null || zones[i].IsExtinguished) continue;
                // Allow progress only when sweep speed requirement is met
                zones[i].SweepGateOpen = !requireSweepMotion || sweepOk;
            }

            // ── Check for newly extinguished zones ─────────────────────────────
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null || !zones[i].IsExtinguished) continue;

                // Zone just finished — remove from active count (guard with flag inside FireBehavior)
                if (!zones[i].ReportedToDesk)
                {
                    zones[i].ReportedToDesk = true;
                    _activeZoneCount--;
                    OnZoneExtinguished?.Invoke(_activeZoneCount);
                    Debug.Log($"[DeskFire] Zone {i} extinguished. {_activeZoneCount} remaining.");
                }
            }

            // ── Update desk light ──────────────────────────────────────────────
            if (deskLight != null)
            {
                float intensity = (float)_activeZoneCount / zones.Length;
                deskLight.intensity = Mathf.Lerp(deskLight.intensity, intensity * 3f, Time.deltaTime * 2f);
            }

            // ── Win condition ──────────────────────────────────────────────────
            if (_activeZoneCount <= 0)
            {
                _done = true;
                Debug.Log("[DeskFire] All zones extinguished!");
                if (deskHalo != null) deskHalo.SetActive(false);
                if (deskExtinguishSound != null)
                 AudioSource.PlayClipAtPoint(deskExtinguishSound, transform.position);
                OnDeskFullyExtinguished?.Invoke();
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public bool IsFullyExtinguished => _done;
        public int ActiveZoneCount => _activeZoneCount;
        public int TotalZones => zones != null ? zones.Length : 0;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (zones == null) return;

            // Draw the sweep axis on the desk
            Vector3 axis = transform.TransformDirection(sweepAxisLocal).normalized;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, axis * 0.5f);
            Gizmos.DrawRay(transform.position, -axis * 0.5f);

            // Per-zone state
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null) continue;
                Gizmos.color = zones[i].IsExtinguished ? Color.green : Color.red;
                Gizmos.DrawWireCube(zones[i].transform.position, new Vector3(0.3f, 0.4f, 0.3f));
            }
        }
#endif
    }
}
