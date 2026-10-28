using UnityEngine;

namespace Echoes
{
    // Grabbed: instead of instant death you get a few seconds to break free by SHAKING the phone or SHOUTING
    // (shouting is loud: other monsters hear it). Breaking free burns your battery and stuns the monster.
    // Each escape makes the next one harder. Solo / host's own player only — co-op rules are unchanged.
    public class Struggle : MonoBehaviour
    {
        public static Struggle I;
        public static bool Active => I && I.monster;
        public static float GraceUntil = -99f;   // a moment of safety after breaking free

        public float duration = 3.2f;
        public float effortPerSec = 0.55f;
        public float Progress { get; private set; }
        public float TimeLeft01 => monster ? Mathf.Clamp01((until - Time.time) / duration) : 0;
        public Vector3 MonsterEyes => monster ? monster.transform.position + Vector3.up * 1.75f : Vector3.zero;

        Survivor s;
        Monster monster;
        float until, required, pulseAt;
        int escapes;

        void Awake()
        {
            I = this;
            GraceUntil = -99f;
        }

        public bool Begin(Survivor who, Monster by)
        {
            if (Active) return false;
            s = who;
            monster = by;
            Progress = 0;
            until = Time.time + duration;
            required = 1f + escapes * 0.35f;
            GameManager.I.PlaySting(Vector3.zero, AudioSynth.Screech, 1f);
            Haptics.Pulse(300, 255);
            return true;
        }

        void Update()
        {
            if (!monster) { monster = null; return; }
            if (!GameManager.Playing) return;
            float effort = (s.Shaking ? 1f : 0f) + (s.Shouting ? 1f : 0f);
            Progress += effort * effortPerSec * Time.deltaTime / required;
            if (Time.time >= pulseAt) { pulseAt = Time.time + 0.25f; Haptics.Pulse(60, 255); }

            if (Progress >= 1f) Escape();
            else if (Time.time > until) Fail();
        }

        void Escape()
        {
            escapes++;
            s.Battery = Mathf.Min(s.Battery, 0.12f);   // the fight drains the torch
            GraceUntil = Time.time + 3f;
            var m = monster;
            monster = null;
            m.Release(true);
            GameManager.I.PlaySting(Vector3.zero, AudioSynth.Chirp, 1f);
        }

        void Fail()
        {
            var m = monster;
            monster = null;
            m.Release(false);
            GameManager.I.Caught(s);
        }
    }
}
