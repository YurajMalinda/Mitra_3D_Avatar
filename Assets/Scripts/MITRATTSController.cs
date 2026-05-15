using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class MITRATTSController : MonoBehaviour
{
    private AudioSource audioSource;
    private AudioClip   _pendingClip;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        Debug.Log($"MITRA TTS: AudioSource found = {audioSource != null}");
    }

    // Runs Piper and pre-loads the AudioClip — does NOT play.
    // Call this before triggering an animation so audio is ready the moment the trigger fires.
    public IEnumerator GenerateAudio(string text)
    {
        Debug.Log($"MITRA TTS: Generating '{text}'");
        _pendingClip = null;

        bool ttsReady = false;
        string ttsError = null;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (File.Exists(MITRAConfig.TTS_OUTPUT_WAV))
                    File.Delete(MITRAConfig.TTS_OUTPUT_WAV);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = MITRAConfig.PYTHON_PATH,
                    Arguments              = $"-m piper --model \"{MITRAConfig.PIPER_MODEL}\" --output_file \"{MITRAConfig.TTS_OUTPUT_WAV}\"",
                    RedirectStandardInput  = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                proc.StandardInput.WriteLine(text);
                proc.StandardInput.Close();
                proc.WaitForExit();
            }
            catch (Exception e) { ttsError = e.Message; }
            ttsReady = true;
        });

        yield return new WaitUntil(() => ttsReady);

        if (ttsError != null)
        {
            Debug.LogError($"MITRA TTS error: {ttsError}");
            yield break;
        }

        if (!File.Exists(MITRAConfig.TTS_OUTPUT_WAV))
        {
            Debug.LogError("MITRA TTS: WAV file not created");
            yield break;
        }

        using var webReq = UnityWebRequestMultimedia.GetAudioClip(
            "file://" + MITRAConfig.TTS_OUTPUT_WAV, AudioType.WAV);
        yield return webReq.SendWebRequest();

        if (webReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"MITRA TTS: WAV load error — {webReq.error}");
            yield break;
        }

        _pendingClip = DownloadHandlerAudioClip.GetContent(webReq);
    }

    // Plays the pre-loaded clip and waits for playback to finish.
    public IEnumerator PlayAudio(Action onComplete = null)
    {
        if (_pendingClip == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        audioSource.clip = _pendingClip;
        audioSource.Play();
        Debug.Log($"MITRA TTS: Playing ({_pendingClip.length:F2}s)");

        yield return new WaitForSeconds(_pendingClip.length + 0.1f);
        Debug.Log("MITRA TTS: Playback complete");

        onComplete?.Invoke();
    }

    // Convenience wrapper used by SpeakWithWordEmphasis (animation already running at call site).
    public IEnumerator SpeakWord(string text, Action onComplete = null)
    {
        yield return StartCoroutine(GenerateAudio(text));
        yield return StartCoroutine(PlayAudio(onComplete));
    }
}
