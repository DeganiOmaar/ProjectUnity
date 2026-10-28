using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Echoes
{
    // On-screen fallbacks for every sensor (GDD 6: "every mechanic needs a touch-button fallback").
    public static class TouchInput
    {
        public static Vector2 Move;
        public static bool Shout, Cover, Shake, Radio;
        static Vector2 look;
        public static void AddLook(Vector2 d) => look += d;
        public static Vector2 ConsumeLook() { var l = look; look = Vector2.zero; return l; }
        public static bool FlashPressed, ThrowPressed;   // one-shot taps (FLASH / THROW buttons, double-tap, flick)
        public static bool ConsumeFlash() { bool b = FlashPressed; FlashPressed = false; return b; }
        public static bool ConsumeThrow() { bool b = ThrowPressed; ThrowPressed = false; return b; }
    }

    [RequireComponent(typeof(UIDocument))]
    public class HUD : MonoBehaviour
    {
        enum Screen { None, Menu, Join, Lobby, Calibrate, Settings, Pause, End, Tutorial }
        public static bool AutoSolo;   // "play again" in solo skips the menu after the reload

        VisualElement app, hudLayer;
        VisualElement menu, join, lobby, calib, settings, pause, end, tutorial;
        VisualElement batteryFill, micFill, darkFill, calibFill, damage;
        Label batteryPct, micState, timer, objective, prompt, hint, chipVis, chipFrozen, chipShake, chipCover, chipFlash, chipBottles;
        VisualElement struggleBox, struggleFill, flash;
        Label calibTitle, calibSub, endTitle, endReason, endTime, lobbyStatus, sensorList, menuSensors;
        TextField ipField;
        Screen screen = (Screen)(-1), settingsBack = Screen.Menu;
        bool touchUI;

        void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var css = Resources.Load<StyleSheet>("HUD");
            if (css) root.styleSheets.Add(css);
            app = new VisualElement { pickingMode = PickingMode.Ignore };
            app.AddToClassList("root");
            root.Add(app);
            touchUI = Touchscreen.current != null || Application.isMobilePlatform;

            if (touchUI) app.Add(BuildLookArea());
            app.Add(hudLayer = BuildHud());
            app.Add(gameObject.AddComponent<TiltLock>().BuildUI());
            app.Add(M6_WalkieTalkie.I.BuildUI());
            flash = Div("flash");
            flash.pickingMode = PickingMode.Ignore;
            app.Add(flash);
            damage = Div("damage");
            damage.pickingMode = PickingMode.Ignore;
            app.Add(damage);
            app.Add(menu = BuildMenu());
            app.Add(join = BuildJoin());
            app.Add(lobby = BuildLobby());
            app.Add(calib = BuildCalibration());
            app.Add(settings = BuildSettings());
            app.Add(pause = BuildPause());
            app.Add(end = BuildEnd());
            app.Add(tutorial = BuildTutorial());

            if (AutoSolo) { AutoSolo = false; GameManager.I.StartSolo(); }
        }

        // ---------------- builders ----------------

        static VisualElement Div(params string[] classes)
        {
            var v = new VisualElement();
            foreach (var c in classes) v.AddToClassList(c);
            return v;
        }

        static Label Text(string t, params string[] classes)
        {
            var l = new Label(t);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        static Button Btn(string t, Action a, params string[] classes)
        {
            var b = new Button(a) { text = t };
            b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        VisualElement Bar(out VisualElement fill, string kind)
        {
            var bar = Div("bar");
            fill = Div("bar-fill", kind);
            bar.Add(fill);
            return bar;
        }

        VisualElement BuildHud()
        {
            var layer = Div("layer");
            layer.pickingMode = PickingMode.Ignore;

            var status = Div("panel", "status");
            var r1 = Div("row");
            r1.Add(Text("LIGHT", "k"));
            r1.Add(Bar(out batteryFill, "light"));
            r1.Add(batteryPct = Text("100%", "v"));
            status.Add(r1);
            var r2 = Div("row");
            r2.Add(Text("MIC", "k"));
            var micBar = Bar(out micFill, "mic");
            var m1 = M1_VoiceFlashlight.I;
            var noiseTick = Div("tick"); noiseTick.style.left = Length.Percent(m1.noiseThreshold * 100);
            var shoutTick = Div("tick", "shout"); shoutTick.style.left = Length.Percent(m1.shoutThreshold * 100);
            micBar.Add(noiseTick); micBar.Add(shoutTick);
            r2.Add(micBar);
            r2.Add(micState = Text("QUIET", "v"));
            status.Add(r2);
            var r3 = Div("row");
            r3.Add(Text("DARK", "k"));
            r3.Add(Bar(out darkFill, "dark"));
            r3.Add(Text("", "v"));
            status.Add(r3);
            var chips = Div("chips");
            chips.Add(chipVis = Text("VISIBLE", "chip"));
            chips.Add(chipFrozen = Text("FROZEN", "chip"));
            chips.Add(chipCover = Text("COVERED", "chip"));
            chips.Add(chipShake = Text("SHAKING", "chip"));
            chips.Add(chipFlash = Text("FLASH READY", "chip"));
            chips.Add(chipBottles = Text("BOTTLES 0", "chip"));
            status.Add(chips);
            layer.Add(status);

            var top = Div("top-center");
            top.Add(timer = Text("00:00", "timer"));
            top.Add(objective = Text("", "objective"));
            layer.Add(top);

            var compassHost = Div("compass-host");
            compassHost.Add(M5_HauntedCompass.I.BuildUI());
            if (touchUI)
            {
                var row = Div("corner-buttons");
                row.Add(Btn("RECENTER", () => M3_GyroLook.I.Recenter(), "small"));
                row.Add(Btn("PAUSE", () => GameManager.I.SetPaused(true), "small"));
                compassHost.Add(row);
            }
            layer.Add(compassHost);

            layer.Add(prompt = Text("", "prompt"));
            struggleBox = Div("struggle");
            struggleBox.Add(Bar(out struggleFill, "light"));
            layer.Add(struggleBox);
            layer.Add(hint = Text(touchUI ? "" : "WASD move  ·  mouse look  ·  SPACE shout  ·  C cover  ·  R shake  ·  V radio  ·  F flash  ·  T throw  ·  Q recenter  ·  ESC pause", "hint"));

            if (touchUI)
            {
                layer.Add(BuildJoystick());
                var actions = Div("actions");
                actions.Add(Hold("SHOUT", v => TouchInput.Shout = v));
                actions.Add(Hold("COVER", v => TouchInput.Cover = v));
                actions.Add(Hold("SHAKE", v => TouchInput.Shake = v));
                actions.Add(Hold("RADIO", v => TouchInput.Radio = v));
                actions.Add(Tap("FLASH", () => TouchInput.FlashPressed = true));
                actions.Add(Tap("THROW", () => TouchInput.ThrowPressed = true));
                layer.Add(actions);
            }
            return layer;
        }

        VisualElement Hold(string label, Action<bool> set)
        {
            var b = Text(label, "hold-btn");
            b.pickingMode = PickingMode.Position;
            b.RegisterCallback<PointerDownEvent>(e => { b.CapturePointer(e.pointerId); set(true); b.AddToClassList("held"); });
            void Up(int id) { if (b.HasPointerCapture(id)) b.ReleasePointer(id); set(false); b.RemoveFromClassList("held"); }
            b.RegisterCallback<PointerUpEvent>(e => Up(e.pointerId));
            b.RegisterCallback<PointerCancelEvent>(e => Up(e.pointerId));
            return b;
        }

        VisualElement Tap(string label, Action onTap)
        {
            var b = Text(label, "hold-btn");
            b.pickingMode = PickingMode.Position;
            b.RegisterCallback<PointerDownEvent>(e => { onTap(); b.AddToClassList("held"); });
            b.RegisterCallback<PointerUpEvent>(e => b.RemoveFromClassList("held"));
            b.RegisterCallback<PointerLeaveEvent>(e => b.RemoveFromClassList("held"));
            return b;
        }

        VisualElement BuildJoystick()
        {
            var area = Div("joy-area");
            var baseEl = Div("joy-base");
            var knob = Div("joy-knob");
            baseEl.Add(knob);
            area.Add(baseEl);
            int id = -1;
            Vector2 center = default;
            const float R = 60f;
            area.RegisterCallback<PointerDownEvent>(e =>
            {
                id = e.pointerId;
                area.CapturePointer(id);
                center = e.localPosition;
                baseEl.style.left = center.x - 70;
                baseEl.style.top = center.y - 70;
                baseEl.AddToClassList("active");
            });
            area.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != id) return;
                var d = Vector2.ClampMagnitude((Vector2)e.localPosition - center, R);
                knob.style.translate = new Translate(d.x, d.y);
                TouchInput.Move = new Vector2(d.x, -d.y) / R;
            });
            void Release(int pid)
            {
                if (pid != id) return;
                if (area.HasPointerCapture(pid)) area.ReleasePointer(pid);
                id = -1;
                TouchInput.Move = Vector2.zero;
                knob.style.translate = new Translate(0, 0);
                baseEl.RemoveFromClassList("active");
            }
            area.RegisterCallback<PointerUpEvent>(e => Release(e.pointerId));
            area.RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId));
            return area;
        }

        VisualElement BuildLookArea()
        {
            // drag anywhere else to look (gyro fallback, or fine-tuning on top of the gyro)
            var area = Div("look-area");
            int id = -1;
            float lastTap = -1f, downAt = 0f;
            Vector2 lastTapPos = default, downPos = default;
            area.RegisterCallback<PointerDownEvent>(e =>
            {
                id = e.pointerId;
                area.CapturePointer(id);
                // double-tap = flash burst
                if (Time.unscaledTime - lastTap < 0.3f && ((Vector2)e.position - lastTapPos).magnitude < 80f) TouchInput.FlashPressed = true;
                lastTap = Time.unscaledTime;
                lastTapPos = downPos = e.position;
                downAt = Time.unscaledTime;
            });
            area.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == id) TouchInput.AddLook(new Vector2(e.deltaPosition.x, -e.deltaPosition.y)); });
            area.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != id) return;
                area.ReleasePointer(id);
                id = -1;
                // quick upward flick = throw a bottle
                if (Time.unscaledTime - downAt < 0.3f && downPos.y - e.position.y > 140f) TouchInput.ThrowPressed = true;
            });
            return area;
        }

        VisualElement BuildMenu()
        {
            var s = Div("screen", "menu");
            s.Add(Text("ECHOES IN THE DARK", "title"));
            s.Add(Text("Darkness spawns it.   Light attracts it.   Noise reveals you.", "tagline"));
            var col = Div("menu-buttons");
            col.Add(Btn("PLAY SOLO", () => GameManager.I.StartSolo(), "primary"));
            col.Add(Btn("HOST CO-OP", () => GameManager.I.StartHost()));
            col.Add(Btn("JOIN CO-OP", () => Show(Screen.Join)));
            col.Add(Btn("HOW TO PLAY", () => Show(Screen.Tutorial)));
            col.Add(Btn("SETTINGS", () => { settingsBack = Screen.Menu; Show(Screen.Settings); }));
            s.Add(col);
            s.Add(menuSensors = Text("", "foot"));
            s.Add(Text("Best played in a dark room with headphones.  2 players: same Wi-Fi.", "foot", "dim"));
            return s;
        }

        VisualElement BuildJoin()
        {
            var s = Div("screen");
            var card = Div("card");
            card.Add(Text("JOIN CO-OP", "card-title"));
            card.Add(Text("Both phones on the same Wi-Fi. Leave empty to find the host automatically, or type the host's IP (Android emulator: 10.0.2.2).", "card-sub"));
            ipField = new TextField { value = Settings.LastIP };
            ipField.AddToClassList("field");
            card.Add(ipField);
            card.Add(Btn("CONNECT", () => GameManager.I.StartJoin(ipField.value), "primary"));
            card.Add(Btn("BACK", () => Show(Screen.Menu)));
            s.Add(card);
            return s;
        }

        VisualElement BuildLobby()
        {
            var s = Div("screen");
            var card = Div("card");
            card.Add(Text("CO-OP", "card-title"));
            card.Add(lobbyStatus = Text("", "card-sub"));
            card.Add(Text("You spawn at opposite ends of the maze. Find each other at the beacon and escape together.", "card-sub", "dim"));
            card.Add(Btn("CANCEL", () => GameManager.I.CancelLobby()));
            s.Add(card);
            return s;
        }

        VisualElement BuildCalibration()
        {
            var s = Div("screen", "calib");
            var card = Div("card");
            card.Add(Text("SENSOR CALIBRATION", "card-kicker"));
            card.Add(calibTitle = Text("", "card-title"));
            card.Add(calibSub = Text("", "card-sub"));
            card.Add(Bar(out calibFill, "light"));
            card.Add(Btn("SKIP", () => GameManager.I.SkipCalibration(), "small"));
            s.Add(card);
            return s;
        }

        VisualElement BuildSettings()
        {
            var s = Div("screen");
            var card = Div("card", "wide");
            card.Add(Text("SETTINGS", "card-title"));
            var look = new Slider("Gyro / touch look sensitivity", 0.3f, 2.5f) { value = Settings.LookSensitivity };
            look.RegisterValueChangedCallback(e => Settings.LookSensitivity = e.newValue);
            var mouse = new Slider("Mouse sensitivity", 0.2f, 3f) { value = Settings.MouseSensitivity };
            mouse.RegisterValueChangedCallback(e => Settings.MouseSensitivity = e.newValue);
            var vol = new Slider("Volume", 0f, 1f) { value = Settings.Volume };
            vol.RegisterValueChangedCallback(e => Settings.Volume = e.newValue);
            var inv = new Toggle("Invert look Y") { value = Settings.InvertY };
            inv.RegisterValueChangedCallback(e => Settings.InvertY = e.newValue);
            foreach (var c in new VisualElement[] { look, mouse, vol, inv }) { c.AddToClassList("setting"); card.Add(c); }
            card.Add(Btn("RECENTER VIEW", () => M3_GyroLook.I.Recenter(), "small"));
            card.Add(Text("SENSORS ON THIS DEVICE", "card-kicker"));
            card.Add(sensorList = Text("", "sensors"));
            card.Add(Btn("BACK", () => Show(settingsBack)));
            s.Add(card);
            return s;
        }

        VisualElement BuildPause()
        {
            var s = Div("screen");
            var card = Div("card");
            card.Add(Text("PAUSED", "card-title"));
            card.Add(Btn("RESUME", () => GameManager.I.SetPaused(false), "primary"));
            card.Add(Btn("SETTINGS", () => { settingsBack = Screen.Pause; Show(Screen.Settings); }));
            card.Add(Btn("QUIT TO MENU", () => { Time.timeScale = 1; GameManager.I.Restart(); }));
            s.Add(card);
            return s;
        }

        VisualElement BuildEnd()
        {
            var s = Div("screen", "end");
            s.Add(endTitle = Text("", "title"));
            s.Add(endReason = Text("", "tagline"));
            s.Add(endTime = Text("", "foot"));
            var col = Div("menu-buttons");
            col.Add(Btn("PLAY AGAIN", () => { AutoSolo = !GameManager.I.Coop && GameManager.I.net == null; GameManager.I.Restart(); }, "primary"));
            col.Add(Btn("MAIN MENU", () => GameManager.I.Restart()));
            s.Add(col);
            return s;
        }

        VisualElement BuildTutorial()
        {
            var s = Div("screen");
            var card = Div("card", "wide");
            card.Add(Text("HOW TO SURVIVE", "card-title"));
            card.Add(Text("Darkness spawns it.  Light attracts it.  Noise reveals you.", "card-sub"));
            var grid = Div("tips");
            void Tip(string head, string body)
            {
                var t = Div("tip");
                t.Add(Text(head, "tip-head"));
                t.Add(Text(body, "tip-body"));
                grid.Add(t);
            }
            Tip(touchUI ? "SHOUT" : "SHOUT  ·  SPACE", "Flashlight flickering? Shout to recharge it. The monster hears every shout.");
            Tip(touchUI ? "COVER" : "COVER  ·  C", "Cover the light sensor (top of the phone) to dim your light and hide. Stay dark too long and the dark spawns it.");
            Tip(touchUI ? "SHAKE / FREEZE" : "SHAKE / FREEZE  ·  R", "Shake to recharge silently. When it is close, hold perfectly still.");
            Tip(touchUI ? "RADIO" : "RADIO  ·  V", "Phone to your ear: pings lead to the exit hatch and the radio names the nearest fuse. You can't see while listening.");
            Tip("FUSES", "The hatch has no power. Find 3 glowing fuses under the red lamps. The compass only hints roughly where the nearest one is.");
            Tip(touchUI ? "FLASH  ·  DOUBLE-TAP" : "FLASH  ·  F", "Full battery? Fire a blinding flash that stuns it for a few seconds. It empties your light.");
            Tip(touchUI ? "BOTTLES  ·  FLICK UP" : "BOTTLES  ·  T", "Pick up bottles and throw one far away. The crash lures it to the noise.");
            Tip("GRABBED  /  THE PALE ONE", "Grabbed? Shake or shout to break free. The pale one only moves when you are not looking at it.");
            card.Add(grid);
            card.Add(Text("Move the phone to look around (RECENTER resets). Sealed doors open when you shake or shout at them. With power on, tilt the phone at the hatch to roll the key into the lock.", "card-sub", "dim"));
            card.Add(Btn("GOT IT", () => { Settings.SeenTutorial = true; Show(Screen.Menu); }, "primary"));
            s.Add(card);
            return s;
        }

        // ---------------- called by GameManager ----------------

        public void ShowCalibration(bool on) { if (on) Show(Screen.Calibrate); else Show(Screen.None); }

        public void SetCalibration(string title, string sub, float progress)
        {
            if (title != null) calibTitle.text = title;
            if (sub != null) calibSub.text = sub;
            calibFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
        }

        public void ShowEnd(bool won, string reason, float time)
        {
            endTitle.text = won ? "YOU ESCAPED" : "CAUGHT";
            endTitle.EnableInClassList("danger", !won);
            endReason.text = reason;
            endTime.text = $"Survived {(int)(time / 60):00}:{(int)(time % 60):00}";
            Show(Screen.End);
        }

        void Show(Screen s)
        {
            if (screen == s) return;
            screen = s;
            void Vis(VisualElement v, bool on) => v.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            Vis(menu, s == Screen.Menu);
            Vis(join, s == Screen.Join);
            Vis(lobby, s == Screen.Lobby);
            Vis(calib, s == Screen.Calibrate);
            Vis(settings, s == Screen.Settings);
            Vis(pause, s == Screen.Pause);
            Vis(end, s == Screen.End);
            Vis(tutorial, s == Screen.Tutorial);
            if (s == Screen.Settings) sensorList.text = SensorReport();
        }

        // ---------------- per frame ----------------

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || hudLayer == null) return;

            switch (gm.phase)
            {
                case Phase.Menu:
                    if (screen is not (Screen.Menu or Screen.Join or Screen.Settings or Screen.Tutorial))
                        Show(Settings.SeenTutorial ? Screen.Menu : Screen.Tutorial);
                    menuSensors.text = SensorSummary();
                    break;
                case Phase.Lobby:
                    Show(Screen.Lobby);
                    lobbyStatus.text = gm.net ? gm.net.Status : "";
                    break;
                case Phase.Calibrating:
                    Show(Screen.Calibrate);
                    break;
                case Phase.Playing:
                    if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) gm.SetPaused(!gm.Paused);
                    if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) M3_GyroLook.I.Recenter();
                    if (gm.Paused) { if (screen is not (Screen.Pause or Screen.Settings)) Show(Screen.Pause); }
                    else Show(Screen.None);
                    break;
                case Phase.Ended:
                    Show(Screen.End);
                    break;
            }
            bool showHud = gm.phase == Phase.Playing && (screen is Screen.None or Screen.Pause or Screen.Settings);
            hudLayer.style.display = showHud ? DisplayStyle.Flex : DisplayStyle.None;
            if (gm.phase == Phase.Playing) UpdateHud(gm);
        }

        void UpdateHud(GameManager gm)
        {
            var s = Survivor.Local;
            var torch = s.GetComponent<Flashlight>();
            var m1 = M1_VoiceFlashlight.I;
            var m2 = M2_HideInDarkness.I;

            batteryFill.style.width = Length.Percent(s.Battery * 100f);
            batteryFill.EnableInClassList("flicker", torch.Flickering);
            batteryFill.EnableInClassList("charging", torch.Recharging);
            batteryPct.text = Mathf.RoundToInt(s.Battery * 100) + "%";
            micFill.style.width = Length.Percent(s.Loudness * 100f);
            micState.text = s.Loudness > m1.shoutThreshold ? "SHOUT" : s.Loudness > m1.noiseThreshold ? "HEARD" : m1.Silent ? "SILENT" : "QUIET";
            micState.EnableInClassList("danger", s.Loudness > m1.noiseThreshold);
            darkFill.style.width = Length.Percent(m2.Danger01 * 100f);

            chipVis.text = s.Hidden ? "HIDDEN" : s.Visibility > 0.5f ? "EXPOSED" : "VISIBLE";
            chipVis.EnableInClassList("on", s.Hidden);
            chipVis.EnableInClassList("danger", !s.Hidden && s.Visibility > 0.5f);
            chipFrozen.EnableInClassList("on", s.Frozen);
            chipCover.EnableInClassList("on", s.Cover > 0.5f);
            chipShake.EnableInClassList("on", s.Shaking);
            chipFlash.EnableInClassList("ready", FlashBurst.I && FlashBurst.I.Ready);
            int bottles = Lures.I ? Lures.I.Count : 0;
            chipBottles.text = "BOTTLES " + bottles;
            chipBottles.EnableInClassList("on", bottles > 0);

            float t = gm.RoundTime;
            timer.text = $"{(int)(t / 60):00}:{(int)(t % 60):00}";
            var p = gm.Partner;
            objective.text = p ? "Meet your partner at the beacon"
                : gm.FusesNeeded == 0 ? "Reach the beacon"
                : gm.HatchPowered ? "Hatch powered  -  find it with the RADIO"
                : $"Find the fuses  {gm.FusesFound} / {gm.FusesNeeded}";

            // the pale Watcher and the ordinary stalkers need opposite advice
            float near = float.MaxValue, watcherDist = float.MaxValue;
            Monster watcher = null;
            foreach (var m in Monster.All)
            {
                if (m.Sink > 0.5f) continue;
                float d = Vector3.Distance(m.transform.position, s.Position);
                if (m.IsWatcher) { watcher = m; watcherDist = d; } else near = Mathf.Min(near, d);
            }
            string msg = "";
            bool danger = false;
            struggleBox.style.display = Struggle.Active ? DisplayStyle.Flex : DisplayStyle.None;
            if (Struggle.Active) struggleFill.style.width = Length.Percent(Struggle.I.Progress * 100f);
            if (s.Downed) { msg = gm.Coop ? "YOU'RE DOWN  -  YOUR PARTNER CAN REVIVE YOU" : "YOU'RE DOWN"; danger = true; }
            else if (Struggle.Active) { msg = "IT'S GOT YOU!  SHAKE OR SHOUT TO BREAK FREE"; danger = true; }
            else if (watcher && watcherDist < 14f && watcher.Observed) msg = "DON'T LOOK AWAY...";
            else if (watcher && watcherDist < 9f) { msg = "SOMETHING IS RIGHT BEHIND YOU"; danger = true; }
            else if (near < M4_ShakeFreeze.I.monsterWarnRange) { msg = s.Hidden ? "HIDDEN...  DON'T MOVE" : "IT'S CLOSE  -  FREEZE, DIM, SILENCE"; danger = !s.Hidden; }
            else if (Time.time - Monster.LastHeardAt < 2.5f) { msg = "IT HEARD SOMETHING"; danger = true; }
            else if (TiltLock.Active) msg = "";
            else if (Time.time - gm.FuseFoundAt < 2.5f) msg = gm.HatchPowered ? "ALL FUSES FOUND  -  THE HATCH HAS POWER" : $"FUSE FOUND  {gm.FusesFound} / {gm.FusesNeeded}";
            else if (gm.OnMeetingPoint(s) && !gm.HatchPowered) msg = $"NO POWER  -  FIND THE FUSES  {gm.FusesFound} / {gm.FusesNeeded}";
            else if (gm.OnMeetingPoint(s) && p && !gm.OnMeetingPoint(p)) msg = "WAIT FOR YOUR PARTNER AT THE BEACON";
            else if (SealedDoor.Nearest(s.Position, out float doorDist) is { } door && doorDist < SealedDoor.Range)
                msg = (p ? "SEALED DOOR  -  SHAKE OR SHOUT TOGETHER" : "SEALED DOOR  -  SHAKE OR SHOUT TO FORCE IT") + (door.Charge > 0.02f ? $"  {Mathf.RoundToInt(door.Charge * 100)}%" : "");
            else if (p && p.Downed) msg = gm.ReviveProgress > 0 ? $"REVIVING  {Mathf.RoundToInt(gm.ReviveProgress * 100)}%" : "YOUR PARTNER IS DOWN  -  GO TO THEM";
            else if (torch.Flickering) msg = "FLASHLIGHT FAILING  -  SHOUT OR SHAKE";
            else if (m2.Danger01 > 0.5f) { msg = "THE DARK IS WATCHING  -  LIGHT UP"; danger = true; }
            else if (s.Battery < 0.2f) msg = "LOW LIGHT";
            prompt.text = msg;
            prompt.EnableInClassList("danger", danger);
            prompt.EnableInClassList("pulse", danger && Heartbeat.Pulse > 0.5f);

            damage.style.opacity = s.Downed ? 0.85f : Struggle.Active ? 0.6f : Heartbeat.Fear * 0.22f * (0.5f + 0.5f * Heartbeat.Pulse);
            flash.style.opacity = Mathf.Clamp01(1f - (Time.time - FlashBurst.FiredAt) / 0.6f);
        }

        // GDD polish: a device testing matrix right in the settings screen
        static string SensorReport()
        {
            string Y(bool ok, string fallback) => ok ? "OK" : "missing  ->  " + fallback;
            return
                $"Microphone (M1)        {Y(M1_VoiceFlashlight.I.HasMic, "SPACE / SHOUT button")}\n" +
                $"Light sensor (M2)      {M2_HideInDarkness.I.ActiveSource switch { M2_HideInDarkness.Source.LightSensor => "OK", M2_HideInDarkness.Source.FrontCamera => "front camera fallback", _ => "missing  ->  C / COVER button" }}\n" +
                $"Gyroscope (M3)         {(M3_GyroLook.I.HasAttitude ? "OK (attitude)" : M3_GyroLook.I.HasGyroRate ? "OK (rate)" : "missing  ->  mouse / drag")}\n" +
                $"Accelerometer (M4)     {Y(M4_ShakeFreeze.I.HasAccelerometer, "R / SHAKE button")}\n" +
                $"Compass (M5)           {Y(M5_HauntedCompass.RealHeading.HasValue, "maze north")}\n" +
                $"Proximity (M6)         {Y(M6_WalkieTalkie.I.HasProximity, "V / RADIO button")}\n" +
                $"Vibration              {(Application.isMobilePlatform ? "OK" : "missing  ->  audio heartbeat")}";
        }

        static string SensorSummary()
        {
            string D(bool ok, string n) => ok ? n : n + " (fallback)";
            return string.Join("    ",
                D(M1_VoiceFlashlight.I.HasMic, "MIC"),
                D(M2_HideInDarkness.I.ActiveSource != M2_HideInDarkness.Source.Manual, "LIGHT"),
                D(M3_GyroLook.I.UseGyro, "GYRO"),
                D(M4_ShakeFreeze.I.HasAccelerometer, "ACCEL"),
                D(M5_HauntedCompass.RealHeading.HasValue, "COMPASS"),
                D(M6_WalkieTalkie.I.HasProximity, "PROXIMITY"));
        }
    }
}
