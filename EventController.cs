using System.Collections;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

namespace Skydome.Audio
{
    public class EventController : MonoBehaviour
    {
        [Range(0f, 1f)] public float eventVolume = 1f;
        public int stepSoundFactor = 20;
        public EventType eventType;

        PLAYBACK_STATE _state;
        StudioEventEmitter _emitter;
        StepSoundChecker _stepSoundChecker;
        Coroutine _fadeToPause;
        FadeStatus _fadeStatus;
        float from, to, time;
        int _timelinePosition = 0;

        private void Awake()
        {
            _emitter = GetComponent<StudioEventEmitter>();
            _stepSoundChecker = GetComponent<StepSoundChecker>();
            _emitter.EventInstance.setVolume(eventVolume);
            if (eventType == EventType.Music)
            {
                _emitter.Play();
                _fadeStatus = FadeStatus.Unpaused;
            }
        }

        /// <summary> Animation event </summary>
        public void PlayFootstep(int step)
        {
            switch (_stepSoundChecker.GetStepSound(step))
            {
                case StepSoundType.Grass:
                    _emitter.SetParameter("Dirt", 0f, ignoreseekspeed: true);
                    _emitter.SetParameter("Grass", 1f, ignoreseekspeed: true);
                    break;
                case StepSoundType.Dirt:
                    _emitter.SetParameter("Dirt", 1f, ignoreseekspeed: true);
                    _emitter.SetParameter("Grass", 0f, ignoreseekspeed: true);
                    break;
                default:
                    Debug.LogWarning("Unimplemented step sound type!");
                    break;
            }

            _emitter.EventInstance.setVolume(eventVolume * stepSoundFactor);
            _emitter.Play();
            _emitter.EventInstance.release();
        }

        public void SetFade(FadeTarget fadeTarget, float duration)
        {
            // current fade is valid
            if ((fadeTarget == FadeTarget.Pause && _fadeStatus == FadeStatus.Pausing) || (fadeTarget == FadeTarget.Unpause && _fadeStatus == FadeStatus.Unpausing))
                return;

            // set time for fade swap
            if ((_fadeStatus == FadeStatus.Pausing && fadeTarget == FadeTarget.Unpause) || (_fadeStatus == FadeStatus.Unpausing && fadeTarget == FadeTarget.Pause))
                time = 1f - time;
            // set time for new fade
            else if (_fadeStatus == FadeStatus.Paused || _fadeStatus == FadeStatus.Unpaused)
                time = 0f;

            // setup fade
            if (fadeTarget == FadeTarget.Pause)
            {
                _fadeStatus = FadeStatus.Pausing;
                from = eventVolume; to = 0f;
            }
            else
            {
                _fadeStatus = FadeStatus.Unpausing;
                from = 0f; to = eventVolume;
            }

            // stop and start fade
            if (_fadeToPause != null) StopCoroutine(_fadeToPause);
            _fadeToPause = StartCoroutine(Fade(duration));
        }

        void Unpause()
        {
            _emitter.EventInstance.getPlaybackState(out _state);
            switch (_state)
            {
                case PLAYBACK_STATE.PLAYING or PLAYBACK_STATE.STARTING:
                    _emitter.EventInstance.setPaused(false);
                    _fadeStatus = FadeStatus.Unpaused;
                    break;
                case PLAYBACK_STATE.STOPPED:
                    _emitter.Stop();
                    _emitter.Play();
                    _emitter.EventInstance.setTimelinePosition(_timelinePosition);
                    _fadeStatus = FadeStatus.Unpaused;
                    break;
                default:
                    Debug.LogWarning("Unimplemented case '" + _state.ToString() + "'!", this);
                    return;
            }
        }

        void Pause()
        {
            _emitter.EventInstance.getTimelinePosition(out _timelinePosition);
            _emitter.EventInstance.setPaused(true);
            _fadeStatus = FadeStatus.Paused;
        }

        IEnumerator Fade(float duration)
        {
            if (_fadeStatus == FadeStatus.Unpausing) Unpause();

            if (duration > 0f) // divide by zero check
                while (time < 1f)
                {
                    time += Time.deltaTime / duration;
                    _emitter.EventInstance.setVolume(Mathf.Lerp(from, to, time));
                    yield return null;
                }
            _emitter.EventInstance.setVolume(to);

            if (_fadeStatus == FadeStatus.Pausing) Pause();

            if (_fadeStatus == FadeStatus.Pausing || _fadeStatus == FadeStatus.Unpausing)
                Debug.LogError("Failed on '" + _fadeStatus.ToString() + "'!", this);

            StopCoroutine(_fadeToPause);
            _fadeToPause = null;
        }
    }
}
