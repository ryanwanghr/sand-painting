using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    [RequireComponent(typeof(UIDocument), typeof(MandalaAudio))]
    public sealed class MandalaGame : MonoBehaviour
    {
        [SerializeField, Range(40, 100), Tooltip("Distance in UI panel points between a touch and the tool tip.")]
        float touchTipOffset = 64;
        public SandTray Tray { get; private set; }
        public MandalaProgress Progress { get; private set; }
        readonly Button[] swatches = new Button[SandTray.ColorCount];
        readonly VisualElement[] colorFills = new VisualElement[SandTray.ColorCount];
        SandTexture surface;
        CalmWaterBackground water;
        MandalaGuideLabels guideLabels;
        TrayGesture gesture;
        TrayToolTip toolTip;
        MandalaAudio audioPlayer;
        MandalaHaptics haptics;
        VisualElement root, hud, dialog, settings, menuDismiss, palette, progressPanel, pourFill, cleanup, cleanupFill;
        VisualElement paintingScreen, traySpace;
        SandStudioMenu studio;
        SandWork activeWork;
        Button menu, release;
        SandTool tool;
        RakePattern rakePattern;
        RakePatternKind rakePatternKind;
        int color, lastRendered = -1;
        bool guides = true, down, saveFailed, loading = true, suctionRendered, airborneRendered;
        Vector2 current, previous;
        float accumulator, nextProgress, nextRender, nextSurfaceRender, animateUntil;
        bool dirty, initialWon;
        int initialChanges;
        Task saveTask = Task.CompletedTask;
        SandTray.Snapshot queuedSave;
        Action confirmation;
        Rect lastSafeArea;
        int lastWidth, lastHeight, loadGeneration;
        Image trayImage;
        const float SimulationStep = 1f / 60;

        void OnEnable()
        {
            Application.targetFrameRate = 60;
            audioPlayer = GetComponent<MandalaAudio>();
            audioPlayer.SetEnabled(SandStudioMenu.SoundEnabled);
            haptics = new MandalaHaptics();
            root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            GetComponent<UIDocument>().visualTreeAsset.CloneTree(root);
            var designs = Resources.LoadAll<SandDesign>(SandDesign.ResourceFolder)
                .Where(design => !string.IsNullOrEmpty(design.Id))
                .GroupBy(design => design.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(design => design.Id, StringComparer.Ordinal)
                .ToArray();
            studio = new SandStudioMenu(root.Q("studioScreen"), designs,
                new SandWorks(Application.persistentDataPath, designs), audioPlayer, OpenPainting);
            paintingScreen = root.Q("paintingScreen");
            BindPainting();
            studio.Show();
        }

        void BindPainting()
        {
            trayImage = root.Q<Image>("tray");
            trayImage.scaleMode = ScaleMode.StretchToFill;
            traySpace = root.Q("traySpace");
            toolTip = new TrayToolTip();
            hud = root.Q("safeArea");
            hud.parent.Insert(hud.parent.IndexOf(hud), toolTip);
            dialog = root.Q("dialog");
            menu = root.Q<Button>("menu");
            settings = root.Q("settings");
            menuDismiss = root.Q("menuDismiss");
            palette = root.Q("palette");
            progressPanel = root.Q("progress");
            pourFill = root.Q("pourFill");
            cleanup = root.Q("cleanup");
            cleanupFill = root.Q("cleanupFill");
            release = root.Q<Button>("release");
            AddToolPicture("pour", "Pour", SandTool.Pour);
            AddToolPicture("vacuum", "Vacuum", SandTool.Vacuum);
            AddToolPicture("sweep", "Sweep", SandTool.Sweep);
            AddToolPicture("rake", "Rake", SandTool.Rake);
            for (int i = 0; i < swatches.Length; i++)
            {
                int choice = i;
                var swatch = swatches[i] = root.Q<Button>("color" + i);
                var chip = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore };
                chip.AddToClassList("swatch-chip");
                var meter = new VisualElement { pickingMode = PickingMode.Ignore };
                meter.AddToClassList("meter");
                meter.AddToClassList("swatch-meter");
                var fill = colorFills[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("meter-fill");
                meter.Add(fill);
                swatch.Add(chip);
                swatch.Add(meter);
                swatch.clicked += () =>
                {
                    color = choice;
                    Choose(SandTool.Pour);
                    surface.Highlight(color, Time.unscaledTime);
                };
            }
            Bind("pour", () => Choose(SandTool.Pour)); Bind("vacuum", () => Choose(SandTool.Vacuum)); Bind("sweep", () => Choose(SandTool.Sweep)); Bind("rake", () => Choose(SandTool.Rake));
            Bind("rakeSpiral", () => StartRakePattern(RakePatternKind.Spiral));
            Bind("rakePetals", () => StartRakePattern(RakePatternKind.Petals));
            Bind("rakeWaves", () => StartRakePattern(RakePatternKind.Waves));
            Bind("rakeStop", () => StopRakePattern());
            menu.clicked += () => SetMenu(settings.ClassListContains("hidden"));
            menuDismiss.RegisterCallback<PointerDownEvent>(e => SetMenu(false));
            Bind("studioBack", ReturnToStudio);
            Bind("guides", () =>
            {
                guides = !guides;
                SandStudioMenu.GuidesEnabled = guides;
                lastRendered = -1;
                Select();
            });
            Bind("sound", () => { SandStudioMenu.SetSound(audioPlayer, !audioPlayer.Enabled); Select(); });
            Bind("waterAnimation", () =>
            {
                water.Animate = !water.Animate;
                SandStudioMenu.WaterAnimationEnabled = water.Animate;
                Select();
            });
            Bind("clear", () => ShowDialog("A fresh tray?", "Remove all sand and start over?", "Clear tray", "Keep painting", ClearTray));
            release.clicked += () =>
            {
                if (CanLetGo)
                    ShowDialog("Let go", "Sweep or vacuum across an edge to release the sand there.", "Release sand", "Keep painting", ReleaseSand);
            };
            Bind("confirm", () => { dialog.AddToClassList("hidden"); var action = confirmation; confirmation = null; action?.Invoke(); });
            Bind("cancel", () => { dialog.AddToClassList("hidden"); confirmation = null; });
        }

        void OpenPainting(SandWork work)
        {
            if (activeWork != null) return;
            sbyte[] guide;
            try { guide = work.Design.BuildGuide(); }
            catch (Exception error)
            {
                studio.ShowError("This design could not be opened.");
                Debug.LogWarning($"Sand design {work.Design.Title}: {error.Message}");
                return;
            }

            activeWork = work;
            loading = true; down = false; guides = SandStudioMenu.GuidesEnabled; tool = SandTool.Pour;
            suctionRendered = airborneRendered = dirty = saveFailed = false;
            rakePattern = null;
            lastRendered = -1; accumulator = nextProgress = nextRender = nextSurfaceRender = animateUntil = 0;
            lastSafeArea = default; lastWidth = lastHeight = 0; confirmation = null;
            saveTask = Task.CompletedTask;
            Tray = new SandTray(Environment.TickCount, guide);
            Progress = new MandalaProgress(Tray);
            color = Enumerable.Range(0, SandTray.ColorCount).First(Progress.UsesColor);
            surface = new SandTexture(Progress.Sections.Count, mobileSurface: Application.isMobilePlatform);
            audioPlayer.ResetPattern();
            water = new CalmWaterBackground(root.Q<Image>("water")) { Animate = SandStudioMenu.WaterAnimationEnabled };
            trayImage.image = surface.Texture;
            gesture = new TrayGesture(trayImage, touchTipOffset) { Begin = BeginStroke, Move = p => current = p, End = EndStroke };
            traySpace.AddManipulator(gesture);
            guideLabels = new MandalaGuideLabels(trayImage, Tray, Progress);
            traySpace.RegisterCallback<GeometryChangedEvent>(ResizeTray);
            dialog.AddToClassList("hidden");
            SetMenu(false);
            studio.Hide();
            paintingScreen.RemoveFromClassList("hidden");
            Select();
            RefreshProgress();
            Load(work);
        }

        void ReturnToStudio()
        {
            ClosePainting();
            studio.Show();
        }
        void Bind(string name, Action action) => root.Q<Button>(name).clicked += action;
        void AddToolPicture(string name, string title, SandTool value)
        {
            var button = root.Q<Button>(name);
            button.text = "";
            button.AddToClassList("tool-button");
            button.Add(new TrayToolIcon(value));
            var caption = new Label(title) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("tool-caption");
            button.Add(caption);
        }
        async void Load(SandWork work)
        {
            int generation = ++loadGeneration;
            try
            {
                var state = await Task.Run(work.Load);
                if (this == null || !isActiveAndEnabled || generation != loadGeneration) return;
                if (state != null) Tray.Restore(state);
            }
            catch (Exception error)
            {
                if (this != null && generation == loadGeneration) Debug.LogWarning("Mandala load: " + error.Message);
            }
            finally
            {
                if (this != null && isActiveAndEnabled && generation == loadGeneration)
                {
                    loading = false;
                    guides = Tray.Protection && SandStudioMenu.GuidesEnabled;
                    if (!Tray.Protection) color = 0;
                    initialChanges = Tray.Changes;
                    initialWon = Tray.HasWon;
                    Progress.Update(Tray, Time.unscaledTime, false);
                    ArchiveIfMissing();
                    lastRendered = -1;
                    Select();
                    RefreshProgress();
                }
            }
        }
        void ResizeTray(GeometryChangedEvent e)
        {
            Rect space = e.newRect;
            float gutter = space.height >= space.width || Application.isMobilePlatform ? 2 * touchTipOffset : 0;
            gesture.Resize(space.size, gutter);
        }
        void SafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe == lastSafeArea && lastWidth == Screen.width && lastHeight == Screen.height) return;
            lastSafeArea = safe; lastWidth = Screen.width; lastHeight = Screen.height;
            float scale = root.resolvedStyle.width / Mathf.Max(1, Screen.width);
            if (!float.IsFinite(scale)) scale = 1;
            hud.style.left = 10 + safe.xMin * scale;
            hud.style.right = 10 + (Screen.width - safe.xMax) * scale;
            hud.style.top = 10 + (Screen.height - safe.yMax) * scale;
            hud.style.bottom = 10 + safe.yMin * scale;
            hud.EnableInClassList("wide", safe.width >= safe.height * 1.45f);
            hud.EnableInClassList("compact", safe.height * scale < 220);
        }
        void Choose(SandTool value)
        {
            gesture.CancelAll(); StopRakePattern(); tool = value; Select();
            haptics.Select();
        }
        void SetMenu(bool open)
        {
            if (open) gesture.CancelAll();
            settings.EnableInClassList("hidden", !open);
            menuDismiss.EnableInClassList("hidden", !open);
            menu.text = open ? "Close" : "Menu";
        }
        void Select()
        {
            for (int i = 0; i < swatches.Length; i++)
                swatches[i].EnableInClassList("selected", tool == SandTool.Pour && color == i);
            root.Q<Button>("pour").EnableInClassList("selected", tool == SandTool.Pour);
            root.Q<Button>("vacuum").EnableInClassList("selected", tool == SandTool.Vacuum);
            root.Q<Button>("sweep").EnableInClassList("selected", tool == SandTool.Sweep);
            root.Q<Button>("rake").EnableInClassList("selected", tool == SandTool.Rake);
            root.Q("rakePatterns").EnableInClassList("hidden", tool != SandTool.Rake);
            root.Q<Button>("rakeSpiral").EnableInClassList("selected", rakePattern != null && rakePatternKind == RakePatternKind.Spiral);
            root.Q<Button>("rakePetals").EnableInClassList("selected", rakePattern != null && rakePatternKind == RakePatternKind.Petals);
            root.Q<Button>("rakeWaves").EnableInClassList("selected", rakePattern != null && rakePatternKind == RakePatternKind.Waves);
            root.Q<Button>("rakeStop").SetEnabled(rakePattern != null);
            root.Q<Button>("guides").text = guides ? "Guides on" : "Guides off";
            root.Q<Button>("sound").text = audioPlayer.Enabled ? "Sound on" : "Sound off";
            root.Q<Button>("waterAnimation").text = water.Animate ? "Animated water" : "Still water";
        }
        bool CanLetGo => !loading && Tray.Protection && Tray.HasWon && Progress.PaintingComplete && Progress.Clean
            && !down && rakePattern == null && Tray.Grains.Count == 0 && Tray.RollingGrains.Count == 0;

        void RefreshProgress()
        {
            progressPanel.EnableInClassList("hidden", loading || !Tray.Protection);
            pourFill.style.width = Length.Percent(100 * Progress.PouringProgress);
            cleanup.EnableInClassList("hidden", !Progress.AllComplete);
            cleanupFill.style.width = Length.Percent(100 * Progress.CleanupProgress);
            release.EnableInClassList("hidden", !CanLetGo);
            palette.EnableInClassList("free-paint", !Tray.Protection);
            for (int i = 0; i < swatches.Length; i++)
            {
                swatches[i].EnableInClassList("hidden", Tray.Protection && !Progress.UsesColor(i));
                float value = Tray.Protection ? Progress.ColorProgress(i) : 0;
                colorFills[i].style.width = Length.Percent(100 * value);
                bool done = value >= 1;
                swatches[i].EnableInClassList("complete", done);
                swatches[i].EnableInClassList("unfinished", Tray.Protection && !done);
            }
            int slot = 0;
            foreach (var swatch in swatches)
            {
                if (swatch.ClassListContains("complete")) continue;
                if (palette[slot] != swatch) swatch.PlaceBehind(palette[slot]);
                slot++;
            }
            foreach (var swatch in swatches)
            {
                if (!swatch.ClassListContains("complete")) continue;
                if (palette[slot] != swatch) swatch.PlaceBehind(palette[slot]);
                slot++;
            }
        }
        public void BeginStroke(Vector2 point)
        {
            if (loading || !dialog.ClassListContains("hidden")) return;
            StopRakePattern();
            previous = current = point; down = true;
        }
        public void MoveStroke(Vector2 point) => current = point;
        public void EndStroke() { if (!down) return; down = false; Tray.EndStroke(); QueueSave(); }
        void Changed() { Progress.Update(Tray, Time.unscaledTime, false); lastRendered = -1; QueueSave(); Select(); RefreshProgress(); }
        void ClearTray()
        {
            if (!Tray.Protection) guides = SandStudioMenu.GuidesEnabled;
            if (!Progress.UsesColor(color)) color = Enumerable.Range(0, SandTray.ColorCount).First(Progress.UsesColor);
            Tray.Clear();
            audioPlayer.ResetPattern();
            Changed();
        }
        void ReleaseSand()
        {
            Progress.Update(Tray, Time.unscaledTime, false);
            if (!CanLetGo) return;
            ArchiveIfMissing();
            Tray.ReleaseColors();
            guides = false;
            tool = SandTool.Sweep;
            Changed();
        }
        void ShowDialog(string title, string body, string accept, string cancel, Action action)
        {
            if (loading) return; gesture.CancelAll(); StopRakePattern(); SetMenu(false); confirmation = action;
            root.Q<Label>("dialogTitle").text = title; root.Q<Label>("dialogText").text = body;
            root.Q<Button>("confirm").text = accept; root.Q<Button>("cancel").text = cancel; dialog.RemoveFromClassList("hidden");
        }
        public void StartRakePattern(RakePatternKind kind)
        {
            if (loading || !dialog.ClassListContains("hidden")) return;
            if (Tray.Mass() == 0) return;
            gesture.ResetView(); StopRakePattern();
            tool = SandTool.Rake; Tray.EndStroke();
            rakePatternKind = kind; rakePattern = new RakePattern(kind);
            previous = current = rakePattern.Position;
            Select();
            haptics.Select();
        }
        public void StopRakePattern()
        {
            if (rakePattern == null) return;
            rakePattern = null; Tray.EndStroke(); QueueSave(); Select();
        }
        Vector2 RakeTipPosition()
        {
            Rect view = gesture.View;
            return new Vector2((current.x / SandTray.Size - view.x) / view.width * trayImage.contentRect.width,
                (current.y / SandTray.Size - (1 - view.yMax)) / view.height * trayImage.contentRect.height);
        }
        void UpdateToolTip()
        {
            toolTip.UpdateTip(trayImage.ChangeCoordinatesTo(toolTip, rakePattern != null ? RakeTipPosition() : gesture.TipPosition),
                ToolRadius() * PixelsPerCell(),
                touchTipOffset, tool, SandTexture.Palette[color], (rakePattern != null || gesture.ToolVisible) && dialog.ClassListContains("hidden") && settings.ClassListContains("hidden"),
                down || rakePattern != null, Tray.RakeAngle);
        }
        void Update()
        {
            if (activeWork == null) { studio?.Update(); return; }
            float now = Time.unscaledTime;
            water?.Render(now, gesture.View, gesture.Zoom);
            SafeArea(); if (loading) return;
            float pixelsPerCell = PixelsPerCell();
            Vector2? outletOffset = null;
            if (tool == SandTool.Pour)
            {
                UpdateToolTip();
                if (down && pixelsPerCell > 0) outletOffset = toolTip.PourOutletOffset / pixelsPerCell;
            }
            int activity = 0;
            accumulator += Mathf.Min(Time.unscaledDeltaTime, .05f);
            while (accumulator >= SimulationStep)
            {
                if (rakePattern != null)
                {
                    current = rakePattern.Advance(90f * SandTray.GridScale / 60);
                    activity += Tray.Apply(SandTool.Rake, previous, current, ToolRadius(), color, SimulationStep); previous = current;
                    if (rakePattern.Complete) StopRakePattern();
                }
                else if (down)
                {
                    activity += Tray.Apply(tool, previous, current, ToolRadius(), color, SimulationStep, outletOffset); previous = current;
                }
                bool airborne = Tray.Grains.Count > 0 || Tray.RollingGrains.Count > 0;
                Tray.Step(SimulationStep);
                if (airborne && Tray.Grains.Count == 0 && Tray.RollingGrains.Count == 0 && !down) QueueSave();
                accumulator -= SimulationStep;
            }
            if (down) haptics.Tool(tool, activity, now);
            if (tool != SandTool.Pour) UpdateToolTip();
            Rect view = gesture.View;
            Vector2 cellOrigin = trayImage.ChangeCoordinatesTo(toolTip, trayImage.contentRect.min)
                - new Vector2(view.xMin, 1 - view.yMax) * (SandTray.Size * pixelsPerCell);
            toolTip.UpdatePourStream(Tray, cellOrigin, pixelsPerCell);
            if (now >= nextProgress)
            {
                int finished = Progress.Update(Tray, now); nextProgress = now + .1f;
                if (finished > 0 && Tray.Protection) { QueueSave(); audioPlayer.Celebrate(finished); animateUntil = now + .75f; }
                if (Progress.CheckWin(Tray, now, down || rakePattern != null))
                {
                    Archive();
                    audioPlayer.CompletePainting(); QueueSave();
                }
                RefreshProgress();
            }
            float panelWidth = root.resolvedStyle.width;
            float pixelsPerPoint = panelWidth > 0 ? Screen.width / panelWidth : 1;
            Vector2 viewport = trayImage.contentRect.size;
            if (surface.SetViewport(viewport.x * pixelsPerPoint, gesture.View, viewport.y * pixelsPerPoint))
                trayImage.image = surface.Texture;
            guideLabels.Update(gesture.View, viewport, guides);
            bool suction = Tray.SuctionGrains.Count > 0;
            bool rolling = Tray.RollingGrains.Count > 0;
            bool falling = Tray.Grains.Count > 0 || rolling;
            // View changes and moving grains can redraw using the cached surface fields.
            // Landed grains and settling changes still refresh those fields immediately.
            bool updateSurface = lastRendered != Tray.Changes || (animateUntil > 0 && now >= nextSurfaceRender);
            if (surface.NeedsRedraw || updateSurface || rolling || suction || suctionRendered ||
                (now >= nextRender && (down || rakePattern != null || falling || airborneRendered || surface.HighlightActive)))
            {
                surface.Render(Tray, Progress, guides, now, updateSurface, accumulator / SimulationStep);
                if (updateSurface)
                {
                    lastRendered = Tray.Changes;
                    nextSurfaceRender = now + 1f / (suction ? 60 : 30);
                    if (now >= animateUntil) animateUntil = 0;
                }
                nextRender = now + 1f / (suction || falling || (down && tool == SandTool.Pour) ? 60 : 30);
                suctionRendered = suction; // Clear the final grain frame after the release tail ends.
                airborneRendered = falling;
            }
            audioPlayer.Tool(tool, down || rakePattern != null || Tray.Grains.Count > 0, activity / (120f * SandTray.AreaScale) + (Tray.Grains.Count > 0 ? .3f : 0));
            PumpSave();
        }
        float ToolRadius() => tool == SandTool.Pour ? SandTray.PourRadius
            : tool == SandTool.Vacuum ? 32 * SandTray.GridScale
            : tool == SandTool.Rake ? 24 * SandTray.GridScale
            : 32 / Mathf.Max(1f / SandTray.Size, PixelsPerCell());
        float PixelsPerCell() => trayImage.contentRect.width / (SandTray.Size * gesture.View.width);
        void ArchiveIfMissing()
        {
            if (!activeWork.HasImage && Tray.Protection && Tray.HasWon && Progress.PaintingComplete && Progress.Clean)
                Archive();
        }

        void Archive()
        {
            try { activeWork.SaveImage(SandArtPreview.Capture(Tray)); }
            catch (Exception error) { Debug.LogWarning("Sand painting archive: " + error.Message); }
        }

        void QueueSave()
        {
            if (loading || (Tray.Changes == initialChanges && Tray.HasWon == initialWon)) return;
            queuedSave = Tray.Capture();
            dirty = true;
        }
        void PumpSave()
        {
            if (!saveTask.IsCompleted) return;
            if (saveTask.IsFaulted && !saveFailed)
            {
                saveFailed = true; Debug.LogWarning("Mandala save: " + saveTask.Exception?.GetBaseException().Message);
            }
            if (queuedSave == null) return;
            var snapshot = queuedSave; queuedSave = null; saveFailed = false;
            string path = activeWork.SavePath;
            saveTask = Task.Run(() => MandalaSave.Write(path, snapshot));
        }
        void FlushSave()
        {
            if (loading || activeWork == null) return;
            gesture.CancelAll(); StopRakePattern();
            ArchiveIfMissing();
            if (!dirty) return;
            try { saveTask.GetAwaiter().GetResult(); } catch (Exception error) { Debug.LogWarning("Mandala previous save: " + error.Message); }
            try
            {
                MandalaSave.Write(activeWork.SavePath, Tray.Capture());
                queuedSave = null;
                initialChanges = Tray.Changes;
                initialWon = Tray.HasWon;
                dirty = false;
            }
            catch (Exception error) { Debug.LogWarning("Mandala save: " + error.Message); }
        }
        void OnApplicationPause(bool paused) { if (paused) FlushSave(); }
        void OnApplicationFocus(bool focused) { if (!focused) FlushSave(); }
        void OnApplicationQuit() => FlushSave();
        void ClosePainting()
        {
            if (activeWork == null) return;
            FlushSave();
            loadGeneration++;
            traySpace.UnregisterCallback<GeometryChangedEvent>(ResizeTray);
            traySpace.RemoveManipulator(gesture);
            trayImage.Clear();
            trayImage.image = null;
            water.Dispose();
            surface.Dispose();
            audioPlayer.Tool(tool, false, 0);
            audioPlayer.ResetPattern();
            paintingScreen.AddToClassList("hidden");
            activeWork = null;
            queuedSave = null;
            Tray = null;
            Progress = null;
            water = null;
            surface = null;
            guideLabels = null;
            gesture = null;
        }

        void OnDisable()
        {
            ClosePainting();
            studio?.Dispose();
            studio = null;
        }
    }
}
