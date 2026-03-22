using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// World-space anchor menu for hand interaction (poke/touch).
    /// 
    /// Setup:
    ///   1. Create an empty GO as menu root → assign to AnchorManager.anchorMenuPanel
    ///   2. Position it ~0.5m in front of the camera rig (child of CenterEyeAnchor or follow script)
    ///   3. Add this script to the menu root
    ///   4. For each button:
    ///      - Use a world-space Canvas (RenderMode = WorldSpace, scale ~0.001)
    ///      - Add a BoxCollider on the button for poke detection
    ///      - Add PokeInteractable + PointableUnityEventWrapper (Interaction SDK)
    ///      - Wire WhenSelect → button.onClick
    ///   5. Assign your pre-built buttons to typeButtons[], eraseAllButton, cancelButton
    ///   6. Assign statusText for feedback
    ///   
    /// Hierarchy example (world-space poke panel):
    ///   CenterEyeAnchor (or use a FollowScript)
    ///     └─ AnchorMenuRoot (this script)  ← anchorManager.anchorMenuPanel
    ///          └─ Canvas (World Space, scale 0.001)
    ///               └─ Panel (background)
    ///                    ├─ TitleText
    ///                    ├─ TypeButton_0  ← BoxCollider + PokeInteractable + PointableUnityEventWrapper
    ///                    ├─ TypeButton_1
    ///                    ├─ TypeButton_2
    ///                    ├─ EraseAllButton
    ///                    ├─ CancelButton
    ///                    └─ StatusText
    ///
    /// Each poke button needs:
    ///   - BoxCollider (sized to button rect, e.g. 300x60x10)
    ///   - PokeInteractable (from Meta Interaction SDK)
    ///   - PointableUnityEventWrapper → WhenSelect wired to Button.onClick
    ///   - Or simpler: OVRRaycaster on canvas for pinch-to-click without poke setup
    /// </summary>
    public class AnchorMenuUIBeta : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AnchorManager anchorManager;

        [Header("Follow Rig (optional)")]
        [Tooltip("If set, menu repositions in front of this transform when shown.")]
        [SerializeField] private Transform headTransform;  // CenterEyeAnchor
        [SerializeField] private float spawnDistance = 0.5f;
        [SerializeField] private float spawnHeightOffset = -0.1f;

        [Header("Pre-built Buttons (assign in inspector)")]
        [Tooltip("One button per anchor type, in order matching AnchorManager.anchorTypes[].")]
        [SerializeField] private Button[] typeButtons;
        [SerializeField] private Button eraseAllButton;
        [SerializeField] private Button cancelButton;

        [Header("UI Elements")]
        [SerializeField] private Text statusText;

        private void Start()
        {
            WireTypeButtons();
            WireFixedButtons();
            SubscribeToEvents();
            SetStatus("Select an anchor type to place.");
        }

        private void OnEnable()
        {
            RepositionInFrontOfUser();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        // ─── Positioning ────────────────────────────────────────────────

        private void RepositionInFrontOfUser()
        {
            if (headTransform == null) return;

            var flatForward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;

            var targetPos = headTransform.position
                + flatForward * spawnDistance
                + Vector3.up * spawnHeightOffset;

            transform.position = targetPos;
            transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
        }

        // ─── Button Wiring ──────────────────────────────────────────────

        private void WireTypeButtons()
        {
            if (anchorManager == null)
            {
                Debug.LogError("[AnchorMenuUI] AnchorManager reference is missing.");
                return;
            }

            var names = anchorManager.GetAnchorTypeNames();

            for (int i = 0; i < typeButtons.Length; i++)
            {
                if (typeButtons[i] == null) continue;

                int capturedIndex = i;
                string typeName = i < names.Length ? names[i] : $"Type {i}";

                var label = typeButtons[i].GetComponentInChildren<Text>();
                if (label != null) label.text = typeName;

                typeButtons[i].onClick.AddListener(() =>
                {
                    anchorManager.StartPlacing(capturedIndex);
                    SetStatus($"Placing: {typeName}\nPoint and pinch to confirm.");
                });
            }

            if (typeButtons.Length != names.Length)
            {
                Debug.LogWarning($"[AnchorMenuUI] Button count ({typeButtons.Length}) != anchor type count ({names.Length}). Match them in inspector.");
            }
        }

        private void WireFixedButtons()
        {
            if (eraseAllButton != null)
            {
                eraseAllButton.onClick.AddListener(() =>
                {
                    SetStatus("Erasing all anchors...");
                    anchorManager.EraseAllAnchors();
                });
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(() =>
                {
                    anchorManager.CancelPlacing();
                    SetStatus("Placement cancelled.");
                });
            }
        }

        // ─── Event Handlers ─────────────────────────────────────────────

        private void SubscribeToEvents()
        {
            if (anchorManager == null) return;
            anchorManager.OnAnchorCreated.AddListener(HandleAnchorCreated);
            anchorManager.OnAnchorsLoaded.AddListener(HandleAnchorsLoaded);
            anchorManager.OnAllAnchorsErased.AddListener(HandleAllErased);
            anchorManager.OnNoSavedAnchorsFound.AddListener(HandleNoAnchorsFound);
        }

        private void UnsubscribeFromEvents()
        {
            if (anchorManager == null) return;
            anchorManager.OnAnchorCreated.RemoveListener(HandleAnchorCreated);
            anchorManager.OnAnchorsLoaded.RemoveListener(HandleAnchorsLoaded);
            anchorManager.OnAllAnchorsErased.RemoveListener(HandleAllErased);
            anchorManager.OnNoSavedAnchorsFound.RemoveListener(HandleNoAnchorsFound);
        }

        private void HandleAnchorCreated(OVRSpatialAnchor anchor, int typeIndex)
        {
            var names = anchorManager.GetAnchorTypeNames();
            var typeName = typeIndex < names.Length ? names[typeIndex] : "Unknown";
            SetStatus($"Anchor placed: {typeName}");
            gameObject.SetActive(true);
            RepositionInFrontOfUser();
        }

        private void HandleAnchorsLoaded(List<OVRSpatialAnchor> anchors)
        {
            SetStatus($"Loaded {anchors.Count} anchor(s).");
        }

        private void HandleAllErased()
        {
            SetStatus("All anchors erased. Place a new anchor.");
        }

        private void HandleNoAnchorsFound()
        {
            SetStatus("No saved anchors. Place your first anchor.");
        }

        // ─── Helpers ────────────────────────────────────────────────────

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            Debug.Log($"[AnchorMenuUI] {message}");
        }
    }
}
