using UnityEngine;

namespace Echoes
{
    // Player-tunable values persisted on the device.
    public static class Settings
    {
        static float F(string k, float d) => PlayerPrefs.GetFloat("echoes." + k, d);
        static void Put(string k, float v) { PlayerPrefs.SetFloat("echoes." + k, v); PlayerPrefs.Save(); }

        public static float LookSensitivity { get => F("lookSens", 1f); set => Put("lookSens", value); }   // gyro + touch
        public static float MouseSensitivity { get => F("mouseSens", 1f); set => Put("mouseSens", value); }
        public static bool InvertY { get => F("invertY", 0) > 0.5f; set => Put("invertY", value ? 1 : 0); }
        public static float Volume { get => F("volume", 1f); set { Put("volume", value); AudioListener.volume = value; } }
        public static float MicFloor { get => F("micFloor", 0.004f); set => Put("micFloor", value); }
        public static float MicMax { get => F("micMax", 0.25f); set => Put("micMax", value); }
        public static float RestMotion { get => F("restMotion", 0.04f); set => Put("restMotion", value); }
        public static bool SeenTutorial { get => F("seenTutorial", 0) > 0.5f; set => Put("seenTutorial", value ? 1 : 0); }
        public static string LastIP { get => PlayerPrefs.GetString("echoes.lastIP", ""); set { PlayerPrefs.SetString("echoes.lastIP", value); PlayerPrefs.Save(); } }
    }
}
