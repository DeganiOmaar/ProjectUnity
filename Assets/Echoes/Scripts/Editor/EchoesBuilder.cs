using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Echoes.EditorTools
{
    // One-click setup: materials from the downloaded textures, animator controllers, post-processing,
    // UI panel, URP + player settings and the game scene. Re-run it any time (Echoes/Build Game Scene).
    public static class EchoesBuilder
    {
        const string Root = "Assets/Echoes";
        const string Gen = Root + "/Generated";
        const string Tex = Root + "/Art/Textures";
        const string ScenePath = Root + "/Scenes/Echoes.unity";
        const string ZombiePath = Root + "/Art/Models/Monster_Zombie.glb";
        const string PartnerPath = Root + "/Art/Models/Partner_Hoodie.glb";

        [MenuItem("Echoes/Build Game Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Gen);
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();

            var wall = SurfaceMaterial("brick_wall_006", "M_Wall", new Color(0.82f, 0.78f, 0.74f), 1.2f);
            var floor = SurfaceMaterial("concrete_floor_worn_001", "M_Floor", new Color(0.75f, 0.74f, 0.72f), 1f);
            var ceiling = SurfaceMaterial("dirty_concrete", "M_Ceiling", new Color(0.55f, 0.55f, 0.55f), 1f);
            var skin = Lit("M_MonsterSkin", new Color(0.2f, 0.21f, 0.19f), 0.35f);
            skin.SetTexture("_BaseMap", AssetDatabase.LoadAllAssetsAtPath(ZombiePath).OfType<Texture2D>().FirstOrDefault());
            var eye = Emissive("M_MonsterEye", new Color(1f, 0.05f, 0.02f) * 6f);
            var beacon = Emissive("M_Beacon", new Color(1f, 0.6f, 0.2f) * 4f);
            var lamp = Emissive("M_Lamp", new Color(1f, 0.1f, 0.06f) * 3f);
            var cookie = FlashlightCookie();

            var monsterCtrl = Controller("Monster", ZombiePath, "Idle", "Walk", "Run", "Punch");
            var partnerCtrl = Controller("Partner", PartnerPath, "Idle", "Walk", "Run", "Death");
            var profile = PostProfile();
            var panel = Panel();
            ConfigureUrp();
            ConfigurePlayer();

            // ---------------- scene ----------------
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.014f, 0.014f, 0.02f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.075f;
            RenderSettings.fogColor = new Color(0.006f, 0.006f, 0.009f);
            RenderSettings.reflectionIntensity = 0.15f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            var game = new GameObject("Game");
            var maze = game.AddComponent<Maze>();
            maze.wallMat = wall; maze.floorMat = floor; maze.ceilingMat = ceiling; maze.beaconMat = beacon; maze.lampMat = lamp;
            game.AddComponent<Heartbeat>();
            var gm = game.AddComponent<GameManager>();

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            var player = new GameObject("Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.875f, 0);
            var survivor = player.AddComponent<Survivor>();
            survivor.IsLocal = true;
            var head = new GameObject("Head");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = new Vector3(0, 1.6f, 0);
            var cam = head.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 70f;
            cam.fieldOfView = 70f;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            head.AddComponent<AudioListener>();

            var torch = new GameObject("Flashlight").AddComponent<Light>();
            torch.transform.SetParent(head.transform, false);
            torch.transform.localPosition = new Vector3(0.18f, -0.16f, 0.1f);
            torch.type = LightType.Spot;
            torch.range = 24f;
            torch.spotAngle = 62f;
            torch.innerSpotAngle = 24f;
            torch.color = new Color(1f, 0.93f, 0.82f);
            torch.intensity = 60f;
            torch.shadows = LightShadows.Soft;
            torch.shadowNearPlane = 0.2f;
            torch.cookie = cookie;
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.transform.SetParent(head.transform, false);
            fill.type = LightType.Point;
            fill.range = 3.5f;
            fill.intensity = 0.8f;
            fill.color = new Color(0.55f, 0.65f, 1f);
            fill.shadows = LightShadows.None;

            var pc = player.AddComponent<PlayerController>();
            pc.head = head.transform;
            var fl = player.AddComponent<Flashlight>();
            fl.spot = torch;
            player.AddComponent<M1_VoiceFlashlight>();
            player.AddComponent<M2_HideInDarkness>();
            player.AddComponent<M3_GyroLook>().cam = head.transform;
            player.AddComponent<M4_ShakeFreeze>();
            player.AddComponent<M5_HauntedCompass>();
            player.AddComponent<M6_WalkieTalkie>();

            var ui = new GameObject("HUD");
            ui.AddComponent<UIDocument>().panelSettings = panel;
            var hud = ui.AddComponent<HUD>();

            gm.maze = maze;
            gm.player = survivor;
            gm.hud = hud;
            gm.monsterModel = AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePath);
            gm.monsterController = monsterCtrl;
            gm.monsterSkin = skin;
            gm.eyeMaterial = eye;
            gm.partnerModel = AssetDatabase.LoadAssetAtPath<GameObject>(PartnerPath);
            gm.partnerController = partnerCtrl;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Echoes: scene built at " + ScenePath);
        }

        [MenuItem("Echoes/Build Android APK")]
        public static void BuildAndroid() => BuildAndroid(false);

        [MenuItem("Echoes/Build Android APK (Development)")]
        public static void BuildAndroidDev() => BuildAndroid(true);

        static void BuildAndroid(bool development)
        {
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/EchoesInTheDark.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"Echoes: Android build {sum.result} - {sum.totalSize / (1024f * 1024f):F1} MB, {sum.totalErrors} errors, {sum.totalTime.TotalSeconds:F0}s -> {sum.outputPath}");
        }

        // ---------------- materials ----------------

        static Material Lit(string name, Color color, float smoothness)
        {
            string path = $"{Gen}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Emissive(string name, Color hdr)
        {
            var m = Lit(name, Color.black, 0.2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None makes URP strip _EMISSION
            m.SetColor("_EmissionColor", hdr);
            return m;
        }

        static Material SurfaceMaterial(string id, string name, Color tint, float normalStrength)
        {
            string P(string kind) => $"{Tex}/{id}_{kind}.jpg";
            SetImport(P("nor_gl"), i => i.textureType = TextureImporterType.NormalMap);
            SetImport(P("ao"), i => i.sRGBTexture = false);
            var ms = SmoothnessFromRoughness(P("rough"), $"{Gen}/{id}_ms.png");

            var m = Lit(name, tint, 1f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("diff")));
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("nor_gl")));
            m.SetFloat("_BumpScale", normalStrength);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", ms);
            m.SetFloat("_SmoothnessTextureChannel", 0);
            m.SetFloat("_Smoothness", 0.85f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(P("ao")));
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetImport(string path, System.Action<TextureImporter> edit)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            edit(imp);
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
        }

        // URP packs smoothness into the alpha of the metallic map; Poly Haven ships roughness.
        static Texture2D SmoothnessFromRoughness(string roughPath, string outPath)
        {
            if (!File.Exists(outPath))
            {
                var src = new Texture2D(2, 2);
                src.LoadImage(File.ReadAllBytes(roughPath));
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, (byte)(255 - px[i].r));
                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                dst.SetPixels32(px);
                File.WriteAllBytes(outPath, dst.EncodeToPNG());
                AssetDatabase.ImportAsset(outPath);
            }
            SetImport(outPath, i => { i.sRGBTexture = false; i.alphaSource = TextureImporterAlphaSource.FromInput; });
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        // Dirty-lens torch cookie: hot centre, soft rings, a couple of smudges.
        static Texture2D FlashlightCookie()
        {
            string path = $"{Gen}/FlashlightCookie.png";
            if (!File.Exists(path))
            {
                const int N = 256;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float core = Mathf.Clamp01(1 - r * r) * 0.75f + Mathf.Exp(-r * r * 18f) * 0.35f;
                        float rings = 1f + 0.08f * Mathf.Sin(r * 46f) + 0.12f * Mathf.Exp(-Mathf.Pow((r - 0.62f) * 9f, 2));
                        float smudge = 1f - 0.25f * Mathf.PerlinNoise(x * 0.035f + 3.1f, y * 0.035f + 7.7f) * Mathf.Clamp01(r * 1.5f);
                        float edge = Mathf.Clamp01((1f - r) * 6f);
                        float g = Mathf.Clamp01(core * rings * smudge * edge);
                        tex.SetPixel(x, y, new Color(g, g, g, g));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
            }
            SetImport(path, i =>
            {
                i.textureType = TextureImporterType.Default;
                i.sRGBTexture = false;
                i.wrapMode = TextureWrapMode.Clamp;
                i.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                i.textureCompression = TextureImporterCompression.Uncompressed;
                i.mipmapEnabled = false;
            });
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------- animation ----------------

        static AnimatorController Controller(string name, string modelPath, params string[] states)
        {
            string path = $"{Gen}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().ToArray();
            foreach (var s in states)
            {
                var clip = clips.FirstOrDefault(c => c.name == s) ?? clips.FirstOrDefault(c => c.name.EndsWith("|" + s));
                if (!clip) Debug.LogWarning($"Echoes: no clip '{s}' in {modelPath}");
                var st = sm.AddState(s);
                st.motion = clip;
                if (s == states[0]) sm.defaultState = st;
            }
            return ctrl;
        }

        // ---------------- post-processing ----------------

        static VolumeProfile PostProfile()
        {
            string path = $"{Gen}/EchoesPost.asset";
            AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            T Add<T>() where T : VolumeComponent
            {
                var c = p.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, p);
                return c;
            }
            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>();
            bloom.threshold.Override(0.8f); bloom.intensity.Override(1.2f); bloom.scatter.Override(0.72f);
            var vig = Add<Vignette>();
            vig.intensity.Override(0.34f); vig.smoothness.Override(0.5f); vig.color.Override(Color.black);
            Add<ChromaticAberration>().intensity.Override(0.1f);
            var grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Medium3); grain.intensity.Override(0.3f); grain.response.Override(0.8f);
            var col = Add<ColorAdjustments>();
            col.postExposure.Override(1.3f); col.contrast.Override(10f); col.saturation.Override(-20f);
            col.colorFilter.Override(new Color(0.96f, 0.98f, 1f));
            Add<LensDistortion>().intensity.Override(0f);
            EditorUtility.SetDirty(p);
            return p;
        }

        // ---------------- UI ----------------

        static PanelSettings Panel()
        {
            string themePath = $"{Gen}/EchoesTheme.tss";
            File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\nVisualElement {}\n");
            AssetDatabase.ImportAsset(themePath);
            string path = $"{Gen}/EchoesPanel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (!ps) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, path); }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        // ---------------- project settings ----------------

        static void ConfigureUrp()
        {
            var assets = Enumerable.Range(0, QualitySettings.count).Select(QualitySettings.GetRenderPipelineAssetAt)
                .Append(GraphicsSettings.defaultRenderPipeline).OfType<UniversalRenderPipelineAsset>().Distinct();
            foreach (var a in assets)
            {
                var so = new SerializedObject(a);
                void Set(string prop, System.Action<SerializedProperty> f) { var p = so.FindProperty(prop); if (p != null) f(p); }
                Set("m_SupportsHDR", p => p.boolValue = true);
                Set("m_AdditionalLightsRenderingMode", p => p.intValue = 1);   // per pixel
                Set("m_AdditionalLightShadowsSupported", p => p.boolValue = true);
                Set("m_AdditionalLightsShadowmapResolution", p => p.intValue = 2048);
                Set("m_AdditionalLightsPerObjectLimit", p => p.intValue = 6);
                Set("m_SoftShadowsSupported", p => p.boolValue = true);
                so.ApplyModifiedProperties();
            }
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Echoes in the Dark";
            PlayerSettings.companyName = "Echoes Team";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.echoesteam.echoesinthedark");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.echoesteam.echoesinthedark");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.iOS.microphoneUsageDescription = "Shout to recharge your flashlight and talk to your partner on the radio.";
            PlayerSettings.iOS.cameraUsageDescription = "Cover the front camera to dim your flashlight and hide.";
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP); // ARM64 needs IL2CPP
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // App UI (pulled in by the AI Assistant editor tool) would start in the player and throw on touch devices
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            if (!defines.Contains("APP_UI_EDITOR_ONLY"))
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, (defines + ";APP_UI_EDITOR_ONLY").Trim(';'));
            PlayerSettings.runInBackground = true;

            // the compass (M5) uses Input.compass, so enable both input backends
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps)
            {
                var so = new SerializedObject(ps);
                var p = so.FindProperty("activeInputHandler");
                if (p != null && p.intValue != 2) { p.intValue = 2; so.ApplyModifiedProperties(); Debug.LogWarning("Echoes: input handling set to Both - restart the Editor."); }
            }
        }
    }
}
