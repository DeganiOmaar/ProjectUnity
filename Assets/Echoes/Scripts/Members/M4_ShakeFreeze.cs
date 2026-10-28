using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Echoes
{
    // MEMBER 4 — Shake & Freeze Survival (Accelerometer)
    // Shake the phone to recharge the flashlight silently (slower than shouting, and the view shakes).
    // When the monster passes, hold the phone perfectly still or it sees you.
    // Deliverable: shake detection, motion thresholds (calibrated at rest), freeze-to-hide (Survivor.Frozen/Hidden).
    [RequireComponent(typeof(Survivor), typeof(Flashlight), typeof(PlayerController))]
    public class M4_ShakeFreeze : MonoBehaviour
    {
        public static M4_ShakeFreeze I;

        public float shakeThreshold = 1.1f;    // g of linear acceleration that counts as a shake
        public float rechargePerSec = 0.14f;
        public float freezeHoldTime = 0.6f;    // must stay still this long to count as frozen
        public float monsterWarnRange = 9f;

        public bool HasAccelerometer { get; private set; }
        public Vector3 Gravity => gravity;               // low-passed, in g (TiltLock uses it)
        public float PhoneMotion { get; private set; }   // raw g of linear motion
        public float FreezeThreshold => Mathf.Max(0.07f, Settings.RestMotion * 2.5f);
        public bool MonsterNear => Monster.NearestDistance(transform.position) < monsterWarnRange;

        Survivor s;
        Flashlight torch;
        PlayerController body;
        Vector3 gravity;
        float shakeEnergy, stillTime;

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
            torch = GetComponent<Flashlight>();
            body = GetComponent<PlayerController>();
        }

        void Start()
        {
            if (Accelerometer.current != null)
            {
                InputSystem.EnableDevice(Accelerometer.current);
                HasAccelerometer = true;
                gravity = ReadG();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float phone = 0;
            if (HasAccelerometer)
            {
                var a = ReadG();
                gravity = Vector3.Lerp(gravity, a, 1 - Mathf.Exp(-dt * 3f));   // low-pass = gravity
                phone = (a - gravity).magnitude;                                 // high-pass = shake
            }
            PhoneMotion = phone;

            bool key = (Keyboard.current?.rKey.isPressed ?? false) || TouchInput.Shake;
            if (phone > shakeThreshold || key) shakeEnergy = 1f;
            shakeEnergy = Mathf.MoveTowards(shakeEnergy, 0, dt * 2.5f);
            s.Shaking = shakeEnergy > 0.3f && !s.Downed;
            if (GameManager.Playing && s.Shaking) torch.Recharge(rechargePerSec * dt);

            // dynamo shaking makes it hard to see
            PlayerController.CameraShake = s.Shaking
                ? new Vector3(Mathf.PerlinNoise(Time.time * 30f, 0) - 0.5f, Mathf.PerlinNoise(0, Time.time * 30f) - 0.5f, 0) * 0.09f
                : Vector3.zero;

            // motion = phone movement + walking + turning the view
            float look = M3_GyroLook.I ? M3_GyroLook.I.AngularSpeed / 90f : 0;
            float m = Mathf.Max(phone / 0.6f, body.Speed01, look, s.Shaking ? 1f : 0f);
            s.Motion = Mathf.Lerp(s.Motion, Mathf.Clamp01(m), 1 - Mathf.Exp(-dt * 10f));
            stillTime = s.Motion < FreezeThreshold ? stillTime + dt : 0;
            s.Frozen = stillTime > freezeHoldTime;
        }

        // some Android backends report m/s^2, others g: normalise to g
        static Vector3 ReadG()
        {
            var a = Accelerometer.current.acceleration.ReadValue();
            return a.sqrMagnitude > 9f ? a / 9.81f : a;
        }

        // calibration: measure how much a resting hand jitters so "still" is fair on every phone
        public IEnumerator MeasureRest(float seconds, Action<float> progress)
        {
            float peak = 0;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                peak = Mathf.Max(peak, s.Motion);
                progress(t / seconds);
                yield return null;
            }
            Settings.RestMotion = Mathf.Clamp(peak, 0.01f, 0.12f);
        }
    }
}
