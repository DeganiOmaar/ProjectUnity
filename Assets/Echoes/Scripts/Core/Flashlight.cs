using UnityEngine;

namespace Echoes
{
    // The in-game flashlight: battery, random flicker episodes and the final brightness that
    // the monster sees. M1 (shout) and M4 (shake) recharge it, M2 (cover) dims it.
    [RequireComponent(typeof(Survivor))]
    public class Flashlight : MonoBehaviour
    {
        public Light spot;
        public float maxIntensity = 60f;
        public float drainPerSec = 1f / 170f;
        public float flickerDrainPerSec = 1f / 20f;
        public Vector2 flickerEvery = new(24f, 48f);

        public bool Flickering { get; private set; }
        float burstUntil;
        public void Burst(float seconds) => burstUntil = Time.time + seconds;   // FlashBurst: blinding spike
        public bool Recharging => Time.time - lastRecharge < 0.15f;
        // Low phone battery = weaker torch (GDD "clock and battery" touch).
        public static float PhoneBatteryFactor
        {
            get { float b = SystemInfo.batteryLevel; return b < 0 ? 1f : Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.05f, 0.5f, b)); }
        }

        Survivor s;
        AudioSource hum;
        float nextFlicker, lastRecharge, stutter, glare = 1f;

        void Awake()
        {
            s = GetComponent<Survivor>();
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = AudioSynth.Hum;
            hum.loop = true;
            hum.volume = 0;
            hum.spatialBlend = 0;
            hum.Play();
        }

        public void ResetForRound()
        {
            s.Battery = 1f;
            Flickering = false;
            nextFlicker = Time.time + Random.Range(14f, 24f);
        }

        public void Recharge(float amount)
        {
            if (amount <= 0) return;
            s.Battery = Mathf.Min(1f, s.Battery + amount);
            lastRecharge = Time.time;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool playing = GameManager.Playing && !s.Downed;
            if (playing)
            {
                s.Battery = Mathf.Clamp01(s.Battery - (drainPerSec + (Flickering ? flickerDrainPerSec : 0)) * dt);
                if (!Flickering && Time.time > nextFlicker) Flickering = true;
                // GDD: keep shouting until the light indicator is full again
                if (Flickering && s.Battery >= 0.98f)
                {
                    Flickering = false;
                    nextFlicker = Time.time + Random.Range(flickerEvery.x, flickerEvery.y) * (GameManager.Night ? 0.7f : 1f);
                }
            }

            float level = s.Battery > 0.01f ? Mathf.Lerp(0.35f, 1f, s.Battery) : 0f;
            float flick = 1f;
            if (Flickering)
            {
                float n = Mathf.PerlinNoise(Time.time * 9f, 0.37f);
                flick = n < 0.38f ? 0.04f : Mathf.Lerp(0.45f, 1f, n);
            }
            else if (s.Battery < 0.15f)
            {
                stutter -= dt;
                if (stutter < -0.08f) stutter = Random.Range(0.2f, 1.2f);
                if (stutter < 0) flick = 0.2f;
            }
            if (s.Downed) level = 0.15f;

            s.LightOutput = level * flick * (1f - 0.88f * s.Cover) * PhoneBatteryFactor;
            if (spot)
            {
                // inverse-square light blows out walls and faces at arm's length: ease it off up close
                var from = spot.transform.position;
                float close = 3f;
                if (Physics.Raycast(from, spot.transform.forward, out var hit, close, ~0, QueryTriggerInteraction.Ignore)) close = hit.distance;
                foreach (var m in Monster.All)
                {
                    var to = m.transform.position + Vector3.up * 1.5f - from;
                    if (to.magnitude < close && Vector3.Angle(spot.transform.forward, to) < spot.spotAngle * 0.5f) close = to.magnitude;
                }
                glare = Mathf.Lerp(glare, Mathf.Clamp(Mathf.Pow(close / 3f, 1.5f), 0.1f, 1f), 1 - Mathf.Exp(-dt * 8f));
                spot.intensity = maxIntensity * s.LightOutput * glare;
                spot.enabled = s.LightOutput > 0.002f;
                if (Time.time < burstUntil) { spot.enabled = true; spot.intensity = maxIntensity * 6f; }
            }

            // dynamo whine while recharging, pitch rises with the charge
            hum.volume = Mathf.MoveTowards(hum.volume, Recharging ? 0.18f : 0f, dt * 2f);
            hum.pitch = Mathf.Lerp(0.7f, 1.6f, s.Battery);
        }
    }
}
