using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Floating HUD for fire extinguisher training.
    /// Drives a 3-step procedure panel, a real-time distance indicator, and a win screen.
    ///
    /// Win is triggered externally by leveldemonew.OnOneFireDone() via TriggerWin()
    /// once all fires are out — HUD does not poll fires directly for win condition.
    ///
    /// ── SETUP ────────────────────────────────────────────────────────────────
    /// 1. World-space Canvas (RenderMode = WorldSpace, scale ~0.001).
    ///    Attach to CenterEyeAnchor or use StayInView.
    /// 2. Inside the Canvas:
    ///      ├─ Step1Panel  ("1) Retira el seguro")
    ///      ├─ Step2Panel  ("2) Apunta al fuego")
    ///      ├─ Step3Panel  ("3) Acciona extintor")
    ///      ├─ DistanceText
    ///      └─ WinPanel
    /// 3. Call ActivateHUD() from leveldemonew.CompleteIntro().
    /// 4. Call SetFire(fb) from leveldemonew.AboutStart() — pass the primary fire
    ///    (box fire) for distance tracking. Other fires don't need HUD registration.
    /// 5. Call TriggerWin() from leveldemonew when all fires are extinguished.
    /// </summary>
    public class FireTrainingHUD : MonoBehaviour
    {
        [Header("Step Panels")]
        [SerializeField] private GameObject step1Panel;
        [SerializeField] private GameObject step2Panel;
        [SerializeField] private GameObject step3Panel;

        [Header("Distance Indicator")]
        [SerializeField] private Text distanceText;
        [SerializeField] private Text sprayProgressText;

        [Tooltip("Below this distance the text turns red — too close.")]
        [SerializeField] private float closeThreshold = 1.5f;

        [Tooltip("Ideal upper bound — text is yellow between closeThreshold and this.")]
        [SerializeField] private float idealMaxDistance = 3.5f;

        [SerializeField] private Color tooCloseColor = new Color(0.9f, 0.2f, 0.2f);
        [SerializeField] private Color idealColor = new Color(1.0f, 0.85f, 0.0f);
        [SerializeField] private Color tooFarColor = Color.white;

        [Header("Win Panel")]
        [SerializeField] private GameObject winPanel;

        [Header("Scene References")]
        [SerializeField] private ExtinguisherBehavior extinguisher;

        [Tooltip("CenterEyeAnchor or main camera — used for distance calculation.")]
        [SerializeField] private Transform playerHead;

        [Header("Timing")]
        [Tooltip("Seconds before step 1 auto-advances to step 2.")]
        [SerializeField] private float step1Duration = 7f;
        [SerializeField] private float shootEnableDelay = 1.5f;
        [SerializeField] private float aimHoldDuration = 1.5f;
        private float _aimHoldTimer = 0f;
        [Tooltip("Seconds of aim dropout allowed before hold timer resets.")]
        [SerializeField] private float aimDropoutTolerance = 0.25f;
        private float _aimDropoutTimer = 0f;
        [Tooltip("If player never aims, auto-advance from step 2 to step 3 after this many seconds. 0 = disabled.")]
        [SerializeField] private float step2MaxDuration = 5f;

        // ── State ──────────────────────────────────────────────────────────────

        private FireBehavior _fire;
        private readonly List<FireBehavior> _allFires = new();

        private bool _active;
        private bool _won;

        private enum Step { S1_RemovePin, S2_AimAtFire, S3_Spray, Done }
        private Step _step = Step.S1_RemovePin;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Start()
        {
            SetStepPanels(false, false, false);
            if (distanceText != null) distanceText.gameObject.SetActive(false);
            if (winPanel != null) winPanel.SetActive(false);
        }

        private void Update()
        {
            if (!_active || _step == Step.Done) return;

            // Step 2 → 3: extinguisher aimed at fire
            if (_step == Step.S2_AimAtFire && extinguisher != null)
            {
                if (extinguisher.IsAimingAtFire)
                {
                    _aimDropoutTimer = 0f;
                    _aimHoldTimer += Time.deltaTime;
                    if (_aimHoldTimer >= aimHoldDuration)
                        GoToStep(Step.S3_Spray);
                }
                else
                {
                    _aimDropoutTimer += Time.deltaTime;
                    if (_aimDropoutTimer >= aimDropoutTolerance)
                        _aimHoldTimer = 0f; // only reset after sustained non-aim
                }
            }

            UpdateDistanceHUD();
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Call from leveldemonew.CompleteIntro() — starts the HUD sequence.</summary>
        public void ActivateHUD()
        {
            if (extinguisher != null)
                extinguisher.shootingEnabled = false;
            _active = true;
            _won = false;
            if (distanceText != null) distanceText.gameObject.SetActive(true);
            GoToStep(Step.S1_RemovePin);
            StartCoroutine(AutoAdvanceFromStep1());
            Debug.Log("[FireTrainingHUD] HUD activated.");
        }

        /// <summary>
        /// Register the primary fire for distance tracking.
        /// Call from leveldemonew.AboutStart() with the box fire's FireBehavior.
        /// </summary>
        public void SetFire(FireBehavior fb) => _fire = fb;

        /// <summary>Overload accepting a GameObject — finds FireBehavior on it or children.</summary>
        public void SetFire(GameObject fireGO)
        {
            if (fireGO == null) return;
            _fire = fireGO.GetComponentInChildren<FireBehavior>();
            if (_fire == null)
                Debug.LogWarning("[FireTrainingHUD] SetFire: no FireBehavior found on provided GameObject.");
        }

        /// <summary>
        /// Call from leveldemonew when all fires are extinguished.
        /// HUD does not track fire completion itself — leveldemonew owns that count.
        /// </summary>
        public void TriggerWin()
        {
            if (_won) return;
            _won = true;
            _active = false;
            GoToStep(Step.Done);
            ShowWin();
        }

        // ── Step Logic ─────────────────────────────────────────────────────────

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
                case Step.S1_RemovePin: SetStepPanels(true, false, false); break;
                case Step.S2_AimAtFire:
                    SetStepPanels(false, true, false);
                    _aimHoldTimer = 0f;
                    _aimDropoutTimer = 0f;
                    if (step2MaxDuration > 0f)
                        StartCoroutine(Step2MaxDelaySkip());
                    break;
                case Step.S3_Spray:
                    SetStepPanels(false, false, true);
                    StartCoroutine(EnableShootingAfterDelay());
                    break;
                case Step.Done: SetStepPanels(false, false, false); break;
            }
        }

        private void SetStepPanels(bool s1, bool s2, bool s3)
        {
            if (step1Panel != null) step1Panel.SetActive(s1);
            if (step2Panel != null) step2Panel.SetActive(s2);
            if (step3Panel != null) step3Panel.SetActive(s3);
        }

        private IEnumerator Step2MaxDelaySkip()
        {
            yield return new WaitForSeconds(step2MaxDuration);
            if (_active && _step == Step.S2_AimAtFire)
            {
                Debug.Log("[FireTrainingHUD] Step 2 max delay reached — auto-advancing to step 3.");
                GoToStep(Step.S3_Spray);
            }
        }

        private IEnumerator EnableShootingAfterDelay()
        {
            yield return new WaitForSeconds(shootEnableDelay);
            if (extinguisher != null)
                extinguisher.shootingEnabled = true;
            Debug.Log("[FireTrainingHUD] Shooting ENABLED.");
        }

        // ── Distance HUD ───────────────────────────────────────────────────────

        private void UpdateDistanceHUD()
        {
            if (distanceText == null || playerHead == null) return;

            // Find nearest active fire
            FireBehavior nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var fb in _allFires)
            {
                if (fb == null || fb.IsExtinguished) continue;
                float d = Vector3.Distance(playerHead.position, fb.transform.position);
                if (d < nearestDist) { nearestDist = d; nearest = fb; }
            }

            if (nearest == null) { distanceText.gameObject.SetActive(false); return; }

            distanceText.text = $"Distancia al fuego: {nearestDist:F1} m";
            if (nearestDist < closeThreshold)
                distanceText.color = tooCloseColor;
            else if (nearestDist <= idealMaxDistance)
                distanceText.color = idealColor;
            else
                distanceText.color = tooFarColor;




            //SPRAY PROGRESS
            if (sprayProgressText != null)
            {
                if (extinguisher != null && extinguisher.HasTarget)
                {
                    float pct = extinguisher.CurrentTargetProgress * 100f;
                    sprayProgressText.gameObject.SetActive(true);
                    sprayProgressText.text = $"Extinción: {pct:F0}%";
                    sprayProgressText.color = Color.Lerp(Color.red, Color.green,
                                                        extinguisher.CurrentTargetProgress);
                }
                else
                {
                    sprayProgressText.gameObject.SetActive(false);
                }
            }
        }

        public void RegisterFire(FireBehavior fb)
        {
            if (fb != null && !_allFires.Contains(fb))
                _allFires.Add(fb);
        }

        // ── Win ────────────────────────────────────────────────────────────────

        private void ShowWin()
        {
            if (distanceText != null) distanceText.gameObject.SetActive(false);
            if (winPanel != null) winPanel.SetActive(true);
            Debug.Log("[FireTrainingHUD] Win panel shown.");
        }
    }
}
