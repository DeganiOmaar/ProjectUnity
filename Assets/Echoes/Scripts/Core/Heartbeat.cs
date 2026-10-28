using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Echoes
{
    // Core v1 haptic heartbeat: pulses speed up as the monster gets closer.
    // Also drives the fear post-processing (vignette, aberration, desaturation).
    public class Heartbeat : MonoBehaviour
    {
        public static float Fear { get; private set; }   // 0 = nothing near .. 1 = on top of you
        public static float Pulse { get; private set; }  // 1 right after a beat, decays
        public float range = 24f;

        AudioSource src;
        float next;
        Vignette vignette;
        ChromaticAberration aberration;
        ColorAdjustments color;
        LensDistortion lens;
        FilmGrain grain;

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>();
            var vol = FindAnyObjectByType<Volume>();
            if (vol && vol.profile)
            {
                vol.profile.TryGet(out vignette);
                vol.profile.TryGet(out aberration);
                vol.profile.TryGet(out color);
                vol.profile.TryGet(out lens);
                vol.profile.TryGet(out grain);
            }
        }

        void Update()
        {
            var s = Survivor.Local;
            if (!s) return;
            float dt = Time.deltaTime;
            float d = Monster.NearestDistance(s.Position);
            Fear = Mathf.MoveTowards(Fear, Mathf.Clamp01(1f - (d - 2f) / (range - 2f)), dt * 2f);

            if (GameManager.Playing && Fear > 0.02f && Time.time >= next)
            {
                next = Time.time + 60f / Mathf.Lerp(55f, 170f, Fear);
                Pulse = 1f;
                src.PlayOneShot(AudioSynth.Heartbeat, Mathf.Lerp(0.25f, 1f, Fear));
                Haptics.Pulse((long)Mathf.Lerp(25, 70, Fear), (int)Mathf.Lerp(60, 255, Fear), Fear);
            }
            Pulse = Mathf.MoveTowards(Pulse, 0f, dt * 4f);

            float dark = 1f - s.LightOutput;
            bool downed = s.Downed;
            if (vignette) vignette.intensity.value = Mathf.Lerp(0.3f, 0.48f, dark) + Pulse * 0.1f * Fear + (downed ? 0.15f : 0);
            if (aberration) aberration.intensity.value = 0.08f + Fear * 0.55f + (s.Shaking ? 0.3f : 0);
            if (color) color.saturation.value = downed ? -100f : Mathf.Lerp(-18f, -70f, Fear);
            if (lens) lens.intensity.value = Mathf.Lerp(lens.intensity.value, downed ? -0.45f : -Pulse * 0.08f * Fear, dt * 6f);
            if (grain) grain.intensity.value = Mathf.Lerp(0.25f, 0.7f, Mathf.Max(Fear, dark * 0.6f));
        }
    }

    public static class Haptics
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator, effectClass;
        static bool init;
#endif
        // Android: amplitude-controlled pulse (API 26+). iOS only has the long system buzz, so use it sparingly.
        public static void Pulse(long ms, int amplitude, float strength = 1f)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!init)
                {
                    init = true;
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                }
                var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, Mathf.Clamp(amplitude, 1, 255));
                vibrator.Call("vibrate", effect);
            }
            catch { Handheld.Vibrate(); }
#elif UNITY_IOS && !UNITY_EDITOR
            if (strength > 0.6f) Handheld.Vibrate();
#endif
        }
    }
}
