using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MITRAProgressController : MonoBehaviour
{
    [SerializeField] private Slider          progressBar;
    [SerializeField] private TextMeshProUGUI progressLabel;

    public void UpdateProgress(int currentWord, int totalWords)
    {
        if (progressBar  != null) progressBar.value   = (float)currentWord / totalWords;
        if (progressLabel != null) progressLabel.text = $"Word {currentWord} of {totalWords}";
    }
}
