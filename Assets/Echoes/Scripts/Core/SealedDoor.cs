using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    // GDD co-op synchronised action: a sealed door opens only when BOTH players shake their phones
    // at the same moment, or both shout together. Opening it grinds loudly — the monster hears it.
    // Solo: one player can force it, slower.
    public class SealedDoor : MonoBehaviour
    {
        public static readonly List<SealedDoor> All = new();
        public const float Range = 6f;

        public int Index;
        public Vector2Int Cell;
        public int Dir;
        public bool IsOpen { get; private set; }
        public float Charge { get; private set; }

        Transform slab;
        Material glow;
        AudioSource grind;
        float lift;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Init(Transform slabT, Material glowMat)
        {
            slab = slabT;
            glow = glowMat;
            grind = gameObject.AddComponent<AudioSource>();
            grind.spatialBlend = 1f;
            grind.maxDistance = 30f;
            grind.rolloffMode = AudioRolloffMode.Linear;
        }

        public static SealedDoor Nearest(Vector3 p, out float dist)
        {
            SealedDoor best = null;
            dist = float.MaxValue;
            foreach (var d in All)
            {
                if (d.IsOpen) continue;
                float x = Vector3.Distance(p, d.transform.position);
                if (x < dist) { dist = x; best = d; }
            }
            return best;
        }

        // true when the players are doing the synchronised action right now
        public static bool Synced(out bool coop)
        {
            Survivor a = null, b = null;
            foreach (var s in Survivor.All) { if (s.Downed) continue; if (!a) a = s; else b = s; }
            coop = b;
            if (!a) return false;
            if (!b) return a.Shaking || a.Shouting;
            return (a.Shaking && b.Shaking) || (a.Shouting && b.Shouting);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!IsOpen && GameManager.Playing)
            {
                bool near = false;
                foreach (var s in Survivor.All)
                    if (!s.Downed && Vector3.Distance(s.Position, transform.position) < Range) near = true;
                bool synced = Synced(out bool coop);
                // everyone computes the charge for the visuals; only the host decides
                Charge = near && synced ? Charge + dt / (coop ? 1.2f : 2.5f) : Mathf.Max(0, Charge - dt * 0.6f);
                if (Charge >= 1f && GameManager.I.IsHost) GameManager.I.OpenDoor(Index);
            }

            lift = Mathf.MoveTowards(lift, IsOpen ? 1f : 0f, dt / 2.2f);
            slab.localPosition = new Vector3(0, lift * (Maze.Instance.wallHeight - 0.15f), 0);
            var c = IsOpen ? new Color(0.37f, 0.83f, 0.75f) * 2f : Color.Lerp(new Color(1f, 0.1f, 0.06f) * 2f, new Color(1f, 0.64f, 0.24f) * 3f, Charge);
            glow.SetColor(Emission, c * (IsOpen ? 1f : 0.7f + 0.3f * Mathf.Sin(Time.time * (3f + Charge * 12f))));
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Charge = 1f;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
            grind.PlayOneShot(AudioSynth.Grind, 1f);
            if (GameManager.I.IsHost) Noise.Emit(transform.position, 0.85f, -1); // the grinding is loud
        }
    }
}
