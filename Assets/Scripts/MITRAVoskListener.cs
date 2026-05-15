using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

public class MITRAVoskListener : MonoBehaviour
{
    private const int SAMPLE_RATE = 16000;

    // Confidence from the last Vosk recognition — read by MITRAScorer
    public static float LastConfidence { get; private set; } = 0f;

    // Configurable silence timeout — 5s matches DTTC wait window for children (ASHA 2024)
    [SerializeField] public float maxRecordingSeconds = 5.0f;

    public IEnumerator StartListening(List<WordModel> vocabulary, Action<string> onResult)
    {
        Debug.Log("MITRA: Recording child's response...");

        // ── 1. Record microphone ──────────────────────────────────────────
        AudioClip clip = Microphone.Start(null, false, Mathf.CeilToInt(maxRecordingSeconds), SAMPLE_RATE);
        yield return new WaitForSeconds(maxRecordingSeconds);

        int endPos = Microphone.GetPosition(null);
        Microphone.End(null);

        if (endPos == 0)
        {
            Debug.LogWarning("MITRA Vosk: No microphone input detected");
            onResult?.Invoke("");
            yield break;
        }

        // ── 2. Extract samples, downmix to mono, save as WAV on background thread
        float[] rawSamples = new float[endPos * clip.channels];
        clip.GetData(rawSamples, 0);

        // Vosk requires mono — downmix stereo (or more) by averaging channels
        float[] monoSamples;
        if (clip.channels > 1)
        {
            monoSamples = new float[endPos];
            for (int i = 0; i < endPos; i++)
            {
                float sum = 0f;
                for (int c = 0; c < clip.channels; c++)
                    sum += rawSamples[i * clip.channels + c];
                monoSamples[i] = sum / clip.channels;
            }
        }
        else
        {
            monoSamples = rawSamples;
        }

        bool wavSaved = false;
        string wavError = null;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                byte[] wavBytes = EncodeWav(monoSamples, SAMPLE_RATE, 1);
                File.WriteAllBytes(MITRAConfig.RECORDING_WAV, wavBytes);
            }
            catch (Exception e)
            {
                wavError = e.Message;
            }
            wavSaved = true;
        });

        yield return new WaitUntil(() => wavSaved);

        if (wavError != null)
        {
            Debug.LogError($"MITRA Vosk: WAV save failed — {wavError}");
            onResult?.Invoke("");
            yield break;
        }

        // ── 3. Call vosk_transcribe.py on background thread ───────────────
        // Vocabulary passed as comma-separated word list (e.g. "apple,ball,cat")
        string vocabArg = string.Join(",", vocabulary.ConvertAll(w => w.word.ToLower()));

        bool voskDone = false;
        string transcription = "";
        string voskError = null;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = MITRAConfig.PYTHON_PATH,
                    Arguments              = $"\"{MITRAConfig.VoskScript}\" \"{MITRAConfig.RECORDING_WAV}\" \"{vocabArg}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                string raw = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                // vosk_transcribe.py outputs: line 1 = word, line 2 = confidence
                string[] lines = raw.Split(new[] {'\n', '\r'},
                    System.StringSplitOptions.RemoveEmptyEntries);
                transcription = lines.Length > 0 ? lines[0].Trim().ToLower() : "";
                float conf = 0f;
                if (lines.Length > 1)
                    float.TryParse(lines[1].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out conf);
                LastConfidence = conf;
            }
            catch (Exception e)
            {
                voskError = e.Message;
            }
            voskDone = true;
        });

        yield return new WaitUntil(() => voskDone);

        if (voskError != null)
            Debug.LogError($"MITRA Vosk process error: {voskError}");

        Debug.Log($"MITRA Vosk: '{transcription}'");
        onResult?.Invoke(transcription);
    }

    // ── WAV encoder (16-bit PCM, mono) ────────────────────────────────────────

    private static byte[] EncodeWav(float[] samples, int sampleRate, int channels)
    {
        int dataSize = samples.Length * 2; // 16-bit = 2 bytes per sample

        using var ms     = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt sub-chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                        // chunk size
        writer.Write((short)1);                  // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2); // byte rate
        writer.Write((short)(channels * 2));     // block align
        writer.Write((short)16);                 // bits per sample

        // data sub-chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        foreach (float s in samples)
        {
            short pcm = (short)(Mathf.Clamp(s, -1f, 1f) * 32767f);
            writer.Write(pcm);
        }

        return ms.ToArray();
    }
}
