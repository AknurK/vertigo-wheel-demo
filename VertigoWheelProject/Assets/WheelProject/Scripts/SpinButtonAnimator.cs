using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SpinButtonAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Refs")]
    [SerializeField] private RectTransform target;
    [SerializeField] private Button button;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Optional Glow")]
    [SerializeField] private Image glowImage;                 // Assign ui_spin_glow 
    [SerializeField, Range(0f, 1f)] private float glowBase = 0.12f;
    [SerializeField, Range(0f, 1f)] private float glowOnHover = 0.28f;
    [SerializeField, Range(0f, 1f)] private float glowOnClick = 0.35f;
    [SerializeField] private float glowLerpSpeed = 12f;

    [Header("Idle Pulse")]
    [SerializeField] private bool playIdlePulse = true;
    [SerializeField] private float pulseScale = 1.08f;
    [SerializeField] private float pulseSpeed = 2.2f;

    [Header("Hover")]
    [SerializeField] private float hoverScale = 1.12f;
    [SerializeField] private float hoverSpeed = 10f;

    [Header("Click Punch")]
    [SerializeField] private float clickScale = 1.15f;
    [SerializeField] private float clickInTime = 0.07f;
    [SerializeField] private float clickOutTime = 0.10f;

    [Header("Disabled Look")]
    [SerializeField] private bool fadeWhenDisabled = true;
    [SerializeField] private float disabledAlpha = 0.55f;
    [SerializeField] private float enabledAlpha = 1f;

    private Vector3 baseScale;
    private bool isClickAnimating;
    private bool isHovering;
    private bool lastInteractableState;
    private float clickGlowTimer;

    private void Awake()
    {
        if (!target) target = GetComponent<RectTransform>();
        if (!button) button = GetComponent<Button>();
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();

        baseScale = target.localScale;

        if (button)
        {
            lastInteractableState = button.interactable;
            button.onClick.AddListener(PlayClickPunch);
        }

        ApplyInteractableVisuals(true);
        ApplyGlowImmediate();
    }

    private void OnEnable()
    {
        if (target) target.localScale = baseScale;
        isClickAnimating = false;
        isHovering = false;
        clickGlowTimer = 0f;

        ApplyInteractableVisuals(true);
        ApplyGlowImmediate();
    }

    private void Update()
    {
        ApplyInteractableVisuals(false);

        // Glow works even when not interactable 
        UpdateGlow();

        if (!button || !button.interactable) return;
        if (isClickAnimating) return;

        float targetScale = 1f;

        if (isHovering)
        {
            targetScale = hoverScale;
        }
        else if (playIdlePulse)
        {
            float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            targetScale = Mathf.Lerp(1f, pulseScale, t);
        }

        target.localScale = Vector3.Lerp(
            target.localScale,
            baseScale * targetScale,
            Time.unscaledDeltaTime * hoverSpeed
        );
    }

    private void ApplyInteractableVisuals(bool force)
    {
        if (!button) return;

        bool now = button.interactable;
        if (!force && now == lastInteractableState) return;

        lastInteractableState = now;

        if (!now)
        {
            target.localScale = baseScale;
            isHovering = false;
        }

        if (fadeWhenDisabled && canvasGroup)
            canvasGroup.alpha = now ? enabledAlpha : disabledAlpha;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button && button.interactable)
            isHovering = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
    }

    private void PlayClickPunch()
    {
        if (!button || !button.interactable) return;

        // Briefly boost glow on click
        clickGlowTimer = 0.12f;

        StopAllCoroutines();
        StartCoroutine(ClickPunchRoutine());
    }

    private IEnumerator ClickPunchRoutine()
    {
        isClickAnimating = true;

        float t = 0f;
        Vector3 from = baseScale;
        Vector3 to = baseScale * clickScale;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, clickInTime);
            target.localScale = Vector3.Lerp(from, to, EaseOutCubic(Mathf.Clamp01(t)));
            yield return null;
        }

        t = 0f;
        from = target.localScale;
        to = baseScale;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, clickOutTime);
            target.localScale = Vector3.Lerp(from, to, EaseOutCubic(Mathf.Clamp01(t)));
            yield return null;
        }

        target.localScale = baseScale;
        isClickAnimating = false;
    }

    private void UpdateGlow()
    {
        if (!glowImage) return;

        // Decrease click glow timer
        if (clickGlowTimer > 0f)
            clickGlowTimer -= Time.unscaledDeltaTime;

        float targetAlpha = glowBase;

        if (button && !button.interactable)
        {
            // Dim glow when disabled
            targetAlpha = Mathf.Min(targetAlpha, 0.08f);
        }
        else
        {
            if (isHovering) targetAlpha = glowOnHover;
            if (clickGlowTimer > 0f) targetAlpha = Mathf.Max(targetAlpha, glowOnClick);
        }

        Color c = glowImage.color;
        float newA = Mathf.Lerp(c.a, targetAlpha, Time.unscaledDeltaTime * glowLerpSpeed);
        glowImage.color = new Color(c.r, c.g, c.b, newA);
    }

    private void ApplyGlowImmediate()
    {
        if (!glowImage) return;

        float a = glowBase;
        if (button && !button.interactable) a = Mathf.Min(a, 0.08f);

        Color c = glowImage.color;
        glowImage.color = new Color(c.r, c.g, c.b, a);
    }

    private float EaseOutCubic(float x)
    {
        return 1f - Mathf.Pow(1f - x, 3f);
    }
}