using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Fire Prevention Training controller for Quest 3.
    ///
    /// Anchor types (configure in AnchorManager.anchorTypes[]):
    ///   Index 0 = Fire
    ///   Index 1 = Extinguisher
    ///
    /// Flow:
    ///   Scene loads → AnchorManager checks for saved anchors
    ///     A) No anchors → PREP: menu shows Place Fire / Place Extinguisher
    ///        User places anchors → they accumulate in _prepAnchors
    ///        User hits "Start Game" → enters PLAY using _prepAnchors
    ///     B) Anchors found → READY: menu shows Start Game / Erase All
    ///        User hits Start → enters PLAY using loaded anchors
    ///     C) PLAY: fires spawn, extinguisher spawns
    ///        All fires extinguished → WIN canvas
    ///
    /// The menu panel (FireTrainingMenuUI) is the SAME GameObject as
    /// AnchorManager.anchorMenuPanel — AnchorManager toggles it during
    /// placement, this controller does NOT touch it directly.
    /// Only playCanvas and winCanvas are managed here.
    /// </summary>
    public class FireTrainingController : MonoBehaviour
    {
        // ─── Inspector ─────────────────────────────────────────────────

        [Header("References")]
        [SerializeField] private AnchorManager anchorManager;

        [Header("Gameplay Prefabs")]
        [Tooltip("Visible fire prefab (particle system + collider).")]
        [SerializeField] private GameObject firePrefab;
        [Tooltip("Extinguisher prefab (3D model + NozzleOrigin + grab).")]
        [SerializeField] private GameObject extinguisherPrefab;

        [Header("UI Canvases (managed by this controller)")]
        [Tooltip("HUD during gameplay — fire count, hints.")]
        [SerializeField] private GameObject playCanvas;
        [Tooltip("Shown when all fires extinguished.")]
        [SerializeField] private GameObject winCanvas;

        [Header("Play UI Elements")]
        [SerializeField] private UnityEngine.UI.Text fireCountText;

        [Header("Events")]
        public UnityEvent OnGameStarted;
        public UnityEvent<GameObject> OnFireSpawned;
        public UnityEvent<GameObject> OnFireExtinguished;
        public UnityEvent OnAllFiresExtinguished;

        // ─── Constants ─────────────────────────────────────────────────

        public const int FIRE_TYPE_INDEX = 0;
        public const int EXTINGUISHER_TYPE_INDEX = 1;

        // ─── State ─────────────────────────────────────────────────────

        public enum GameStage { Init, Prep, Ready, Play, Win }
        public GameStage CurrentStage { get; private set; } = GameStage.Init;

        private readonly List<GameObject> _spawnedFires = new();
        private GameObject _spawnedExtinguisher;

        // Anchors collected during prep (placed this session)
        private readonly List<OVRSpatialAnchor> _prepAnchors = new();
        // Anchors loaded from storage
        private List<OVRSpatialAnchor> _loadedAnchors;

        private int _totalFires;
        private int _extinguishedCount;

        // ─── Public Getters ────────────────────────────────────────────

        public int TotalFires => _totalFires;
        public int ExtinguishedCount => _extinguishedCount;
        public int RemainingFires => _totalFires - _extinguishedCount;
        public int PrepAnchorCount => _prepAnchors.Count;

        // ─── Lifecycle ─────────────────────────────────────────────────

        private void OnEnable()
        {
            if (anchorManager == null)
            {
                Debug.LogError("[FireTraining] AnchorManager reference missing.");
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

        private void Start()
        {
            // Hide gameplay canvases at start
            if (playCanvas != null) playCanvas.SetActive(false);
            if (winCanvas != null) winCanvas.SetActive(false);
        }

        // ─── Event Handlers ────────────────────────────────────────────

        private void HandleAnchorsLoaded(List<OVRSpatialAnchor> anchors)
        {
            Debug.Log($"[FireTraining] {anchors.Count} anchor(s) loaded from storage.");
            _loadedAnchors = anchors;
            CurrentStage = GameStage.Ready;
            // Menu UI handles showing Start/Erase via its own event listener
        }

        private void HandleNoAnchorsFound()
        {
            Debug.Log("[FireTraining] No saved anchors. Entering prep.");
            CurrentStage = GameStage.Prep;
            _prepAnchors.Clear();
            // Menu UI handles showing Place buttons via its own event listener
        }

        /// <summary>
        /// Called each time an anchor is created during prep.
        /// We track it so StartGame can use anchors placed this session.
        /// </summary>
        private void HandleAnchorCreated(OVRSpatialAnchor anchor, int typeIndex)
        {
            string typeName = typeIndex == FIRE_TYPE_INDEX ? "Fuego" : "Extintor";
            Debug.Log($"[FireTraining] Anchor placed: {typeName}. Total this session: {_prepAnchors.Count + 1}");

            _prepAnchors.Add(anchor);
        }

        private void HandleAllErased()
        {
            Debug.Log("[FireTraining] All anchors erased. Back to prep.");
            CleanupGameplay();
            _prepAnchors.Clear();
            _loadedAnchors = null;
            CurrentStage = GameStage.Prep;
        }

        // ─── Public API (called by menu UI) ────────────────────────────

        /// <summary>
        /// Start the game using whichever anchors are available:
        /// - If we loaded from storage → use _loadedAnchors
        /// - If user just placed them during prep → use _prepAnchors
        /// </summary>
        public void StartGame()
        {
            // Determine which anchors to use
            List<OVRSpatialAnchor> anchors;

            if (_loadedAnchors != null && _loadedAnchors.Count > 0)
            {
                anchors = _loadedAnchors;
                Debug.Log($"[FireTraining] Starting with {anchors.Count} loaded anchor(s).");
            }
            else if (_prepAnchors.Count > 0)
            {
                anchors = new List<OVRSpatialAnchor>(_prepAnchors);
                Debug.Log($"[FireTraining] Starting with {anchors.Count} freshly placed anchor(s).");
            }
            else
            {
                Debug.LogWarning("[FireTraining] No anchors available. Place some first.");
                return;
            }

            // Validate: need at least 1 fire
            bool hasFire = false;
            foreach (var a in anchors)
            {
                if (anchorManager.GetAnchorTypeIndex(a.Uuid) == FIRE_TYPE_INDEX)
                {
                    hasFire = true;
                    break;
                }
            }

            if (!hasFire)
            {
                Debug.LogWarning("[FireTraining] No fire anchors placed! Place at least one fire.");
                return;
            }

            // Enter play
            CurrentStage = GameStage.Play;
            if (playCanvas != null) playCanvas.SetActive(true);
            if (winCanvas != null) winCanvas.SetActive(false);

            _extinguishedCount = 0;
            _totalFires = 0;

            SpawnGameplayObjects(anchors);
            UpdateFireCountUI();

            OnGameStarted?.Invoke();
            Debug.Log("[FireTraining] Game started!");
        }

        /// <summary>
        /// Wire to "Erase All" button.
        /// </summary>
        public void EraseAllAnchors()
        {
            anchorManager.EraseAllAnchors();
        }

        // ─── Spawning ──────────────────────────────────────────────────

        private void SpawnGameplayObjects(List<OVRSpatialAnchor> anchors)
        {
            foreach (var anchor in anchors)
            {
                int typeIndex = anchorManager.GetAnchorTypeIndex(anchor.Uuid);

                if (typeIndex == FIRE_TYPE_INDEX && firePrefab != null)
                {
                    var fire = Instantiate(firePrefab, anchor.transform.position, anchor.transform.rotation);
                    fire.transform.SetParent(anchor.transform, worldPositionStays: true);

                    var fb = fire.GetComponent<FireBehavior>();
                    if (fb == null) fb = fire.AddComponent<FireBehavior>();
                    fb.Init(this);

                    _spawnedFires.Add(fire);
                    _totalFires++;

                    OnFireSpawned?.Invoke(fire);
                    Debug.Log($"[FireTraining] Fire spawned at {anchor.transform.position}");
                }
                else if (typeIndex == EXTINGUISHER_TYPE_INDEX && extinguisherPrefab != null)
                {
                    _spawnedExtinguisher = Instantiate(
                        extinguisherPrefab,
                        anchor.transform.position,
                        anchor.transform.rotation
                    );
                    _spawnedExtinguisher.transform.SetParent(anchor.transform, worldPositionStays: true);

                    var eb = _spawnedExtinguisher.GetComponent<ExtinguisherBehavior>();
                    if (eb == null) eb = _spawnedExtinguisher.AddComponent<ExtinguisherBehavior>();

                    Debug.Log($"[FireTraining] Extinguisher spawned at {anchor.transform.position}");
                }
            }

            if (_totalFires == 0)
                Debug.LogWarning("[FireTraining] No fire anchors found!");
            if (_spawnedExtinguisher == null)
                Debug.LogWarning("[FireTraining] No extinguisher anchor found!");

            Debug.Log($"[FireTraining] Fires to extinguish: {_totalFires}");
        }

        // ─── Fire Extinguishing ────────────────────────────────────────

        public void ReportFireExtinguished(GameObject fire)
        {
            if (CurrentStage != GameStage.Play) return;

            _extinguishedCount++;
            _spawnedFires.Remove(fire);

            OnFireExtinguished?.Invoke(fire);
            UpdateFireCountUI();
            Debug.Log($"[FireTraining] Fire extinguished! {_extinguishedCount}/{_totalFires}");

            Destroy(fire);

            if (_extinguishedCount >= _totalFires)
            {
                OnAllFiresExtinguished?.Invoke();
                EnterWinStage();
            }
        }

        private void EnterWinStage()
        {
            CurrentStage = GameStage.Win;
            if (playCanvas != null) playCanvas.SetActive(false);
            if (winCanvas != null) winCanvas.SetActive(true);
            Debug.Log("[FireTraining] All fires extinguished! Training complete.");
        }

        // ─── UI ────────────────────────────────────────────────────────

        private void UpdateFireCountUI()
        {
            if (fireCountText != null)
                fireCountText.text = $"Incendios: {RemainingFires} / {_totalFires}";
        }

        // ─── Cleanup ───────────────────────────────────────────────────

        private void CleanupGameplay()
        {
            foreach (var fire in _spawnedFires)
                if (fire != null) Destroy(fire);
            _spawnedFires.Clear();

            if (_spawnedExtinguisher != null)
            {
                Destroy(_spawnedExtinguisher);
                _spawnedExtinguisher = null;
            }

            _totalFires = 0;
            _extinguishedCount = 0;

            if (playCanvas != null) playCanvas.SetActive(false);
            if (winCanvas != null) winCanvas.SetActive(false);
        }
    }
}
