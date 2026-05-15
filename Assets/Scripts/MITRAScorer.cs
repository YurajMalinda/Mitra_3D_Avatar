using System;
using System.Collections;
using UnityEngine;

public class MITRAScorer : MonoBehaviour
{
    public IEnumerator ScoreRecording(Action<float> onResult)
    {
        yield return null;
        float score = MITRAVoskListener.LastConfidence;
        Debug.Log($"MITRA Score: {score:F3}  (Vosk confidence)");
        onResult?.Invoke(score);
    }
}
