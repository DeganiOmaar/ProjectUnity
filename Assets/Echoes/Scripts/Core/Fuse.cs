using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    // Objective: the escape hatch has no power. Three fuses lie in dead ends under the dying red lamps.
    // Walk into one to pick it up. The compass only hints roughly where the nearest one is.
    public class Fuse : MonoBehaviour
    {
        public static readonly List<Fuse> All = new();
        public const float PickupRadius = 1.3f;

        Transform body;
        float phase;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static Fuse Nearest(Vector3 p, out float dist)
        {
            Fuse best = null;
            dist = float.MaxValue;
            foreach (var f in All)
            {
                float d = Vector3.Distance(p, f.transform.position);
                if (d < dist) { dist = d; best = f; }
            }
            return best;
        }

        public static Fuse Create(Transform parent, Vector3 pos, Material glowSource)
        {
            var root = new GameObject("Fuse").transform;
            root.SetParent(parent);
            root.position = pos;
            var f = root.gameObject.AddComponent<Fuse>();
            f.phase = Random.value * 10f;

            var mat = glowSource ? new Material(glowSource) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.3f, 0.95f, 0.85f) * 4f);
            f.body = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            f.body.name = "FuseBody";
            if (Application.isPlaying) Destroy(f.body.GetComponent<Collider>()); else DestroyImmediate(f.body.GetComponent<Collider>());
            f.body.SetParent(root, false);
            f.body.localScale = new Vector3(0.12f, 0.16f, 0.12f);
            f.body.GetComponent<Renderer>().sharedMaterial = mat;

            var glow = new GameObject("FuseGlow").AddComponent<Light>();
            glow.transform.SetParent(root, false);
            glow.type = LightType.Point;
            glow.color = new Color(0.3f, 0.95f, 0.85f);
            glow.range = 3f;
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;
            return f;
        }

        void Update()
        {
            body.localPosition = Vector3.up * (1f + Mathf.Sin(Time.time * 2f + phase) * 0.06f);
            body.Rotate(0, 90f * Time.deltaTime, 25f * Time.deltaTime, Space.World);

            var s = Survivor.Local;
            if (!GameManager.Playing || !s || s.Downed) return;
            var d = s.Position - transform.position;
            d.y = 0;
            if (d.magnitude < PickupRadius) GameManager.I.CollectFuse(this);
        }
    }
}
