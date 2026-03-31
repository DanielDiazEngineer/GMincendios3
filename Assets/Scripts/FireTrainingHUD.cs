using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Floating HUD for fire extinguisher training.
    /// Drives a 3-step procedure panel, a real-time distance indicator, and a win screen.
    ///
    /// ── SETUP ────────────────────────────────────────────────────────────────
    /// 1. Create a world-space Canvas (RenderMode = WorldSpace, scale ~0.001).
    ///    Attach it to CenterEyeAnchor or use StayInView so it follows the user.
    /// 2. Inside the Canvas create:
    ///      ├─ Step1Panel  (GameObject with Text: "1) Retira el seguro")
    ///      ├─ Step2Panel  (GameObject with Text: "2) Apunta al fuego")
    ///      ├─ Step3Panel  (GameObject with Text: "3) Acciona extintor")
    ///      ├─ DistanceText (UI Text — shown during all steps)
    ///      └─ WinPanel   (full-screen panel with congratulatory message)
    /// 3. Assign all references in Inspector.
    /// 4. Call ActivateHUD() from leveldemonew.CompleteIntro().
    /// 5. Call SetFire(fire1) from leveldemonew.AboutStart() (pass the fire GO).
    ///
    /// ── STEP LOGIC ──────────────────────────────────────────────────────────
    ///   Step 1 "Retira el seguro"   → auto-advances after step1Duration seconds
    ///   Step 2 "Apunta al fuego"    → advances when extinguisher starts spraying
    ///   Step 3 "Acciona extintor"   → stays until fire is fully extinguished
    ///   Done   → hides steps, shows win panel
    ///
    /// ── DISTANCE HUD ────────────────────────────────────────────────────────
    ///   < closeThreshold m  → red   (too close)
    ///   idealMin – idealMax → yellow (ideal range)
    ///   > idealMax m        → white  (too far, move closer)
    /// </summary>
    public class FireTrainingHUD : MonoBehaviour
    {
        // ── Step panels ──────────────────────────────────────────────────────

        [Header("Step Panels (one GameObject each)")]
        [Tooltip("Panel shown for step 1: Retira el seguro")]
        [SerializeField] private GameObject step1Panel;

        [Tooltip("Panel shown for step 2: Apunta al fuego")]
        [SerializeField] private GameObject step2Panel;

        [Tooltip("Panel shown for step 3: Acciona extintor")]
        [SerializeField] private GameObject step3Panel;

        // ── Distance HUD ─────────────────────────────────────────────────────

        [Header("Distance Indicator")]
        [SerializeField] private Text distanceText;

        [Tooltip("Below this distance (m) the text turns red — user is too close.")]
        [SerializeField] private float closeThreshold = 1.5f;

        [Tooltip("Ideal operating range: text is yellow between closeThreshold and this value.")]
        [SerializeField] private float idealMaxDistance = 3.5f;

        [SerializeField] private Color tooCloseColor  = new Color(0.9f, 0.2f, 0.2f);   // red
        [SerializeField] private Color idealColor     = new Color(1.0f, 0.85f, 0.0f);  // yellow
        [SerializeField] private Color tooFarColor    = Color.white;

        // ── Win panel ────────────────────────────────────────────────────────

        [Header("Win Panel")]
        [Tooltip("Shown after fire is fully extinguished.")]
        [SerializeField] private GameObject winPanel;

        // ── References ───────────────────────────────────────────────────────

        [Header("Scene References")]
        [SerializeField] private ExtinguisherBehavior extinguisher;

        [Tooltip("CenterEyeAnchor or main camera transform — used for distance calculation.")]
        [SerializeField] private Transform playerHead;

        // ── Timing ───────────────────────────────────────────────────────────

        [Header("Timing")]
        [Tooltip("Seconds before step 1 auto-advances to step 2.")]
        [SerializeField] private float step1Duration = 7f;

        // ── Runtime state ─────────────────────────────────────────────────────

        private FireBehavior _fire;
        private bool _active;

        private enum Step { S1_RemovePin, S2_AimAtFire, S3_Spray, Done }
        private Step _step = Step.S1_RemovePin;

        // ── Unity lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            // Start fully hidden — ActivateHUD() turns things on
            SetStepPanels(false, false, false);
            if (distanceText != null) distanceText.gameObject.SetActive(false);
            if (winPanel   != null) winPanel.SetActive(false);
        }

        private void Update()
        {
            if (!_active || _step == Step.Done) return;

            // Step 2 → 3: extinguisher started spraying
            if (_step == Step.S2_AimAtFire && extinguisher != null && extinguisher._isSpraying)
            {
                GoToStep(Step.S3_Spray);
            }

            // Win condition: fire fully out
            if (_fire != null && _fire.IsExtinguished)
            {
                GoToStep(Step.Done);
                ShowWin();
            }

            UpdateDistanceHUD();
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Call from leveldemonew.CompleteIntro() — starts the HUD sequence.
        /// </summary>
        public void ActivateHUD()
        {
            _active = true;
            if (distanceText != null) distanceText.gameObject.SetActive(true);
            GoToStep(Step.S1_RemovePin);
            StartCoroutine(AutoAdvanceFromStep1());
            Debug.Log("[FireTrainingHUD] HUD activated.");
        }

        /// <summary>
        /// Register the active fire object so we can poll IsExtinguished and calculate distance.
        /// Call from leveldemonew.AboutStart() — pass fire1 (the fire GameObject).
        /// </summary>
        public void SetFire(GameObject fireGO)
        {
            if (fireGO == null) return;
            _fire = fireGO.GetComponent<FireBehavior>();
            if (_fire == null)
                Debug.LogWarning("[FireTrainingHUD] SetFire: no FireBehavior on the provided GameObject.");
        }

        /// <summary>Overload that accepts a FireBehavior directly.</summary>
        public void SetFire(FireBehavior fb) => _fire = fb;

        // ── Step logic ───────────────────────────────────────────────────────

        private IEnumerator AutoAdvanceFromStep1()
        {
            yield return new WaitForSeconds(step1Duration);
            if (_active && _step == Step.S1_RemovePin)
            {
                Debug.Log("[FireTrainingHUD] Step 1 auto-advanced to step 2.");
                GoToStep(Step.S2_AimAtFire);
            }
        }

        private void GoToStep(Step target)
        {
            _step = target;
            switch (target)
            {
                case Step.S1_RemovePin:
                    SetStepPanels(true,  false, false);
                    break;
                case Step.S2_AimAtFire:
                    SetStepPanels(false, true,  false);
                    break;
                case Step.S3_Spray:
                    SetStepPanels(false, false, true);
                    break;
                case Step.Done:
                    SetStepPanels(false, false, false);
                    break;
            }
        }

        private void SetStepPanels(bool s1, bool s2, bool s3)
        {
            if (step1Panel != null) step1Panel.SetActive(s1);
            if (step2Panel != null) step2Panel.SetActive(s2);
            if (step3Panel != null) step3Panel.SetActive(s3);
        }

        // ── Distance HUD ─────────────────────────────────────────────────────

        private void UpdateDistanceHUD()
        {
            if (distanceText == null || _fire == null || playerHead == null) return;

            float dist = Vector3.Distance(playerHead.position, _fire.transform.position);
            distanceText.text = $"Distancia al fuego: {dist:F1} m";

            if (dist < closeThreshold)
                distanceText.color = tooCloseColor;          // red  — back up!
            else if (dist <= idealMaxDistance)
                distanceText.color = idealColor;             // yellow — ideal range
            else
                distanceText.color = tooFarColor;            // white — move closer
        }

        // ── Win screen ───────────────────────────────────────────────────────

        private void ShowWin()
        {
            if (distanceText != null) distanceText.gameObject.SetActive(false);
            if (winPanel     != null) winPanel.SetActive(true);
            Debug.Log("[FireTrainingHUD] Win panel shown.");
        }
    }
}
