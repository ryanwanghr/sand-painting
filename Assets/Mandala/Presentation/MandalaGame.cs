using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    [RequireComponent(typeof(UIDocument), typeof(MandalaAudio))]
    public sealed class MandalaGame : MonoBehaviour
    {
        [SerializeField, Range(40, 100), Tooltip("Touch clearance in UI panel points; also reserves the gutter below the tray.")]
        float touchTipOffset = 64;
        public SandTray Tray { get; private set; }
        public MandalaProgress Progress { get; private set; }
        readonly List<SandTray.Snapshot> undo = new List<SandTray.Snapshot>(8);
        SandTexture surface;
        MandalaGuideLabels guideLabels;
        TrayGesture gesture;
        TrayToolTip toolTip;
        MandalaAudio audioPlayer;
        VisualElement root, dialog;
        Label message, progressLabel;
        Button undoButton;
        SandTool tool;
        RakePattern rakePattern;
        RakePatternKind rakePatternKind;
        int color, lastRendered = -1;
        bool guides = true, down, saveFailed, loading = true, suctionRendered;
        Vector2 current, previous;
        float accumulator, nextProgress, nextRender, animateUntil;
        string savePath;
        Task saveTask = Task.CompletedTask;
        SandTray.Snapshot queuedSave;
        Action confirmation;
        Rect lastSafeArea;
        int lastWidth, lastHeight, loadGeneration;
        Image trayImage;

        void OnEnable()
        {
            Application.targetFrameRate = 60;
            loading = true; down = false; guides = true; tool = SandTool.Pour; color = 0; suctionRendered = false;
            rakePattern = null;
            undo.Clear(); lastRendered = -1; accumulator = nextProgress = nextRender = animateUntil = 0;
            lastSafeArea = default; lastWidth = lastHeight = 0; confirmation = null;
            savePath = Path.Combine(Application.persistentDataPath, "mandala-v1.sand");
            Tray = new SandTray(Environment.TickCount); Progress = new MandalaProgress(Tray); surface = new SandTexture(Progress.Sections.Count);
            audioPlayer = GetComponent<MandalaAudio>();
            audioPlayer.ResetPattern();
            root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            GetComponent<UIDocument>().visualTreeAsset.CloneTree(root);
            trayImage = root.Q<Image>("tray");
            trayImage.image = surface.Texture;
            trayImage.scaleMode = ScaleMode.StretchToFill;
            var traySpace = root.Q("traySpace");
            traySpace.style.paddingBottom = touchTipOffset;
            gesture = new TrayGesture(trayImage, touchTipOffset) { Begin = BeginStroke, Move = p => current = p, End = EndStroke };
            traySpace.AddManipulator(gesture);
            guideLabels = new MandalaGuideLabels(trayImage, Tray, Progress);
            toolTip = new TrayToolTip();
            var safeArea = root.Q("safeArea");
            safeArea.Insert(safeArea.IndexOf(root.Q("dialog")), toolTip);
            traySpace.RegisterCallback<GeometryChangedEvent>(ResizeTray);
            message = root.Q<Label>("message"); progressLabel = root.Q<Label>("progress"); dialog = root.Q("dialog");
            undoButton = root.Q<Button>("undo");
            AddToolPicture("pour", "Pour", SandTool.Pour);
            AddToolPicture("vacuum", "Vacuum", SandTool.Vacuum);
            AddToolPicture("sweep", "Sweep", SandTool.Sweep);
            AddToolPicture("rake", "Rake", SandTool.Rake);
            for (int i = 0; i < 5; i++) { int choice = i; root.Q<Button>("color" + i).clicked += () => { gesture.CancelAll(); StopRakePattern(); color = choice; surface.Highlight(color, Time.unscaledTime); Select(); }; }
            Bind("pour", () => Choose(SandTool.Pour)); Bind("vacuum", () => Choose(SandTool.Vacuum)); Bind("sweep", () => Choose(SandTool.Sweep)); Bind("rake", () => Choose(SandTool.Rake));
            Bind("rakeSpiral", () => StartRakePattern(RakePatternKind.Spiral));
            Bind("rakePetals", () => StartRakePattern(RakePatternKind.Petals));
            Bind("rakeWaves", () => StartRakePattern(RakePatternKind.Waves));
            Bind("rakeStop", () => StopRakePattern());
            Bind("undo", Undo);
            Bind("guides", () => { guides = !guides; lastRendered = -1; Select(); });
            Bind("sound", () => { audioPlayer.SetEnabled(!audioPlayer.Enabled); Select(); });
            Bind("resetView", () => { StopRakePattern(); gesture.ResetView(); });
            Bind("clear", () => ShowDialog("A fresh tray?", "Remove all sand. You can undo this.", "Clear tray", "Keep painting", () => { Remember(); Tray.Clear(); audioPlayer.ResetPattern(); Changed(); }));
            Bind("release", () => ShowDialog("Let it go", "Release the protected sections. Sweep, rake, vacuum, or pour freely.", "Release sand", "Keep painting", ReleaseSand));
            Bind("confirm", () => { dialog.AddToClassList("hidden"); var action = confirmation; confirmation = null; action?.Invoke(); });
            Bind("cancel", () => { dialog.AddToClassList("hidden"); confirmation = null; });
            Select(); Load();
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
        async void Load()
        {
            int generation = ++loadGeneration;
            try
            {
                var state = await Task.Run(() => {
                    if (!File.Exists(savePath)) return null;
                    try { return MandalaSave.Read(savePath); }
                    catch (Exception) { if (File.Exists(savePath + ".bak")) return MandalaSave.Read(savePath + ".bak"); throw; }
                });
                if (this == null || !isActiveAndEnabled || generation != loadGeneration) return;
                if (state != null) { Tray.Restore(state); message.text = "Welcome back. Your sand is just as you left it."; }
            }
            catch (Exception error) { if (this != null && generation == loadGeneration) { message.text = "Saved sand could not be loaded. A fresh tray is ready."; Debug.LogWarning("Mandala load: " + error.Message); } }
            finally { if (this != null && isActiveAndEnabled && generation == loadGeneration) { loading = false; Progress.Update(Tray, Time.unscaledTime, false); } }
        }
        void ResizeTray(GeometryChangedEvent e)
        {
            gesture.CancelAll();
            Rect space = root.Q("traySpace").contentRect;
            float size = Mathf.Max(1, Mathf.Min(space.width, space.height));
            // Geometry and safe-area insets are runtime measurements, not visual theme overrides.
            var image = root.Q<Image>("tray"); image.style.width = size; image.style.height = size;
        }
        void SafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe == lastSafeArea && lastWidth == Screen.width && lastHeight == Screen.height) return;
            lastSafeArea = safe; lastWidth = Screen.width; lastHeight = Screen.height;
            var area = root.Q("safeArea"); float scale = root.resolvedStyle.width / Mathf.Max(1, Screen.width);
            if (!float.IsFinite(scale)) scale = 1;
            area.style.paddingLeft = 16 + safe.xMin * scale;
            area.style.paddingRight = 16 + (Screen.width - safe.xMax) * scale;
            area.style.paddingTop = 12 + (Screen.height - safe.yMax) * scale;
            area.style.paddingBottom = 12 + safe.yMin * scale;
        }
        void Choose(SandTool value)
        {
            gesture.CancelAll(); StopRakePattern(); tool = value; Select();
            if (value == SandTool.Rake) message.text = "Drag to comb the sand, or choose a pattern. Pour sand first; Let go frees protected sections.";
        }
        void Select()
        {
            for (int i = 0; i < 5; i++) root.Q<Button>("color" + i).EnableInClassList("selected", color == i);
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
            undoButton.SetEnabled(undo.Count > 0);
        }
        public void BeginStroke(Vector2 point)
        {
            if (loading || !dialog.ClassListContains("hidden")) return;
            StopRakePattern();
            Remember(); previous = current = point; down = true;
        }
        public void MoveStroke(Vector2 point) => current = point;
        public void EndStroke() { if (!down) return; down = false; Tray.EndStroke(); QueueSave(); }
        void Remember() { if (undo.Count == 8) undo.RemoveAt(0); undo.Add(Tray.Capture()); Select(); }
        public void Undo()
        {
            gesture.CancelAll(); StopRakePattern(); if (loading || undo.Count == 0) return;
            Tray.Restore(undo[undo.Count - 1]); undo.RemoveAt(undo.Count - 1); Changed();
        }
        void Changed() { Progress.Update(Tray, Time.unscaledTime, false); lastRendered = -1; QueueSave(); Select(); }
        void ReleaseSand() { Remember(); Tray.Protection = false; guides = false; tool = SandTool.Sweep; message.text = "The tray is open. Undo restores its protection."; Changed(); }
        void ShowDialog(string title, string body, string accept, string cancel, Action action)
        {
            if (loading) return; gesture.CancelAll(); StopRakePattern(); confirmation = action;
            root.Q<Label>("dialogTitle").text = title; root.Q<Label>("dialogText").text = body;
            root.Q<Button>("confirm").text = accept; root.Q<Button>("cancel").text = cancel; dialog.RemoveFromClassList("hidden");
        }
        public void StartRakePattern(RakePatternKind kind)
        {
            if (loading || !dialog.ClassListContains("hidden")) return;
            if (Tray.Mass() == 0) { message.text = "Pour a layer of sand first, then choose a rake pattern."; return; }
            gesture.ResetView(); StopRakePattern();
            tool = SandTool.Rake; Remember(); Tray.EndStroke();
            rakePatternKind = kind; rakePattern = new RakePattern(kind);
            previous = current = rakePattern.Position;
            message.text = "The rake is drawing. Touch the sand to take over, or tap Stop. Undo restores the whole pattern.";
            Select();
        }
        public void StopRakePattern()
        {
            if (rakePattern == null) return;
            rakePattern = null; Tray.EndStroke(); QueueSave(); Select();
            message.text = "Drag to rake freely, or choose another pattern. Undo restores the last pass.";
        }
        Vector2 RakeTipPosition()
        {
            Rect view = gesture.View;
            return new Vector2((current.x / SandTray.Size - view.x) / view.width * trayImage.contentRect.width,
                (current.y / SandTray.Size - (1 - view.yMax)) / view.height * trayImage.contentRect.height);
        }
        void Update()
        {
            SafeArea(); if (loading) return;
            float now = Time.unscaledTime; int activity = 0;
            accumulator += Mathf.Min(Time.unscaledDeltaTime, .05f);
            while (accumulator >= 1f / 60)
            {
                if (rakePattern != null)
                {
                    current = rakePattern.Advance(90f / 60);
                    activity += Tray.Apply(SandTool.Rake, previous, current, ToolRadius(), color, 1f / 60); previous = current;
                    if (rakePattern.Complete) StopRakePattern();
                }
                else if (down)
                {
                    activity += Tray.Apply(tool, previous, current, ToolRadius(), color, 1f / 60); previous = current;
                }
                bool airborne = Tray.Grains.Count > 0;
                Tray.Step(1f / 60);
                if (airborne && Tray.Grains.Count == 0 && !down) QueueSave();
                accumulator -= 1f / 60;
            }
            toolTip.UpdateTip(trayImage.ChangeCoordinatesTo(toolTip, rakePattern != null ? RakeTipPosition() : gesture.TipPosition),
                ToolRadius() * trayImage.contentRect.width * gesture.Zoom / SandTray.Size,
                touchTipOffset, tool, SandTexture.Palette[color], (rakePattern != null || gesture.ToolVisible) && dialog.ClassListContains("hidden"),
                down || rakePattern != null, Tray.RakeAngle);
            if (now >= nextProgress)
            {
                int finished = Progress.Update(Tray, now); nextProgress = now + .1f;
                if (finished > 0 && Tray.Protection) { audioPlayer.Celebrate(finished); animateUntil = now + .75f; message.text = "A section settled. Its matching sand is protected."; }
                progressLabel.text = $"{Progress.Coverage:P0} colored  ·  {Progress.CompletedCount}/{Progress.Sections.Count} sections";
                if (Progress.AllComplete && !Progress.Clean && Tray.Protection) message.text = "Vacuum stray sand from the gaps to finish.";
                if (Progress.CheckWin(Tray, now, down || rakePattern != null))
                {
                    audioPlayer.CompletePainting(); QueueSave();
                    ShowDialog("A moment, made by you.", "Your mandala is complete. Keep it a little longer, or let the sand go.", "Let go", "Keep painting", ReleaseSand);
                }
            }
            float panelWidth = root.resolvedStyle.width;
            float pixelsPerPoint = panelWidth > 0 ? Screen.width / panelWidth : 1;
            if (surface.SetViewport(trayImage.contentRect.width * pixelsPerPoint, gesture.View))
                trayImage.image = surface.Texture;
            guideLabels.Update(gesture.View, trayImage.contentRect.width, guides);
            bool suction = Tray.SuctionGrains.Count > 0;
            if (surface.NeedsRedraw || (now >= nextRender && (lastRendered != Tray.Changes || down || rakePattern != null || Tray.Grains.Count > 0 || suction || suctionRendered || now < animateUntil || surface.HighlightActive)))
            {
                surface.Render(Tray, Progress, guides, now); lastRendered = Tray.Changes; nextRender = now + 1f / (suction ? 60 : 30);
                suctionRendered = suction; // Clear the final grain frame after the release tail ends.
            }
            audioPlayer.Tool(tool, down || rakePattern != null || Tray.Grains.Count > 0, activity / 120f + (Tray.Grains.Count > 0 ? .3f : 0));
            PumpSave();
        }
        float ToolRadius() => tool == SandTool.Pour ? 16 : tool == SandTool.Vacuum ? 32 : tool == SandTool.Rake ? 24 : 32 * SandTray.Size / Mathf.Max(1, trayImage.contentRect.width * gesture.Zoom);
        void QueueSave() { if (!loading) { queuedSave = Tray.Capture(); } }
        void PumpSave()
        {
            if (!saveTask.IsCompleted) return;
            if (saveTask.IsFaulted && !saveFailed)
            {
                saveFailed = true; message.text = "Could not save sand on this device."; Debug.LogWarning("Mandala save: " + saveTask.Exception?.GetBaseException().Message);
            }
            if (queuedSave == null) return;
            var snapshot = queuedSave; queuedSave = null; saveFailed = false;
            saveTask = Task.Run(() => MandalaSave.Write(savePath, snapshot));
        }
        void FlushSave()
        {
            if (loading || Tray == null) return;
            gesture.CancelAll(); StopRakePattern();
            try { saveTask.GetAwaiter().GetResult(); } catch (Exception error) { Debug.LogWarning("Mandala previous save: " + error.Message); }
            try { MandalaSave.Write(savePath, Tray.Capture()); queuedSave = null; }
            catch (Exception error) { Debug.LogWarning("Mandala save: " + error.Message); }
        }
        void OnApplicationPause(bool paused) { if (paused) FlushSave(); }
        void OnApplicationFocus(bool focused) { if (!focused) FlushSave(); }
        void OnApplicationQuit() => FlushSave();
        void OnDisable()
        {
            FlushSave(); loadGeneration++; if (root != null) root.Q("traySpace")?.UnregisterCallback<GeometryChangedEvent>(ResizeTray);
            if (gesture != null) root?.Q("traySpace")?.RemoveManipulator(gesture);
            surface?.Dispose();
        }
    }
}
