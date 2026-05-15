using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

public class MITRAFirebaseClient : MonoBehaviour
{
    // ── Firebase SDK (real-time listeners) ───────────────────────────────────

    private DatabaseReference _db;
    public bool SdkReady { get; private set; }

    // Status event — fired whenever active_session/status changes.
    // MITRASessionManager subscribes to drive the session lifecycle.
    public event Action<string> StatusChanged;
    public string LastKnownStatus { get; private set; } = "";

    private EventHandler<ValueChangedEventArgs> _pauseHandler;
    private EventHandler<ValueChangedEventArgs> _skipHandler;
    private EventHandler<ValueChangedEventArgs> _repeatHandler;

    private const string APP_NAME = "MITRA";

    void Awake()
    {
        // Always use a named app so we supply our own DatabaseUrl.
        // DefaultInstance reads google-services.json and may omit the DatabaseUrl.
        // On second Play Mode run, Create() throws — catch and reuse the named app.
        var options = new AppOptions
        {
            ApiKey            = "AIzaSyDzkYH4iyL-r4JLHUNHsLTE5goJv79lo00",
            AppId             = "1:375051843104:web:ce2f84bdbcc24c16a82e45",
            ProjectId         = "mitraapp-90036",
            MessageSenderId   = "375051843104",
            StorageBucket     = "mitraapp-90036.firebasestorage.app",
            DatabaseUrl       = new Uri(MITRAConfig.FIREBASE_URL)
        };

        FirebaseApp app;
        try
        {
            app = FirebaseApp.Create(options, APP_NAME);
        }
        catch (Exception)
        {
            // Named app already exists from a previous Play Mode session — reuse it.
            app = FirebaseApp.GetInstance(APP_NAME);
            Debug.Log("MITRA: Reusing existing FirebaseApp");
        }

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available)
            {
                Debug.LogError($"MITRA Firebase SDK unavailable: {task.Result}");
                return;
            }

            _db = FirebaseDatabase.GetInstance(app).RootReference;
            SdkReady = true;
            Debug.Log("MITRA: Firebase SDK ready");

            // Single status listener — MITRASessionManager subscribes via StatusChanged event
            _db.Child("mitra-parents/active_session/status").ValueChanged += (_, args) =>
            {
                if (args.DatabaseError != null)
                {
                    Debug.LogError($"MITRA Status listener error: {args.DatabaseError.Message} (code {args.DatabaseError.Code})");
                    return;
                }
                LastKnownStatus = args.Snapshot.Value as string ?? "";
                Debug.Log($"MITRA: Status changed → '{LastKnownStatus}'");
                StatusChanged?.Invoke(LastKnownStatus);
            };
        });
    }

    // ── Pause / Skip / Repeat real-time listeners ─────────────────────────────

    public void InitRealtimeListeners(
        Action<bool> onPauseChanged,
        Action onSkipRequested,
        Action onRepeatRequested)
    {
        if (_db == null)
        {
            Debug.LogError("MITRA: Cannot register listeners — Firebase SDK not ready");
            return;
        }

        var sessionRef = _db.Child("mitra-parents/active_session");

        _pauseHandler = (_, args) =>
        {
            if (args.DatabaseError != null) return;
            onPauseChanged?.Invoke(args.Snapshot.Value is bool b && b);
        };

        _skipHandler = (_, args) =>
        {
            if (args.DatabaseError != null) return;
            if (args.Snapshot.Value is bool b && b) onSkipRequested?.Invoke();
        };

        _repeatHandler = (_, args) =>
        {
            if (args.DatabaseError != null) return;
            if (args.Snapshot.Value is bool b && b) onRepeatRequested?.Invoke();
        };

        sessionRef.Child("paused").ValueChanged     += _pauseHandler;
        sessionRef.Child("skip_word").ValueChanged  += _skipHandler;
        sessionRef.Child("repeat_word").ValueChanged += _repeatHandler;

        Debug.Log("MITRA: Real-time listeners registered");
    }

    public void RemoveRealtimeListeners()
    {
        if (_db == null) return;
        var sessionRef = _db.Child("mitra-parents/active_session");
        if (_pauseHandler  != null) sessionRef.Child("paused").ValueChanged     -= _pauseHandler;
        if (_skipHandler   != null) sessionRef.Child("skip_word").ValueChanged  -= _skipHandler;
        if (_repeatHandler != null) sessionRef.Child("repeat_word").ValueChanged -= _repeatHandler;
    }

    public void ClearFlag(string field)
    {
        // REST PUT instead of SDK SetValueAsync — SDK writes are also unreliable on Linux.
        StartCoroutine(PutRequest(
            $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session/{field}.json",
            "false"));
    }

    // ── One-shot REST reads/writes ────────────────────────────────────────────

    // REST fallback for pause / skip / repeat — used by MITRASessionLoop.PollControlFlags.
    public IEnumerator GetSessionFlags(Action<bool, bool, bool> onComplete)
    {
        string url = $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session.json";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success || req.downloadHandler.text == "null")
        {
            onComplete?.Invoke(false, false, false);
            yield break;
        }
        var raw = JsonUtility.FromJson<ActiveSessionControlJson>(req.downloadHandler.text);
        onComplete?.Invoke(raw?.paused ?? false, raw?.skip_word ?? false, raw?.repeat_word ?? false);
    }

    // REST fallback — used by MITRASessionManager polling to guarantee status detection
    // even when the SDK real-time WebSocket connection is not established yet.
    public IEnumerator GetSessionStatus(Action<string> onComplete)
    {
        string url = $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session/status.json";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { onComplete?.Invoke(""); yield break; }
        var raw = req.downloadHandler.text.Trim('"');
        onComplete?.Invoke(raw == "null" ? "" : raw);
    }

    public IEnumerator GetActiveSession(Action<ActiveSessionData> onComplete, Action<string> onError = null)
    {
        string url = $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session.json";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        var json = req.downloadHandler.text;
        if (json == "null" || string.IsNullOrEmpty(json))
        {
            onError?.Invoke("No active session in Firebase");
            yield break;
        }

        var raw = JsonUtility.FromJson<ActiveSessionJson>(json);
        if (raw == null || string.IsNullOrEmpty(raw.child_id))
        {
            onError?.Invoke("Invalid session data");
            yield break;
        }

        onComplete?.Invoke(new ActiveSessionData
        {
            childId   = raw.child_id,
            childName = raw.child_name,
            ageYears  = raw.age_years,
            nextWeek  = raw.next_week
        });
    }

    public IEnumerator GetWordSchedule(string childId, int weekNum,
        Action<List<WordModel>> onComplete, Action<string> onError = null)
    {
        string url = $"{MITRAConfig.FIREBASE_URL}/mitra-parents/{childId}/schedule/week{weekNum}.json";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        onComplete?.Invoke(ParseWordSchedule(req.downloadHandler.text));
    }

    public IEnumerator WriteProgress(string childId, string word, float score,
        int repsDone, int repsTarget, Action onComplete = null, Action<string> onError = null)
    {
        string url = $"{MITRAConfig.FIREBASE_URL}/mitra-parents/{childId}/progress/{word}.json";
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string json = $"{{\"score\":{score:F1},\"reps_done\":{repsDone},\"reps_target\":{repsTarget},\"trend\":\"stable\",\"timestamp\":{ts}}}";
        yield return PatchRequest(url, json, onComplete, onError);
    }

    public IEnumerator UpdateNextWeek(string childId, int weekNum,
        Action onComplete = null, Action<string> onError = null)
    {
        string url = $"{MITRAConfig.FIREBASE_URL}/mitra-parents/{childId}/metadata.json";
        string json = $"{{\"next_week\":{weekNum}}}";
        yield return PatchRequest(url, json, onComplete, onError);
    }

    // ── Live session status writes (fire-and-forget) ──────────────────────────

    public void WriteSessionField(string field, string value)
        => StartCoroutine(PutRequest(
            $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session/{field}.json",
            $"\"{value}\""));

    public void WriteSessionField(string field, float value)
        => StartCoroutine(PutRequest(
            $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session/{field}.json",
            value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)));

    public void WriteSessionField(string field, int value)
        => StartCoroutine(PutRequest(
            $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session/{field}.json",
            value.ToString()));

    public void WriteSessionField(string field, bool value)
        => StartCoroutine(PutRequest(
            $"{MITRAConfig.FIREBASE_URL}/mitra-parents/active_session/{field}.json",
            value ? "true" : "false"));

    // ── helpers ──────────────────────────────────────────────────────────────

    private IEnumerator PutRequest(string url, string rawJson)
    {
        byte[] body = Encoding.UTF8.GetBytes(rawJson);
        using var req = new UnityWebRequest(url, "PUT");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"MITRA Firebase PUT failed: {req.error}");
    }

    private IEnumerator PatchRequest(string url, string json,
        Action onComplete, Action<string> onError)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        using var req = new UnityWebRequest(url, "PATCH");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            onError?.Invoke(req.error);
        else
            onComplete?.Invoke();
    }

    private List<WordModel> ParseWordSchedule(string json)
    {
        var words = new List<WordModel>();
        if (string.IsNullOrEmpty(json) || json == "null") return words;

        var entries = Regex.Matches(json, @"""([^""]+)""\s*:\s*\{([^}]*)\}");
        foreach (Match m in entries)
        {
            string wordName = m.Groups[1].Value;
            string obj      = m.Groups[2].Value;

            var repsM  = Regex.Match(obj, @"""reps""\s*:\s*(\d+)");
            var soundM = Regex.Match(obj, @"""target_sound""\s*:\s*""([^""]*)""");
            var tipM   = Regex.Match(obj, @"""pronunciation_tip""\s*:\s*""([^""]*)""");

            words.Add(new WordModel
            {
                word             = wordName,
                reps             = repsM.Success  ? int.Parse(repsM.Groups[1].Value)  : 1,
                targetSound      = soundM.Success ? soundM.Groups[1].Value            : "",
                pronunciationTip = tipM.Success   ? tipM.Groups[1].Value              : ""
            });
        }
        return words;
    }

    [Serializable]
    private class ActiveSessionJson
    {
        public string child_id;
        public string child_name;
        public float  age_years;
        public int    next_week;
        public string avatar_id;
    }

    [Serializable]
    private class ActiveSessionControlJson
    {
        public bool paused;
        public bool skip_word;
        public bool repeat_word;
    }
}

// ── shared data models ────────────────────────────────────────────────────────

public class ActiveSessionData
{
    public string childId;
    public string childName;
    public float  ageYears;
    public int    nextWeek;
}

public class WordModel
{
    public string word;
    public int    reps;
    public string targetSound;
    public string pronunciationTip;
}
