using UnityEngine;
using UnityEngine.UIElements;

namespace Echoes
{
    // MEMBER 5 — Haunted Compass Navigation (Magnetometer)
    // The maze is aligned to real north (+Z). The compass follows the phone, so players turn their bodies and
    // coordinate in real terms ("I'm heading north"). The needle goes unstable when the monster is close.
    // Deliverable: compass UI (BuildUI), real-north navigation (RealHeading -> M3 alignment), monster interference.
    [RequireComponent(typeof(Survivor))]
    public class M5_HauntedCompass : MonoBehaviour
    {
        public static M5_HauntedCompass I;
        public static float? RealHeading { get; private set; }  // degrees from north; null = no magnetometer

        public float interferenceRange = 16f;
        public float Interference { get; private set; }  // 0 calm .. 1 spinning
        public float ShownHeading { get; private set; }  // what the dial shows (with interference)

        Survivor s;
        VisualElement ring, meetMark, partnerMark;
        Label headingLabel, cardinalLabel, statusLabel;
        readonly Label[] letters = new Label[4];
        float spin;
        const float Size = 176f, Radius = 76f;

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
        }

        void Start()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (SystemInfo.supportsGyroscope || Application.isMobilePlatform) Input.compass.enabled = true;
#endif
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            var c = Input.compass;
            RealHeading = c.enabled && c.timestamp > 0 ? (c.trueHeading > 0 ? c.trueHeading : c.magneticHeading) : (float?)null;
#endif
            // the monster distorts the field: jitter grows, then the needle spins
            float d = Monster.NearestDistance(s.Position);
            Interference = Mathf.MoveTowards(Interference, Mathf.Clamp01(1f - (d - 3f) / (interferenceRange - 3f)), Time.deltaTime * 1.5f);
            float jitter = (Mathf.PerlinNoise(Time.time * 7f, 1.3f) - 0.5f) * 140f * Interference * Interference;
            spin = Interference > 0.75f ? spin + Time.deltaTime * 900f * (Interference - 0.75f) * 4f : Mathf.MoveTowards(spin, 0, Time.deltaTime * 360f);
            ShownHeading = Mathf.Repeat(s.Yaw + jitter + spin, 360f);
            UpdateUI();
        }

        public static string Cardinal(float bearing)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8];
        }

        public static string CardinalWord(float bearing)
        {
            string[] names = { "NORTH", "NORTH-EAST", "EAST", "SOUTH-EAST", "SOUTH", "SOUTH-WEST", "WEST", "NORTH-WEST" };
            return names[Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8];
        }

        public static float Bearing(Vector3 from, Vector3 to) => Mathf.Repeat(Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg, 360f);

        // ---------------- UI ----------------

        public VisualElement BuildUI()
        {
            var root = new VisualElement { name = "compass" };
            root.AddToClassList("compass");
            root.style.width = root.style.height = Size;

            ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("compass-ring");
            ring.generateVisualContent += DrawTicks;
            root.Add(ring);

            string[] l = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                var lab = new Label(l[i]);
                lab.AddToClassList("compass-letter");
                if (i == 0) lab.AddToClassList("north");
                float a = i * 90f * Mathf.Deg2Rad;
                lab.style.left = Size / 2 + Mathf.Sin(a) * (Radius - 22) - 12;
                lab.style.top = Size / 2 - Mathf.Cos(a) * (Radius - 22) - 14;
                ring.Add(lab);
                letters[i] = lab;
            }

            var lubber = new VisualElement();
            lubber.AddToClassList("compass-lubber");
            root.Add(lubber);

            meetMark = new VisualElement();
            meetMark.AddToClassList("compass-mark");
            meetMark.AddToClassList("meet");
            root.Add(meetMark);
            partnerMark = new VisualElement();
            partnerMark.AddToClassList("compass-mark");
            partnerMark.AddToClassList("partner");
            root.Add(partnerMark);

            var center = new VisualElement();
            center.AddToClassList("compass-center");
            cardinalLabel = new Label("N");
            cardinalLabel.AddToClassList("compass-cardinal");
            headingLabel = new Label("000°");
            headingLabel.AddToClassList("compass-heading");
            center.Add(cardinalLabel);
            center.Add(headingLabel);
            root.Add(center);

            statusLabel = new Label("");
            statusLabel.AddToClassList("compass-status");
            root.Add(statusLabel);
            return root;
        }

        void DrawTicks(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var c = new Vector2(Size / 2, Size / 2);
            for (int deg = 0; deg < 360; deg += 15)
            {
                bool major = deg % 45 == 0;
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                p.strokeColor = deg == 0 ? new Color(0.91f, 0.64f, 0.24f) : new Color(1, 1, 1, major ? 0.55f : 0.22f);
                p.lineWidth = major ? 2f : 1f;
                p.BeginPath();
                p.MoveTo(c + dir * (Radius - (major ? 10 : 6)));
                p.LineTo(c + dir * Radius);
                p.Stroke();
            }
        }

        void UpdateUI()
        {
            if (ring == null) return;
            float h = ShownHeading;
            ring.style.rotate = new Rotate(new Angle(-h, AngleUnit.Degree));
            foreach (var lab in letters) lab.style.rotate = new Rotate(new Angle(h, AngleUnit.Degree));
            cardinalLabel.text = Interference > 0.6f ? "?" : Cardinal(h);
            headingLabel.text = Interference > 0.6f ? "---°" : Mathf.RoundToInt(h).ToString("000") + "°";
            statusLabel.text = Interference > 0.35f ? "FIELD DISTORTED" : RealHeading.HasValue ? "REAL NORTH" : "MAZE NORTH";
            statusLabel.EnableInClassList("danger", Interference > 0.35f);

            var mz = Maze.Instance;
            var gm = GameManager.I;
            if (gm && gm.FusesNeeded > 0)
            {
                // solo objective: only a rough hint toward the nearest fuse; the exit itself is found by radio
                var fuse = Fuse.Nearest(s.Position, out _);
                float rough = (Mathf.PerlinNoise(Time.time * 0.15f, 7.7f) - 0.5f) * 50f;
                Place(meetMark, fuse ? Bearing(s.Position, fuse.transform.position) + rough : 0, fuse && !gm.HatchPowered);
                meetMark.EnableInClassList("fuse", true);
            }
            else Place(meetMark, mz ? Bearing(s.Position, mz.MeetingPoint) : 0, mz);
            var partner = GameManager.I ? GameManager.I.Partner : null;
            partnerMark.style.display = partner ? DisplayStyle.Flex : DisplayStyle.None;
            if (partner) Place(partnerMark, Bearing(s.Position, partner.Position), true);
        }

        void Place(VisualElement mark, float bearing, bool show)
        {
            mark.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            float a = (bearing - ShownHeading) * Mathf.Deg2Rad;
            mark.style.left = Size / 2 + Mathf.Sin(a) * (Radius + 9) - 5;
            mark.style.top = Size / 2 - Mathf.Cos(a) * (Radius + 9) - 5;
        }
    }
}
