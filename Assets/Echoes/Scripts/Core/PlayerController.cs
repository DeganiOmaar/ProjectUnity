using UnityEngine;
using UnityEngine.InputSystem;

namespace Echoes
{
    // Thumb / WASD movement only — looking is M3's job (gyro). Footsteps and head bob.
    [RequireComponent(typeof(CharacterController), typeof(Survivor))]
    public class PlayerController : MonoBehaviour
    {
        public Transform head;
        public float walkSpeed = 2.6f;
        public float eyeHeight = 1.6f;
        public static Vector3 CameraShake;      // M4 adds jitter while shaking the phone

        public float Speed01 { get; private set; }
        CharacterController cc;
        Survivor s;
        AudioSource feet;
        float vy, stepT, bob, downedLerp;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            s = GetComponent<Survivor>();
            feet = gameObject.AddComponent<AudioSource>();
            feet.spatialBlend = 0;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector2 mv = Vector2.zero;
            if (GameManager.CanControl && !s.Downed && !TiltLock.Active && !Struggle.Active)
            {
                var k = Keyboard.current;
                if (k != null)
                {
                    if (k.wKey.isPressed || k.upArrowKey.isPressed) mv.y += 1;
                    if (k.sKey.isPressed || k.downArrowKey.isPressed) mv.y -= 1;
                    if (k.dKey.isPressed || k.rightArrowKey.isPressed) mv.x += 1;
                    if (k.aKey.isPressed || k.leftArrowKey.isPressed) mv.x -= 1;
                }
                if (Gamepad.current != null) mv += Gamepad.current.leftStick.ReadValue();
                mv += TouchInput.Move;
                mv = Vector2.ClampMagnitude(mv, 1f);
            }

            var dir = transform.right * mv.x + transform.forward * mv.y;
            vy = cc.isGrounded ? -1f : vy - 9.81f * dt;
            cc.Move((dir * walkSpeed + Vector3.up * vy) * dt);
            var hv = cc.velocity;
            hv.y = 0;
            Speed01 = Mathf.Clamp01(hv.magnitude / walkSpeed);

            if (Speed01 > 0.1f)
            {
                stepT += dt * Speed01 * 1.9f;
                if (stepT >= 1f)
                {
                    stepT = 0;
                    feet.pitch = Random.Range(0.85f, 1.1f);
                    feet.PlayOneShot(AudioSynth.Footstep, 0.35f);
                }
            }
            bob += dt * Speed01 * Mathf.PI * 1.9f;
            downedLerp = Mathf.MoveTowards(downedLerp, s.Downed ? 1f : 0f, dt * 1.5f);
            float y = eyeHeight + Mathf.Abs(Mathf.Sin(bob)) * 0.05f * Speed01 - downedLerp * 1.25f;
            head.localPosition = new Vector3(Mathf.Sin(bob) * 0.025f * Speed01, y, 0) + CameraShake;
        }
    }
}
