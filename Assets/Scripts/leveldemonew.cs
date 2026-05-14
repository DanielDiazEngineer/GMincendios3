using System.Collections;
using Meta.XR.BuildingBlocks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Autohand;

/// <summary>
/// Scene controller for the fire training demo.
/// Manages intro gate, 3 fire objects (box, barrel, desk), and scene restart.
///
/// Win condition is owned by FireTrainingHUD — call hud.TriggerWin() when all fires done.
///
/// ── SCENE HIERARCHY ──────────────────────────────────────────────────────────
/// BoxFire     — FireBehavior on root or child GO.
/// BarrelFire  — FireBehavior on a CHILD GO above the barrel rim.
///               Barrel mesh collider stays on Default layer, blocks side shots.
/// DeskFire    — DeskFireBehavior parent with 3 FireBehavior children
///               (DeskZone_L, DeskZone_C, DeskZone_R).
/// </summary>
public class leveldemonew : MonoBehaviour
{
    [Header("Fire Objects")]
    public GameObject boxFire;
    public GameObject barrelFire;

    [Tooltip("Parent GO with DeskFireBehavior + 3 FireBehavior children.")]
    public GameObject deskFire;

    [Header("Extinguisher")]
    public ExtinguisherBehavior extinguisherBehavior;

    [Header("HUD")]
    public FireTrainingHUD hud;
    public GameObject canvashud;
    public GameObject canvasintropanels;

    [Header("Intro Canvas")]
    public GameObject canvasconato;

    [Header("Controller Trigger Shoot")]
    public OVRInput.Controller shootController = OVRInput.Controller.RTouch;

    [Header("Scene Restart")]
    [Tooltip("Hold joystick down for this many seconds to restart.")]
    public float restartHoldTime = 2f;

     [Header("menu panel")]
    public GameObject canvasmenu;
    public ResetBall menuball;
    [SerializeField] private MetaHandPinchEvent pinchEvent;


    // ── State ──────────────────────────────────────────────────────────────────

    private bool _introComplete = false;
    private float _restartHoldTimer = 0f;
    private AudioSource _audio;

    private int _firesDone = 0;
    private int _totalFires = 0;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Start()
    {
        _audio = GetComponent<AudioSource>();

          canvasmenu.SetActive(false);
    }

    void Update()
    {
        HandleRestartInput();
    }

    // ── Intro Gate ─────────────────────────────────────────────────────────────

    /// <summary>Call from the last intro panel's Continue button.</summary>
    public void CompleteIntro()
    {
        _introComplete = true;

        //if (extinguisherBehavior != null) //moved to canvashud
            //extinguisherBehavior.shootingEnabled = true;

        if (hud != null)
            hud.ActivateHUD();

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

    // ── Start Demo ─────────────────────────────────────────────────────────────

    /// <summary>Called from intro sequence to activate all fires.</summary>
    public void AboutStart()
        {
            CompleteIntro();

            if (_audio != null) _audio.Play();
            if (canvasconato != null) canvasconato.SetActive(true);

            _firesDone = 0;
            _totalFires = 0;

            // Box fire
            if (boxFire != null)
            {
                boxFire.SetActive(true);
                _totalFires++;
                var fb = boxFire.GetComponentInChildren<FireBehavior>();
                if (hud != null) hud.RegisterFire(fb);
                StartCoroutine(WatchFire(fb, "BoxFire"));
            }

            // Barrel fire
            if (barrelFire != null)
            {
                barrelFire.SetActive(true);
                _totalFires++;
                var fb = barrelFire.GetComponentInChildren<FireBehavior>();
                if (hud != null) hud.RegisterFire(fb);
                StartCoroutine(WatchFire(fb, "BarrelFire"));
            }

            // Desk fire
            if (deskFire != null)
            {
                deskFire.SetActive(true);
                _totalFires++;
                var desk = deskFire.GetComponent<DeskFireBehavior>();
                if (desk != null)
                {
                    if (hud != null)
                        foreach (var fb in deskFire.GetComponentsInChildren<FireBehavior>())
                            hud.RegisterFire(fb);

                    desk.OnDeskFullyExtinguished.AddListener(() => OnOneFireDone());
                }
                else
                {
                    var fb = deskFire.GetComponentInChildren<FireBehavior>();
                    if (hud != null) hud.RegisterFire(fb);
                    StartCoroutine(WatchFire(fb, "DeskFire"));
                }
            }

            Debug.Log($"[leveldemo] Demo started. Tracking {_totalFires} fire(s).");
        }

    // ── Fire Completion ────────────────────────────────────────────────────────

    private IEnumerator WatchFire(FireBehavior fb, string label)
    {
        if (fb == null) yield break;

        while (!fb.IsExtinguished)
            yield return new WaitForSeconds(0.25f);

        Debug.Log($"[leveldemo] {label} extinguished.");
        OnOneFireDone();
    }

    public void OnOneFireDone()
    {
        _firesDone++;
        Debug.Log($"[leveldemo] Fires done: {_firesDone}/{_totalFires}");

        if (_firesDone >= _totalFires)
        {   menuball.OnLevelCleared(); // Start the "pinch me" pulse on the ball
            Debug.Log("[leveldemo] ALL FIRES OUT — training complete!");
            if (hud != null)
                hud.TriggerWin(); // HUD owns the win panel
        }
    }

    // ── Restart ────────────────────────────────────────────────────────────────

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

    public void RestartScene() // Call from UI button or HandleRestartInput()
    {
        Debug.Log("[leveldemo] Restarting scene.");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }


    public void OpenMenuPause()
    {
        if (canvasmenu != null)
        {
            canvasmenu.SetActive(true);
            //Time.timeScale = 0f; // Pause the game

            canvashud.SetActive(false); // Hide the HUD when the menu is open
            canvasintropanels.SetActive(false); // Hide the intro panels when the menu is open

              if (pinchEvent != null) pinchEvent.Disabled = true;
        }
    }

    public void CloseMenuResume()
    {
        if (canvasmenu != null)
        {
            canvasmenu.SetActive(false);
            //Time.timeScale = 1f; // Resume the game
            canvashud.SetActive(true); // Show the HUD when the menu is closed
            canvasintropanels.SetActive(true); // Show the intro panels when the menu is closed

                if (pinchEvent != null) pinchEvent.Disabled = false;

        }
    }
}
