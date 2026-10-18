using Meta.XR.BuildingBlocks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Left-handed / right-handed switch for the hand-tracking extinguisher.
/// Saved in PlayerPrefs so it survives RestartScene() and app restarts.
///
///   Right-handed (default): right hand = handle/trigger, left hand = nozzle + menu gesture
///   Left-handed           : left hand  = handle/trigger, right hand = nozzle + menu gesture
///
/// ── SETUP ────────────────────────────────────────────────────────────────────
/// Put this on an always-active GO (e.g. the one with leveldemonew), NOT inside
/// canvasmenu — the menu starts inactive, so Start() would not run until opened.
///   tracker       → HandTrackingExtinguisherTracker
///   menuGestures  → MenuGestureDetector(s) to keep on the non-dominant hand
///   leftHandToggle→ the checkbox in the pause panel (optional)
/// Toggle wiring: On Value Changed → DominantHandSetting.SetLeftHanded (Dynamic bool)
/// </summary>
/// 
/// 
/// 
/// 
/// 
/// 
/// NOtesWhat changed:

// New DominantHandSetting.cs: this is the script your checkbox calls. Checked means left-handed. It saves the choice in PlayerPrefs, so it survives Restart (which reloads the scene) and app restarts. It then sends the choice to the tracker and the menu gesture. When the scene loads, it sets the checkbox to the saved value without triggering it.
// HandTrackingExtinguisherTracker.cs: SetDominantHand now also clears the trigger state, so switching mid-spray can't leave it spraying from the old hand. I also added a read-only DominantHandIsRight property.
// MenuGestureDetector.cs:
// isLeftHandOnly is now two settings: restrictToOneHand and menuHandIsLeft. Your scenes keep their current value.
// A new SetMenuHandLeft method moves the gesture to the other hand. The setting component uses it to keep the menu pinch on the non-dominant hand, so a left-handed user won't open the menu while squeezing the trigger.
// To set it up in the Editor:

// Add DominantHandSetting to a GameObject that's always active, such as the one with leveldemonew. Don't put it inside canvasmenu: that menu starts inactive, so the setting wouldn't load until someone opened it.
// Assign tracker, menuGestures and leftHandToggle.
// On the Toggle, set On Value Changed to DominantHandSetting → SetLeftHanded, using the Dynamic bool entry at the top of the list. The Static one would always pass the same value.
// Repeat for each scene that uses the tracker: minimalfirewithhandtrack, Version2 and Version2.5copy.
// RealExtinguisherTracker (the physical-controller version) still only supports the right hand. I left it alone because it's a separate path from hand tracking.
public class DominantHandSetting : MonoBehaviour
{
    const string PrefKey = "LeftHanded";

    [Header("Targets")]
    public HandTrackingExtinguisherTracker tracker;
    public MenuGestureDetector[] menuGestures;

    [Header("UI (optional)")]
    [Tooltip("Checked = left-handed. Synced to the saved value on Start.")]
    public Toggle leftHandToggle;

    public bool IsLeftHanded { get; private set; }

    void Start()
    {
        IsLeftHanded = PlayerPrefs.GetInt(PrefKey, 0) == 1;

        if (leftHandToggle != null)
            leftHandToggle.SetIsOnWithoutNotify(IsLeftHanded);

        Apply();
    }

    /// <summary>Call from the pause panel checkbox (Dynamic bool). true = left-handed.</summary>
    public void SetLeftHanded(bool leftHanded)
    {
        IsLeftHanded = leftHanded;
        PlayerPrefs.SetInt(PrefKey, leftHanded ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    void Apply()
    {
        if (tracker != null)
            tracker.SetDominantHand(!IsLeftHanded);

        // Menu gesture lives on the non-dominant hand
        foreach (var gesture in menuGestures)
            if (gesture != null)
                gesture.SetMenuHandLeft(!IsLeftHanded);

        Debug.Log($"[DominantHand] {(IsLeftHanded ? "Left" : "Right")}-handed applied.");
    }
}
