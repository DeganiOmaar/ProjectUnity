using UnityEngine;
using UnityEngine.InputSystem;

namespace Echoes
{
    // Noise as a tool: pick up bottles, then flick up on the screen (or THROW / T) to smash one far away.
    // The crash is loud — every monster in earshot goes to check it out.
    [RequireComponent(typeof(Survivor))]
    public class Lures : MonoBehaviour
    {
        public static Lures I;
        public const int Max = 3;
        public float throwSpeed = 10f;

        public int Count { get; set; }
        Survivor s;
        bool keyWasDown;

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
        }

        void Update()
        {
            bool down = Keyboard.current?.tKey.isPressed ?? false;
            bool pressed = (down && !keyWasDown) | TouchInput.ConsumeThrow();
            keyWasDown = down;
            if (pressed) Throw();
        }

        public bool Throw()
        {
            if (Count <= 0 || !GameManager.CanControl || s.Downed || Struggle.Active) return false;
            Count--;
            var cam = Camera.main.transform;
            var b = Bottle.Make(cam.position + cam.forward * 0.6f, Maze.Instance.lampMat);
            var rb = b.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = cam.forward * throwSpeed + Vector3.up * 2.5f;
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            var col = b.gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.06f;
            col.height = 0.3f;
            Physics.IgnoreCollision(col, GetComponent<CharacterController>());
            b.thrown = true;
            b.born = Time.time;
            return true;
        }
    }

    public class Bottle : MonoBehaviour
    {
        public bool thrown;
        public float born;

        public static Bottle Make(Vector3 pos, Material glowSource)
        {
            var root = new GameObject("Bottle").transform;
            root.position = pos;
            var mat = glowSource ? new Material(glowSource) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.12f, 0.3f, 0.16f));
            mat.SetFloat("_Smoothness", 0.95f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.1f, 0.35f, 0.15f) * 0.6f);  // a faint glint so you can spot it
            foreach (var (y, scale) in new[] { (0.12f, new Vector3(0.09f, 0.11f, 0.09f)), (0.29f, new Vector3(0.035f, 0.06f, 0.035f)) })
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                if (Application.isPlaying) Destroy(part.GetComponent<Collider>()); else DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(root, false);
                part.transform.localPosition = Vector3.up * (y - 0.15f);
                part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = mat;
            }
            return root.gameObject.AddComponent<Bottle>();
        }

        void Update()
        {
            if (thrown)
            {
                if (Time.time - born > 4f) Shatter();   // never hit anything (very unlikely in a maze)
                return;
            }
            var s = Survivor.Local;
            if (!GameManager.Playing || !s || s.Downed || !Lures.I || Lures.I.Count >= Lures.Max) return;
            var d = s.Position - transform.position;
            d.y = 0;
            if (d.magnitude < 1.2f)
            {
                Lures.I.Count++;
                GameManager.I.PlaySting(Vector3.zero, AudioSynth.Pickup, 0.7f);
                Destroy(gameObject);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (thrown && c.relativeVelocity.magnitude > 1.5f) Shatter();
        }

        void Shatter()
        {
            if (!thrown) return;
            thrown = false;
            var p = transform.position;
            AudioSource.PlayClipAtPoint(AudioSynth.Shatter, p, 1f);
            if (GameManager.I.IsHost) Noise.Emit(p, 0.8f, -1);
            Destroy(gameObject);
        }
    }
}
