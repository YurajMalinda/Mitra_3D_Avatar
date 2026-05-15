using System.Collections;
using UnityEngine;

public class MITRACelebrationController : MonoBehaviour
{
    [SerializeField] private ParticleSystem confettiSystem;
    [SerializeField] private ParticleSystem starSystem;

    public void PlayExcellent()
    {
        if (confettiSystem != null) { confettiSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); confettiSystem.Play(); }
        if (starSystem     != null) { starSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);     starSystem.Play(); }
    }

    public void PlayVictory()
    {
        PlayExcellent();
        StartCoroutine(PlayDelayed(1.0f));
    }

    public void PlayGood()
    {
        if (starSystem != null) { starSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); starSystem.Play(); }
    }

    private IEnumerator PlayDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (confettiSystem != null) { confettiSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); confettiSystem.Play(); }
    }
}
