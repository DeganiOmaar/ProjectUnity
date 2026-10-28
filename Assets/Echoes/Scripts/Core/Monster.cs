using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Echoes
{
    // GDD states. Dormant = not spawned (no object).
    public enum MonsterState : byte { Dormant, Spawn, HuntLight, TrackNoise, Search, Attack }

    // "Noise reveals you": anything over the noise threshold is heard through walls.
    public static class Noise
    {
        public struct Event { public Vector3 Pos; public float Level; public float Time; public int Source; }
        public static Event Latest = new() { Time = -999 };
        public static float Radius(float level) => Mathf.Lerp(8f, 34f, level);
        public static void Emit(Vector3 pos, float level, int source) =>
            Latest = new Event { Pos = pos, Level = level, Time = UnityEngine.Time.time, Source = source };
    }

    // Darkness spawns it. Light attracts it. Noise reveals you.
    // The host (or solo phone) simulates; the co-op client only mirrors the host's snapshot.
    public class Monster : MonoBehaviour
    {
        public static readonly List<Monster> All = new();
        public static int MaxActive => GameManager.Night ? 3 : 2; // after midnight it gets nastier
        public static float LastHeardAt = -99f;                    // for the HUD "it heard you" alert
        static int nextId;

        public int Id;
        public bool IsWatcher;                   // pale variant: only moves while you are not looking at it
        public bool Observed { get; private set; }
        public bool Stunned => Time.time < stunUntil;
        public MonsterState State = MonsterState.Spawn;
        public bool Simulated = true;
        [Range(0, 1)] public float Eyes = 0.4f;
        [Range(0, 1)] public float Sink = 1f;   // 1 = below the floor, 0 = standing

        [Header("Tuning")]
        public float searchSpeed = 1.4f, huntSpeed = 2.2f, noiseSpeed = 2.7f, attackSpeed = 3.4f;
        public float closeRange = 7.5f, lightRange = 20f, catchRange = 1.35f, searchDuration = 14f;
        public float watcherSpeed = 4.2f;

        Transform model;
        Animator anim;
        string animState;
        Light eyeLight;
        Material eyeMat;
        AudioSource voice;
        Vector3 target, searchCenter, netPos, lastPos;
        float netYaw, repathAt, senseAt, stateUntil, lastHeard, punchUntil, speedNow, stunUntil;
        Survivor holding;
        Color eyeColor = new(1f, 0.05f, 0.02f);
        List<Vector3> path = new();
        bool despawning;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static float NearestDistance(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var m in All)
                if (m.Sink < 0.9f) best = Mathf.Min(best, Vector3.Distance(p, m.transform.position));
            return best;
        }

        public static Monster Create(int id, bool watcher = false)
        {
            var m = new GameObject((watcher ? "Watcher " : "Monster ") + id).AddComponent<Monster>();
            m.Id = id;
            m.IsWatcher = watcher;
            m.Build(GameManager.I);
            return m;
        }

        void Build(GameManager gm)
        {
            model = Instantiate(gm.monsterModel, transform).transform;
            model.localScale = Vector3.one * gm.monsterScale * (IsWatcher ? 1.2f : 1f);
            model.localRotation = Quaternion.Euler(0, gm.modelYawFix, 0);
            if (!model.TryGetComponent(out anim)) anim = model.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = gm.monsterController;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var skin = gm.monsterSkin;
            if (IsWatcher)
            {
                // bone-white and matte: it should look like a statue in your torch beam
                skin = new Material(gm.monsterSkin);
                skin.SetTexture("_BaseMap", null);   // plain bone-white, like a mannequin
                skin.SetColor("_BaseColor", new Color(0.78f, 0.77f, 0.72f));
                skin.SetFloat("_Smoothness", 0.12f);
                eyeColor = new Color(0.55f, 0.9f, 1f);
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = skin;
                r.sharedMaterials = mats;
            }

            // glowing eyes, parented to the head bone so they follow the animation
            eyeMat = new Material(gm.eyeMaterial);
            eyeMat.EnableKeyword("_EMISSION");
            var head = FindDeep(model, "Head") ?? model;
            var eyeBase = transform.position + Vector3.up * gm.eyeHeight + transform.forward * gm.eyeForward;
            for (int side = -1; side <= 1; side += 2)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(e.GetComponent<Collider>());
                e.name = "Eye";
                // the rig has eyelid bones right on the eyeballs; fall back to the tuned offsets
                var lid = FindDeep(model, side < 0 ? "Eyelid.L" : "Eyelid.R");
                e.transform.position = lid
                    ? lid.position + transform.forward * 0.11f - Vector3.up * 0.07f
                    : eyeBase + transform.right * side * gm.eyeSpacing;
                e.transform.localScale = Vector3.one * 0.11f;
                e.transform.SetParent(lid ? lid : head, true);
                e.GetComponent<Renderer>().sharedMaterial = eyeMat;
                e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            eyeLight = new GameObject("EyeLight").AddComponent<Light>();
            eyeLight.transform.SetParent(head, false);
            eyeLight.transform.position = eyeBase + transform.forward * 0.25f;
            eyeLight.type = LightType.Point;
            eyeLight.color = eyeColor;
            eyeLight.range = 3.5f;
            eyeLight.shadows = LightShadows.None;

            voice = gameObject.AddComponent<AudioSource>();
            voice.clip = IsWatcher ? AudioSynth.Scrape : AudioSynth.Growl;
            voice.loop = true;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 1.5f;
            voice.maxDistance = 22f;
            voice.dopplerLevel = 0;
            voice.volume = 0;
            voice.Play();
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, n);
                if (f) return f;
            }
            return null;
        }

        // ---------------- director (host only) ----------------

        public static void SpawnNear(Vector3 pos)
        {
            var mz = Maze.Instance;
            if (!mz) return;
            int active = 0;
            foreach (var m in All) if (!m.despawning && !m.IsWatcher) active++;
            if (active >= MaxActive) return;

            // GDD: appears out of sight, in a dark area near the player
            var dist = mz.DistanceMap(mz.ToCell(pos));
            var picks = new List<Vector2Int>();
            for (int pass = 0; pass < 2 && picks.Count == 0; pass++)
                for (int x = 0; x < mz.width; x++)
                    for (int y = 0; y < mz.height; y++)
                    {
                        var c = new Vector2Int(x, y);
                        int d = dist[x, y];
                        if (d < (pass == 0 ? 3 : 2) || d > (pass == 0 ? 6 : 99) || c == mz.MeetingCell) continue;
                        bool seen = false;
                        foreach (var s in Survivor.All)
                            if (Vector3.Distance(s.Position, mz.Center(c)) < 4f || mz.LineOfSight(s.Eye, mz.Center(c) + Vector3.up * 1.5f)) seen = true;
                        if (!seen) picks.Add(c);
                    }
            if (picks.Count == 0) return;

            var m2 = Create(nextId++ & 255);
            m2.transform.position = mz.Center(picks[Random.Range(0, picks.Count)]);
            m2.State = MonsterState.Spawn;
            m2.Sink = 1f;
            m2.searchCenter = m2.target = pos;
        }

        // The Watcher wakes up far away, out of sight, and never leaves.
        public static void SpawnWatcher(Vector3 playerPos)
        {
            var mz = Maze.Instance;
            if (!mz || All.Exists(m => m.IsWatcher)) return;
            var dist = mz.DistanceMap(mz.ToCell(playerPos));
            Vector2Int best = default;
            int bestD = -1;
            for (int x = 0; x < mz.width; x++)
                for (int y = 0; y < mz.height; y++)
                {
                    var c = new Vector2Int(x, y);
                    int d = dist[x, y];
                    if (d < 6 || d > 12 || c == mz.MeetingCell || d <= bestD) continue;
                    if (mz.LineOfSight(playerPos + Vector3.up * 1.6f, mz.Center(c) + Vector3.up * 1.5f)) continue;
                    best = c; bestD = d;
                }
            if (bestD < 0) return;
            var w = Create(nextId++ & 255, watcher: true);
            w.transform.position = mz.Center(best);
            w.State = MonsterState.Spawn;
            w.Sink = 1f;
            w.searchCenter = w.target = playerPos;
        }

        // ---------------- grab / stun (called by Struggle and FlashBurst) ----------------

        public void Hold(Survivor s)
        {
            holding = s;
            State = MonsterState.Attack;
        }

        public void Release(bool playerEscaped)
        {
            var s = holding;
            holding = null;
            if (playerEscaped) Stun(4f);
            else Search(s ? s.Position : transform.position);
        }

        public void Stun(float seconds)
        {
            holding = null;
            stunUntil = Time.time + seconds;
            Search(transform.position);
            stateUntil = stunUntil + searchDuration;
        }

        // ---------------- per frame ----------------

        void Update()
        {
            if (Simulated) Think(); else Mirror();

            float vis = 1f - Sink;
            model.localPosition = Vector3.down * (Sink * 2.6f);
            eyeMat.SetColor(Emission, eyeColor * Mathf.Lerp(3f, 14f, Eyes) * vis);
            eyeLight.intensity = Mathf.Lerp(0.5f, 2.5f, Eyes) * vis;
            voice.volume = IsWatcher ? (speedNow > 0.1f ? 0.9f : 0f) * vis : vis * Mathf.Lerp(0.55f, 1f, Eyes);
            Animate();
        }

        void Think()
        {
            float dt = Time.deltaTime;
            speedNow = 0;
            if (despawning)
            {
                Sink = Mathf.MoveTowards(Sink, 1f, dt / 1.5f);
                if (Sink >= 1f) Destroy(gameObject);
                return;
            }
            if (State == MonsterState.Spawn)
            {
                Sink = Mathf.MoveTowards(Sink, 0f, dt / 2f);
                if (Sink <= 0f) Search(searchCenter);
                return;
            }
            if (!GameManager.Playing) return;
            if (Stunned)
            {
                Eyes = Mathf.MoveTowards(Eyes, 0.05f, dt * 4f);
                return;
            }
            if (holding)
            {
                // pinned: keep hitting while the struggle plays out
                var to = holding.Position - transform.position;
                to.y = 0;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
                punchUntil = Time.time + 0.2f;
                Eyes = 1f;
                return;
            }
            if (IsWatcher) { WatcherThink(dt); return; }

            if (Time.time >= senseAt) { senseAt = Time.time + 0.15f; Sense(); }
            if (!despawning) Move(dt);
            Eyes = Mathf.MoveTowards(Eyes, State == MonsterState.Attack ? 1f : State == MonsterState.Search ? 0.35f : 0.65f, dt * 2f);
            if (State == MonsterState.Search && Time.time > stateUntil) Despawn();
        }

        void WatcherThink(float dt)
        {
            Survivor prey = null;
            foreach (var s in Survivor.All)
                if (!s.Downed && (!prey || Vector3.Distance(s.Position, transform.position) < Vector3.Distance(prey.Position, transform.position))) prey = s;
            if (!prey) return;
            Observed = IsObserved();
            Eyes = Mathf.MoveTowards(Eyes, Observed ? 1f : 0.45f, dt * 3f);
            if (Observed) return;   // frozen like a statue
            if (Vector3.Distance(transform.position, prey.Position) < catchRange && !(prey.IsLocal && Time.time < Struggle.GraceUntil))
            {
                punchUntil = Time.time + 0.75f;
                GameManager.I.Grab(prey, this);
                return;
            }
            target = prey.Position;
            Move(dt, watcherSpeed);
        }

        // looking at it = it is inside the camera's view, not behind a wall, and you can actually see it
        bool IsObserved()
        {
            var cam = Camera.main;
            if (!cam || !Survivor.Local) return false;
            var eyes = transform.position + Vector3.up * 1.4f;
            var to = eyes - cam.transform.position;
            float d = to.magnitude;
            if (d > 30f || Vector3.Angle(cam.transform.forward, to) > 42f) return false;
            if (!Maze.Instance.LineOfSight(cam.transform.position, eyes)) return false;
            return Survivor.Local.LightOutput > 0.15f || d < 4f;
        }

        bool Detects(Survivor s, float d)
        {
            if (s.IsLocal && Time.time < Struggle.GraceUntil) return false;   // just broke free
            if (s.Hidden) return false;
            return s.Visibility > Mathf.Lerp(0.1f, 0.4f, d / closeRange);
        }

        void Sense()
        {
            var mz = Maze.Instance;
            var eye = transform.position + Vector3.up * 1.8f;
            Survivor seen = null, lit = null;
            float seenD = float.MaxValue, litScore = 0;
            foreach (var s in Survivor.All)
            {
                if (s.Downed) continue;
                float d = Vector3.Distance(transform.position, s.Position);
                bool los = d < lightRange && mz.LineOfSight(eye, s.Eye);
                if (d < closeRange && los && Detects(s, d) && d < seenD) { seen = s; seenD = d; }
                // hunt light: a flashlight at full brightness within range (spill reaches around corners)
                if (s.LightOutput > 0.72f && (los || d < 9f))
                {
                    float score = s.LightOutput / (1f + d);
                    if (score > litScore) { litScore = score; lit = s; }
                }
            }

            if (seen)
            {
                if (State != MonsterState.Attack) GameManager.I.PlaySting(transform.position, AudioSynth.Screech, 0.9f);
                State = MonsterState.Attack;
                target = seen.Position;
                if (seenD < catchRange)
                {
                    punchUntil = Time.time + 0.75f;
                    GameManager.I.Grab(seen, this);
                }
                return;
            }

            var n = Noise.Latest;
            if (n.Time > lastHeard && Vector3.Distance(n.Pos, transform.position) < Noise.Radius(n.Level))
            {
                lastHeard = n.Time;
                LastHeardAt = Time.time;
                State = MonsterState.TrackNoise;
                target = n.Pos;
                repathAt = 0;
                return;
            }

            if (lit)
            {
                State = MonsterState.HuntLight;
                target = lit.Position;
                return;
            }

            // lost the light: carry on to where it was, then search
            if (State == MonsterState.HuntLight || State == MonsterState.Attack) Search(target);
        }

        void Search(Vector3 center)
        {
            State = MonsterState.Search;
            searchCenter = target = center;
            stateUntil = Time.time + searchDuration;
            repathAt = 0;
        }

        void Despawn()
        {
            despawning = true;
            State = MonsterState.Dormant;
        }

        void Move(float dt, float speedOverride = -1f)
        {
            float speed = speedOverride > 0 ? speedOverride : State switch
            {
                MonsterState.Attack => attackSpeed,
                MonsterState.TrackNoise => noiseSpeed,
                MonsterState.HuntLight => huntSpeed,
                _ => searchSpeed,
            };
            if (Time.time < punchUntil) speed = 0;
            if (Time.time >= repathAt)
            {
                path = Maze.Instance.Path(transform.position, target);
                repathAt = Time.time + (State == MonsterState.Attack || IsWatcher ? 0.3f : 0.7f);
            }
            if (path.Count == 0) { OnReached(); return; }
            var to = path[0] - transform.position;
            to.y = 0;
            if (to.magnitude < 0.25f)
            {
                path.RemoveAt(0);
                if (path.Count == 0) OnReached();
                return;
            }
            transform.position += Vector3.ClampMagnitude(to.normalized * speed * dt, to.magnitude);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1 - Mathf.Exp(-dt * 8f));
            speedNow = speed;
        }

        void OnReached()
        {
            switch (State)
            {
                case MonsterState.TrackNoise:
                case MonsterState.Attack:
                    Search(target);
                    break;
                case MonsterState.Search:
                    // wander: pick a cell within a couple of steps of the search centre
                    var mz = Maze.Instance;
                    var dist = mz.DistanceMap(mz.ToCell(searchCenter));
                    var options = new List<Vector2Int>();
                    for (int x = 0; x < mz.width; x++)
                        for (int y = 0; y < mz.height; y++)
                            if (dist[x, y] >= 0 && dist[x, y] <= 2) options.Add(new Vector2Int(x, y));
                    target = mz.Center(options[Random.Range(0, options.Count)]);
                    repathAt = 0;
                    break;
            }
        }

        void Animate()
        {
            if (!Simulated)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                speedNow = Mathf.Lerp(speedNow, Vector3.Distance(transform.position, lastPos) / dt, 0.2f);
                lastPos = transform.position;
            }
            string st = Time.time < punchUntil ? "Punch" : speedNow < 0.1f ? "Idle" : speedNow < 2.0f ? "Walk" : "Run";
            if (st != animState) { anim.CrossFadeInFixedTime(st, 0.2f); animState = st; }
            anim.speed = st == "Walk" ? speedNow / 1.4f : st == "Run" ? speedNow / 3.4f : 1f;
            if ((IsWatcher && Observed) || Stunned) anim.speed = 0f;   // statue / dazed
        }

        // ---------------- network mirror (client) ----------------

        public void Write(BinaryWriter w)
        {
            var p = transform.position;
            w.Write((byte)Id); w.Write(p.x); w.Write(p.y); w.Write(p.z);
            w.Write(transform.eulerAngles.y); w.Write((byte)State); w.Write(Sink); w.Write(Eyes);
            w.Write(Time.time < punchUntil);
        }

        public static void ReadAll(BinaryReader r)
        {
            int n = r.ReadByte();
            var alive = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadByte();
                var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float yaw = r.ReadSingle();
                var st = (MonsterState)r.ReadByte();
                float sink = r.ReadSingle(), eyes = r.ReadSingle();
                bool punch = r.ReadBoolean();
                alive.Add(id);
                var m = All.Find(x => x.Id == id);
                if (!m)
                {
                    m = Create(id);
                    m.Simulated = false;
                    m.transform.position = p;
                }
                if (st == MonsterState.TrackNoise && m.State != MonsterState.TrackNoise) LastHeardAt = Time.time;
                m.netPos = p; m.netYaw = yaw; m.State = st; m.Sink = sink; m.Eyes = eyes;
                if (punch) m.punchUntil = Time.time + 0.1f;
            }
            for (int i = All.Count - 1; i >= 0; i--)
                if (!alive.Contains(All[i].Id)) Destroy(All[i].gameObject);
        }

        void Mirror()
        {
            float k = 1 - Mathf.Exp(-Time.deltaTime * 10f);
            transform.position = Vector3.Lerp(transform.position, netPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, netYaw, 0), k);
        }
    }
}
