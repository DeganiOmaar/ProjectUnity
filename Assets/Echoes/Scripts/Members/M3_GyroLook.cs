using UnityEngine;
using UnityEngine.InputSystem;

namespace Echoes
{
    // MEMBER 3 — Motion-Controlled Camera (Gyroscope)
    // Physically rotate the phone to look around the maze; the thumb only moves.
    // Deliverable: first-person camera controller, orientation calibration (Recenter + real-north alignment),
    // sensitivity settings (Settings.LookSensitivity). Mouse / touch-drag / gamepad fallbacks.
    [RequireComponent(typeof(Survivor))]
    public class M3_GyroLook : MonoBehaviour
    {
        public static M3_GyroLook I;

        public Transform cam;
        public float pitchLimit = 80f;
        public float northCorrection = 0.4f;  // how fast drift is pulled back to the real compass heading

        public bool HasAttitude { get; private set; }
        public bool HasGyroRate { get; private set; }
        public bool UseGyro => HasAttitude || HasGyroRate;
        public float AngularSpeed { get; private set; }  // deg/s of view change, M4 counts it as motion

        Survivor s;
        float manualYaw, manualPitch, yawOffset, calibYaw, rateYaw, ratePitch;
        Quaternion lastRot;

        void Awake()
        {
            I = this;
            s = GetComponent<Survivor>();
        }

        void Start()
        {
            InputSystem.settings.compensateForScreenOrientation = true;
            if (AttitudeSensor.current != null)
            {
                InputSystem.EnableDevice(AttitudeSensor.current);
                HasAttitude = true;
            }
            else if (UnityEngine.InputSystem.Gyroscope.current != null)
            {
                InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
                HasGyroRate = true;
            }
            lastRot = cam.rotation;
        }

        // Unity's attitude -> camera space (right-handed device frame to Unity's left-handed one)
        Quaternion Attitude()
        {
            var a = AttitudeSensor.current.attitude.ReadValue();
            return Quaternion.Euler(90f, 0, 0) * new Quaternion(a.x, a.y, -a.z, -a.w);
        }

        public void FaceYaw(float yaw)
        {
            manualYaw = 0;
            manualPitch = 0;
            yawOffset = yaw;
            if (HasAttitude) calibYaw = Attitude().eulerAngles.y;
            rateYaw = ratePitch = 0;
        }

        // "Look forward and tap recenter": current pose becomes straight ahead.
        // With a compass the maze is aligned to real north, so the yaw becomes the real heading.
        public void Recenter()
        {
            float current = s.Yaw;
            manualYaw = manualPitch = rateYaw = ratePitch = 0;
            if (HasAttitude) calibYaw = Attitude().eulerAngles.y;
            yawOffset = M5_HauntedCompass.RealHeading ?? current;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float sens = Settings.LookSensitivity;
            bool canLook = GameManager.I && GameManager.I.phase != Phase.Ended && !(GameManager.I.Paused) && !s.Downed;

            if (canLook)
            {
                Vector2 d = TouchInput.ConsumeLook() * 0.12f * sens;
                if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
                    d += Mouse.current.delta.ReadValue() * 0.08f * Settings.MouseSensitivity;
                if (Gamepad.current != null) d += Gamepad.current.rightStick.ReadValue() * 140f * dt;
                manualYaw += d.x;
                manualPitch += (Settings.InvertY ? d.y : -d.y);
            }
            else TouchInput.ConsumeLook();

            float yaw, pitch, roll = 0;
            if (HasAttitude)
            {
                var e = Attitude().eulerAngles;
                float gy = Mathf.DeltaAngle(calibYaw, e.y) * sens;
                yaw = yawOffset + gy + manualYaw;
                pitch = Mathf.DeltaAngle(0, e.x) * sens + manualPitch;
                roll = Mathf.DeltaAngle(0, e.z) * 0.5f;

                // real-north drift correction (only meaningful at 1:1 sensitivity, and the monster jams it)
                var heading = M5_HauntedCompass.RealHeading;
                if (heading.HasValue && Mathf.Abs(sens - 1f) < 0.05f && M5_HauntedCompass.I.Interference < 0.2f && Mathf.Abs(manualYaw) < 0.01f)
                    yawOffset += Mathf.DeltaAngle(yawOffset + gy, heading.Value) * northCorrection * dt;
            }
            else
            {
                if (HasGyroRate && canLook)
                {
                    var r = UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue() * Mathf.Rad2Deg * sens * dt;
                    rateYaw -= r.y;
                    ratePitch -= r.x;
                }
                yaw = yawOffset + manualYaw + rateYaw;
                pitch = manualPitch + ratePitch;
            }

            pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);
            manualPitch = Mathf.Clamp(manualPitch, -pitchLimit, pitchLimit);
            if (s.Downed) { pitch = -10f; roll = 65f; }
            var gm = GameManager.I;
            if (Struggle.Active || (s.Downed && gm && Time.time - gm.CaughtAt < 0.9f))
            {
                // jumpscare / grabbed: forced to stare into its eyes, with a violent shake
                var dir = (Struggle.Active ? Struggle.I.MonsterEyes : gm.CaughtFrom) - cam.position;
                yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + Random.Range(-3f, 3f);
                pitch = -Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg + Random.Range(-3f, 3f);
                roll = Random.Range(-6f, 6f);
                transform.rotation = Quaternion.Euler(0, yaw, 0);
                cam.localRotation = Quaternion.Euler(pitch, 0, roll);
                return;
            }
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cam.localRotation = Quaternion.Slerp(cam.localRotation, Quaternion.Euler(pitch, 0, roll), s.Downed ? dt * 2f : 1f);

            s.Yaw = Mathf.Repeat(yaw, 360f);
            s.Pitch = pitch;
            AngularSpeed = dt > 0 ? Quaternion.Angle(lastRot, cam.rotation) / dt : 0;
            lastRot = cam.rotation;
        }
    }
}
