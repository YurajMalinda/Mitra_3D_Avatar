using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MITRAStarRatingController : MonoBehaviour
{
    [SerializeField] private CanvasGroup      panel;
    [SerializeField] private Image            star1;
    [SerializeField] private Image            star2;
    [SerializeField] private Image            star3;
    [SerializeField] private TextMeshProUGUI  label;
    [SerializeField] private float            fadeDuration = 0.3f;

    private static readonly Color goldColor = new Color(1f, 0.84f, 0f);
    private static readonly Color grayColor = new Color(0.6f, 0.6f, 0.6f);

    public IEnumerator ShowStars(int starCount, string message)
    {
        star1.color = starCount >= 1 ? goldColor : grayColor;
        star2.color = starCount >= 2 ? goldColor : grayColor;
        star3.color = starCount >= 3 ? goldColor : grayColor;
        label.text = message;
        yield return StartCoroutine(FadePanel(0f, 1f));
        yield return new WaitForSeconds(2.0f);
        yield return StartCoroutine(FadePanel(1f, 0f));
    }

    private IEnumerator FadePanel(float from, float to)
    {
        float t = 0f;
        panel.alpha = from;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            panel.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        panel.alpha = to;
    }
}
