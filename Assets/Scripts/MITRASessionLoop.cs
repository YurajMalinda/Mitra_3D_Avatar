using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MITRASessionLoop : MonoBehaviour
{
    [SerializeField] private MITRAFirebaseClient      firebaseClient;
    [SerializeField] private MITRATTSController       ttsController;
    [SerializeField] private MITRAVoskListener        voskListener;
    [SerializeField] private MITRAScorer              scorer;
    [SerializeField] private MITRAAvatarController    avatarController;
    [SerializeField] private MITRAProgressController  progressController;
    [SerializeField] private MITRAStarRatingController starRating;

    private const int MAX_CONSECUTIVE_FAILURES = 3;
    private const int BREAK_EVERY_N_TRIALS     = 10;

    private static readonly string[] RepPrompts = {
        "{0}. Say {0}!",
        "Can you say {0}?",
        "{0}! Your turn!",
        "I love {0}! Now you say {0}!",
        "...{0}?"
    };

    // ── Real-time control flags ───────────────────────────────────────────────
    private volatile bool _isPaused        = false;
    private volatile bool _skipRequested   = false;
    private volatile bool _repeatRequested = false;
    private volatile bool _sessionEndRequested = false;

    private bool _sessionEnding = false;
    private bool sessionRunning = false;

    // Exposed to MITRASessionManager so it can drive session lifecycle
    public bool IsRunning => sessionRunning;
    public bool IsEnding  => _sessionEnding;

    public void RequestEnd()
    {
        if (!_sessionEnding)
            _sessionEndRequested = true;
    }

    void Start()
    {
        StartCoroutine(RegisterListeners());
        StartCoroutine(PollControlFlags());
    }

    void OnDestroy()
    {
        firebaseClient.RemoveRealtimeListeners();
    }

    private IEnumerator RegisterListeners()
    {
        while (!firebaseClient.SdkReady) yield return null;

        firebaseClient.InitRealtimeListeners(
            paused  => { Debug.Log($"MITRA: Pause → {paused}"); _isPaused = paused; },
            ()      => { Debug.Log("MITRA: Skip requested");     _skipRequested   = true; },
            ()      => { Debug.Log("MITRA: Repeat requested");   _repeatRequested = true; }
        );
    }

    // REST fallback for pause/skip/repeat — polls every 1 s while session is running.
    private IEnumerator PollControlFlags()
    {
        while (!firebaseClient.SdkReady) yield return null;

        while (enabled)
        {
            yield return new WaitForSeconds(1f);
            if (!sessionRunning) continue;

            bool p = false, s = false, r = false;
            yield return StartCoroutine(
                firebaseClient.GetSessionFlags((a, b, c) => { p = a; s = b; r = c; }));

            if (p != _isPaused)
            {
                Debug.Log($"MITRA: [Poll] Pause → {p}");
                _isPaused = p;
            }
            if (s && !_skipRequested)
            {
                Debug.Log("MITRA: [Poll] Skip requested");
                _skipRequested = true;
            }
            if (r && !_repeatRequested)
            {
                Debug.Log("MITRA: [Poll] Repeat requested");
                _repeatRequested = true;
            }
        }
    }

    public void StartSession()
    {
        if (!sessionRunning)
            StartCoroutine(RunSession());
    }

    private IEnumerator RunSession()
    {
        sessionRunning       = true;
        _sessionEnding       = false;
        _sessionEndRequested = false;

        var session  = MITRASessionManager.Session;
        var wordList = MITRASessionManager.WordList;

        if (session == null || wordList == null || wordList.Count == 0)
        {
            Debug.LogError("MITRA: Cannot start session — no data loaded");
            sessionRunning = false;
            yield break;
        }

        MITRAConfig.SessionVocabCSV = string.Join(",", wordList.ConvertAll(w => w.word.ToLower()));
        Debug.Log($"MITRA: Session started — {wordList.Count} words, week {session.nextWeek}");

        string bestWord    = null;
        float  bestScore   = 0f;
        int    totalTrials = 0;

        for (int wordIndex = 0; wordIndex < wordList.Count; wordIndex++)
        {
            if (_sessionEndRequested) break;

            var word = wordList[wordIndex];

            progressController?.UpdateProgress(wordIndex + 1, wordList.Count);
            firebaseClient.WriteSessionField("current_word", word.word);
            firebaseClient.WriteSessionField("current_word_index", wordIndex);

            yield return StartCoroutine(avatarController.IntroduceWord(word.word));
            yield return new WaitForSeconds(0.5f);

            int   rep                 = 1;
            int   consecutiveFailures = 0;
            bool  skippedEarly        = false;
            float bestRepScore        = 0f;

            while (rep <= word.reps)
            {
                if (_sessionEndRequested) break;

                yield return StartCoroutine(WaitWhilePaused());

                if (_sessionEndRequested) break;

                // Skip flag — check at rep start (may have been set during previous avatar reaction)
                if (_skipRequested)
                {
                    _skipRequested = false;
                    firebaseClient.ClearFlag("skip_word");
                    yield return StartCoroutine(
                        avatarController.SpeakWord("No problem! Let us try the next word!"));
                    skippedEarly = true;
                    break;
                }

                Debug.Log($"MITRA: [{word.word}] rep {rep}/{word.reps}");

                string template = RepPrompts[(rep - 1) % RepPrompts.Length];
                yield return StartCoroutine(avatarController.SpeakWordModel(template, word.word));
                yield return new WaitForSeconds(0.5f);

                yield return StartCoroutine(avatarController.StartListening());

                string transcription = null;
                yield return StartCoroutine(
                    voskListener.StartListening(wordList, t => transcription = t));

                if (string.IsNullOrEmpty(transcription))
                {
                    yield return StartCoroutine(avatarController.NoSpeechDetected(word.word));
                    continue;
                }

                if (_skipRequested)
                {
                    _skipRequested = false;
                    firebaseClient.ClearFlag("skip_word");
                    yield return StartCoroutine(
                        avatarController.SpeakWord("No problem! Let us try the next word!"));
                    skippedEarly = true;
                    break;
                }

                if (_repeatRequested)
                {
                    _repeatRequested = false;
                    firebaseClient.ClearFlag("repeat_word");
                    yield return StartCoroutine(avatarController.SpeakWordModel(template, word.word));
                    continue;
                }

                float score = 0f;
                if (!transcription.Equals(word.word, System.StringComparison.OrdinalIgnoreCase))
                    Debug.Log($"MITRA Score: 0.000  (wrong word: heard '{transcription}', expected '{word.word}')");
                else
                    yield return StartCoroutine(scorer.ScoreRecording(s => score = s));

                bool isLastRep = rep >= word.reps;
                yield return StartCoroutine(
                    avatarController.ReactToScore(score, session.ageYears, word.word, isLastRep));

                yield return StartCoroutine(firebaseClient.WriteProgress(
                    session.childId, word.word, score * 100f, rep, word.reps));

                firebaseClient.WriteSessionField("last_score", score * 100f);

                if (score > bestScore) { bestScore = score; bestWord = word.word; }
                if (score > bestRepScore) bestRepScore = score;

                totalTrials++;
                if (totalTrials % BREAK_EVERY_N_TRIALS == 0)
                    yield return StartCoroutine(avatarController.TakeABreak());

                float threshold = MITRAConfig.GetPassThreshold(session.ageYears);
                if (score < threshold - 0.15f)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                    {
                        yield return StartCoroutine(ttsController.SpeakWord(
                            $"Let us come back to {word.word} later! Good effort!"));
                        skippedEarly = true;
                        break;
                    }
                }
                else
                {
                    consecutiveFailures = 0;
                }

                if (rep >= word.reps)
                    StartCoroutine(avatarController.PreloadWordComplete(word.word));

                yield return new WaitForSeconds(1f);
                rep++;
            }

            if (!skippedEarly && !_sessionEndRequested)
            {
                yield return StartCoroutine(avatarController.WordComplete(word.word));
                firebaseClient.WriteSessionField($"completed_words/{word.word}", true);

                if (starRating != null)
                {
                    float threshold = MITRAConfig.GetPassThreshold(session.ageYears);
                    int stars = bestRepScore >= 0.85f ? 3 : bestRepScore >= threshold ? 2 : 1;
                    string msg = stars == 3 ? "Amazing!" : stars == 2 ? "Well done!" : "Good try!";
                    yield return StartCoroutine(starRating.ShowStars(stars, msg));
                }

                yield return new WaitForSeconds(2f);
            }
        }

        // ── Session end ───────────────────────────────────────────────────────
        // Set _sessionEnding BEFORE writing status so the StatusChanged listener
        // doesn't call RequestEnd() when it sees 'completed'.
        _sessionEnding = true;

        bool naturalEnd = !_sessionEndRequested;

        if (naturalEnd)
        {
            Debug.Log("MITRA: Session complete — natural end");

            yield return StartCoroutine(
                firebaseClient.UpdateNextWeek(session.childId, session.nextWeek + 1));

            yield return StartCoroutine(avatarController.SessionComplete(bestWord));

            yield return new WaitForSeconds(0.5f);
            yield return StartCoroutine(
                ttsController.SpeakWord("Tell your mummy or daddy what words you practised!"));

            // Signal Flutter to show the summary screen
            firebaseClient.WriteSessionField("status", "completed");
        }
        else
        {
            Debug.Log("MITRA: Session ended early by parent app");

            // Give the same celebration as a natural end, honouring any words already practised.
            // Flutter already wrote status: 'completed' so no status write needed here.
            yield return StartCoroutine(avatarController.SessionComplete(bestWord));
            yield return new WaitForSeconds(0.5f);
            yield return StartCoroutine(
                ttsController.SpeakWord("Tell your mummy or daddy what words you practised!"));
        }

        sessionRunning = false;
    }

    private IEnumerator WaitWhilePaused()
    {
        while (_isPaused) yield return null;
    }
}
