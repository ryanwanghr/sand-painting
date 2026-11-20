using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    internal sealed class SandStudioMenu : IDisposable
    {
        enum Tab { Main, Daily, Works }

        readonly VisualElement screen, safe, content, settings;
        readonly ScrollView scroll;
        readonly Button[] tabs;
        readonly Button settingsButton, soundButton, guidesButton, waterAnimationButton;
        readonly SandDesign[] designs;
        readonly SandWorks works;
        readonly MandalaAudio audio;
        readonly Action<SandWork> openPainting;
        readonly Texture2D originalPreview;
        readonly Dictionary<string, Texture2D> finishedImages = new Dictionary<string, Texture2D>();

        Tab tab;
        DateTime shownDay;
        SandWork viewedWork;
        Rect previousSafeArea;
        int previousWidth, previousHeight;
        float previousPanelWidth;
        Vector2 previousScroll;

        public SandStudioMenu(VisualElement screen, SandDesign[] designs, SandWorks works, MandalaAudio audio,
            Action<SandWork> openPainting)
        {
            this.screen = screen;
            this.designs = designs;
            this.works = works;
            this.audio = audio;
            this.openPainting = openPainting;
            safe = screen.Q("studioSafe");
            scroll = screen.Q<ScrollView>("studioScroll");
            content = screen.Q("studioContent");
            settings = screen.Q("studioSettingsOverlay");
            originalPreview = SandArtPreview.Original();

            tabs = new[] { screen.Q<Button>("mainTab"), screen.Q<Button>("dailyTab"), screen.Q<Button>("worksTab") };
            for (int i = 0; i < tabs.Length; i++)
            {
                Tab choice = (Tab)i;
                tabs[i].clicked += () =>
                {
                    tab = choice;
                    viewedWork = null;
                    Render();
                };
            }
            settingsButton = screen.Q<Button>("studioSettings");
            soundButton = screen.Q<Button>("studioSound");
            guidesButton = screen.Q<Button>("studioGuides");
            waterAnimationButton = screen.Q<Button>("studioWaterAnimation");
            settingsButton.clicked += () => SetSettings(true);
            soundButton.clicked += () => { SetSound(audio, !audio.Enabled); RefreshSettings(); };
            guidesButton.clicked += () => { GuidesEnabled = !GuidesEnabled; RefreshSettings(); };
            waterAnimationButton.clicked += () => { WaterAnimationEnabled = !WaterAnimationEnabled; RefreshSettings(); };
            screen.Q<Button>("studioSettingsDone").clicked += () => SetSettings(false);
            settings.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == settings) SetSettings(false);
            });
            screen.RegisterCallback<NavigationCancelEvent>(evt =>
            {
                if (!settings.ClassListContains("hidden")) SetSettings(false);
                else if (viewedWork != null) { viewedWork = null; Render(); }
                else return;
                evt.StopPropagation();
            });
        }

        const string SoundKey = "sand-painting.sound";
        const string GuidesKey = "sand-painting.guides";
        const string WaterAnimationKey = "sand-painting.water-animation";

        public static bool GuidesEnabled
        {
            get => PlayerPrefs.GetInt(GuidesKey, 1) != 0;
            set { PlayerPrefs.SetInt(GuidesKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool WaterAnimationEnabled
        {
            get => PlayerPrefs.GetInt(WaterAnimationKey, 1) != 0;
            set { PlayerPrefs.SetInt(WaterAnimationKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool SoundEnabled => PlayerPrefs.GetInt(SoundKey, 1) != 0;

        public static void SetSound(MandalaAudio audio, bool enabled)
        {
            audio.SetEnabled(enabled);
            PlayerPrefs.SetInt(SoundKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void Show()
        {
            viewedWork = null;
            screen.RemoveFromClassList("hidden");
            Render(previousScroll);
        }

        public void Hide()
        {
            previousScroll = scroll.scrollOffset;
            screen.AddToClassList("hidden");
            ReleaseImages();
        }

        public void ShowError(string message)
        {
            var status = screen.Q<Label>("studioStatus");
            status.text = message;
            status.RemoveFromClassList("hidden");
        }

        public void Update()
        {
            if (tab == Tab.Daily && viewedWork == null && shownDay != DateTime.Today) Render();
            Rect area = Screen.safeArea;
            float panelWidth = screen.resolvedStyle.width;
            if (!float.IsFinite(panelWidth) || panelWidth <= 0) return;
            if (area == previousSafeArea && Screen.width == previousWidth && Screen.height == previousHeight
                && panelWidth == previousPanelWidth) return;

            previousSafeArea = area;
            previousWidth = Screen.width;
            previousHeight = Screen.height;
            previousPanelWidth = panelWidth;
            float pointsPerPixel = panelWidth / Mathf.Max(1, Screen.width);
            float height = area.height * pointsPerPixel;
            float scale = area.width > area.height ? Mathf.Clamp(height / 620, .25f, 1) : 1;
            float width = area.width * pointsPerPixel / scale;
            safe.style.left = area.xMin * pointsPerPixel;
            safe.style.top = (Screen.height - area.yMax) * pointsPerPixel;
            safe.style.width = width;
            safe.style.height = height / scale;
            safe.style.scale = new Scale(new Vector3(scale, scale, 1));
            safe.EnableInClassList("studio-wide", width >= 660);
        }

        void SetSettings(bool open)
        {
            settings.EnableInClassList("hidden", !open);
            scroll.SetEnabled(!open);
            foreach (var button in tabs) button.SetEnabled(!open);
            settingsButton.SetEnabled(!open);
            if (open) { RefreshSettings(); soundButton.Focus(); }
            else settingsButton.Focus();
        }

        void RefreshSettings()
        {
            soundButton.text = audio.Enabled ? "Sound on" : "Sound off";
            guidesButton.text = GuidesEnabled ? "Guides on" : "Guides off";
            waterAnimationButton.text = WaterAnimationEnabled ? "Animated water" : "Still water";
        }

        void Render(Vector2 offset = default)
        {
            screen.Q("studioStatus").AddToClassList("hidden");
            content.Clear();
            ReleaseImages();
            for (int i = 0; i < tabs.Length; i++) tabs[i].EnableInClassList("active", i == (int)tab);
            scroll.scrollOffset = Vector2.zero;
            if (offset != Vector2.zero) scroll.schedule.Execute(() => scroll.scrollOffset = offset);
            if (viewedWork != null) { ShowFinished(viewedWork); return; }

            switch (tab)
            {
                case Tab.Main: ShowLibrary(); break;
                case Tab.Daily: ShowDaily(); break;
                case Tab.Works: ShowWorks(); break;
            }
        }

        void ShowLibrary()
        {
            Heading("Choose a design", null);
            if (designs.Length == 0)
            {
                content.Add(Label("Designs will appear here when they’re added to the library.", "studio-copy"));
                return;
            }
            var collections = designs.GroupBy(design => design.Collection, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Any(design => design.Id == SandDesign.OriginalId) ? 0 : 1)
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
            int number = 0;
            foreach (var collection in collections)
            {
                var grid = Section(collection.Key);
                grid.EnableInClassList("studio-grid-many", collection.Count() > 2);
                foreach (var design in collection.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
                {
                    var work = works.For(design);
                    string action = work.InProgress ? "Resume painting" : work.HasImage ? "View painting" : "Start painting";
                    grid.Add(Card(work, action, work.HasImage && !work.InProgress, number % 2 == 1));
                    number++;
                }
            }
        }

        void ShowDaily()
        {
            shownDay = DateTime.Today;
            Heading("Daily challenge", shownDay.ToString("dddd, MMMM d"));
            var work = works.Daily(shownDay);
            if (work == null)
            {
                content.Add(Label("Today’s challenge will appear when designs are added.", "studio-copy"));
                return;
            }

            var feature = Feature(work, work.Design.Collection, work.HasImage);
            if (work.HasImage)
                feature.Add(ActionButton("View completed painting", () => ViewFinished(work), true));
            if (work.InProgress || !work.HasImage)
                feature.Add(ActionButton(work.HasSave ? "Resume challenge" : "Start challenge", () => openPainting(work), !work.HasImage));
        }

        void ShowWorks()
        {
            Heading("My Works", null);
            var saved = works.All().ToArray();
            var ongoing = saved.Where(work => work.InProgress).OrderByDescending(work => work.Changed).ToArray();
            var finished = saved.Where(work => work.HasImage).OrderByDescending(work => work.Finished).ToArray();
            if (ongoing.Length == 0 && finished.Length == 0)
            {
                content.Add(Label("Start a design and your paintings will appear here.", "studio-copy"));
                content.Add(ActionButton("Browse designs", () => { tab = Tab.Main; Render(); }, true));
                return;
            }
            AddWorks("In progress", ongoing, false);
            AddWorks("Completed", finished, true);
        }

        void AddWorks(string title, SandWork[] items, bool completed)
        {
            if (items.Length == 0) return;
            var grid = Section(title);
            grid.EnableInClassList("studio-grid-many", items.Length > 2);
            foreach (var work in items)
                grid.Add(Card(work, work.Date.HasValue ? "Daily · " + work.Date.Value.ToString("MMM d") : work.Design.Collection,
                    completed, completed));
        }

        void ViewFinished(SandWork work)
        {
            viewedWork = work;
            Render();
        }

        void ShowFinished(SandWork work)
        {
            var back = ActionButton(tab == Tab.Works ? "Back to My Works" : tab == Tab.Daily ? "Back to Daily" : "Back to designs",
                () => { viewedWork = null; Render(); });
            back.AddToClassList("studio-back-button");
            content.Add(back);
            Heading(work.Design.Title, work.Date.HasValue ? "Daily · " + work.Date.Value.ToString("MMMM d, yyyy") : work.Design.Collection);
            var feature = Element("studio-feature");
            feature.Add(Art(work, true, true));
            if (work.HasSave) feature.Add(ActionButton("Open tray", () => openPainting(work), true));
            content.Add(feature);
        }

        VisualElement Feature(SandWork work, string subtitle, bool completed)
        {
            var feature = Element("studio-feature");
            feature.Add(Art(work, completed, true));
            feature.Add(CardText(work.Design.Title, subtitle));
            content.Add(feature);
            return feature;
        }

        Button Card(SandWork work, string subtitle, bool completed, bool alternate)
        {
            var card = new Button(() => { if (completed) ViewFinished(work); else openPainting(work); })
            {
                name = (completed ? "finished-" : "design-") + work.Design.Id,
                tooltip = work.Design.Title + " · " + subtitle
            };
            card.AddToClassList("studio-card");
            var frame = Element("studio-card-frame", PickingMode.Ignore);
            frame.Add(Art(work, completed, alternate));
            frame.Add(CardText(work.Design.Title, subtitle));
            card.Add(frame);
            return card;
        }

        VisualElement Art(SandWork work, bool completed, bool alternate)
        {
            var art = Element("studio-art", PickingMode.Ignore);
            art.EnableInClassList("alternate", alternate);
            var preview = completed ? FinishedImage(work) : null;
            if (!preview) preview = work.Design.Mask;
            if (!preview) preview = originalPreview;
            var image = new Image
            {
                image = preview,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            image.AddToClassList("studio-art-image");
            art.Add(image);
            return art;
        }

        Texture2D FinishedImage(SandWork work)
        {
            if (finishedImages.TryGetValue(work.ImagePath, out var cached)) return cached;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (texture.LoadImage(File.ReadAllBytes(work.ImagePath), true))
                {
                    finishedImages.Add(work.ImagePath, texture);
                    return texture;
                }
            }
            catch (IOException) { }
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        void ReleaseImages()
        {
            foreach (var image in finishedImages.Values) UnityEngine.Object.Destroy(image);
            finishedImages.Clear();
        }

        public void Dispose()
        {
            ReleaseImages();
            UnityEngine.Object.Destroy(originalPreview);
        }

        void Heading(string title, string subtitle)
        {
            content.Add(Label(title, "studio-title"));
            if (subtitle != null) content.Add(Label(subtitle, "studio-copy"));
        }

        VisualElement Section(string title)
        {
            var section = Element("studio-section");
            section.Add(Label(title, "studio-section-title"));
            var grid = Element("studio-grid");
            section.Add(grid);
            content.Add(section);
            return grid;
        }

        static VisualElement CardText(string title, string subtitle)
        {
            var text = Element("studio-card-text", PickingMode.Ignore);
            text.Add(Label(title, "studio-card-title"));
            text.Add(Label(subtitle, "studio-card-meta"));
            return text;
        }

        static Button ActionButton(string text, Action action, bool primary = false)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("studio-action");
            button.EnableInClassList("studio-primary", primary);
            return button;
        }

        static Label Label(string text, string style)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(style);
            return label;
        }

        static VisualElement Element(string style, PickingMode picking = PickingMode.Position)
        {
            var element = new VisualElement { pickingMode = picking };
            element.AddToClassList(style);
            return element;
        }
    }
}
