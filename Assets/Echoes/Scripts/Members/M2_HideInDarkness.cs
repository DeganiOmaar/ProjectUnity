using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Echoes
{
    // MEMBER 2 — Hide in Darkness (Ambient light sensor / front camera)
    // Cover the sensor with a finger to dim your flashlight so the monster struggles to detect you.
    // Staying dark too long is dangerous: darkness spawns the monster.
    // Deliverable: cover detection, flashlight dimming (Flashlight reads Survivor.Cover), visibility system (Survivor.Visibility).
    [RequireComponent(typeof(Survivor))]
    public class M2_HideInDarkness : MonoBehaviour
    {
        public static M2_HideInDarkness I;
        public enum Source { Manual, LightSensor, FrontCamera }

        [Header("Cover detection")]
        public float luxCoveredRatio = 0.2f;    // covered when lux drops below 20% of the room baseline
        public float minLux = 3f;
        public float cameraCoveredRatio = 0.3f;
        public float minCameraLuma = 0.06f;
        [Header("Darkness danger")]
        public float darkLight = 0.22f;          // light output below this counts as darkness
        public Vector2 spawnAfter = new(7f, 13f); // random timer before the dark spawns a monster

        public Source ActiveSource { get; private set; } = Source.Manual;
        public bool SensorCovered { get; private set; }
        public float Reading { get; private set; }   // lux, or camera luma 0..1
        public float Baseline { get; private set; }
        public float DarkTime { get; private set; }
        public float DarkLimit { get; private set; }
        public float Danger01 => DarkLimit > 0 ? Mathf.Clamp01(DarkTime / DarkLimit) : 0;

        Survivor s;
        WebCamTexture cam;
        Color32[] pixels;
        float sampleAt;

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
            DarkLimit = Random.Range(spawnAfter.x, spawnAfter.y);
        }

        IEnumerator Start()
        {
            // Android: real light sensor when present. iOS (no light sensor API) and the rest: front camera.
            if (LightSensor.current != null)
            {
                InputSystem.EnableDevice(LightSensor.current);
                ActiveSource = Source.LightSensor;
                yield break;
            }
            if (!Application.isMobilePlatform) yield break; // desktop: hold C
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
                float t = 0;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && (t += Time.unscaledDeltaTime) < 10f) yield return null;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
#endif
            foreach (var d in WebCamTexture.devices)
                if (d.isFrontFacing)
                {
                    cam = new WebCamTexture(d.name, 64, 48, 15);
                    cam.Play();
                    ActiveSource = Source.FrontCamera;
                    break;
                }
        }

        public void CalibrateBaseline()
        {
            if (Reading > 0) Baseline = Reading;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool covered = false;
            switch (ActiveSource)
            {
                case Source.LightSensor:
                    Reading = LightSensor.current.lightLevel.ReadValue();
                    covered = Detect(Reading, luxCoveredRatio, minLux, dt);
                    break;
                case Source.FrontCamera:
                    if (cam && cam.didUpdateThisFrame && Time.unscaledTime >= sampleAt)
                    {
                        sampleAt = Time.unscaledTime + 0.1f;
                        pixels = cam.GetPixels32(pixels);
                        long sum = 0;
                        foreach (var p in pixels) sum += p.r * 2 + p.g * 5 + p.b;
                        Reading = sum / (pixels.Length * 8f * 255f);
                    }
                    covered = Detect(Reading, cameraCoveredRatio, minCameraLuma, dt);
                    break;
            }
            bool manual = (Keyboard.current?.cKey.isPressed ?? false) || TouchInput.Cover;
            SensorCovered = covered || manual;
            s.Cover = Mathf.MoveTowards(s.Cover, SensorCovered && !s.Downed ? 1f : 0f, dt * 4f);

            // darkness spawns the monster
            if (!GameManager.Playing || s.Downed) return;
            if (s.LightOutput < darkLight) DarkTime += dt;
            else DarkTime = Mathf.Max(0, DarkTime - dt * 2f);
            if (DarkTime > DarkLimit)
            {
                DarkTime = 0;
                DarkLimit = Random.Range(spawnAfter.x, spawnAfter.y) * (GameManager.Night ? 0.7f : 1f);
                GameManager.I.RequestSpawnNear(transform.position);
            }
        }

        bool Detect(float reading, float ratio, float floor, float dt)
        {
            if (Baseline <= 0) Baseline = reading;
            bool covered = reading < Mathf.Max(floor, Baseline * ratio);
            // follow the room's lighting slowly while uncovered
            if (!covered) Baseline = Mathf.Lerp(Baseline, reading, dt * 0.2f);
            return covered;
        }

        void OnDestroy()
        {
            if (cam) cam.Stop();
        }
    }
}
