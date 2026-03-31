using System.Collections;
using Meta.XR.BuildingBlocks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class leveldemonew : MonoBehaviour
{
    [Header("Scene Objects")]
    public GameObject canvasconato;
    public GameObject fire1;
    public GameObject fire2;
    public GameObject extin;

    [Header("Extinguisher Reference")]
    [Tooltip("The ExtinguisherBehavior on the extinguisher object.")]
    public ExtinguisherBehavior extinguisherBehavior;

    [Header("HUD")]                                              // ← NEW
    public FireTrainingHUD hud;                                  // ← NEW

    [Header("Controller Trigger Shoot")]
    [Tooltip("Which controller triggers the spray via button input.")]
    public OVRInput.Controller shootController = OVRInput.Controller.RTouch;

    [Header("Scene Restart")]
    [Tooltip("Hold joystick down for this many seconds to restart.")]
    public float restartHoldTime = 2f;

    // ── State ────────────────────────────────────────────────────────────────

    private bool _introComplete = false;
    private float _restartHoldTimer = 0f;
    private AudioSource _audio;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Start()
    {
        _audio = GetComponent<AudioSource>();
    }

    void Update()
    {
        HandleRestartInput();
    }

    // ── Intro Gate ───────────────────────────────────────────────────────────

    /// <summary>
    /// Call from the last intro panel's "Continue" button.
    /// Unlocks shooting and activates the step-by-step HUD.
    /// </summary>
    public void CompleteIntro()
    {
        _introComplete = true;
        if (extinguisherBehavior != null)
            extinguisherBehavior.shootingEnabled = true;
        if (hud != null) hud.ActivateHUD();                      // ← NEW
        Debug.Log("[leveldemo] Intro complete — shooting enabled.");
    }

    /// <summary>Called by MetaXRControllerEvent on Press.</summary>
    public void TryShoot()
    {
        if (!_introComplete || extinguisherBehavior == null) return;
        extinguisherBehavior._controllerTriggerHeld = true;
    }

    /// <summary>Called by MetaXRControllerEvent on Release.</summary>
    public void StopShoot()
    {
        if (extinguisherBehavior == null) return;
        extinguisherBehavior._controllerTriggerHeld = false;
    }

    // ── Scene Restart ────────────────────────────────────────────────────────

    private void HandleRestartInput()
    {
        if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick))
        {
            _restartHoldTimer += Time.deltaTime;
            if (_restartHoldTimer >= restartHoldTime)
                RestartScene();
        }
        else
        {
            _restartHoldTimer = 0f;
        }
    }

    private void RestartScene()
    {
        Debug.Log("[leveldemo] Restarting scene.");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ── Original Flow ────────────────────────────────────────────────────────

    public void AboutStart()
    {
        CompleteIntro();
        _audio.Play();
        canvasconato.SetActive(true);
        fire1.SetActive(true);
        if (hud != null) hud.SetFire(fire1);                     // ← NEW
        // fire2.SetActive(true);
        // extin.SetActive(true);
    }
}
