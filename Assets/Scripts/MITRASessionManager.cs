using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MITRASessionManager : MonoBehaviour
{
    [SerializeField] private MITRAFirebaseClient   firebaseClient;
    [SerializeField] private MITRAAvatarController avatarController;
    [SerializeField] private MITRAVoskListener     voskListener;
    [SerializeField] private MITRASessionLoop      sessionLoop;

    public static ActiveSessionData Session  { get; private set; }
    public static List<WordModel>   WordList { get; private set; }
    public static bool              IsReady  { get; private set; }

    private bool _sessionStarted = false;
    private bool _sessionAborted = false;

    void Start()
    {
        StartCoroutine(SubscribeWhenReady());
        StartCoroutine(PollStatusFallback());
    }

    void OnDestroy()
    {
        firebaseClient.StatusChanged -= HandleStatusChange;
    }

    // Waits for SDK, then subscribes to status changes and handles current value
    private IEnumerator SubscribeWhenReady()
    {
        while (!firebaseClient.SdkReady) yield return null;

        firebaseClient.StatusChanged += HandleStatusChange;

        // Firebase fires ValueChanged immediately on registration, but if we
        // subscribed after the first fire, LastKnownStatus holds the cached value.
        HandleStatusChange(firebaseClient.LastKnownStatus);

        Debug.Log($"MITRA: Waiting for session signal — current status: '{firebaseClient.LastKnownStatus}'");
    }

    private void HandleStatusChange(string status)
    {
        if (status == "pending" && !_sessionStarted)
        {
            _sessionStarted = true;
            _sessionAborted = false;
            Debug.Log("MITRA: Session signal received (status: pending) — loading session");
            StartCoroutine(LoadSession());
        }
        else if (status == "completed" && _sessionStarted)
        {
            _sessionAborted = true;   // abort warm-up if still in LoadSession
            // Tell the session loop to stop — works even before IsRunning is true
            if (sessionLoop != null && !sessionLoop.IsEnding)
                sessionLoop.RequestEnd();
            _sessionStarted = false;
        }
        else if (string.IsNullOrEmpty(status))
        {
            _sessionStarted = false;
            _sessionAborted = false;
        }
    }

    // REST polling safety net — fires every 2 s while no session is running.
    // Guarantees session starts even if the SDK real-time listener misses the signal.
    private IEnumerator PollStatusFallback()
    {
        while (!firebaseClient.SdkReady) yield return null;

        while (enabled)
        {
            yield return new WaitForSeconds(2f);

            string polledStatus = null;
            yield return StartCoroutine(firebaseClient.GetSessionStatus(s => polledStatus = s));

            if (!string.IsNullOrEmpty(polledStatus))
            {
                Debug.Log($"MITRA: [Poll] status = '{polledStatus}'");
                HandleStatusChange(polledStatus);
            }
        }
    }

    private IEnumerator LoadSession()
    {
        // Signal to Flutter immediately that Unity has received the session
        firebaseClient.WriteSessionField("status", "active");

        Debug.Log("MITRA: Loading active session from Firebase...");

        // ── 1. Read active_session ────────────────────────────────────────
        ActiveSessionData session = null;
        string error = null;

        yield return StartCoroutine(firebaseClient.GetActiveSession(
            d   => session = d,
            err => error   = err
        ));

        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }

        if (error != null)
        {
            Debug.LogError($"MITRA: Session load failed — {error}");
            firebaseClient.WriteSessionField("status", "pending");
            _sessionStarted = false;
            yield break;
        }

        Session = session;
        Debug.Log($"MITRA: Session — child: {Session.childName}, age: {Session.ageYears}, week: {Session.nextWeek}");

        // ── 2. Read word schedule ─────────────────────────────────────────
        List<WordModel> words = null;
        error = null;

        yield return StartCoroutine(firebaseClient.GetWordSchedule(Session.childId, Session.nextWeek,
            w   => words = w,
            err => error  = err
        ));

        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }

        if (error != null)
        {
            Debug.LogError($"MITRA: Word schedule load failed — {error}");
            firebaseClient.WriteSessionField("status", "pending");
            _sessionStarted = false;
            yield break;
        }

        WordList = words;
        Debug.Log($"MITRA: Loaded {WordList.Count} words for week {Session.nextWeek}");

        IsReady = true;

        // ── 3. Greet child ────────────────────────────────────────────────
        yield return StartCoroutine(avatarController.GreetChild(Session.childName));
        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }
        yield return new WaitForSeconds(0.5f);

        // ── 4. Warm-up ────────────────────────────────────────────────────
        yield return StartCoroutine(
            avatarController.SpeakWord("Let us warm up! Say your name for me!"));
        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }
        yield return StartCoroutine(avatarController.StartListening());

        string warmUpResponse = null;
        yield return StartCoroutine(
            voskListener.StartListening(new List<WordModel>(), r => warmUpResponse = r));
        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }

        if (!string.IsNullOrEmpty(warmUpResponse))
            yield return StartCoroutine(avatarController.SpeakWord("Great! Well done!"));
        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }

        yield return new WaitForSeconds(0.5f);

        // ── 5. Session preview ────────────────────────────────────────────
        string previewWords = WordList.Count > 2
            ? $"{WordList[0].word}, {WordList[1].word}, and more"
            : string.Join(" and ", WordList.ConvertAll(w => w.word));

        yield return StartCoroutine(
            avatarController.SpeakWord($"Today we will practice {previewWords}!"));
        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }

        yield return new WaitForSeconds(1f);
        if (_sessionAborted) { avatarController.ReturnToIdle(); yield break; }

        // ── 6. Hand off to session loop ───────────────────────────────────
        sessionLoop.StartSession();
    }
}
