using System;
using UnityEngine;

namespace Echoes
{
    // All game audio is synthesised at startup: no licensing, tiny build, and it fits the lo-fi horror tone.
    // Clips are cached with Unity's null check (not ??=): Unity can destroy them, e.g. when a play session ends.
    public static class AudioSynth
    {
        const int Rate = 44100;
        static readonly System.Random Rng = new(7);
        static float N() => (float)(Rng.NextDouble() * 2 - 1);
        const float Tau = Mathf.PI * 2f;

        static AudioClip heartbeat, footstep, drone, staticLoop, growl, screech, chirp, beacon, hum, sting, win, grind, drip, knock;
        public static AudioClip Grind => grind ? grind : grind = MakeGrind();
        static AudioClip shatter, burst, pickup, scrape;
        public static AudioClip Shatter => shatter ? shatter : shatter = MakeShatter();
        public static AudioClip Burst => burst ? burst : burst = Make("burst", 0.9f, t => (Mathf.Sin(Tau * (2400 - 1900 * t) * t) * 0.35f + N() * 0.5f * Env(t, 0.002f, 0.08f)) * Env(t, 0.004f, 0.3f));
        public static AudioClip Pickup => pickup ? pickup : pickup = Make("pickup", 0.45f, t => (Mathf.Sin(Tau * 660 * t) * Env(t, 0.005f, 0.12f) + Mathf.Sin(Tau * 990 * t) * Env(t - 0.1f, 0.005f, 0.15f)) * 0.45f);
        public static AudioClip Scrape => scrape ? scrape : scrape = MakeScrape();
        public static AudioClip Drip => drip ? drip : drip = Make("drip", 0.35f, t => Mathf.Sin(Tau * (900 + 1400 * Mathf.Exp(-t * 30)) * t) * Env(t, 0.002f, 0.05f) * 0.5f);
        public static AudioClip Knock => knock ? knock : knock = MakeKnock();
        public static AudioClip Heartbeat => heartbeat ? heartbeat : heartbeat = MakeHeartbeat();
        public static AudioClip Footstep => footstep ? footstep : footstep = MakeFootstep();
        public static AudioClip Drone => drone ? drone : drone = MakeDrone();
        public static AudioClip Static => staticLoop ? staticLoop : staticLoop = MakeStatic();
        public static AudioClip Growl => growl ? growl : growl = MakeGrowl();
        public static AudioClip Screech => screech ? screech : screech = MakeScreech();
        public static AudioClip Chirp => chirp ? chirp : chirp = Make("chirp", 0.14f, t => (Mathf.Sin(Tau * 1800 * t) * 0.5f + N() * 0.15f) * Env(t, 0.004f, 0.04f));
        public static AudioClip Beacon => beacon ? beacon : beacon = Make("beacon", 0.3f, t => (Mathf.Sin(Tau * 880 * t) + 0.3f * Mathf.Sin(Tau * 1760 * t)) * 0.45f * Env(t, 0.003f, 0.07f));
        public static AudioClip Hum => hum ? hum : hum = MakeHum();
        public static AudioClip Sting => sting ? sting : sting = MakeSting();
        public static AudioClip Win => win ? win : win = Make("win", 3.5f, t =>
            (Mathf.Sin(Tau * 220 * t) + Mathf.Sin(Tau * 277.2f * t) + Mathf.Sin(Tau * 329.6f * t) + 0.5f * Mathf.Sin(Tau * 440 * t)) * 0.12f * Env(t, 0.8f, 1.2f));

        static float Env(float t, float attack, float decay) => t < 0 ? 0 : t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        static AudioClip Make(string name, float seconds, Func<float, float> f, bool loop = false)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            if (loop)
            {
                // crossfade the tail into the head so the loop is seamless
                int fade = Rate / 4;
                for (int i = 0; i < fade; i++)
                {
                    float k = i / (float)fade;
                    d[i] = d[i] * k + d[n - fade + i] * (1 - k);
                }
                Array.Resize(ref d, n - fade);
            }
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip MakeHeartbeat()
        {
            float lp = 0;
            float Thump(float t) => t < 0 ? 0 : Mathf.Sin(Tau * 50 * t * (1 - t * 0.9f)) * Env(t, 0.006f, 0.07f);
            return Make("heartbeat", 0.6f, t =>
            {
                lp += (N() - lp) * 0.03f;
                return (Thump(t) + 0.7f * Thump(t - 0.18f)) * 0.9f + lp * (Env(t, 0.002f, 0.02f) + Env(t - 0.18f, 0.002f, 0.02f)) * 0.6f;
            });
        }

        static AudioClip MakeFootstep()
        {
            float lp = 0, hp = 0, prev = 0;
            return Make("footstep", 0.2f, t =>
            {
                float n = N();
                lp += (n - lp) * 0.22f;
                hp = 0.9f * (hp + n - prev); prev = n;
                return lp * Env(t, 0.003f, 0.045f) * 1.2f + hp * Env(t, 0.001f, 0.012f) * 0.25f + Mathf.Sin(Tau * 85 * t) * Env(t, 0.002f, 0.03f) * 0.4f;
            });
        }

        static AudioClip MakeDrone()
        {
            float wind = 0, wind2 = 0;
            return Make("drone", 12f, t =>
            {
                wind += (N() - wind) * 0.015f;
                wind2 += (wind - wind2) * 0.05f;
                float swell = 0.6f + 0.4f * Mathf.Sin(Tau * t / 6f);
                return (0.16f * Mathf.Sin(Tau * 40 * t) + 0.11f * Mathf.Sin(Tau * 40.25f * t + 0.3f) + 0.07f * Mathf.Sin(Tau * 61.5f * t)) * swell
                       + wind2 * (0.6f + 0.4f * Mathf.Sin(Tau * t / 4f)) * 1.4f;
            }, loop: true);
        }

        static AudioClip MakeStatic()
        {
            float a = 0, b = 0;
            return Make("static", 2.5f, t =>
            {
                float n = N();
                a += (n - a) * 0.6f;
                b += (n - b) * 0.04f;
                float crackle = Rng.NextDouble() < 0.0006 ? N() * 0.9f : 0;
                return (a - b) * 0.35f + crackle;
            }, loop: true);
        }

        static AudioClip MakeGrowl()
        {
            float lp = 0, phase = 0, lp2 = 0;
            return Make("growl", 5f, t =>
            {
                float breath = Mathf.Pow(Mathf.Max(0, Mathf.Sin(Tau * 0.42f * t)), 2f);
                lp += (N() - lp) * 0.06f;
                phase += (62f + 9f * Mathf.Sin(Tau * 0.7f * t) + N() * 25f) / Rate;
                float saw = (phase % 1f) * 2 - 1;
                lp2 += (saw - lp2) * 0.08f;
                return (lp * 1.8f + lp2 * 0.45f) * breath;
            }, loop: true);
        }

        static AudioClip MakeScreech()
        {
            float phase = 0, lp = 0;
            return Make("screech", 1.5f, t =>
            {
                float f = Mathf.Lerp(1150f, 320f, t / 1.5f) * (1 + 0.04f * Mathf.Sin(Tau * 23 * t));
                phase += f / Rate;
                float saw = (phase % 1f) * 2 - 1;
                lp += (saw + N() * 0.5f - lp) * 0.35f;
                return lp * Env(t, 0.03f, 0.55f) * (0.75f + 0.25f * Mathf.Sin(Tau * 17 * t)) * 0.8f;
            });
        }

        static AudioClip MakeHum()
        {
            float phase = 0, lp = 0;
            return Make("hum", 1f, t =>
            {
                phase += 98f / Rate;
                lp += (((phase % 1f) * 2 - 1) - lp) * 0.1f;
                return lp * 0.5f + Mathf.Sin(Tau * 196 * t) * 0.15f;
            });
        }

        static AudioClip MakeGrind()
        {
            float lp = 0, phase = 0;
            return Make("grind", 2.4f, t =>
            {
                lp += (N() - lp) * 0.12f;
                phase += (55f + 20f * Mathf.Sin(Tau * 3f * t) + N() * 30f) / Rate;
                float scrape = ((phase % 1f) * 2 - 1) * 0.35f + lp * 1.4f;
                return scrape * Mathf.Clamp01(t * 6f) * Mathf.Clamp01((2.4f - t) * 2f) + Mathf.Sin(Tau * 42 * t) * Env(t - 2.1f, 0.01f, 0.25f) * 0.9f;
            });
        }

        static AudioClip MakeShatter()
        {
            float hp = 0, prev = 0;
            return Make("shatter", 1.1f, t =>
            {
                float n = N();
                hp = 0.95f * (hp + n - prev); prev = n;
                float tinkle = Rng.NextDouble() < 0.004 * Mathf.Exp(-t * 3) ? N() : 0;
                return hp * Env(t, 0.001f, 0.09f) * 0.9f + Mathf.Sin(Tau * 3100 * t) * Env(t, 0.001f, 0.05f) * 0.3f + tinkle * 0.6f;
            });
        }

        static AudioClip MakeScrape()
        {
            float lp = 0;
            return Make("scrape", 3f, t =>
            {
                lp += (N() - lp) * 0.3f;
                float drag = 0.5f + 0.5f * Mathf.Sin(Tau * 1.3f * t);
                return lp * drag * 0.6f + Mathf.Sin(Tau * 47 * t) * 0.08f;
            }, loop: true);
        }

        static AudioClip MakeKnock()
        {
            float lp = 0;
            float Hit(float t) => t < 0 ? 0 : (Mathf.Sin(Tau * 110 * t) * 0.8f + lp * 0.6f) * Env(t, 0.002f, 0.06f);
            return Make("knock", 1.2f, t => { lp += (N() - lp) * 0.2f; return Hit(t) + Hit(t - 0.32f) * 0.8f + Hit(t - 0.58f) * 0.9f; });
        }

        static AudioClip MakeSting()
        {
            float lp = 0;
            return Make("sting", 2.6f, t =>
            {
                lp += (N() - lp) * 0.08f;
                return Mathf.Sin(Tau * 38 * t) * Env(t, 0.01f, 0.8f) * 0.9f + lp * Env(t, 0.005f, 0.35f) * 1.5f
                       + (Mathf.Sin(Tau * 311 * t) + Mathf.Sin(Tau * 329.6f * t) + Mathf.Sin(Tau * 349.2f * t)) * 0.08f * Env(t, 0.05f, 0.9f);
            });
        }
    }
}
