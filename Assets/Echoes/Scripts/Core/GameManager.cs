using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Echoes
{
    public enum Phase { Menu, Lobby, Calibrating, Playing, Ended }

    // Game flow: menu -> (co-op lobby) -> sensor calibration -> play -> caught / escaped.
    // The host (or the solo phone) is authoritative for monsters, catches, revives and the escape.
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;
        // co-op never freezes the world on pause; solo does
        public static bool Playing => I && I.phase == Phase.Playing && (!I.Paused || I.Coop);
        public static bool CanControl => Playing && !I.Paused;
        public static bool Night => DateTime.Now.Hour < 5;   // GDD: after midnight it gets nastier

        [Header("Scene")]
        public Maze maze;
        public Survivor player;
        public HUD hud;
        [Header("Monster")]
        public GameObject monsterModel;
        public RuntimeAnimatorController monsterController;
        public Material monsterSkin, eyeMaterial;
        public float monsterScale = 1.6f, modelYawFix = 0f;
        public float eyeHeight = 1.5f, eyeForward = 0.36f, eyeSpacing = 0.25f;
        [Header("Partner")]
        public GameObject partnerModel;
        public RuntimeAnimatorController partnerController;
        [Header("Rules")]
        public float meetRadius = 2.4f, reviveRadius = 1.8f, reviveTime = 3f;

        public Phase phase = Phase.Menu;
        public bool Paused { get; private set; }
        public Net net;
        public bool IsHost => net == null || net.IsHost;
        public bool Coop => net != null && partner != null;
        public float RoundTime { get; private set; }
        public bool Won { get; private set; }
        public string EndReason { get; private set; } = "";
        public float ReviveProgress { get; private set; }
        public int FusesFound { get; private set; }
        public int FusesNeeded { get; private set; }
        public bool HatchPowered => FusesFound >= FusesNeeded;
        public float FuseFoundAt { get; private set; } = -99f;
        public Survivor Partner => partner;

        Survivor partner;
        AudioSource ui, ambience;
        float sendAt, reviveTimer;
        int seed;

        // network message types beyond Net's handshake
        const byte EvCaught = 1, EvRevived = 2, EvEnd = 3, EvDoor = 4, EvSolved = 5;

        public bool AtHatch { get; private set; }        // everyone alive is standing on the meeting point
        public float CaughtAt { get; private set; } = -99f;
        public Vector3 CaughtFrom { get; private set; }  // monster eyes, for the jumpscare
        float nextAmbient;

        void Awake()
        {
            I = this;
            // statics outlive a scene reload
            Noise.Latest = new Noise.Event { Time = -999f };
            Monster.LastHeardAt = -99f;
            TouchInput.Move = Vector2.zero;
            TouchInput.Shout = TouchInput.Cover = TouchInput.Shake = TouchInput.Radio = false;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (Application.isMobilePlatform) Screen.fullScreen = true;   // immersive: hide the status bar
            AudioListener.volume = Settings.Volume;
            ui = gameObject.AddComponent<AudioSource>();
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = AudioSynth.Drone;
            ambience.loop = true;
            ambience.volume = 0.45f;
            ambience.Play();
            gameObject.AddComponent<Struggle>();
            player.gameObject.AddComponent<FlashBurst>();
            player.gameObject.AddComponent<Lures>();
        }

        void Start()
        {
            // the menu sits inside a live maze: atmosphere for free
            maze.Generate(UnityEngine.Random.Range(1, int.MaxValue));
            PlaceLocal(0);

            // command line: -solo | -host | -join [ip]   (LAN sessions and two-copies-on-one-machine testing)
            var args = Environment.GetCommandLineArgs();
            int j = Array.IndexOf(args, "-join");
            if (Array.IndexOf(args, "-host") >= 0) StartHost();
            else if (j >= 0) StartJoin(j + 1 < args.Length && !args[j + 1].StartsWith("-") ? args[j + 1] : "");
            else if (Array.IndexOf(args, "-solo") >= 0) StartSolo();
        }

        // ---------------- menu actions ----------------

        public void StartSolo()
        {
            seed = UnityEngine.Random.Range(1, int.MaxValue);
            BeginGame(0);
        }

        public void StartHost()
        {
            net = gameObject.AddComponent<Net>();
            net.Message += OnNet;
            net.PeerJoined += () =>
            {
                net.Send(Net.Welcome, w => w.Write(seed));
                if (phase == Phase.Lobby) BeginGame(0);
            };
            net.PeerLost += OnPeerLost;
            seed = UnityEngine.Random.Range(1, int.MaxValue);
            net.Host();
            phase = Phase.Lobby;
        }

        public void StartJoin(string ip)
        {
            Settings.LastIP = ip;
            net = gameObject.AddComponent<Net>();
            net.Message += OnNet;
            net.PeerLost += OnPeerLost;
            net.Join(ip);
            phase = Phase.Lobby;
        }

        public void CancelLobby() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        public void SetPaused(bool p)
        {
            Paused = p && phase == Phase.Playing;
            if (!Coop) Time.timeScale = Paused ? 0f : 1f;
        }

        // ---------------- round ----------------

        void BeginGame(int localId)
        {
            maze.Generate(seed);
            player.Id = localId;
            PlaceLocal(localId);
            if (net != null)
            {
                partner = new GameObject("Partner").AddComponent<Survivor>();
                partner.Id = 1 - localId;
                partner.transform.position = maze.Center(maze.SpawnCell(partner.Id));
                partner.gameObject.AddComponent<PartnerAvatar>().Init(partnerModel, partnerController, modelYawFix);
            }
            player.GetComponent<Flashlight>().ResetForRound();
            FusesFound = 0;
            FusesNeeded = maze.FuseCount;   // 0 in co-op: the maze places no fuses there
            StartCoroutine(Calibrate());
        }

        void PlaceLocal(int id)
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = maze.Center(maze.SpawnCell(id)) + Vector3.up * 0.05f;
            cc.enabled = true;
            // face down the first open corridor
            var c = maze.SpawnCell(id);
            float yaw = maze.Open(c, 0) ? 0 : maze.Open(c, 1) ? 90 : maze.Open(c, 2) ? 180 : 270;
            M3_GyroLook.I?.FaceYaw(yaw);
        }

        IEnumerator Calibrate()
        {
            phase = Phase.Calibrating;
            hud.ShowCalibration(true);
            skipCalibration = false;
            var mic = M1_VoiceFlashlight.I;
            if (mic && mic.HasMic)
            {
                hud.SetCalibration("1 / 3   STAY SILENT", "Measuring the room's noise floor…", 0);
                yield return mic.MeasureFloor(2.5f, p => hud.SetCalibration(null, null, p));
                if (skipCalibration) { Go(); yield break; }
                hud.SetCalibration("2 / 3   SHOUT!", "One loud shout sets your maximum", 0);
                yield return mic.MeasureMax(3f, p => hud.SetCalibration(null, null, p));
                if (skipCalibration) { Go(); yield break; }
            }
            hud.SetCalibration("3 / 3   HOLD STILL", "Hold the phone naturally and face forward", 0);
            yield return M4_ShakeFreeze.I.MeasureRest(1.6f, p => hud.SetCalibration(null, null, p));
            Go();
        }

        bool skipCalibration;
        public void SkipCalibration() { skipCalibration = true; if (phase == Phase.Calibrating) { StopAllCoroutines(); Go(); } }

        void Go()
        {
            M3_GyroLook.I.Recenter();
            M2_HideInDarkness.I.CalibrateBaseline();
            hud.ShowCalibration(false);
            RoundTime = 0;
            phase = Phase.Playing;
            PlaySting(Vector3.zero, AudioSynth.Chirp, 0.5f);
        }

        void Update()
        {
            if (phase == Phase.Playing)
            {
                RoundTime += Time.deltaTime;
                if (IsHost) HostRules();
                Ambience();
                bool all = true;
                foreach (var s in Survivor.All) if (!OnMeetingPoint(s)) all = false;
                AtHatch = all && Survivor.All.Count > 0 && HatchPowered;
            }
            else AtHatch = false;
            TiltLock.I?.SetVisible(AtHatch && !Paused);
            if (net != null && net.Connected && Time.unscaledTime >= sendAt)
            {
                sendAt = Time.unscaledTime + 1f / 15f;
                if (phase is Phase.Playing or Phase.Calibrating or Phase.Ended)
                    net.Send(Net.State, player.Write);
                if (IsHost && phase != Phase.Lobby)
                    net.Send(Net.Monsters, w =>
                    {
                        w.Write((byte)Monster.All.Count);
                        foreach (var m in Monster.All) m.Write(w);
                    });
            }
            Cursor.lockState = phase == Phase.Playing && !Paused && !Application.isMobilePlatform ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }

        void HostRules()
        {
            // revive: stand next to a downed partner
            Survivor downed = null, helper = null;
            foreach (var s in Survivor.All) if (s.Downed) downed = s; else helper = s;
            if (downed && helper && Vector3.Distance(downed.Position, helper.Position) < reviveRadius)
            {
                reviveTimer += Time.deltaTime;
                if (reviveTimer >= reviveTime)
                {
                    downed.Downed = false;
                    reviveTimer = 0;
                    if (!downed.IsLocal) net?.Send(Net.Event, w => { w.Write(EvRevived); w.Write((byte)downed.Id); });
                }
            }
            else reviveTimer = 0;
            ReviveProgress = reviveTimer / reviveTime;

        }

        public bool OnMeetingPoint(Survivor s)
        {
            var d = s.Position - maze.MeetingPoint;
            d.y = 0;
            return !s.Downed && d.magnitude <= meetRadius;
        }

        // escape together: everyone on the meeting point, then the hatch's tilt lock (TiltLock)
        public void HatchSolved()
        {
            if (IsHost) End(true, Coop ? "You found each other and escaped together." : "You reached the meeting point and escaped.");
            else net.Send(Net.Event, w => w.Write(EvSolved));
        }

        public void OpenDoor(int index)
        {
            var door = SealedDoor.All.Find(d => d.Index == index);
            if (!door || door.IsOpen) return;
            door.Open();
            if (IsHost) net?.Send(Net.Event, w => { w.Write(EvDoor); w.Write((byte)index); });
        }

        void Ambience()
        {
            if (Time.time < nextAmbient) return;
            nextAmbient = Time.time + UnityEngine.Random.Range(6f, 14f);
            var dir = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0) * Vector3.forward;
            var clip = UnityEngine.Random.value < 0.65f ? AudioSynth.Drip : AudioSynth.Knock;
            AudioSource.PlayClipAtPoint(clip, player.Position + dir * UnityEngine.Random.Range(6f, 14f) + Vector3.up * 2.5f, clip == AudioSynth.Knock ? 0.35f : 0.6f);
        }

        public void RequestSpawnNear(Vector3 pos)
        {
            if (IsHost) Monster.SpawnNear(pos);
            else net.Send(Net.Spawn, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); });
        }

        public void CollectFuse(Fuse f)
        {
            Destroy(f.gameObject);
            FusesFound++;
            FuseFoundAt = Time.time;
            PlaySting(Vector3.zero, AudioSynth.Pickup, 1f);
            Haptics.Pulse(80, 200);
            // taking the first fuse wakes something up
            if (FusesFound == 1 && IsHost && !Coop) Monster.SpawnWatcher(player.Position);
        }

        // a monster reached someone: your own survivor gets a chance to struggle free, everyone else is downed
        public void Grab(Survivor s, Monster m)
        {
            if (!IsHost || s.Downed || phase != Phase.Playing) return;
            if (s.IsLocal && Struggle.I.Begin(s, m))
            {
                m.Hold(s);
                CaughtAt = Time.time;
                return;
            }
            Caught(s);
            m.Release(false);
        }

        public void Caught(Survivor s)
        {
            if (!IsHost || s.Downed || phase != Phase.Playing) return;
            s.Downed = true;
            if (s.IsLocal) OnLocalDowned();
            else net?.Send(Net.Event, w => { w.Write(EvCaught); w.Write((byte)s.Id); });
            bool anyUp = false;
            foreach (var x in Survivor.All) if (!x.Downed) anyUp = true;
            if (!anyUp) End(false, Coop ? "The darkness took you both." : "The darkness took you.");
        }

        void OnLocalDowned()
        {
            // jumpscare: snap the view to the closest pair of red eyes before you fall
            CaughtAt = Time.time;
            float best = float.MaxValue;
            foreach (var m in Monster.All)
            {
                float d = Vector3.Distance(m.transform.position, player.Position);
                if (d < best) { best = d; CaughtFrom = m.transform.position + Vector3.up * 1.75f; }
            }
            PlaySting(Vector3.zero, AudioSynth.Sting, 1f);
            Haptics.Pulse(400, 255);
        }

        void End(bool won, string reason)
        {
            if (phase == Phase.Ended) return;
            phase = Phase.Ended;
            Won = won;
            EndReason = reason;
            Time.timeScale = 1f;
            PlaySting(Vector3.zero, won ? AudioSynth.Win : AudioSynth.Sting, 1f);
            if (IsHost) net?.Send(Net.Event, w => { w.Write(EvEnd); w.Write(won); w.Write(reason); });
            hud.ShowEnd(won, reason, RoundTime);
        }

        public void PlaySting(Vector3 at, AudioClip clip, float vol)
        {
            if (at == Vector3.zero) ui.PlayOneShot(clip, vol);
            else AudioSource.PlayClipAtPoint(clip, at, vol);
        }

        // ---------------- network ----------------

        void OnNet(byte type, BinaryReader r)
        {
            switch (type)
            {
                case Net.Welcome:
                    seed = r.ReadInt32();
                    if (phase == Phase.Lobby) BeginGame(1);
                    break;
                case Net.State:
                    if (!partner) break;
                    partner.GetComponent<PartnerAvatar>().Read(r);
                    // the host's monsters hear the partner's phone too
                    if (IsHost && partner.Loudness > M1_VoiceFlashlight.I.noiseThreshold && Time.time - Noise.Latest.Time > 0.3f)
                        Noise.Emit(partner.Position, partner.Loudness, partner.Id);
                    break;
                case Net.Monsters:
                    if (!IsHost) Monster.ReadAll(r);
                    break;
                case Net.Voice:
                    M6_WalkieTalkie.I?.Receive(r);
                    break;
                case Net.Spawn:
                    if (IsHost) Monster.SpawnNear(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                    break;
                case Net.Event:
                    byte ev = r.ReadByte();
                    if (ev == EvCaught && r.ReadByte() == player.Id) { player.Downed = true; OnLocalDowned(); }
                    else if (ev == EvRevived && r.ReadByte() == player.Id) player.Downed = false;
                    else if (ev == EvEnd) End(r.ReadBoolean(), r.ReadString());
                    else if (ev == EvDoor) OpenDoor(r.ReadByte());
                    else if (ev == EvSolved && IsHost) HatchSolved();
                    break;
            }
        }

        void OnPeerLost()
        {
            if (partner) Destroy(partner.gameObject);
            partner = null;
            if (!IsHost && phase != Phase.Ended && phase != Phase.Menu) End(false, "Lost the connection to your partner.");
        }
    }
}
