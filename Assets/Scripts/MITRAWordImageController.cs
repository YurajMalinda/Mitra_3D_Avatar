using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MITRAWordImageController : MonoBehaviour
{
    [SerializeField] private RawImage    wordImage;
    [SerializeField] private CanvasGroup imagePanel;
    [SerializeField] private float       fadeDuration = 0.3f;

    public IEnumerator ShowWordImage(string word)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Images", word.ToLower() + ".png");
        if (!File.Exists(path)) { HideImage(); yield break; }

        using var req = UnityWebRequestTexture.GetTexture("file://" + path);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) { HideImage(); yield break; }

        wordImage.texture = DownloadHandlerTexture.GetContent(req);
        yield return StartCoroutine(FadePanel(0f, 1f));
    }

    public IEnumerator HideWordImage()
    {
        yield return StartCoroutine(FadePanel(1f, 0f));
        HideImage();
    }

    private void HideImage()
    {
        if (imagePanel != null) imagePanel.alpha = 0f;
    }

    private IEnumerator FadePanel(float from, float to)
    {
        float t = 0f;
        imagePanel.alpha = from;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            imagePanel.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        imagePanel.alpha = to;
    }
}
