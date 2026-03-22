using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class PanelNavigator : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private List<GameObject> panels = new List<GameObject>();
    [SerializeField] private int currentPanelIndex = 0;

    [Header("Click Detection")]
    [SerializeField] private bool useManualClickDetection = true;
    [SerializeField] private KeyCode advanceKey = KeyCode.Space; // Optional keyboard input

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip transitionSound;
    [SerializeField] private AudioClip completionSound;

    [Header("Fade Settings")]
    [SerializeField] private float fadeDuration = 0.5f;
    [SerializeField] private CanvasGroup currentPanelCanvasGroup;

    [Header("Click Protection")]
    [SerializeField] private float clickCooldown = 0.5f;
    private float lastClickTime = -1f;
    private bool isTransitioning = false;

    [Header("Completion Events")]
    [SerializeField] private UnityEvent onLastPanelCompleted;
    [SerializeField] private bool hideUIAfterCompletion = true;
    [SerializeField] private float delayBeforeEvent = 0f;

    private bool hasCompleted = false;

    private void Start()
    {
        ShowPanel(currentPanelIndex);

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        if (onLastPanelCompleted == null)
        {
            onLastPanelCompleted = new UnityEvent();
        }

        // Add event triggers to panels if using manual detection
        if (useManualClickDetection)
        {
            SetupPanelClickDetection();
        }
    }

    private void Update()
    {
        // Optional keyboard input
        if (Input.GetKeyDown(advanceKey))
        {
            GoToNextPanel();
        }
    }

    private void SetupPanelClickDetection()
    {
        foreach (GameObject panel in panels)
        {
            // Ensure panel has an Image component for raycasting
            Image image = panel.GetComponent<Image>();
            if (image == null)
            {
                image = panel.AddComponent<Image>();
                image.color = new Color(1, 1, 1, 0.01f); // Nearly transparent but clickable
            }

            // Add EventTrigger component
            EventTrigger trigger = panel.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = panel.AddComponent<EventTrigger>();
            }

            // Create pointer click entry
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerClick;
            entry.callback.AddListener((data) => { OnPanelClicked(); });
            trigger.triggers.Add(entry);
        }
    }

    private void OnPanelClicked()
    {
        GoToNextPanel();
    }

    public void GoToNextPanel()
    {
        if (Time.time - lastClickTime < clickCooldown || isTransitioning)
        {
            Debug.Log("Click too fast! Please wait.");
            return;
        }

        lastClickTime = Time.time;
        if (hasCompleted)
        {
            Debug.Log("Tutorial already completed.");
            return;
        }

        if (currentPanelIndex >= panels.Count - 1)
        {
            Debug.Log("Last panel reached - triggering completion event");
            hasCompleted = true;
            StartCoroutine(HandleLastPanelCompletion());
        }
        else
        {
            StartCoroutine(TransitionToPanel(currentPanelIndex + 1));
        }
    }

    public void GoToPreviousPanel()
    {
        if (Time.time - lastClickTime < clickCooldown || isTransitioning)
        {
            return;
        }

        lastClickTime = Time.time;

        if (currentPanelIndex > 0)
        {
            StartCoroutine(TransitionToPanel(currentPanelIndex - 1));
        }
    }

    public void GoToPanel(int index)
    {
        if (Time.time - lastClickTime < clickCooldown || isTransitioning)
        {
            return;
        }

        lastClickTime = Time.time;

        if (index >= 0 && index < panels.Count && index != currentPanelIndex)
        {
            StartCoroutine(TransitionToPanel(index));
        }
    }

    private IEnumerator HandleLastPanelCompletion()
    {
        isTransitioning = true;

        AudioClip soundToPlay = completionSound != null ? completionSound : transitionSound;
        if (soundToPlay != null && audioSource != null)
        {
            audioSource.PlayOneShot(soundToPlay);
        }

        if (hideUIAfterCompletion && currentPanelCanvasGroup != null)
        {
            yield return StartCoroutine(FadeOut(currentPanelCanvasGroup));
            panels[currentPanelIndex].SetActive(false);
        }

        if (delayBeforeEvent > 0)
        {
            yield return new WaitForSeconds(delayBeforeEvent);
        }

        onLastPanelCompleted?.Invoke();

        isTransitioning = false;
    }

    private IEnumerator TransitionToPanel(int targetIndex)
    {
        isTransitioning = true;

        if (transitionSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(transitionSound);
        }

        if (currentPanelCanvasGroup != null)
        {
            yield return StartCoroutine(FadeOut(currentPanelCanvasGroup));
        }

        panels[currentPanelIndex].SetActive(false);
        currentPanelIndex = targetIndex;
        panels[currentPanelIndex].SetActive(true);
        currentPanelCanvasGroup = panels[currentPanelIndex].GetComponent<CanvasGroup>();

        if (currentPanelCanvasGroup == null)
        {
            currentPanelCanvasGroup = panels[currentPanelIndex].AddComponent<CanvasGroup>();
        }

        yield return StartCoroutine(FadeIn(currentPanelCanvasGroup));

        isTransitioning = false;
    }

    private IEnumerator FadeOut(CanvasGroup canvasGroup)
    {
        float elapsedTime = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsedTime / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
    }

    private IEnumerator FadeIn(CanvasGroup canvasGroup)
    {
        float elapsedTime = 0f;
        canvasGroup.alpha = 0f;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsedTime / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 1f;
    }

    private void ShowPanel(int index)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            panels[i].SetActive(i == index);
        }

        currentPanelCanvasGroup = panels[index].GetComponent<CanvasGroup>();
        if (currentPanelCanvasGroup == null)
        {
            currentPanelCanvasGroup = panels[index].AddComponent<CanvasGroup>();
        }
        currentPanelCanvasGroup.alpha = 1f;
    }

    public void TriggerCompletion()
    {
        StartCoroutine(HandleLastPanelCompletion());
    }

    public void ResetPanels()
    {
        currentPanelIndex = 0;
        hasCompleted = false;
        ShowPanel(0);
    }
}