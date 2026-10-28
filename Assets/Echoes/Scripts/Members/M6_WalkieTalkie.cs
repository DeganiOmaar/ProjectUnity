using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Echoes
{
    // MEMBER 6 — Secret Walkie-Talkie (Proximity sensor)
    // Hold the phone to your ear: radio mode. You hear your partner's voice and an audio beacon that points to the
    // meeting point, but the screen goes dark — you lose sight of the maze while listening.
    // Deliverable: proximity detection (+ hold-button fallback, iOS has no proximity API in Unity),
    // radio mode, audio communication interface (live voice over the co-op link + radio overlay UI).
    [RequireComponent(typeof(Survivor))]
    public class M6_WalkieTalkie : MonoBehaviour
    {
        public static M6_WalkieTalkie I;

        public float nearCm = 3f;
        public bool HasProximity { get; private set; }
        public bool PartnerTransmitting => partnerVoiceAt > Time.time - 0.4f;
        public float VoiceLevel { get; private set; }

        Survivor s;
        AudioSource staticSrc, beaconSrc, voiceSrc, chirpSrc;
        float beaconTimer, partnerVoiceAt, hintAt, chirpAt;
        bool wasActive;
        float proxMax;

        // voice: 8 kHz mono, 16-bit, ring buffer filled by the network, drained by the audio thread
        const int VoiceRate = 8000;
        readonly float[] ring = new float[VoiceRate * 2];
        int ringW, ringR;
        readonly object ringLock = new();
        int micReadPos;
        float[] micTmp = new float[0];
        readonly short[] packet = new short[400];
        float resampleAcc;

        VisualElement overlay;
        Label beaconLine, fuseLine, partnerLine, warnLine, statusLine;
        readonly VisualElement[] bars = new VisualElement[24];

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
        }

        void Start()
        {
            if (ProximitySensor.current != null)
            {
                InputSystem.EnableDevice(ProximitySensor.current);
                HasProximity = true;
            }
            staticSrc = Source(AudioSynth.Static, true);
            beaconSrc = Source(null, false);
            chirpSrc = Source(null, false);
            voiceSrc = Source(AudioClip.Create("radio-voice", VoiceRate, 1, VoiceRate, true, ReadVoice), true);
            // walkie-talkie colour: band-limited and a bit crunchy
            voiceSrc.gameObject.AddComponent<AudioHighPassFilter>().cutoffFrequency = 300f;
            var lp = voiceSrc.gameObject.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = 3200f;
        }

        AudioSource Source(AudioClip clip, bool loop)
        {
            var go = new GameObject("Radio");
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = loop;
            a.volume = 0;
            a.spatialBlend = 0;
            if (clip) a.Play();
            return a;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool near = false;
            if (HasProximity)
            {
                float v = ProximitySensor.current.distance.ReadValue();
                proxMax = Mathf.Max(proxMax, v);
                near = proxMax > 0.5f && v < Mathf.Min(nearCm, proxMax * 0.5f);
            }
            bool want = near || (Keyboard.current?.vKey.isPressed ?? false) || TouchInput.Radio;
            bool active = want && GameManager.Playing && !s.Downed;
            s.RadioMode = active;
            if (active != wasActive) { chirpSrc.PlayOneShot(AudioSynth.Chirp, 0.7f); wasActive = active; }

            // more static when the monster is near
            staticSrc.volume = Mathf.MoveTowards(staticSrc.volume, active ? 0.12f + 0.45f * Heartbeat.Fear : 0f, dt * 3f);
            voiceSrc.volume = active ? 1f : 0f;

            var mz = Maze.Instance;
            if (active && mz)
            {
                // audio beacon: faster pings when closer, panned toward the meeting point, brighter when facing it
                float dist = Vector3.Distance(s.Position, mz.MeetingPoint);
                float rel = Mathf.DeltaAngle(s.Yaw, M5_HauntedCompass.Bearing(s.Position, mz.MeetingPoint));
                beaconTimer -= dt;
                if (beaconTimer <= 0)
                {
                    beaconTimer = Mathf.Lerp(0.3f, 1.6f, dist / 45f);
                    beaconSrc.panStereo = Mathf.Sin(rel * Mathf.Deg2Rad) * 0.9f;
                    beaconSrc.pitch = Mathf.Abs(rel) < 25f ? 1.35f : 0.85f;
                    beaconSrc.PlayOneShot(AudioSynth.Beacon, 0.6f);
                }
                if (Time.time >= hintAt) { hintAt = Time.time + 0.25f; UpdateHints(dist); }
            }

            // tell the player to raise the phone when the partner is talking
            if (!active && PartnerTransmitting && Time.time >= chirpAt) { chirpAt = Time.time + 1.2f; chirpSrc.PlayOneShot(AudioSynth.Chirp, 0.35f); }

            if (active && GameManager.I.Coop) Transmit();
            else if (M1_VoiceFlashlight.I && M1_VoiceFlashlight.I.HasMic) micReadPos = Microphone.GetPosition(M1_VoiceFlashlight.I.Device);

            UpdateOverlay(active);
        }

        void UpdateHints(float dist)
        {
            if (overlay == null) return;
            var mz = Maze.Instance;
            var gm = GameManager.I;
            beaconLine.text = $"BEACON    {M5_HauntedCompass.CardinalWord(M5_HauntedCompass.Bearing(s.Position, mz.MeetingPoint))}  -  {Mathf.RoundToInt(dist)} m{(gm.HatchPowered ? "" : "  -  NO POWER")}";
            var fuse = Fuse.Nearest(s.Position, out float fd);
            fuseLine.style.display = gm.FusesNeeded > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            fuseLine.text = fuse
                ? $"FUSE      {M5_HauntedCompass.CardinalWord(M5_HauntedCompass.Bearing(s.Position, fuse.transform.position))}  -  {Mathf.RoundToInt(fd)} m  ({gm.FusesFound}/{gm.FusesNeeded})"
                : "FUSE      ALL FOUND - HATCH POWERED";
            var p = GameManager.I.Partner;
            partnerLine.text = p
                ? $"PARTNER   {M5_HauntedCompass.CardinalWord(M5_HauntedCompass.Bearing(s.Position, p.Position))}  -  {Mathf.RoundToInt(Vector3.Distance(s.Position, p.Position))} m{(p.RadioMode ? "  -  ON RADIO" : "")}{(p.Downed ? "  -  DOWN!" : "")}"
                : "PARTNER   NO SIGNAL";
            partnerLine.style.display = p || gm.net ? DisplayStyle.Flex : DisplayStyle.None;
            float md = Monster.NearestDistance(s.Position);
            Monster nearest = null;
            foreach (var m in Monster.All) if (!nearest || Vector3.Distance(m.transform.position, s.Position) < Vector3.Distance(nearest.transform.position, s.Position)) nearest = m;
            warnLine.text = nearest && md < 25f
                ? $"WARNING   MOVEMENT {M5_HauntedCompass.CardinalWord(M5_HauntedCompass.Bearing(s.Position, nearest.transform.position))}  -  {(md < 8 ? "VERY CLOSE" : md < 15 ? "CLOSE" : "FAR")}"
                : "WARNING   —";
            warnLine.EnableInClassList("danger", nearest && md < 15f);
        }

        // ---------------- voice ----------------

        void Transmit()
        {
            var mic = M1_VoiceFlashlight.I;
            if (!mic || !mic.HasMic) return;
            int pos = Microphone.GetPosition(mic.Device);
            int total = mic.MicClip.samples;
            int count = (pos - micReadPos + total) % total;
            if (count <= 0) return;
            if (micTmp.Length != count) micTmp = new float[count];
            mic.MicClip.GetData(micTmp, micReadPos);
            micReadPos = pos;

            float step = mic.MicClip.frequency / (float)VoiceRate;
            int n = 0;
            float peak = 0;
            for (; resampleAcc < count; resampleAcc += step)
            {
                float v = micTmp[(int)resampleAcc];
                peak = Mathf.Max(peak, Mathf.Abs(v));
                packet[n++] = (short)(Mathf.Clamp(v * 1.5f, -1f, 1f) * 32767);
                if (n == packet.Length) { SendPacket(n); n = 0; }
            }
            resampleAcc -= count;
            if (n > 0) SendPacket(n);
            VoiceLevel = peak;
        }

        void SendPacket(int n)
        {
            GameManager.I.net.Send(Net.Voice, w =>
            {
                w.Write((ushort)n);
                for (int i = 0; i < n; i++) w.Write(packet[i]);
            });
        }

        public void Receive(BinaryReader r)
        {
            int n = r.ReadUInt16();
            float peak = 0;
            lock (ringLock)
            {
                for (int i = 0; i < n; i++)
                {
                    float v = r.ReadInt16() / 32768f;
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                    ring[ringW] = v;
                    ringW = (ringW + 1) % ring.Length;
                    if (ringW == ringR) ringR = (ringR + 1) % ring.Length; // overrun: drop oldest
                }
            }
            partnerVoiceAt = Time.time;
            VoiceLevel = Mathf.Max(VoiceLevel, peak);
        }

        void ReadVoice(float[] data)
        {
            lock (ringLock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (ringR == ringW) { data[i] = 0; continue; }
                    data[i] = ring[ringR];
                    ringR = (ringR + 1) % ring.Length;
                }
            }
        }

        // ---------------- radio overlay UI ----------------

        public VisualElement BuildUI()
        {
            overlay = new VisualElement { name = "radio", pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("radio");
            var head = new Label("[ RADIO MODE ]   CH-07");
            head.AddToClassList("radio-title");
            overlay.Add(head);
            var wave = new VisualElement();
            wave.AddToClassList("radio-wave");
            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = new VisualElement();
                bars[i].AddToClassList("radio-bar");
                wave.Add(bars[i]);
            }
            overlay.Add(wave);
            beaconLine = Line(overlay);
            fuseLine = Line(overlay);
            partnerLine = Line(overlay);
            warnLine = Line(overlay);
            statusLine = new Label("Lower the phone to see the maze again");
            statusLine.AddToClassList("radio-foot");
            overlay.Add(statusLine);
            overlay.style.display = DisplayStyle.None;
            return overlay;
        }

        static Label Line(VisualElement parent)
        {
            var l = new Label();
            l.AddToClassList("radio-line");
            parent.Add(l);
            return l;
        }

        void UpdateOverlay(bool active)
        {
            if (overlay == null) return;
            overlay.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active) return;
            float level = Mathf.Max(VoiceLevel, s.Loudness * 0.6f, Heartbeat.Fear * 0.3f);
            for (int i = 0; i < bars.Length; i++)
            {
                float n = Mathf.PerlinNoise(Time.time * 8f, i * 0.7f);
                bars[i].style.height = 6 + n * 70f * (0.15f + level);
            }
            VoiceLevel = Mathf.MoveTowards(VoiceLevel, 0, Time.deltaTime);
            statusLine.text = PartnerTransmitting ? ">> PARTNER TALKING" : GameManager.I.Coop ? "Talk — your partner hears you on their radio" : "Lower the phone to see the maze again";
        }
    }
}
