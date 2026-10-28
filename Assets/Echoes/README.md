# Echoes in the Dark

Open `Scenes/Echoes.unity` and press Play. `Echoes/Build Game Scene` regenerates the scene, materials and settings;
`Echoes/Build Android APK` writes `Builds/EchoesInTheDark.apk`.

| Member | Feature | Script |
|---|---|---|
| 1 | Voice-controlled flashlight (mic RMS, calibration, noise alert) | `Scripts/Members/M1_VoiceFlashlight.cs` + `Core/Flashlight.cs` |
| 2 | Hide in darkness (light sensor / front camera, visibility, dark spawns) | `Scripts/Members/M2_HideInDarkness.cs` |
| 3 | Motion camera (attitude/gyro, recenter, sensitivity, real-north align) | `Scripts/Members/M3_GyroLook.cs` |
| 4 | Shake & freeze (shake recharge, motion thresholds, freeze-to-hide) | `Scripts/Members/M4_ShakeFreeze.cs` (+ `Core/TiltLock.cs` tilt puzzle) |
| 5 | Haunted compass (compass UI, real north, monster interference) | `Scripts/Members/M5_HauntedCompass.cs` |
| 6 | Walkie-talkie (proximity, radio overlay, beacon ping, live LAN voice) | `Scripts/Members/M6_WalkieTalkie.cs` |

Gameplay layer (solo; co-op keeps its original rules):
- **Fuses** (`Core/Fuse.cs`): the hatch has no power until 3 fuses under the red lamps are collected. The compass only
  hints roughly at the nearest fuse; the radio beacon leads to the hatch. Powered hatch -> tilt lock (`Core/TiltLock.cs`).
- **Grab & struggle** (`Core/Struggle.cs`): when caught, shake or shout to break free. Costs the battery, stuns the monster.
- **Flash burst** (`Core/FlashBurst.cs`): full battery + double-tap / FLASH / F stuns monsters in front, empties the light.
- **Bottle lures** (`Core/Lures.cs`): pick up bottles, flick up / THROW / T to smash one far away and pull monsters to the noise.
- **The Watcher** (`Monster.IsWatcher`): wakes with the first fuse, only moves while you are not looking at it.

Shared: `Survivor` (player state), `Monster` (Dormant/Spawn/HuntLight/TrackNoise/Search/Attack), `Maze` (seeded maze +
sealed co-op doors), `GameManager` (flow, co-op rules, revive, hatch), `Net` (LAN UDP co-op), `Heartbeat` (haptics + fear FX),
`HUD` (UI Toolkit, touch fallbacks), `AudioSynth` (all sounds are synthesised).

Desktop keys: WASD move, mouse look, SPACE shout, C cover, R shake, V radio, F flash, T throw, Q recenter, ESC pause.
Co-op: both devices on the same Wi-Fi, one taps HOST, the other JOIN (auto-discovery or type the host IP;
from the Android emulator the host PC is `10.0.2.2`). Command line: `-solo`, `-host`, `-join <ip>`.
