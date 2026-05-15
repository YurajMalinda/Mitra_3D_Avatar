using UnityEngine;

public static class MITRAConfig
{
    public const string FIREBASE_URL = "https://mitraapp-90036-default-rtdb.asia-southeast1.firebasedatabase.app";

    public const string PYTHON_PATH       = "/home/yuraj/mitra-env/bin/python3";
    public const string PIPER_MODEL       = "/home/yuraj/mitra-env/piper-voices/en_US-ljspeech-high.onnx";
    public const string VOSK_MODEL        = "/home/yuraj/mitra-env/models/vosk-model-small-en-us-0.15";
    public const string TTS_OUTPUT_WAV    = "/tmp/mitra_tts.wav";
    public const string RECORDING_WAV     = "/tmp/mitra_recording.wav";

    public static string VoskScript         => System.IO.Path.Combine(Application.streamingAssetsPath, "Python", "vosk_transcribe.py");
    public static string ScorerScript       => System.IO.Path.Combine(Application.streamingAssetsPath, "Python", "mitra_scorer_v5.py");
    public static string ScorerServerScript => System.IO.Path.Combine(Application.streamingAssetsPath, "Python", "mitra_scorer_server.py");

    // Comma-separated word list for the current session — restricts Vosk to session vocabulary
    public static string SessionVocabCSV = "";

    // Age-adjusted pass thresholds — calibrated to wav2vec2 scorer output range.
    // Clear adult speech scores ~0.46; correct child speech scores ~0.30-0.42.
    // Adjust these if scoring feels too strict or too lenient after observation.
    public static float GetPassThreshold(float ageYears)
    {
        if (ageYears < 4f) return 0.35f;   // Band A: 3-4 years
        if (ageYears < 5f) return 0.40f;   // Band B: 4-5 years
        return 0.45f;                       // Band C: 5-6 years
    }
}
