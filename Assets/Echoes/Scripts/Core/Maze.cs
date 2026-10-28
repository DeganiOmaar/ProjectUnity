using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    // Seeded procedural maze (both phones build the same one from the host's seed).
    // +Z is north, +X is east, so the compass maps straight onto the grid.
    public class Maze : MonoBehaviour
    {
        public static Maze Instance;

        public int width = 11, height = 11;
        public float cell = 4f, wallHeight = 3.2f, wallThickness = 0.35f;
        [Range(0, 0.4f)] public float loops = 0.12f;
        public Material wallMat, floorMat, ceilingMat, beaconMat, lampMat;

        static readonly Vector2Int[] Dir = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) }; // N E S W
        int[,] walls;
        readonly List<Light> lamps = new();
        Light beaconLight;
        Transform beacon;
        Material beaconInst;
        public int FuseCount { get; private set; }

        public Vector2Int MeetingCell => new(width / 2, height / 2);
        public Vector3 MeetingPoint => Center(MeetingCell);
        public Vector2Int SpawnCell(int id) => id == 0 ? new Vector2Int(0, 0) : new Vector2Int(width - 1, height - 1);

        void Awake()
        {
            Instance = this;
            foreach (var m in new[] { beaconMat, lampMat }) if (m) m.EnableKeyword("_EMISSION");
        }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;
        readonly Dictionary<(Vector2Int, int), SealedDoor> doorEdges = new();
        public bool Open(Vector2Int c, int d) =>
            (walls[c.x, c.y] & (1 << d)) == 0 && !(doorEdges.TryGetValue((c, d), out var door) && door && !door.IsOpen);
        public Vector3 Center(Vector2Int c) => new((c.x + 0.5f) * cell, 0, (c.y + 0.5f) * cell);
        public Vector2Int ToCell(Vector3 p) =>
            new(Mathf.Clamp(Mathf.FloorToInt(p.x / cell), 0, width - 1), Mathf.Clamp(Mathf.FloorToInt(p.z / cell), 0, height - 1));

        void Carve(Vector2Int c, int d)
        {
            var n = c + Dir[d];
            walls[c.x, c.y] &= ~(1 << d);
            walls[n.x, n.y] &= ~(1 << ((d + 2) % 4));
        }

        public void Generate(int seed)
        {
            var rng = new System.Random(seed);
            doorEdges.Clear();
            walls = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) walls[x, y] = 15;

            // recursive backtracker
            var seen = new bool[width, height];
            var stack = new Stack<Vector2Int>();
            stack.Push(Vector2Int.zero);
            seen[0, 0] = true;
            var options = new List<int>(4);
            while (stack.Count > 0)
            {
                var c = stack.Peek();
                options.Clear();
                for (int d = 0; d < 4; d++)
                {
                    var n = c + Dir[d];
                    if (InBounds(n) && !seen[n.x, n.y]) options.Add(d);
                }
                if (options.Count == 0) { stack.Pop(); continue; }
                int pick = options[rng.Next(options.Count)];
                Carve(c, pick);
                var next = c + Dir[pick];
                seen[next.x, next.y] = true;
                stack.Push(next);
            }

            // knock out a few extra walls so the monster can circle around you
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    for (int d = 0; d < 2; d++)
                    {
                        var c = new Vector2Int(x, y);
                        if (InBounds(c + Dir[d]) && !Open(c, d) && rng.NextDouble() < loops) Carve(c, d);
                    }

            // the meeting point is a small open plaza
            for (int d = 0; d < 4; d++) Carve(MeetingCell, d);

            Build(rng);
        }

        void Build(System.Random rng)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            lamps.Clear();

            float w = width * cell, h = height * cell;
            var floor = Block("Floor", new Vector3(w / 2, -0.1f, h / 2), new Vector3(w, 0.2f, h), floorMat, new Vector2(w / 3f, h / 3f));
            floor.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ceiling = Block("Ceiling", new Vector3(w / 2, wallHeight + 0.1f, h / 2), new Vector3(w, 0.2f, h), ceilingMat, new Vector2(w / 4f, h / 4f));
            ceiling.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var wallTiling = new Vector2(cell / 2.5f, wallHeight / 2.5f);
            float t = wallThickness, len = cell + t, y = wallHeight / 2;
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    var c = new Vector2Int(x, z);
                    if (!Open(c, 2)) Block("Wall", new Vector3((x + 0.5f) * cell, y, z * cell), new Vector3(len, wallHeight, t), wallMat, wallTiling);
                    if (!Open(c, 3)) Block("Wall", new Vector3(x * cell, y, (z + 0.5f) * cell), new Vector3(t, wallHeight, len), wallMat, wallTiling);
                    if (z == height - 1 && !Open(c, 0)) Block("Wall", new Vector3((x + 0.5f) * cell, y, (z + 1) * cell), new Vector3(len, wallHeight, t), wallMat, wallTiling);
                    if (x == width - 1 && !Open(c, 1)) Block("Wall", new Vector3((x + 1) * cell, y, (z + 0.5f) * cell), new Vector3(t, wallHeight, len), wallMat, wallTiling);
                }

            BuildMeetingPoint();
            BuildDoors();

            // a few dying emergency lamps in dead ends: atmosphere, not safety
            var deadEnds = new List<Vector2Int>();
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                {
                    var c = new Vector2Int(x, z);
                    int open = 0;
                    for (int d = 0; d < 4; d++) if (Open(c, d)) open++;
                    if (open == 1 && c != SpawnCell(0) && c != SpawnCell(1)) deadEnds.Add(c);
                }
            var lampCells = new List<Vector2Int>();
            for (int i = 0; i < 5 && deadEnds.Count > 0; i++)
            {
                int k = rng.Next(deadEnds.Count);
                var c = deadEnds[k];
                deadEnds.RemoveAt(k);
                lampCells.Add(c);
                int d = 0;
                while (!Open(c, d)) d = (d + 1) % 4; // the only way out of the dead end
                var back = -new Vector3(Dir[d].x, 0, Dir[d].y);
                var pos = Center(c) + back * (cell / 2 - t / 2 - 0.08f) + Vector3.up * (wallHeight - 0.5f);
                var lamp = Block("Lamp", pos, new Vector3(0.5f, 0.12f, 0.12f), lampMat, Vector2.one);
                lamp.transform.rotation = Quaternion.LookRotation(back == Vector3.zero ? Vector3.forward : back);
                Kill(lamp.GetComponent<Collider>());
                var l = new GameObject("LampLight").AddComponent<Light>();
                l.transform.SetParent(transform);
                l.transform.position = pos - back * 0.4f;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.12f, 0.08f);
                l.range = 6f;
                l.shadows = LightShadows.None;
                lamps.Add(l);
            }

            StaticBatchingUtility.Combine(gameObject);
            BuildPickups(rng, lampCells);
        }

        // solo objective: 3 fuses under the red lamps (furthest from the start first), and a few bottles to throw.
        // Co-op keeps its original rules, so no fuses there.
        void BuildPickups(System.Random rng, List<Vector2Int> lampCells)
        {
            FuseCount = 0;
            bool coop = GameManager.I && GameManager.I.net != null;
            var dist = DistanceMap(SpawnCell(0));
            lampCells.Sort((a, b) => dist[b.x, b.y].CompareTo(dist[a.x, a.y]));
            var used = new HashSet<Vector2Int> { SpawnCell(0), SpawnCell(1), MeetingCell };
            if (!coop)
                foreach (var c in lampCells)
                {
                    if (FuseCount == 3) break;
                    Fuse.Create(transform, Center(c), lampMat);
                    used.Add(c);
                    FuseCount++;
                }
            for (int placed = 0, guard = 0; placed < 6 && guard < 200; guard++)
            {
                var c = new Vector2Int(rng.Next(width), rng.Next(height));
                if (used.Contains(c) || dist[c.x, c.y] < 2) continue;
                used.Add(c);
                var off = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 1.6f;
                Bottle.Make(Center(c) + off + Vector3.up * 0.15f, lampMat).transform.SetParent(transform, true);
                placed++;
            }
        }

        void BuildMeetingPoint()
        {
            var p = MeetingPoint;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "MeetingRing";
            Kill(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform);
            ring.transform.position = p + Vector3.up * 0.01f;
            ring.transform.localScale = new Vector3(3.2f, 0.005f, 3.2f);
            beaconInst = beaconMat ? new Material(beaconMat) : null;
            if (beaconInst) beaconInst.EnableKeyword("_EMISSION");
            ring.GetComponent<Renderer>().sharedMaterial = beaconInst;

            beacon = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            beacon.name = "Beacon";
            Kill(beacon.GetComponent<Collider>());
            beacon.SetParent(transform);
            beacon.position = p + Vector3.up * 1.3f;
            beacon.localScale = Vector3.one * 0.28f;
            beacon.GetComponent<Renderer>().sharedMaterial = beaconInst;

            beaconLight = new GameObject("BeaconLight").AddComponent<Light>();
            beaconLight.transform.SetParent(transform);
            beaconLight.transform.position = p + Vector3.up * 1.6f;
            beaconLight.type = LightType.Point;
            beaconLight.color = new Color(1f, 0.62f, 0.2f);
            beaconLight.range = 7f;
            beaconLight.shadows = LightShadows.None;
        }

        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        // one sealed door on each spawn's shortest route to the meeting point (co-op synchronised action)
        void BuildDoors()
        {
            var dist = DistanceMap(MeetingCell);
            var edges = new List<(Vector2Int, int)>();
            for (int id = 0; id < 2; id++)
            {
                var c = SpawnCell(id);
                var route = new List<Vector2Int> { c };
                for (int guard = 0; c != MeetingCell && guard < width * height; guard++)
                    for (int d = 0; d < 4; d++)
                    {
                        var n = c + Dir[d];
                        if (Open(c, d) && InBounds(n) && dist[n.x, n.y] == dist[c.x, c.y] - 1) { c = n; route.Add(c); break; }
                    }
                if (route.Count < 6) continue;
                int k = Mathf.Clamp(Mathf.RoundToInt(route.Count * 0.55f), 2, route.Count - 3);
                var a = route[k];
                int dir = System.Array.IndexOf(Dir, route[k + 1] - a);
                if (!edges.Contains((a, dir)) && !edges.Contains((a + Dir[dir], (dir + 2) % 4))) edges.Add((a, dir));
            }
            for (int i = 0; i < edges.Count; i++) PlaceDoor(i, edges[i].Item1, edges[i].Item2);
        }

        void PlaceDoor(int index, Vector2Int c, int d)
        {
            var dirV = new Vector3(Dir[d].x, 0, Dir[d].y);
            var root = new GameObject("SealedDoor " + index).transform;
            root.SetParent(transform);
            root.position = Center(c) + dirV * (cell / 2);
            root.rotation = Quaternion.LookRotation(dirV);
            var slab = new GameObject("Slab").transform;
            slab.SetParent(root, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "DoorBody";
            body.transform.SetParent(slab, false);
            body.transform.localPosition = new Vector3(0, wallHeight / 2, 0);
            body.transform.localScale = new Vector3(cell - wallThickness * 0.5f, wallHeight, 0.25f);
            body.GetComponent<Renderer>().sharedMaterial = TiledMaterial(ceilingMat, new Vector2(cell / 2.5f, wallHeight / 2.5f));

            var glow = lampMat ? new Material(lampMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            glow.EnableKeyword("_EMISSION");
            foreach (var (pos, size) in new[] { (new Vector3(0, 1.7f, 0), new Vector3(cell * 0.55f, 0.1f, 0.3f)), (new Vector3(0, 1.7f, 0), new Vector3(0.1f, 1.2f, 0.3f)) })
            {
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Seal";
                Kill(strip.GetComponent<Collider>());
                strip.transform.SetParent(slab, false);
                strip.transform.localPosition = pos;
                strip.transform.localScale = size;
                strip.GetComponent<Renderer>().sharedMaterial = glow;
            }

            var door = root.gameObject.AddComponent<SealedDoor>();
            door.Index = index; door.Cell = c; door.Dir = d;
            door.Init(slab, glow);
            doorEdges[(c, d)] = door;
            doorEdges[(c + Dir[d], (d + 2) % 4)] = door;
        }

        GameObject Block(string n, Vector3 pos, Vector3 size, Material mat, Vector2 tiling)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = n;
            g.isStatic = true;
            g.transform.SetParent(transform);
            g.transform.position = pos;
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = TiledMaterial(mat, tiling);
            return g;
        }

        readonly Dictionary<(Material, Vector2), Material> tiled = new();
        Material TiledMaterial(Material m, Vector2 tiling)
        {
            if (!m || tiling == Vector2.one) return m;
            if (!tiled.TryGetValue((m, tiling), out var t))
            {
                t = new Material(m) { mainTextureScale = tiling };
                foreach (var prop in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" })
                    if (t.HasProperty(prop)) t.SetTextureScale(prop, tiling);
                tiled[(m, tiling)] = t;
            }
            return t;
        }

        void Update()
        {
            if (beacon)
            {
                beacon.Rotate(20f * Time.deltaTime, 45f * Time.deltaTime, 0);
                beacon.position = MeetingPoint + Vector3.up * (1.3f + Mathf.Sin(Time.time * 1.3f) * 0.1f);
                // no power: a weak red glow. Powered (all fuses in): pulsing amber
                bool powered = !GameManager.I || GameManager.I.HatchPowered;
                beaconLight.color = powered ? new Color(1f, 0.62f, 0.2f) : new Color(1f, 0.15f, 0.1f);
                beaconLight.intensity = powered ? 5f + Mathf.Sin(Time.time * 2.2f) * 1.5f : 1.2f + Mathf.PerlinNoise(Time.time * 4f, 0) * 0.8f;
                if (beaconInst) beaconInst.SetColor("_EmissionColor", powered ? new Color(1f, 0.6f, 0.2f) * 1.8f : new Color(1f, 0.1f, 0.06f) * 0.8f);
            }
            for (int i = 0; i < lamps.Count; i++)
            {
                float n = Mathf.PerlinNoise(Time.time * 6f, i * 3.3f);
                lamps[i].intensity = n < 0.3f ? 0f : n * 4f;
            }
        }

        // ---------- pathfinding (BFS on the grid; the maze is tiny) ----------

        public int[,] DistanceMap(Vector2Int from)
        {
            var dist = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) dist[x, y] = -1;
            var q = new Queue<Vector2Int>();
            dist[from.x, from.y] = 0;
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!Open(c, d)) continue;
                    var n = c + Dir[d];
                    if (!InBounds(n) || dist[n.x, n.y] >= 0) continue;
                    dist[n.x, n.y] = dist[c.x, c.y] + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }

        // Waypoints (cell centres) from 'from' to 'to', ending exactly at 'to'.
        public List<Vector3> Path(Vector3 from, Vector3 to)
        {
            var path = new List<Vector3>();
            var goal = ToCell(to);
            var dist = DistanceMap(goal);
            var c = ToCell(from);
            int guard = width * height;
            while (c != goal && guard-- > 0)
            {
                bool moved = false;
                for (int d = 0; d < 4; d++)
                {
                    var n = c + Dir[d];
                    if (Open(c, d) && InBounds(n) && dist[n.x, n.y] >= 0 && dist[n.x, n.y] == dist[c.x, c.y] - 1)
                    {
                        c = n;
                        path.Add(Center(c));
                        moved = true;
                        break;
                    }
                }
                if (!moved) break;
            }
            path.Add(to);
            return path;
        }

        public bool LineOfSight(Vector3 a, Vector3 b) =>
            !Physics.Linecast(a, b, out var hit, ~0, QueryTriggerInteraction.Ignore) || !hit.transform.IsChildOf(transform);

#if UNITY_EDITOR
        // ponytail: one runnable self-check for the generator + BFS (Echoes/Self-check menu).
        [UnityEditor.MenuItem("Echoes/Self-check maze")]
        static void SelfCheck()
        {
            var go = new GameObject("MazeCheck");
            var m = go.AddComponent<Maze>();
            for (int seed = 1; seed < 20; seed++)
            {
                m.Generate(seed);
                Debug.Assert(m.doorEdges.Count >= 2, $"seed {seed}: no sealed doors placed");
                Debug.Assert(m.FuseCount == 3, $"seed {seed}: expected 3 fuses, got {m.FuseCount}");
                m.doorEdges.Clear(); // check the carved maze itself; doors are meant to block
                var dist = m.DistanceMap(Vector2Int.zero);
                for (int x = 0; x < m.width; x++)
                    for (int y = 0; y < m.height; y++)
                        Debug.Assert(dist[x, y] >= 0, $"seed {seed}: cell {x},{y} unreachable");
                var p = m.Path(m.Center(m.SpawnCell(0)), m.MeetingPoint);
                Debug.Assert(p.Count == dist[m.MeetingCell.x, m.MeetingCell.y] + 1, "path length mismatch");
            }
            DestroyImmediate(go);
            Debug.Log("Maze self-check passed");
        }
#endif
    }
}
