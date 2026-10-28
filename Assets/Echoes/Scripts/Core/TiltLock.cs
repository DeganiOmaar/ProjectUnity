using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Echoes
{
    // GDD tilt puzzle (accelerometer): the escape hatch at the meeting point is locked. Tilt the phone to roll
    // the key through a little box labyrinth into the lock. The monster keeps hunting while you do it,
    // and tilting the phone means you are not frozen. Fallbacks: arrows / WASD, or drag inside the box.
    public class TiltLock : MonoBehaviour
    {
        public static TiltLock I;
        public static bool Active => I && I.visible;

        public float gain = 3f, accel = 950f, friction = 1.4f;

        const float Size = 440f, BallR = 15f, HoleR = 20f;
        // walls in 0..1 box space (x, y-down, w, h): an S-shaped run from top-left to the bottom-right lock
        static readonly Rect[] Walls = { new(0f, 0.3f, 0.7f, 0.06f), new(0.3f, 0.64f, 0.7f, 0.06f), new(0.5f, 0f, 0.05f, 0.16f) };
        static readonly Vector2 StartPos = new(0.1f, 0.12f), HolePos = new(0.86f, 0.86f);

        VisualElement root, box, ball;
        Vector2 pos, vel, neutral, drag;
        bool visible, solved;
        AudioSource sfx;

        void Awake()
        {
            I = this;
            sfx = gameObject.AddComponent<AudioSource>();
        }

        public VisualElement BuildUI()
        {
            root = new VisualElement { name = "tilt-lock" };
            root.AddToClassList("tilt");
            var title = new Label("HATCH LOCK");
            title.AddToClassList("card-title");
            root.Add(title);
            var sub = new Label("Hold the phone flat and tilt it to roll the key into the lock");
            sub.AddToClassList("card-sub");
            root.Add(sub);

            box = new VisualElement();
            box.AddToClassList("tilt-box");
            box.style.width = box.style.height = Size;
            foreach (var w in Walls)
            {
                var e = new VisualElement();
                e.AddToClassList("tilt-wall");
                e.style.left = w.x * Size; e.style.top = w.y * Size;
                e.style.width = w.width * Size; e.style.height = w.height * Size;
                box.Add(e);
            }
            var hole = new VisualElement();
            hole.AddToClassList("tilt-hole");
            hole.style.left = HolePos.x * Size - HoleR; hole.style.top = HolePos.y * Size - HoleR;
            hole.style.width = hole.style.height = HoleR * 2;
            box.Add(hole);
            ball = new VisualElement();
            ball.AddToClassList("tilt-ball");
            ball.style.width = ball.style.height = BallR * 2;
            box.Add(ball);

            // touch fallback: drag inside the box = tilt direction
            int id = -1;
            Vector2 start = default;
            box.RegisterCallback<PointerDownEvent>(e => { id = e.pointerId; start = e.localPosition; box.CapturePointer(id); });
            box.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == id) drag = Vector2.ClampMagnitude(((Vector2)e.localPosition - start) / 120f, 1f); });
            box.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == id) { box.ReleasePointer(id); id = -1; drag = Vector2.zero; } });
            root.Add(box);
            root.style.display = DisplayStyle.None;
            return root;
        }

        public void SetVisible(bool on)
        {
            if (on == visible || root == null) return;
            visible = on;
            root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on)
            {
                pos = StartPos * Size;
                vel = Vector2.zero;
                solved = false;
                neutral = M4_ShakeFreeze.I ? M4_ShakeFreeze.I.Gravity : Vector3.zero; // tilt is relative to how you hold it now
            }
        }

        Vector2 ReadTilt()
        {
            Vector2 t = drag;
            var m4 = M4_ShakeFreeze.I;
            if (m4 && m4.HasAccelerometer)
            {
                var g = (Vector2)(m4.Gravity - (Vector3)neutral) * gain;
                t += new Vector2(g.x, -g.y);   // gravity-down convention -> screen space (y down)
            }
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.leftArrowKey.isPressed || k.aKey.isPressed) t.x -= 1;
                if (k.rightArrowKey.isPressed || k.dKey.isPressed) t.x += 1;
                if (k.upArrowKey.isPressed || k.wKey.isPressed) t.y -= 1;
                if (k.downArrowKey.isPressed || k.sKey.isPressed) t.y += 1;
            }
            if (Gamepad.current != null) { var s = Gamepad.current.leftStick.ReadValue(); t += new Vector2(s.x, -s.y); }
            return Vector2.ClampMagnitude(t, 1.5f);
        }

        static bool Hits(Vector2 p)
        {
            foreach (var w in Walls)
            {
                var r = new Rect(w.x * Size, w.y * Size, w.width * Size, w.height * Size);
                var closest = new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
                if ((p - closest).sqrMagnitude < BallR * BallR) return true;
            }
            return false;
        }

        void Update()
        {
            if (!visible || solved) return;
            float dt = Time.deltaTime;
            vel += ReadTilt() * accel * dt;
            vel *= 1f - Mathf.Clamp01(friction * dt);

            var p = pos; p.x += vel.x * dt;
            if (Hits(p) || p.x < BallR || p.x > Size - BallR) vel.x *= -0.35f; else pos.x = p.x;
            p = pos; p.y += vel.y * dt;
            if (Hits(p) || p.y < BallR || p.y > Size - BallR) vel.y *= -0.35f; else pos.y = p.y;

            ball.style.left = pos.x - BallR;
            ball.style.top = pos.y - BallR;

            if ((pos - HolePos * Size).magnitude < HoleR)
            {
                solved = true;
                sfx.PlayOneShot(AudioSynth.Chirp, 1f);
                Haptics.Pulse(120, 200);
                GameManager.I.HatchSolved();
            }
        }

#if UNITY_EDITOR
        // ponytail: one runnable check that the labyrinth is solvable and walls collide.
        [UnityEditor.MenuItem("Echoes/Self-check tilt lock")]
        static void SelfCheck()
        {
            Debug.Assert(!Hits(StartPos * Size), "ball starts inside a wall");
            Debug.Assert(!Hits(HolePos * Size), "lock is inside a wall");
            Debug.Assert(Hits(new Vector2(0.2f, 0.33f) * Size), "first wall does not collide");
            // the gaps the S-route needs are wider than the ball
            Debug.Assert((1f - 0.7f) * Size > BallR * 2 && 0.3f * Size > BallR * 2, "gaps too narrow");
            Debug.Log("Tilt lock self-check passed");
        }
#endif
    }
}
