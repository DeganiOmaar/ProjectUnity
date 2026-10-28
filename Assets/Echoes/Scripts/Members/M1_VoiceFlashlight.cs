using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Echoes
{
    // MEMBER 1 — Voice-Controlled Flashlight (Microphone)
    // Shout to recharge the flickering flashlight; anything loud is heard by the monster through walls.
    // Deliverable: flashlight battery (Flashlight.cs), sound detection (RMS + calibration), monster noise alert.
    [RequireComponent(typeof(Survivor), typeof(Flashlight))]
    public class M1_VoiceFlashlight : MonoBehaviour
    {
        public static M1_VoiceFlashlight I;

        [Header("Thresholds (0..1 after calibration)")]
        public float noiseThreshold = 0.3f;    // above this the monster hears you
        public float shoutThreshold = 0.55f;   // above this the flashlight recharges
        public float silenceThreshold = 0.12f; // below this you count as silent
        public float rechargePerSec = 0.32f;

        public bool HasMic { get; private set; }
        public AudioClip MicClip { get; private set; }   // M6 streams radio voice from this
        public string Device { get; private set; }
        public float Raw { get; private set; }           // raw RMS before calibration
        public bool Silent => s.Loudness < silenceThreshold;

        Survivor s;
        Flashlight torch;
        readonly float[] buf = new float[256];
        float noiseCooldown;

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
            torch = GetComponent<Flashlight>();
        }

        IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                float t = 0;
                while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) && (t += Time.unscaledDeltaTime) < 10f) yield return null;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
#endif
            StartMic();
            yield break;
        }

        void StartMic()
        {
            if (Microphone.devices.Length == 0) return;
            Device = Microphone.devices[0];
            Microphone.GetDeviceCaps(Device, out int min, out int max);
            int freq = max == 0 ? 16000 : Mathf.Clamp(16000, min, max);
            MicClip = Microphone.Start(Device, true, 1, freq);
            HasMic = MicClip != null;
        }

        public float ReadRms()
        {
            if (!HasMic) return 0;
            int pos = Microphone.GetPosition(Device) - buf.Length;
            if (pos < 0) pos += MicClip.samples;
            MicClip.GetData(buf, pos);
            float sum = 0;
            foreach (var v in buf) sum += v * v;
            return Mathf.Sqrt(sum / buf.Length);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Raw = ReadRms();
            float floor = Settings.MicFloor, max = Settings.MicMax;
            float level = Mathf.Clamp01((Raw - floor) / Mathf.Max(1e-4f, max - floor));
            // fallbacks: keyboard SPACE or the on-screen SHOUT button (accessibility / no mic)
            if ((Keyboard.current?.spaceKey.isPressed ?? false) || TouchInput.Shout) level = Mathf.Max(level, 0.9f);
            // fast attack, slower release so short shouts still register
            s.Loudness = Mathf.Lerp(s.Loudness, level, 1 - Mathf.Exp(-dt * (level > s.Loudness ? 25f : 6f)));

            s.Shouting = GameManager.Playing && !s.Downed && s.Loudness > shoutThreshold;
            if (s.Shouting) torch.Recharge(rechargePerSec * dt * Mathf.Lerp(0.6f, 1.2f, Mathf.InverseLerp(shoutThreshold, 1f, s.Loudness)));

            // noise alert: the monster hears anything over the threshold, even through walls
            noiseCooldown -= dt;
            if (GameManager.Playing && s.Loudness > noiseThreshold && noiseCooldown <= 0)
            {
                noiseCooldown = 0.3f;
                Noise.Emit(transform.position, s.Loudness, s.Id);
            }
        }

        // ---- calibration (GDD 6: measure the room's noise floor, then one shout for the maximum) ----

        public IEnumerator MeasureFloor(float seconds, Action<float> progress)
        {
            float sum = 0; int n = 0;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                sum += ReadRms(); n++;
                progress(t / seconds);
                yield return null;
            }
            Settings.MicFloor = Mathf.Max(0.001f, sum / Mathf.Max(1, n) * 1.4f);
        }

        public IEnumerator MeasureMax(float seconds, Action<float> progress)
        {
            float peak = 0;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                peak = Mathf.Max(peak, ReadRms());
                progress(t / seconds);
                yield return null;
            }
            float floor = Settings.MicFloor;
            // a whisper-quiet "shout" would make everything count as shouting; keep a sane span
            Settings.MicMax = Mathf.Max(peak * 0.85f, floor * 4f, floor + 0.03f);
        }
    }
}
