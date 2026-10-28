using UnityEngine;
using UnityEngine.InputSystem;

namespace Echoes
{
    // Fight back with light: with a (nearly) full battery, double-tap the screen / FLASH / F to fire a blinding
    // burst that stuns every monster in front of you. It empties the battery — and darkness spawns it.
    [RequireComponent(typeof(Survivor), typeof(Flashlight))]
    public class FlashBurst : MonoBehaviour
    {
        public static FlashBurst I;
        public static float FiredAt = -99f;

        public float readyAt = 0.95f, range = 12f, cone = 40f, stunSeconds = 3.5f;

        Survivor s;
        Flashlight torch;
        bool keyWasDown;

        public bool Ready => s.Battery >= readyAt && GameManager.CanControl && !s.Downed && !Struggle.Active;

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
            torch = GetComponent<Flashlight>();
            FiredAt = -99f;
        }

        void Update()
        {
            bool down = Keyboard.current?.fKey.isPressed ?? false;
            bool pressed = (down && !keyWasDown) | TouchInput.ConsumeFlash();
            keyWasDown = down;
            if (pressed) Fire();
        }

        public bool Fire()
        {
            if (!Ready) return false;
            FiredAt = Time.time;
            s.Battery = 0f;
            torch.Burst(0.3f);
            GameManager.I.PlaySting(Vector3.zero, AudioSynth.Burst, 1f);
            Haptics.Pulse(150, 255);

            var cam = Camera.main.transform;
            foreach (var m in Monster.All)
            {
                if (!m.Simulated || m.Sink > 0.5f) continue;   // co-op client: the host owns the monsters
                var eyes = m.transform.position + Vector3.up * 1.4f;
                var to = eyes - cam.position;
                if (to.magnitude < range && Vector3.Angle(cam.forward, to) < cone && Maze.Instance.LineOfSight(cam.position, eyes))
                    m.Stun(stunSeconds);
            }
            return true;
        }
    }
}
