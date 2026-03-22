using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// World-space menu UI for Fire Prevention Training.
    ///
    /// THIS GameObject is the same as AnchorManager.anchorMenuPanel.
    /// AnchorManager hides it during placement and re-shows it after.
    /// This script only configures which BUTTONS are visible.
    ///
    /// Modes:
    ///   PREP (no saved anchors)
    ///     → Place Fire, Place Extinguisher, Erase All
    ///     → After placing 1+ anchors: also shows Start Game
    ///
    ///   READY (anchors loaded from storage)
    ///     → Start Game, Erase All
    ///
    /// Setup:
    ///   1. This GO = AnchorManager.anchorMenuPanel
    ///   2. World-space Canvas child (scale ~0.001)
    ///   3. Assign buttons in inspector
    ///   4. For poke: BoxCollider + PokeInteractable on each button
    ///      OR OVRRaycaster on canvas for pinch-to-click
    /// </summary>
    public class FireTrainingMenuUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AnchorManager anchorManager;
        [SerializeField] private FireTrainingController gameController;

        [Header("Follow Rig")]
        [SerializeField] private Transform headTransform;
        [SerializeField] private float spawnDistance = 0.5f;
        [SerializeField] private float spawnHeightOffset = -0.1f;

        [Header("Buttons")]
        [SerializeField] private Button placeFireButton;
        [SerializeField] private Button placeExtinguisherButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button eraseAllButton;

        [Header("UI Elements")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text statusText;

        // ─── Lifecycle ─────────────────────────────────────────────────

        private void Start()
        {
            WireButtons();
            SubscribeToEvents();
            SetStatus("Inicializando...");
        }

        private void OnEnable()
        {
            RepositionInFrontOfUser();

            // Refresh button visibility every time panel is shown
            // (AnchorManager re-shows this panel after placement)
            RefreshButtonVisibility();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        // ─── Button Wiring ─────────────────────────────────────────────

        private void WireButtons()
        {
            if (placeFireButton != null)
            {
                placeFireButton.onClick.AddListener(() =>
                {
                    anchorManager.StartPlacing(FireTrainingController.FIRE_TYPE_INDEX);
                    SetStatus("Colocando: FUEGO\nApunta y pellizca para confirmar.");
                });
            }

            if (placeExtinguisherButton != null)
            {
                placeExtinguisherButton.onClick.AddListener(() =>
                {
                    anchorManager.StartPlacing(FireTrainingController.EXTINGUISHER_TYPE_INDEX);
                    SetStatus("Colocando: EXTINTOR\nApunta y pellizca para confirmar.");
                });
            }

            if (startGameButton != null)
            {
                startGameButton.onClick.AddListener(() =>
                {
                    gameController.StartGame();
                    // Hide the entire menu panel during gameplay
                    gameObject.SetActive(false);
                });
            }

            if (eraseAllButton != null)
            {
                eraseAllButton.onClick.AddListener(() =>
                {
                    SetStatus("Borrando anclas...");
                    gameController.EraseAllAnchors();
                });
            }
        }

        // ─── Event Subscriptions ───────────────────────────────────────

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

        // ─── Event Handlers ────────────────────────────────────────────

        private void HandleAnchorCreated(OVRSpatialAnchor anchor, int typeIndex)
        {
            string typeName = typeIndex == FireTrainingController.FIRE_TYPE_INDEX
                ? "Fuego" : "Extintor";

            int count = gameController.PrepAnchorCount;
            SetStatus($"Ancla colocada: {typeName} ({count} total)\nColoca mas o inicia el entrenamiento.");

            // Now that we have anchors, refresh to show Start Game button
            RefreshButtonVisibility();
            RepositionInFrontOfUser();
        }

        private void HandleAnchorsLoaded(List<OVRSpatialAnchor> anchors)
        {
            SetStatus($"{anchors.Count} ancla(s) detectadas.\nInicia o borra para reconfigurar.");
            RefreshButtonVisibility();
            RepositionInFrontOfUser();
        }

        private void HandleAllErased()
        {
            SetStatus("Anclas borradas. Coloca nuevos puntos.");
            RefreshButtonVisibility();
        }

        private void HandleNoAnchorsFound()
        {
            SetStatus("Sin anclas guardadas.\nColoca fuegos y el extintor.");
            RefreshButtonVisibility();
        }

        // ─── Button Visibility ─────────────────────────────────────────

        /// <summary>
        /// Configures which buttons are visible based on current game stage.
        /// Called on OnEnable (panel re-shown) and after events.
        /// </summary>
        private void RefreshButtonVisibility()
        {
            var stage = gameController.CurrentStage;

            switch (stage)
            {
                case FireTrainingController.GameStage.Init:
                case FireTrainingController.GameStage.Prep:
                    // Show placement buttons always
                    SetButtonActive(placeFireButton, true);
                    SetButtonActive(placeExtinguisherButton, true);
                    SetButtonActive(eraseAllButton, true);
                    // Show Start only if user has placed at least 1 anchor
                    SetButtonActive(startGameButton, gameController.PrepAnchorCount > 0);

                    if (titleText != null)
                        titleText.text = gameController.PrepAnchorCount > 0
                            ? "CONFIGURACION"
                            : "COLOCAR ANCLAS";
                    break;

                case FireTrainingController.GameStage.Ready:
                    // Loaded from storage — only Start and Erase
                    SetButtonActive(placeFireButton, false);
                    SetButtonActive(placeExtinguisherButton, false);
                    SetButtonActive(startGameButton, true);
                    SetButtonActive(eraseAllButton, true);

                    if (titleText != null) titleText.text = "ENTRENAMIENTO LISTO";
                    break;

                case FireTrainingController.GameStage.Play:
                case FireTrainingController.GameStage.Win:
                    // Menu should be hidden during these stages
                    break;
            }
        }

        private void SetButtonActive(Button button, bool active)
        {
            if (button != null) button.gameObject.SetActive(active);
        }

        // ─── Positioning ───────────────────────────────────────────────

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

        // ─── Helpers ───────────────────────────────────────────────────

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
            Debug.Log($"[FireMenuUI] {message}");
        }
    }
}
