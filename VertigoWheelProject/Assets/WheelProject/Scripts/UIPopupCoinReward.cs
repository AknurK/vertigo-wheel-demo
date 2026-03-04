using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight UI flyup popup used for both currency and item rewards.
/// Animates upward movement + fade-out, with an optional scale bounce.
/// Destroys itself after the animation completes.
/// </summary>
public class UIPopupCoinReward : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform ui_anim_coin_reward;
    [SerializeField] private Image ui_image_coin_icon;
    [SerializeField] private TMP_Text ui_text_coin_amount_value;

    [Header("Animation")]
    [SerializeField, Min(0.05f)] private float duration = 1.0f;
    [SerializeField] private float moveUp = 120f;
    [SerializeField] private Vector3 startScale = new Vector3(0.85f, 0.85f, 0.85f);
    [SerializeField] private Vector3 endScale = Vector3.one;

    [Header("Bounce (Optional)")]
    [SerializeField] private bool enableBounce = true;

    [Tooltip("How much to overshoot beyond End Scale (e.g., 1.10 - 1.25).")]
    [SerializeField, Min(1f)] private float bounceOvershoot = 1.15f;

    [Tooltip("Portion of the animation used to reach the overshoot scale (e.g., 0.15 - 0.35).")]
    [SerializeField, Range(0f, 1f)] private float bounceInPortion = 0.25f;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        if (!ui_anim_coin_reward)
            ui_anim_coin_reward = GetComponent<RectTransform>();

        // Ensure we can fade reliably.
        canvasGroup = ui_anim_coin_reward.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = ui_anim_coin_reward.gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>
    /// Currency popup variant (e.g., "+50").
    /// </summary>
    public void Play(Sprite iconSprite, int amount)
    {
        PlayInternal(iconSprite, $"+{amount}");
    }

    /// <summary>
    /// Item popup variant (e.g., "x1", "x3", etc.).
    /// </summary>
    public void Play(Sprite iconSprite, string amountText)
    {
        PlayInternal(iconSprite, amountText);
    }

    private void PlayInternal(Sprite iconSprite, string amountText)
    {
        if (ui_image_coin_icon) ui_image_coin_icon.sprite = iconSprite;
        if (ui_text_coin_amount_value) ui_text_coin_amount_value.text = amountText;

        if (ui_anim_coin_reward)
            ui_anim_coin_reward.localScale = startScale;

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        StopAllCoroutines();
        StartCoroutine(AnimateRoutine());
    }

    private IEnumerator AnimateRoutine()
    {
        if (!ui_anim_coin_reward || canvasGroup == null)
        {
            Destroy(gameObject);
            yield break;
        }

        float t = 0f;

        Vector3 startPos = ui_anim_coin_reward.localPosition;
        Vector3 endPos = startPos + new Vector3(0f, moveUp, 0f);

        float dur = Mathf.Max(0.05f, duration);

        // Bounce targets
        float inPortion = Mathf.Clamp01(bounceInPortion);
        Vector3 overshootScale = endScale * Mathf.Max(1f, bounceOvershoot);

        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);

            // Move + fade
            ui_anim_coin_reward.localPosition = Vector3.Lerp(startPos, endPos, p);
            canvasGroup.alpha = 1f - p;

            // Scale (with optional bounce)
            ui_anim_coin_reward.localScale = EvaluateScale(p, inPortion, overshootScale);

            yield return null;
        }

        Destroy(gameObject);
    }

    private Vector3 EvaluateScale(float p, float inPortion, Vector3 overshootScale)
    {
        if (!enableBounce || inPortion <= 0f)
            return Vector3.Lerp(startScale, endScale, p);

        if (p <= inPortion)
        {
            float pp = Mathf.Clamp01(p / inPortion);
            return Vector3.Lerp(startScale, overshootScale, pp);
        }
        else
        {
            float pp = Mathf.Clamp01((p - inPortion) / (1f - inPortion));
            return Vector3.Lerp(overshootScale, endScale, pp);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        duration = Mathf.Max(0.05f, duration);
        bounceOvershoot = Mathf.Max(1f, bounceOvershoot);
        bounceInPortion = Mathf.Clamp01(bounceInPortion);
    }
#endif
}