using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Echoes
{
    // Shared state for one player (local or partner). The six member sensor scripts write
    // into the local survivor; the monster AI, HUD and network read from every survivor.
    public class Survivor : MonoBehaviour
    {
        public static readonly List<Survivor> All = new();
        public static Survivor Local;

        public int Id;                            // 0 = host / solo, 1 = joined client
        public bool IsLocal;
        [Range(0, 1)] public float Battery = 1f;  // flashlight charge (M1 shout / M4 shake refill it)
        [Range(0, 1)] public float Cover;         // M2: 0 = light sensor open, 1 = fully covered
        [Range(0, 1)] public float Loudness;      // M1: calibrated microphone level
        [Range(0, 1)] public float Motion;        // M4: phone + body + look motion
        [Range(0, 1)] public float LightOutput;   // final flashlight brightness (Flashlight)
        public bool Frozen, Shaking, Shouting, RadioMode, Downed;
        public float Yaw, Pitch;                  // look direction, 0 yaw = north (+Z)

        public Vector3 Position => transform.position;
        public Vector3 Eye => transform.position + Vector3.up * 1.6f;

        // M2 visibility system: how easy this survivor is to notice (0 hidden .. 1 exposed).
        public float Visibility => Downed ? 0f : Mathf.Clamp01(LightOutput * 0.7f + Motion * 0.5f + Loudness * 0.8f);
        // Dim + frozen + silent = the monster walks right past you.
        public bool Hidden => Frozen && LightOutput < 0.35f && Loudness < 0.3f;

        void OnEnable() { All.Add(this); if (IsLocal) Local = this; }
        void OnDisable() { All.Remove(this); if (Local == this) Local = null; }

        public byte Flags => (byte)((Frozen ? 1 : 0) | (Shaking ? 2 : 0) | (Shouting ? 4 : 0) | (RadioMode ? 8 : 0) | (Downed ? 16 : 0));

        public void Write(BinaryWriter w)
        {
            var p = Position;
            w.Write(p.x); w.Write(p.y); w.Write(p.z);
            w.Write(Yaw); w.Write(Pitch); w.Write(LightOutput); w.Write(Loudness); w.Write(Motion); w.Write(Battery);
            w.Write(Flags);
        }
    }

    // The partner's body on this phone: driven by network state, shows their flashlight from afar
    // (a navigation clue that also attracts the monster).
    public class PartnerAvatar : MonoBehaviour
    {
        Survivor s;
        Animator anim;
        Light torch;
        Transform head;
        Vector3 netPos, lastPos;
        string animState;
        bool hasPos;

        public void Init(GameObject model, RuntimeAnimatorController ctrl, float yawFix)
        {
            s = GetComponent<Survivor>();
            var m = Instantiate(model, transform);
            m.transform.localRotation = Quaternion.Euler(0, yawFix, 0);
            if (!m.TryGetComponent(out anim)) anim = m.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            head = new GameObject("Head").transform;
            head.SetParent(transform, false);
            head.localPosition = new Vector3(0, 1.5f, 0.15f);
            torch = head.gameObject.AddComponent<Light>();
            torch.type = LightType.Spot;
            torch.range = 18f;
            torch.spotAngle = 55f;
            torch.innerSpotAngle = 25f;
            torch.color = new Color(1f, 0.9f, 0.75f);
            torch.shadows = LightShadows.None;
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.center = Vector3.up * 0.9f;
            cap.height = 1.8f;
            cap.radius = 0.3f;
        }

        public void Read(BinaryReader r)
        {
            netPos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            s.Yaw = r.ReadSingle(); s.Pitch = r.ReadSingle();
            s.LightOutput = r.ReadSingle(); s.Loudness = r.ReadSingle(); s.Motion = r.ReadSingle(); s.Battery = r.ReadSingle();
            byte f = r.ReadByte();
            s.Frozen = (f & 1) != 0; s.Shaking = (f & 2) != 0; s.Shouting = (f & 4) != 0; s.RadioMode = (f & 8) != 0;
            // Downed is host-authoritative; the host's own flag arrives in its state
            if (!GameManager.I.IsHost) s.Downed = (f & 16) != 0;
            if (!hasPos) { transform.position = netPos; hasPos = true; }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, netPos, 1 - Mathf.Exp(-dt * 12f));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, s.Yaw, 0), 1 - Mathf.Exp(-dt * 12f));
            head.localRotation = Quaternion.Euler(s.Pitch, 0, 0);
            torch.intensity = s.LightOutput * 40f;
            float speed = dt > 0 ? Vector3.Distance(transform.position, lastPos) / dt : 0;
            lastPos = transform.position;
            string st = s.Downed ? "Death" : speed > 0.25f ? "Walk" : "Idle";
            if (st != animState) { anim.CrossFadeInFixedTime(st, 0.2f); animState = st; }
        }
    }
}
