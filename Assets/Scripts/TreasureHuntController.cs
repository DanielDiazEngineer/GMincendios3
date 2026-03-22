using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Treasure Hunt game controller.
    /// Listens to AnchorManager events and manages two stages:
    ///   PREP  → No anchors exist, show UI to place them
    ///   PLAY  → Anchors loaded, spawn gameplay objects, hide prep UI
    ///
    /// This script contains NO anchor logic — it only reacts to events.
    /// 
    /// Setup:
    ///   1. Attach to a "GameController" GameObject
    ///   2. Assign anchorManager reference
    ///   3. Assign gameplayPrefabs[] matching AnchorManager.anchorTypes[] order
    ///      (e.g. index 0 = treasure chest, index 1 = clue marker, index 2 = trap)
    ///   4. Optionally assign prepUI / playUI root objects to toggle visibility
    ///   5. Wire OnAllTreasuresFound for win condition
    /// </summary>
    public class TreasureHuntController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AnchorManager anchorManager;

        [Header("Gameplay Prefabs (by anchor type index)")]
        [Tooltip("Maps 1:1 with AnchorManager.anchorTypes[]. These are the GAMEPLAY versions, not the anchor marker prefabs.")]
        [SerializeField] private GameObject[] gameplayPrefabs;

        [Header("UI Roots (optional)")]
        [Tooltip("Root object for prep/setup UI. Hidden during play.")]
        [SerializeField] private GameObject prepUI;
        [Tooltip("Root object for gameplay UI (score, hints, etc). Hidden during prep.")]
        [SerializeField] private GameObject playUI;

        [Header("Events")]
        public UnityEvent OnGameStarted;
        public UnityEvent<GameObject, int> OnTreasureSpawned;    // spawnedObject, typeIndex
        public UnityEvent<GameObject> OnTreasureCollected;
        public UnityEvent OnAllTreasuresCollected;

        // ─── State ──────────────────────────────────────────────────────

        public enum GameStage { Init, Prep, Play }

        public GameStage CurrentStage { get; private set; } = GameStage.Init;

        private readonly List<GameObject> _spawnedGameplayObjects = new();
        private int _totalTreasures = 0;
        private int _collectedCount = 0;

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void OnEnable()
        {
            if (anchorManager == null)
            {
                Debug.LogError("[TreasureHunt] AnchorManager reference missing.");
                return;
            }

            anchorManager.OnAnchorsLoaded.AddListener(HandleAnchorsLoaded);
            anchorManager.OnNoSavedAnchorsFound.AddListener(HandleNoAnchorsFound);
            anchorManager.OnAnchorCreated.AddListener(HandleAnchorCreated);
            anchorManager.OnAllAnchorsErased.AddListener(HandleAllErased);
        }

        private void OnDisable()
        {
            if (anchorManager == null) return;

            anchorManager.OnAnchorsLoaded.RemoveListener(HandleAnchorsLoaded);
            anchorManager.OnNoSavedAnchorsFound.RemoveListener(HandleNoAnchorsFound);
            anchorManager.OnAnchorCreated.RemoveListener(HandleAnchorCreated);
            anchorManager.OnAllAnchorsErased.RemoveListener(HandleAllErased);
        }

        // ─── Event Handlers ─────────────────────────────────────────────

        /// <summary>
        /// Anchors loaded from storage → enter play mode.
        /// </summary>
        private void HandleAnchorsLoaded(List<OVRSpatialAnchor> anchors)
        {
            Debug.Log($"[TreasureHunt] {anchors.Count} anchor(s) loaded. Starting game.");
            EnterPlayStage(anchors);
        }

        /// <summary>
        /// No saved anchors → enter prep mode so user can place them.
        /// </summary>
        private void HandleNoAnchorsFound()
        {
            Debug.Log("[TreasureHunt] No anchors found. Entering prep stage.");
            EnterPrepStage();
        }

        /// <summary>
        /// New anchor placed during prep. Could auto-transition or wait for user.
        /// </summary>
        private void HandleAnchorCreated(OVRSpatialAnchor anchor, int typeIndex)
        {
            var names = anchorManager.GetAnchorTypeNames();
            var name = typeIndex < names.Length ? names[typeIndex] : "Unknown";
            Debug.Log($"[TreasureHunt] Anchor placed: {name}. Place more or reload scene to play.");
        }

        /// <summary>
        /// All anchors erased → back to prep.
        /// </summary>
        private void HandleAllErased()
        {
            Debug.Log("[TreasureHunt] Anchors erased. Back to prep.");
            CleanupGameplayObjects();
            EnterPrepStage();
        }

        // ─── Stage Transitions ──────────────────────────────────────────

        private void EnterPrepStage()
        {
            CurrentStage = GameStage.Prep;
            SetUIVisibility(prep: true, play: false);
            CleanupGameplayObjects();
        }

        private void EnterPlayStage(List<OVRSpatialAnchor> anchors)
        {
            CurrentStage = GameStage.Play;
            SetUIVisibility(prep: false, play: true);

            _collectedCount = 0;
            _totalTreasures = 0;

            SpawnGameplayObjects(anchors);

            OnGameStarted?.Invoke();
        }

        // ─── Gameplay Object Spawning ───────────────────────────────────

        /// <summary>
        /// For each loaded anchor, spawn the corresponding gameplay prefab at the anchor position.
        /// The anchor's own prefab (from AnchorManager) can be a simple invisible marker;
        /// the gameplay prefab here is the visible treasure/clue/trap.
        /// </summary>
        private void SpawnGameplayObjects(List<OVRSpatialAnchor> anchors)
        {
            foreach (var anchor in anchors)
            {
                // Determine type from the anchor's UUID via AnchorManager
                int typeIndex = anchorManager.GetAnchorTypeIndex(anchor.Uuid);
                typeIndex = Mathf.Clamp(typeIndex, 0, gameplayPrefabs.Length - 1);

                if (typeIndex >= gameplayPrefabs.Length || gameplayPrefabs[typeIndex] == null)
                {
                    Debug.LogWarning($"[TreasureHunt] No gameplay prefab for type index {typeIndex}. Skipping.");
                    continue;
                }

                var go = Instantiate(
                    gameplayPrefabs[typeIndex],
                    anchor.transform.position,
                    anchor.transform.rotation
                );

                // Optionally parent to anchor so it tracks if anchor drifts
                go.transform.SetParent(anchor.transform, worldPositionStays: true);

                // Add collectible behavior if not already on prefab
                var collectible = go.GetComponent<TreasureCollectible>();
                if (collectible == null)
                    collectible = go.AddComponent<TreasureCollectible>();

                collectible.Init(this, typeIndex);

                _spawnedGameplayObjects.Add(go);
                _totalTreasures++;

                OnTreasureSpawned?.Invoke(go, typeIndex);
                Debug.Log($"[TreasureHunt] Spawned gameplay object type {typeIndex} at {anchor.transform.position}");
            }

            Debug.Log($"[TreasureHunt] Total treasures to find: {_totalTreasures}");
        }

        // ─── Collection ─────────────────────────────────────────────────

        /// <summary>
        /// Called by TreasureCollectible when player interacts with a treasure.
        /// </summary>
        public void CollectTreasure(GameObject treasure)
        {
            if (CurrentStage != GameStage.Play) return;

            _collectedCount++;
            _spawnedGameplayObjects.Remove(treasure);

            OnTreasureCollected?.Invoke(treasure);
            Debug.Log($"[TreasureHunt] Collected! {_collectedCount}/{_totalTreasures}");

            Destroy(treasure);

            if (_collectedCount >= _totalTreasures)
            {
                Debug.Log("[TreasureHunt] All treasures collected!");
                OnAllTreasuresCollected?.Invoke();
            }
        }

        // ─── Public Getters ─────────────────────────────────────────────

        public int TotalTreasures => _totalTreasures;
        public int CollectedCount => _collectedCount;
        public int RemainingCount => _totalTreasures - _collectedCount;

        // ─── Helpers ────────────────────────────────────────────────────

        private void CleanupGameplayObjects()
        {
            foreach (var go in _spawnedGameplayObjects)
            {
                if (go != null) Destroy(go);
            }
            _spawnedGameplayObjects.Clear();
            _totalTreasures = 0;
            _collectedCount = 0;
        }

        private void SetUIVisibility(bool prep, bool play)
        {
            if (prepUI != null) prepUI.SetActive(prep);
            if (playUI != null) playUI.SetActive(play);
        }
    }

    // ─── Collectible Component ──────────────────────────────────────────

    /// <summary>
    /// Attach to gameplay prefabs (or auto-added by TreasureHuntController).
    /// Handles player interaction to collect the treasure.
    /// 
    /// Requires a Collider (set as trigger) on the GameObject.
    /// For hand interaction: use a SphereCollider trigger + OVR grab/poke detection,
    /// or simply OnTriggerEnter with a hand collider tag.
    /// </summary>
    public class TreasureCollectible : MonoBehaviour
    {
        private TreasureHuntController _controller;
        private int _typeIndex;
        private bool _collected = false;

        [Header("Optional FX")]
        [SerializeField] private AudioClip collectSound;
        [SerializeField] private GameObject collectVFX;

        public void Init(TreasureHuntController controller, int typeIndex)
        {
            _controller = controller;
            _typeIndex = typeIndex;

            // Ensure there's a trigger collider
            var col = GetComponent<Collider>();
            if (col == null)
            {
                var sphere = gameObject.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = 0.15f; // 15cm grab radius
            }
            else if (!col.isTrigger)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Filter: only collect on hand/controller contact
            // Adjust tag or layer to match your hand colliders
            if (_collected) return;
            if (!other.CompareTag("Player") && !IsHandCollider(other)) return;

            Collect();
        }

        /// <summary>
        /// Can also be called directly from a poke/grab event if using Interaction SDK.
        /// </summary>
        public void Collect()
        {
            if (_collected) return;
            _collected = true;

            if (collectSound != null)
                AudioSource.PlayClipAtPoint(collectSound, transform.position);

            if (collectVFX != null)
                Instantiate(collectVFX, transform.position, Quaternion.identity);

            _controller.CollectTreasure(gameObject);
        }

        private bool IsHandCollider(Collider col)
        {
            // Check common patterns for Meta hand tracking colliders
            var name = col.gameObject.name.ToLower();
            return name.Contains("hand") || name.Contains("finger") || name.Contains("poke");
        }
    }
}
