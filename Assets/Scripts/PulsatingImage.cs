using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes an Image or Material pulsate with animated transparency, emission, and size.
/// Works with UI Images, SpriteRenderers, or Materials on Quads/3D objects.
/// </summary>
public class PulsatingImage : MonoBehaviour
{
    [Header("Pulsation Settings")]
    [SerializeField] private float pulseDuration = 1f;
    [SerializeField] private AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Transparency")]
    [SerializeField] private bool animateTransparency = true;
    [SerializeField] private float minAlpha = 0.3f;
    [SerializeField] private float maxAlpha = 1f;
    
    [Header("Emission")]
    [SerializeField] private bool animateEmission = true;
    [SerializeField] private float minEmissionIntensity = 0f;
    [SerializeField] private float maxEmissionIntensity = 2f;
    [SerializeField] private Color emissionColor = Color.white;
    
    [Header("Size")]
    [SerializeField] private bool animateSize = true;
    [SerializeField] [Range(0f, 0.5f)] private float sizeVariation = 0.1f; // 10% by default
    
    [Header("Options")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool loop = true;
    
    [Header("Fade Out Settings")]
    [SerializeField] private float fadeOutDuration = 1f;
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    private Image uiImage;
    private SpriteRenderer spriteRenderer;
    private Material material;
    private Renderer meshRenderer;
    private Vector3 originalScale;
    private float timer = 0f;
    private bool isPlaying = false;
    private bool isFadingOut = false;
    private float fadeOutTimer = 0f;
    private Color originalColor;
    private bool hasEmissionKeyword = false;
    private float currentAlpha = 1f;
    private float currentEmission = 0f;
    private float currentSizeMultiplier = 1f;

    private void Awake()
    {
        // Detect what component we're working with
        uiImage = GetComponent<Image>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        meshRenderer = GetComponent<Renderer>();
        
        if (meshRenderer != null)
        {
            material = meshRenderer.material; // Creates instance
            hasEmissionKeyword = material.IsKeywordEnabled("_EMISSION");
            if (animateEmission && !hasEmissionKeyword)
            {
                material.EnableKeyword("_EMISSION");
            }
        }
        
        originalScale = transform.localScale;
        
        // Store original color
        if (uiImage != null)
            originalColor = uiImage.color;
        else if (spriteRenderer != null)
            originalColor = spriteRenderer.color;
        else if (material != null)
            originalColor = material.color;
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
            
            FadeOut(fadeValue);
            
            if (fadeProgress >= 1f)
            {
                isFadingOut = false;
                isPlaying = false;
                gameObject.SetActive(false); // Optional: deactivate when fully faded
            }
            return;
        }
        
        if (!isPlaying) return;

        timer += Time.deltaTime;
        float progress = timer / pulseDuration;
        
        if (progress >= 1f)
        {
            if (loop)
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
        ApplyEffects(curveValue);
    }

    private void ApplyEffects(float normalizedValue)
    {
        // Animate Transparency
        if (animateTransparency)
        {
            currentAlpha = Mathf.Lerp(minAlpha, maxAlpha, normalizedValue);
            
            if (uiImage != null)
            {
                Color color = uiImage.color;
                color.a = currentAlpha;
                uiImage.color = color;
            }
            else if (spriteRenderer != null)
            {
                Color color = spriteRenderer.color;
                color.a = currentAlpha;
                spriteRenderer.color = color;
            }
            else if (material != null)
            {
                Color color = material.color;
                color.a = currentAlpha;
                material.color = color;
            }
        }

        // Animate Emission
        if (animateEmission && material != null)
        {
            currentEmission = Mathf.Lerp(minEmissionIntensity, maxEmissionIntensity, normalizedValue);
            Color finalEmission = emissionColor * currentEmission;
            
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", finalEmission);
            }
        }

        // Animate Size
        if (animateSize)
        {
            currentSizeMultiplier = 1f + (sizeVariation * normalizedValue);
            transform.localScale = originalScale * currentSizeMultiplier;
        }
    }

    private void FadeOut(float fadeValue)
    {
        // Fade transparency to zero
        float targetAlpha = currentAlpha * fadeValue;
        
        if (uiImage != null)
        {
            Color color = uiImage.color;
            color.a = targetAlpha;
            uiImage.color = color;
        }
        else if (spriteRenderer != null)
        {
            Color color = spriteRenderer.color;
            color.a = targetAlpha;
            spriteRenderer.color = color;
        }
        else if (material != null)
        {
            Color color = material.color;
            color.a = targetAlpha;
            material.color = color;
        }

        // Fade emission to zero
        if (animateEmission && material != null)
        {
            float targetEmission = currentEmission * fadeValue;
            Color finalEmission = emissionColor * targetEmission;
            
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", finalEmission);
            }
        }

        // Optionally fade size back to original
        if (animateSize)
        {
            float targetSize = Mathf.Lerp(currentSizeMultiplier, 1f, 1f - fadeValue);
            transform.localScale = originalScale * targetSize;
        }
    }

    /// <summary>
    /// Triggers a smooth fade out to transparent. Stops pulsation and deactivates GameObject when complete.
    /// </summary>
    public void TriggerFadeOut()
    {
        isFadingOut = true;
        isPlaying = false;
        fadeOutTimer = 0f;
    }

    public void Play()
    {
        isPlaying = true;
        timer = 0f;
    }

    public void Stop()
    {
        isPlaying = false;
        timer = 0f;
        ResetToOriginal();
    }

    public void Pause()
    {
        isPlaying = false;
    }

    public void Resume()
    {
        isPlaying = true;
    }

    private void ResetToOriginal()
    {
        transform.localScale = originalScale;
        
        if (uiImage != null)
            uiImage.color = originalColor;
        else if (spriteRenderer != null)
            spriteRenderer.color = originalColor;
        else if (material != null)
        {
            material.color = originalColor;
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    private void OnDestroy()
    {
        // Clean up material instance
        if (material != null && meshRenderer != null)
        {
            Destroy(material);
        }
    }
}
