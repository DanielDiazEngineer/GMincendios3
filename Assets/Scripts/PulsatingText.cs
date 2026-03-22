using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Animates TextMeshPro text with pulsating effects.
/// Supports smooth pulsation or marquee-style lightbulb sequences.
/// </summary>
public class PulsatingText : MonoBehaviour
{
    public enum EffectMode
    {
        SmoothPulsate,      // Smooth fade in/out
        MarqueeLightbulbs   // Sequential character lighting like marquee signs
    }

    [Header("Effect Mode")]
    [SerializeField] private EffectMode effectMode = EffectMode.SmoothPulsate;
    
    [Header("Smooth Pulsate Settings")]
    [SerializeField] private float pulseDuration = 1f;
    [SerializeField] private AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float minAlpha = 0.3f;
    [SerializeField] private float maxAlpha = 1f;
    
    [Header("Marquee Settings")]
    [SerializeField] private float characterDelay = 0.1f; // Delay between each character
    [SerializeField] private float characterFadeDuration = 0.3f;
    [SerializeField] private bool marqueeLoop = true;
    [SerializeField] private float marqueeLoopDelay = 0.5f;
    
    [Header("Appearance Animation")]
    [SerializeField] private bool animateAppearance = true;
    [SerializeField] private float appearDuration = 1f;
    [SerializeField] private AnimationCurve appearCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Options")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool loopPulsate = true;
    
    [Header("Fade Out Settings")]
    [SerializeField] private float fadeOutDuration = 1f;
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    private TMP_Text textMesh;
    private Color originalColor;
    private float timer = 0f;
    private bool isPlaying = false;
    private bool hasAppeared = false;
    private bool isFadingOut = false;
    private float fadeOutTimer = 0f;
    private float currentAlpha = 1f;
    private Coroutine marqueeCoroutine;

    private void Awake()
    {
        textMesh = GetComponent<TMP_Text>();
        if (textMesh == null)
        {
            Debug.LogError("PulsatingText requires a TextMeshPro component!");
            enabled = false;
            return;
        }
        
        originalColor = textMesh.color;
        
        if (animateAppearance)
        {
            // Start invisible
            Color invisible = originalColor;
            invisible.a = 0f;
            textMesh.color = invisible;
        }
    }

    private void Start()
    {
        if (playOnStart)
        {
            Play();
        }
    }

    private void Update()
    {
        // Handle fade out
        if (isFadingOut)
        {
            fadeOutTimer += Time.deltaTime;
            float fadeProgress = Mathf.Clamp01(fadeOutTimer / fadeOutDuration);
            float fadeValue = fadeOutCurve.Evaluate(fadeProgress);
            
            Color color = textMesh.color;
            color.a = currentAlpha * fadeValue;
            textMesh.color = color;
            
            if (fadeProgress >= 1f)
            {
                isFadingOut = false;
                isPlaying = false;
                gameObject.SetActive(false); // Optional: deactivate when fully faded
            }
            return;
        }
        
        if (!isPlaying) return;

        // Handle appearance animation
        if (animateAppearance && !hasAppeared)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / appearDuration);
            float curveValue = appearCurve.Evaluate(progress);
            
            Color color = originalColor;
            color.a = Mathf.Lerp(0f, originalColor.a, curveValue);
            textMesh.color = color;
            currentAlpha = color.a;
            
            if (progress >= 1f)
            {
                hasAppeared = true;
                timer = 0f;
                
                // Start marquee effect if selected
                if (effectMode == EffectMode.MarqueeLightbulbs)
                {
                    StartMarquee();
                }
            }
            return;
        }

        // Smooth pulsate effect
        if (effectMode == EffectMode.SmoothPulsate)
        {
            timer += Time.deltaTime;
            float progress = timer / pulseDuration;
            
            if (progress >= 1f)
            {
                if (loopPulsate)
                {
                    timer = 0f;
                    progress = 0f;
                }
                else
                {
                    isPlaying = false;
                    return;
                }
            }

            float curveValue = pulseCurve.Evaluate(progress);
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, curveValue);
            currentAlpha = alpha;
            
            Color color = textMesh.color;
            color.a = alpha;
            textMesh.color = color;
        }
    }

    public void Play()
    {
        isPlaying = true;
        timer = 0f;
        hasAppeared = !animateAppearance;
        
        if (!animateAppearance && effectMode == EffectMode.MarqueeLightbulbs)
        {
            StartMarquee();
        }
    }

    public void Stop()
    {
        isPlaying = false;
        timer = 0f;
        hasAppeared = false;
        
        if (marqueeCoroutine != null)
        {
            StopCoroutine(marqueeCoroutine);
            marqueeCoroutine = null;
        }
        
        textMesh.color = originalColor;
        ResetVertexColors();
    }

    /// <summary>
    /// Triggers a smooth fade out to transparent. Stops all effects and deactivates GameObject when complete.
    /// </summary>
    public void TriggerFadeOut()
    {
        if (marqueeCoroutine != null)
        {
            StopCoroutine(marqueeCoroutine);
            marqueeCoroutine = null;
        }
        
        // Store current alpha for smooth transition
        currentAlpha = textMesh.color.a;
        
        isFadingOut = true;
        isPlaying = false;
        fadeOutTimer = 0f;
        
        // Reset vertex colors for marquee mode
        if (effectMode == EffectMode.MarqueeLightbulbs)
        {
            ResetVertexColors();
        }
    }

    private void StartMarquee()
    {
        if (marqueeCoroutine != null)
        {
            StopCoroutine(marqueeCoroutine);
        }
        marqueeCoroutine = StartCoroutine(MarqueeSequence());
    }

    private IEnumerator MarqueeSequence()
    {
        textMesh.ForceMeshUpdate();
        TMP_TextInfo textInfo = textMesh.textInfo;
        
        while (isPlaying)
        {
            // Fade all characters to min alpha
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                if (!textInfo.characterInfo[i].isVisible) continue;
                
                SetCharacterAlpha(i, minAlpha);
            }
            
            textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            
            // Light up each character sequentially
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                if (!textInfo.characterInfo[i].isVisible) continue;
                
                float elapsedTime = 0f;
                
                // Fade in character
                while (elapsedTime < characterFadeDuration)
                {
                    elapsedTime += Time.deltaTime;
                    float progress = Mathf.Clamp01(elapsedTime / characterFadeDuration);
                    float alpha = Mathf.Lerp(minAlpha, maxAlpha, progress);
                    
                    SetCharacterAlpha(i, alpha);
                    textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                    
                    yield return null;
                }
                
                SetCharacterAlpha(i, maxAlpha);
                textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                
                yield return new WaitForSeconds(characterDelay);
            }
            
            if (!marqueeLoop)
            {
                isPlaying = false;
                yield break;
            }
            
            yield return new WaitForSeconds(marqueeLoopDelay);
        }
    }

    private void SetCharacterAlpha(int charIndex, float alpha)
    {
        TMP_TextInfo textInfo = textMesh.textInfo;
        if (charIndex >= textInfo.characterCount) return;
        
        TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];
        if (!charInfo.isVisible) return;
        
        int materialIndex = charInfo.materialReferenceIndex;
        int vertexIndex = charInfo.vertexIndex;
        
        Color32[] vertexColors = textInfo.meshInfo[materialIndex].colors32;
        byte alphaValue = (byte)(alpha * 255);
        
        vertexColors[vertexIndex + 0].a = alphaValue;
        vertexColors[vertexIndex + 1].a = alphaValue;
        vertexColors[vertexIndex + 2].a = alphaValue;
        vertexColors[vertexIndex + 3].a = alphaValue;
    }

    private void ResetVertexColors()
    {
        if (textMesh == null) return;
        
        textMesh.ForceMeshUpdate();
        TMP_TextInfo textInfo = textMesh.textInfo;
        
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            Color32[] colors = textInfo.meshInfo[i].colors32;
            for (int j = 0; j < colors.Length; j++)
            {
                colors[j].a = 255;
            }
        }
        
        textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private void OnDestroy()
    {
        if (marqueeCoroutine != null)
        {
            StopCoroutine(marqueeCoroutine);
        }
    }
}
