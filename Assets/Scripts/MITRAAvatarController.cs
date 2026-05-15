using System;
using System.Collections;
using UnityEngine;
using VRM;

public class MITRAAvatarController : MonoBehaviour
{
    [SerializeField] private MITRATTSController       ttsController;
    [SerializeField] private MITRAWordImageController imageController;
    [SerializeField] private MITRACelebrationController celebrationController;

    private Animator           animator;
    private VRMBlendShapeProxy blendShapeProxy;

    private const string TRIG_HAPPY       = "Happy";
    private const string TRIG_EXCELLENT   = "Excellent";
    private const string TRIG_ENCOURAGE   = "Encourage";
    private const string TRIG_SPEAK       = "Speak";
    private const string TRIG_THINK       = "Think";
    private const string TRIG_ACKNOWLEDGE = "Acknowledge";
    private const string TRIG_VICTORY     = "Victory";
    private const string TRIG_IDLE        = "Idle";

    private readonly string[] greetPhrases = {
        "Hello {0}! I am so happy to see you today!",
        "Hi {0}! Are you ready to say some amazing words?",
        "Hey {0}! Let us have so much fun practising today!",
        "Welcome {0}! I have been waiting for you!"
    };
    private readonly string[] excellentPhrases = {
        "Wow! That sounded incredible!",
        "Amazing! Did you hear how good that was?",
        "Perfect! That was absolutely brilliant!",
        "Oh wow! You are so good at this!"
    };
    private readonly string[] greatPhrases = {
        "That sounded so clear! Really good!",
        "Brilliant! You are doing so well!",
        "I can really hear you getting better!",
        "That was great! You should be so proud!"
    };
    private readonly string[] passPhrases = {
        "Good job! I heard that!",
        "Well done! That was nice!",
        "You said it! Good work!",
        "Nice one! Keep going!"
    };
    private readonly string[] encouragePhrases = {
        "Almost! Give it one more go!",
        "So close! Try one more time!",
        "I know you can do it! Try again!"
    };
    // Last rep, score below threshold — no retry is coming
    private readonly string[] lastRepPhrases = {
        "Good try! We will practise that more!",
        "Nice effort! You are learning so well!",
        "That was a great try! Keep it up!"
    };
    private readonly string[] retryPhrases = {
        "Let me show you. {0}. Can you try?",
        "Watch carefully. {0}. Now you go!",
        "Listen! {0}. Your turn now!"
    };
    private readonly string[] noSpeechPhrases = {
        "I did not hear you! Can you try again?",
        "Come on! I know you can do it!",
        "Speak up a little! I want to hear you!"
    };
    private readonly string[] wordCompletePhrases = {
        "Brilliant! You practised that so well!",
        "Well done! You are so good at this!",
        "Amazing job! You did it!",
        "Fantastic! I am really proud of you!",
        "You did it! That was wonderful!"
    };
    private readonly string[] introduceWordPhrases = {
        "Our next word is {0}!",
        "Now let us try {0}!",
        "Here comes {0}!",
        "Ready for this one? {0}!"
    };
    private readonly string[] sessionCompletePhrases = {
        "Wow! You practised all your words! Amazing!",
        "You did so well today! I am so proud of you!",
        "That was brilliant! You are a star!"
    };
    private readonly string[] breakPhrases = {
        "Wow you are working so hard! Take a little rest!",
        "Amazing effort! Let us have a quick break!",
        "You are doing so well! Quick rest time!"
    };

    void Awake()
    {
        animator        = GetComponent<Animator>();
        blendShapeProxy = GetComponent<VRMBlendShapeProxy>();
    }

    // ── Public Coroutine API ──────────────────────────────────────────────────

    // Waving + greeting — DTTC rapport building
    public IEnumerator GreetChild(string childName)
    {
        SetExpression(BlendShapePreset.Joy, 1.0f);
        yield return StartCoroutine(
            ttsController.GenerateAudio(string.Format(GetRandomPhrase(greetPhrases), childName)));
        animator.SetTrigger(TRIG_ENCOURAGE);
        yield return StartCoroutine(ttsController.PlayAudio());
        yield return new WaitForSeconds(0.5f);
        ReturnToIdle();
    }

    // Talking animation + any text — used for phrases that don't embed a modeled word
    public IEnumerator SpeakWord(string text)
    {
        SetExpression(BlendShapePreset.Neutral, 1.0f);
        yield return StartCoroutine(ttsController.GenerateAudio(text));
        animator.SetTrigger(TRIG_SPEAK);
        yield return StartCoroutine(ttsController.PlayAudio());
        ReturnToIdle();
    }

    // Talking animation — word is spoken as a clearly articulated utterance within the prompt
    // Piper produces natural pauses from the surrounding punctuation (e.g. "Listen carefully. apple. Your turn!")
    public IEnumerator SpeakWordModel(string template, string word)
    {
        SetExpression(BlendShapePreset.Neutral, 1.0f);
        yield return StartCoroutine(ttsController.GenerateAudio(string.Format(template, word)));
        animator.SetTrigger(TRIG_SPEAK);
        yield return StartCoroutine(ttsController.PlayAudio());
        ReturnToIdle();
    }

    // Announces the upcoming word before reps begin (DTTC session flow §2.2 Step 1)
    public IEnumerator IntroduceWord(string word)
    {
        SetExpression(BlendShapePreset.Neutral, 1.0f);
        // Show word image first so child sees the picture before hearing the word
        if (imageController != null)
            yield return StartCoroutine(imageController.ShowWordImage(word));
        yield return StartCoroutine(ttsController.GenerateAudio(string.Format(GetRandomPhrase(introduceWordPhrases), word)));
        animator.SetTrigger(TRIG_SPEAK);
        yield return StartCoroutine(ttsController.PlayAudio());
        ReturnToIdle();
    }

    // Thinking animation — signals child's turn
    public IEnumerator StartListening()
    {
        animator.SetTrigger(TRIG_THINK);
        SetExpression(BlendShapePreset.Neutral, 1.0f);
        yield break;
    }

    // Core feedback — animation + word-specific verbal response (DTTC + ASHA)
    public IEnumerator ReactToScore(float score, float ageYears, string word, bool isLastRep = false)
    {
        float threshold = MITRAConfig.GetPassThreshold(ageYears);

        if (score <= 0.01f)
        {
            string phrase = isLastRep
                ? string.Format(GetRandomPhrase(lastRepPhrases), word)
                : string.Format(GetRandomPhrase(retryPhrases), word);
            yield return StartCoroutine(ttsController.GenerateAudio(phrase));
            animator.SetTrigger(TRIG_ACKNOWLEDGE);
            yield return StartCoroutine(ttsController.PlayAudio());
        }
        else if (score >= 0.85f)
        {
            SetExpression(BlendShapePreset.Joy, 1.0f);
            animator.SetTrigger(TRIG_VICTORY);
            celebrationController?.PlayVictory();
            if (!isLastRep)
            {
                yield return StartCoroutine(ttsController.GenerateAudio(GetRandomPhrase(excellentPhrases)));
                yield return StartCoroutine(ttsController.PlayAudio());
            }
        }
        else if (score >= 0.65f)
        {
            SetExpression(BlendShapePreset.Joy, 1.0f);
            animator.SetTrigger(TRIG_EXCELLENT);
            celebrationController?.PlayExcellent();
            if (!isLastRep)
            {
                yield return StartCoroutine(ttsController.GenerateAudio(GetRandomPhrase(greatPhrases)));
                yield return StartCoroutine(ttsController.PlayAudio());
            }
        }
        else if (score >= threshold)
        {
            SetExpression(BlendShapePreset.Joy, 0.7f);
            animator.SetTrigger(TRIG_HAPPY);
            celebrationController?.PlayGood();
            if (!isLastRep)
            {
                yield return StartCoroutine(ttsController.GenerateAudio(GetRandomPhrase(passPhrases)));
                yield return StartCoroutine(ttsController.PlayAudio());
            }
        }
        else if (score >= threshold - 0.15f)
        {
            string phrase = isLastRep
                ? string.Format(GetRandomPhrase(lastRepPhrases), word)
                : GetRandomPhrase(encouragePhrases);
            yield return StartCoroutine(ttsController.GenerateAudio(phrase));
            animator.SetTrigger(TRIG_ACKNOWLEDGE);
            yield return StartCoroutine(ttsController.PlayAudio());
        }
        else
        {
            string phrase = isLastRep
                ? string.Format(GetRandomPhrase(lastRepPhrases), word)
                : string.Format(GetRandomPhrase(retryPhrases), word);
            yield return StartCoroutine(ttsController.GenerateAudio(phrase));
            animator.SetTrigger(TRIG_ENCOURAGE);
            yield return StartCoroutine(ttsController.PlayAudio());
        }

        yield return new WaitForSeconds(0.5f);
        ReturnToIdle();
        if (imageController != null)
            yield return StartCoroutine(imageController.HideWordImage());
    }

    public IEnumerator NoSpeechDetected(string word)
    {
        SetExpression(BlendShapePreset.Neutral, 1.0f);
        yield return StartCoroutine(ttsController.GenerateAudio(string.Format(GetRandomPhrase(noSpeechPhrases), word)));
        animator.SetTrigger(TRIG_ENCOURAGE);
        yield return StartCoroutine(ttsController.PlayAudio());
        ReturnToIdle();
    }

    private bool _wordCompletePreloaded  = false;
    private bool _wordCompletePreloading = false;

    // Call during the inter-rep wait on the last rep to pre-generate audio and hide the gap
    public IEnumerator PreloadWordComplete(string word)
    {
        _wordCompletePreloaded  = false;
        _wordCompletePreloading = true;
        yield return StartCoroutine(ttsController.GenerateAudio(
            string.Format(GetRandomPhrase(wordCompletePhrases), word)));
        _wordCompletePreloaded  = true;
        _wordCompletePreloading = false;
    }

    public IEnumerator WordComplete(string word)
    {
        SetExpression(BlendShapePreset.Joy, 1.0f);
        // If preload is still running, wait for it rather than starting a second GenerateAudio
        if (_wordCompletePreloading)
            yield return new WaitUntil(() => !_wordCompletePreloading);
        if (!_wordCompletePreloaded)
            yield return StartCoroutine(ttsController.GenerateAudio(
                string.Format(GetRandomPhrase(wordCompletePhrases), word)));
        _wordCompletePreloaded = false;
        animator.SetTrigger(TRIG_EXCELLENT);
        yield return StartCoroutine(ttsController.PlayAudio());
        yield return new WaitForSeconds(0.5f);
        ReturnToIdle();
    }

    // Celebrates every 10 trials — prevents fatigue in 3-6 year olds (ASHA §1.3)
    public IEnumerator TakeABreak()
    {
        SetExpression(BlendShapePreset.Joy, 1.0f);
        yield return StartCoroutine(ttsController.GenerateAudio(GetRandomPhrase(breakPhrases)));
        animator.SetTrigger(TRIG_EXCELLENT);
        yield return StartCoroutine(ttsController.PlayAudio());
        yield return new WaitForSeconds(2f);
        ReturnToIdle();
    }

    // bestWord may be null if no attempts were scored
    public IEnumerator SessionComplete(string bestWord = null)
    {
        string phrase = GetRandomPhrase(sessionCompletePhrases);
        if (!string.IsNullOrEmpty(bestWord))
            phrase += $" You were really great at {bestWord}!";
        SetExpression(BlendShapePreset.Joy, 1.0f);
        yield return StartCoroutine(ttsController.GenerateAudio(phrase));
        animator.SetTrigger(TRIG_VICTORY);
        yield return StartCoroutine(ttsController.PlayAudio());
        yield return new WaitForSeconds(0.5f);
        ReturnToIdle();
    }

    public void ReturnToIdle()
    {
        animator.SetTrigger(TRIG_IDLE);
        SetExpression(BlendShapePreset.Neutral, 1.0f);
    }

    public void SetExpression(BlendShapePreset preset, float value)
    {
        if (blendShapeProxy == null) return;
        blendShapeProxy.AccumulateValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Neutral), 0f);
        blendShapeProxy.AccumulateValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Joy), 0f);
        blendShapeProxy.AccumulateValue(BlendShapeKey.CreateFromPreset(preset), value);
        blendShapeProxy.Apply();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetRandomPhrase(string[] phrases)
    {
        return phrases[UnityEngine.Random.Range(0, phrases.Length)];
    }


}
