using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Manages typed spatial anchors - create, load, and erase anchors with associated prefab types.
    /// Bypasses SpatialAnchorCoreBuildingBlock limitations by handling OVRSpatialAnchor directly.
    /// </summary>
    public class AnchorManager : MonoBehaviour
    {
        // ─── Inspector Fields ───────────────────────────────────────────

        [Header("Anchor Type Prefabs")]
        [Tooltip("Each entry maps a name to a prefab. Index = anchor type ID.")]
        [SerializeField] private AnchorTypeDef[] anchorTypes;

        [Header("Placement")]
        [SerializeField] private Transform rayOrigin;
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private LayerMask placementLayer = 1 << 10; // layer 10
        [SerializeField] private float maxRayDistance = 5f;

        [Header("UI")]
        [SerializeField] private GameObject anchorMenuPanel;
        [SerializeField] private GameObject placementPreview; // ghost/preview object

        [Header("Events")]
        public UnityEvent<OVRSpatialAnchor, int> OnAnchorCreated;  // anchor + typeIndex
        public UnityEvent<List<OVRSpatialAnchor>> OnAnchorsLoaded;
        public UnityEvent OnAllAnchorsErased;
        public UnityEvent OnNoSavedAnchorsFound;

        // ─── State ──────────────────────────────────────────────────────

        private bool _isPlacing = false;
        private int _selectedTypeIndex = 0;
        private Vector3 _hitPoint;
        private Quaternion _hitRotation;
        private readonly List<OVRSpatialAnchor> _activeAnchors = new();

        // ─── Storage Keys ───────────────────────────────────────────────

        private const string PREF_ANCHOR_COUNT = "anchor_count";
        private const string PREF_UUID_PREFIX = "anchor_uuid_";
        private const string PREF_TYPE_PREFIX = "anchor_type_";

        // ─── Data Types ─────────────────────────────────────────────────

        [Serializable]
        public class AnchorTypeDef
        {
            public string name;        // e.g. "Table", "Chair", "Lamp"
            public GameObject prefab;
        }

        private struct SavedAnchorInfo
        {
            public Guid uuid;
            public int typeIndex;
        }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            SetPlacementActive(false);
            StartCoroutine(LoadAnchorsNextFrame());
        }

        private IEnumerator LoadAnchorsNextFrame()
        {
            yield return null; // let OVR systems initialize
            LoadAnchors();
        }

        private void Update()
        {
            if (!_isPlacing) return;

            if (Physics.Raycast(rayOrigin.position, rayOrigin.forward, out var hit, maxRayDistance, placementLayer))
            {
                lineRenderer.SetPosition(0, rayOrigin.position);
                lineRenderer.SetPosition(1, hit.point);
                lineRenderer.enabled = true;

                if (placementPreview != null)
                {
                    placementPreview.SetActive(true);
                    placementPreview.transform.position = hit.point;
                    // Flatten forward direction to horizontal plane
                    var flatForward = Vector3.ProjectOnPlane(rayOrigin.forward, Vector3.up).normalized;
                    if (flatForward.sqrMagnitude > 0.001f)
                        placementPreview.transform.rotation = Quaternion.LookRotation(flatForward);
                }

                _hitPoint = hit.point;
                _hitRotation = placementPreview != null ? placementPreview.transform.rotation : Quaternion.identity;

                // Confirm placement
                if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger) ||
                    OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger) ||
                    Input.GetKeyDown(KeyCode.Space))
                {
                    CreateAnchor(_selectedTypeIndex, _hitPoint, _hitRotation);
                }
            }
            else
            {
                lineRenderer.enabled = false;
                if (placementPreview != null) placementPreview.SetActive(false);
            }
        }

        // ─── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// Begin placing an anchor of the given type index.
        /// Call this from UI buttons — one per anchor type.
        /// </summary>
        public void StartPlacing(int typeIndex)
        {
            _selectedTypeIndex = Mathf.Clamp(typeIndex, 0, anchorTypes.Length - 1);
            _isPlacing = true;
            anchorMenuPanel.SetActive(false);
            SetPlacementActive(true);

            Debug.Log($"[AnchorManager] Placing anchor type: {anchorTypes[_selectedTypeIndex].name}");
        }

        /// <summary>
        /// Cancel current placement and return to menu.
        /// </summary>
        public void CancelPlacing()
        {
            _isPlacing = false;
            SetPlacementActive(false);
            anchorMenuPanel.SetActive(true);
        }

        /// <summary>
        /// Load all saved anchors from local storage.
        /// </summary>
        public void LoadAnchors()
        {
            var saved = GetSavedAnchors();

            Debug.Log($"[AnchorManager] LoadAnchors: found {saved.Count} saved anchor(s) in PlayerPrefs.");

            if (saved.Count == 0)
            {
                Debug.Log("[AnchorManager] No saved anchors found.");
                OnNoSavedAnchorsFound?.Invoke();
                anchorMenuPanel.SetActive(true);
                return;
            }

            var uuids = new List<Guid>();
            var typeMap = new Dictionary<Guid, int>();

            foreach (var info in saved)
            {
                uuids.Add(info.uuid);
                typeMap[info.uuid] = info.typeIndex;
                Debug.Log($"[AnchorManager]   Will load: UUID={info.uuid}, type={info.typeIndex}");
            }

            StartCoroutine(LoadAnchorsRoutine(uuids, typeMap));
        }

        /// <summary>
        /// Erase all anchors (runtime + saved data) and start fresh.
        /// </summary>
        public void EraseAllAnchors()
        {
            StartCoroutine(EraseAllAnchorsRoutine());
        }

        /// <summary>
        /// Toggle menu visibility.
        /// </summary>
        public void ToggleMenu()
        {
            anchorMenuPanel.SetActive(!anchorMenuPanel.activeSelf);
        }

        // ─── Core: Create ───────────────────────────────────────────────

        private void CreateAnchor(int typeIndex, Vector3 position, Quaternion rotation)
        {
            _isPlacing = false;
            SetPlacementActive(false);

            var prefab = anchorTypes[typeIndex].prefab;
            var go = prefab != null
                ? Instantiate(prefab, position, rotation)
                : new GameObject($"Anchor_{anchorTypes[typeIndex].name}");

            if (prefab == null)
                go.transform.SetPositionAndRotation(position, rotation);

            // SAFETY: destroy any pre-existing OVRSpatialAnchor from the prefab.
            // AddComponent will create a fresh one that properly initializes.
            var existing = go.GetComponent<OVRSpatialAnchor>();
            if (existing != null)
            {
                Debug.LogWarning($"[AnchorManager] Prefab '{anchorTypes[typeIndex].name}' already had " +
                                 "OVRSpatialAnchor — removing it before creating a new one.");
                DestroyImmediate(existing);
            }

            var spatialAnchor = go.AddComponent<OVRSpatialAnchor>();
            Debug.Log($"[AnchorManager] Creating anchor for type '{anchorTypes[typeIndex].name}' at {position}...");
            StartCoroutine(InitAndSaveAnchor(spatialAnchor, typeIndex));
        }

        private IEnumerator InitAndSaveAnchor(OVRSpatialAnchor anchor, int typeIndex)
        {
            // Wait for anchor creation (with timeout)
            float timeout = 10f; // increased from 5s — Link can be slow
            float elapsed = 0f;
            while (anchor != null && !anchor.Created && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (anchor == null)
            {
                Debug.LogError("[AnchorManager] Anchor GameObject was destroyed during creation.");
                yield break;
            }

            if (!anchor.Created)
            {
                Debug.LogError($"[AnchorManager] Anchor creation timed out after {timeout}s. " +
                               "Check: (1) prefab doesn't have OVRSpatialAnchor pre-attached, " +
                               "(2) OVRManager Anchor Support = Required, " +
                               "(3) only one OVRSpatialAnchor per GameObject.");
                Destroy(anchor.gameObject);
                yield break;
            }

            Debug.Log($"[AnchorManager] Anchor created in {elapsed:F1}s. UUID: {anchor.Uuid}. Saving...");

            // Save to Oculus backend
            var saveTask = anchor.SaveAnchorAsync();
            while (!saveTask.IsCompleted)
                yield return null;

            if (!saveTask.GetResult())
            {
                Debug.LogError("[AnchorManager] Failed to save anchor to Oculus storage.");
                Destroy(anchor.gameObject);
                yield break;
            }

            // Persist UUID + type to PlayerPrefs
            SaveAnchorInfo(anchor.Uuid, typeIndex);
            _activeAnchors.Add(anchor);

            Debug.Log($"[AnchorManager] Anchor created & saved. Type: {anchorTypes[typeIndex].name}, UUID: {anchor.Uuid}");
            OnAnchorCreated?.Invoke(anchor, typeIndex);

            // Re-show menu so user can place more anchors or switch type
            if (anchorMenuPanel != null)
                anchorMenuPanel.SetActive(true);
        }

        // ─── Core: Load ─────────────────────────────────────────────────

        private IEnumerator LoadAnchorsRoutine(List<Guid> uuids, Dictionary<Guid, int> typeMap)
        {
            Debug.Log($"[AnchorManager] Attempting to load {uuids.Count} anchor(s) from storage...");
            foreach (var uuid in uuids)
                Debug.Log($"[AnchorManager]   UUID to load: {uuid}");

            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadTask = OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, unboundAnchors);
            while (!loadTask.IsCompleted)
                yield return null;

            // ── Detailed error reporting ──────────────────────────────
            var result = loadTask.GetResult();
            if (!result.Success)
            {
                Debug.LogError($"[AnchorManager] LoadUnboundAnchorsAsync FAILED. Status: {result.Status}");
                Debug.LogError("[AnchorManager] Common causes:");
                Debug.LogError("  1. Testing via Quest Link — anchor persistence requires standalone APK on device");
                Debug.LogError("  2. Missing permission: OVRManager → Quest Features → Anchor Support = Required");
                Debug.LogError("  3. Missing manifest: <uses-permission android:name=\"com.oculus.permission.USE_ANCHOR_API\" />");
                Debug.LogError("  4. Room/environment changed since anchors were saved");
                Debug.LogError("  5. Meta XR SDK version mismatch — try SDK v66+ with Quest OS v62+");

                // Don't clear saved anchors on transient failures — the UUIDs may still be valid
                // Only clear if we're confident the anchors are truly gone
                if (result.Status.ToString().Contains("DataNotFound") ||
                    result.Status.ToString().Contains("SpaceNotFound"))
                {
                    Debug.LogWarning("[AnchorManager] Anchors confirmed missing. Clearing saved data.");
                    ClearSavedAnchors();
                }

                OnNoSavedAnchorsFound?.Invoke();
                anchorMenuPanel.SetActive(true);
                yield break;
            }

            if (unboundAnchors.Count == 0)
            {
                Debug.LogWarning("[AnchorManager] LoadUnboundAnchorsAsync succeeded but returned 0 anchors.");
                ClearSavedAnchors();
                OnNoSavedAnchorsFound?.Invoke();
                anchorMenuPanel.SetActive(true);
                yield break;
            }

            Debug.Log($"[AnchorManager] Successfully loaded {unboundAnchors.Count} unbound anchor(s). Localizing...");

            var loaded = new List<OVRSpatialAnchor>();

            foreach (var unbound in unboundAnchors)
            {
                // ── Localize ──────────────────────────────────────────
                if (!unbound.Localized)
                {
                    Debug.Log($"[AnchorManager] Localizing anchor {unbound.Uuid}...");
                    var localizeTask = unbound.LocalizeAsync();
                    while (!localizeTask.IsCompleted)
                        yield return null;

                    if (!localizeTask.GetResult())
                    {
                        Debug.LogWarning($"[AnchorManager] Failed to localize anchor {unbound.Uuid}. " +
                                         "The physical environment may have changed.");
                        continue;
                    }
                }

                // ── Determine type and prefab ─────────────────────────
                int typeIndex = typeMap.TryGetValue(unbound.Uuid, out var t) ? t : 0;
                typeIndex = Mathf.Clamp(typeIndex, 0, anchorTypes.Length - 1);

                var prefab = anchorTypes[typeIndex].prefab;
                var hasPose = unbound.TryGetPose(out var pose);

                if (!hasPose)
                {
                    Debug.LogWarning($"[AnchorManager] Could not get pose for anchor {unbound.Uuid}. Skipping.");
                    continue;
                }

                // ── Instantiate and bind ──────────────────────────────
                // IMPORTANT: Instantiate prefab at the anchor pose, then add
                // OVRSpatialAnchor and bind. Do NOT let the prefab have
                // OVRSpatialAnchor pre-attached (that would create a NEW anchor).
                GameObject go;
                if (prefab != null)
                    go = Instantiate(prefab, pose.position, pose.rotation);
                else
                    go = new GameObject($"LoadedAnchor_{anchorTypes[typeIndex].name}");

                // Ensure no pre-existing OVRSpatialAnchor (which would conflict)
                var existingAnchor = go.GetComponent<OVRSpatialAnchor>();
                if (existingAnchor != null)
                {
                    Debug.LogWarning($"[AnchorManager] Prefab '{anchorTypes[typeIndex].name}' already has " +
                                     "OVRSpatialAnchor — destroying it to bind the loaded anchor.");
                    DestroyImmediate(existingAnchor);
                }

                var anchor = go.AddComponent<OVRSpatialAnchor>();
                unbound.BindTo(anchor);

                _activeAnchors.Add(anchor);
                loaded.Add(anchor);

                Debug.Log($"[AnchorManager] ✓ Loaded anchor type '{anchorTypes[typeIndex].name}' " +
                          $"UUID: {unbound.Uuid} at {pose.position}");
            }

            if (loaded.Count > 0)
            {
                Debug.Log($"[AnchorManager] Successfully loaded {loaded.Count} anchor(s).");
                OnAnchorsLoaded?.Invoke(loaded);
            }
            else
            {
                Debug.LogWarning("[AnchorManager] All anchors failed to localize.");
                OnNoSavedAnchorsFound?.Invoke();
                anchorMenuPanel.SetActive(true);
            }
        }

        // ─── Core: Erase ────────────────────────────────────────────────

        private IEnumerator EraseAllAnchorsRoutine()
        {
            for (int i = _activeAnchors.Count - 1; i >= 0; i--)
            {
                var anchor = _activeAnchors[i];
                if (anchor == null) continue;

                var eraseTask = anchor.EraseAnchorAsync();
                while (!eraseTask.IsCompleted)
                    yield return null;

                Destroy(anchor.gameObject);
                yield return null; // let cleanup happen
            }

            _activeAnchors.Clear();
            ClearSavedAnchors();

            Debug.Log("[AnchorManager] All anchors erased.");
            OnAllAnchorsErased?.Invoke();
            anchorMenuPanel.SetActive(true);
        }

        // ─── PlayerPrefs Persistence ────────────────────────────────────

        private void SaveAnchorInfo(Guid uuid, int typeIndex)
        {
            int count = PlayerPrefs.GetInt(PREF_ANCHOR_COUNT, 0);
            PlayerPrefs.SetString($"{PREF_UUID_PREFIX}{count}", uuid.ToString());
            PlayerPrefs.SetInt($"{PREF_TYPE_PREFIX}{count}", typeIndex);
            PlayerPrefs.SetInt(PREF_ANCHOR_COUNT, count + 1);
            PlayerPrefs.Save();

            Debug.Log($"[AnchorManager] Saved anchor to PlayerPrefs: index={count}, " +
                      $"UUID={uuid}, type={typeIndex}. Total saved: {count + 1}");
        }

        /// <summary>
        /// Debug utility: logs all saved anchor data from PlayerPrefs.
        /// Call from inspector button or console if anchors aren't loading.
        /// </summary>
        public void DebugLogSavedAnchors()
        {
            int count = PlayerPrefs.GetInt(PREF_ANCHOR_COUNT, 0);
            Debug.Log($"[AnchorManager] === SAVED ANCHORS ({count}) ===");
            for (int i = 0; i < count; i++)
            {
                var uuid = PlayerPrefs.GetString($"{PREF_UUID_PREFIX}{i}", "MISSING");
                var type = PlayerPrefs.GetInt($"{PREF_TYPE_PREFIX}{i}", -1);
                Debug.Log($"[AnchorManager]   [{i}] UUID: {uuid}, Type: {type}");
            }
            Debug.Log("[AnchorManager] === END SAVED ANCHORS ===");
        }

        private List<SavedAnchorInfo> GetSavedAnchors()
        {
            var list = new List<SavedAnchorInfo>();
            int count = PlayerPrefs.GetInt(PREF_ANCHOR_COUNT, 0);

            for (int i = 0; i < count; i++)
            {
                var uuidKey = $"{PREF_UUID_PREFIX}{i}";
                var typeKey = $"{PREF_TYPE_PREFIX}{i}";

                if (!PlayerPrefs.HasKey(uuidKey)) continue;

                try
                {
                    list.Add(new SavedAnchorInfo
                    {
                        uuid = new Guid(PlayerPrefs.GetString(uuidKey)),
                        typeIndex = PlayerPrefs.GetInt(typeKey, 0)
                    });
                }
                catch (FormatException)
                {
                    Debug.LogWarning($"[AnchorManager] Invalid UUID at index {i}, skipping.");
                }
            }

            return list;
        }

        private void ClearSavedAnchors()
        {
            int count = PlayerPrefs.GetInt(PREF_ANCHOR_COUNT, 0);
            for (int i = 0; i < count; i++)
            {
                PlayerPrefs.DeleteKey($"{PREF_UUID_PREFIX}{i}");
                PlayerPrefs.DeleteKey($"{PREF_TYPE_PREFIX}{i}");
            }
            PlayerPrefs.SetInt(PREF_ANCHOR_COUNT, 0);
            PlayerPrefs.Save();
        }

        // ─── Helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the names of all configured anchor types. Used by UI to build buttons.
        /// </summary>
        public string[] GetAnchorTypeNames()
        {
            var names = new string[anchorTypes.Length];
            for (int i = 0; i < anchorTypes.Length; i++)
                names[i] = anchorTypes[i].name;
            return names;
        }

        /// <summary>
        /// Returns the saved type index for a given anchor UUID. Returns 0 if not found.
        /// </summary>
        public int GetAnchorTypeIndex(Guid uuid)
        {
            int count = PlayerPrefs.GetInt(PREF_ANCHOR_COUNT, 0);
            for (int i = 0; i < count; i++)
            {
                var uuidKey = $"{PREF_UUID_PREFIX}{i}";
                if (!PlayerPrefs.HasKey(uuidKey)) continue;

                try
                {
                    if (new Guid(PlayerPrefs.GetString(uuidKey)) == uuid)
                        return PlayerPrefs.GetInt($"{PREF_TYPE_PREFIX}{i}", 0);
                }
                catch (System.FormatException) { }
            }
            return 0;
        }

        private void SetPlacementActive(bool active)
        {
            if (lineRenderer != null) lineRenderer.enabled = active;
            if (placementPreview != null) placementPreview.SetActive(active);
        }
    }
}
