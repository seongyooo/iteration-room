using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IterationRoom.Tests
{
    public class MenuAndVideoRegressionTests
    {
        private readonly Dictionary<string, int?> saved = new Dictionary<string, int?>();
        private GameObject root;

        [SetUp]
        public void SetUp()
        {
            foreach (string key in new[] { "iteration.screenWidth", "iteration.screenHeight", "iteration.fullscreen" })
                saved[key] = PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null;
            root = new GameObject("Regression menu");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var pair in saved)
                if (pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key, pair.Value.Value);
                else PlayerPrefs.DeleteKey(pair.Key);
            saved.Clear();
            PlayerPrefs.Save();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullscreenTogglePreservesChosenResolution(bool fullscreen)
        {
            // Deliberately differs from the Game View: Screen reports the old size in the Editor.
            var chosen = VideoSettings.FitResolution(Screen.width + 37, Screen.height + 29);
            VideoSettings.SetResolution(chosen.x, chosen.y, !fullscreen);
            VideoSettings.Fullscreen = fullscreen;
            Assert.That(VideoSettings.ChosenResolution, Is.EqualTo(chosen));
            Assert.That(VideoSettings.Fullscreen, Is.EqualTo(fullscreen));
        }

        [TestCase("ShowCredits", "creditsGroup")]
        [TestCase("ShowCyclePicker", "cycleGroup")]
        [TestCase("ShowRecord", "recordGroup")]
        public void HiddenPageCannotSubmitAndOpeningItDisablesMainMenu(string method, string field)
        {
            var menu = root.AddComponent<MainMenu>();
            menu.menuGroup = Group("Main");
            CanvasGroup page = Group("Page");
            typeof(MainMenu).GetField(field).SetValue(menu, page);
            var buttonObject = new GameObject("Submit", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(page.transform, false);
            var button = buttonObject.GetComponent<Button>();
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);
            var events = root.AddComponent<EventSystem>();
            MethodInfo navigation = typeof(MainMenu).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(navigation, Is.Not.Null);

            navigation.Invoke(menu, new object[] { false });
            button.OnSubmit(new BaseEventData(events));
            Assert.That(clicks, Is.Zero, "A hidden page must ignore keyboard/controller Submit.");
            Assert.That(button.IsInteractable(), Is.False);
            Assert.That(menu.menuGroup.interactable, Is.True);

            navigation.Invoke(menu, new object[] { true });
            Assert.That(button.IsInteractable(), Is.True);
            Assert.That(page.alpha, Is.EqualTo(1f));
            Assert.That(page.blocksRaycasts, Is.True);
            Assert.That(menu.menuGroup.interactable, Is.False);
        }

        [TestCase(1920, 1080)]
        [TestCase(2560, 1080)]
        [TestCase(1920, 1200)]
        [TestCase(1024, 768)]
        public void ViewportFitsSixteenByNineAndCentersUnusedSpace(int width, int height)
        {
            Rect r = FixedAspectPresentation.Viewport(width, height);
            Assert.That(r.width * width / (r.height * height), Is.EqualTo(16f / 9f).Within(0.0001f));
            Assert.That(r.center, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(r.xMax, Is.LessThanOrEqualTo(1f));
            Assert.That(r.yMax, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void ChoosingDisplaySettingsDoesNotApplyUntilConfirmed()
        {
            VideoSettings.SetResolution(1280, 720, true);
            var panel = root.AddComponent<SettingsPanel>();
            panel.Refresh();
            typeof(SettingsPanel).GetMethod("StepResolution", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(panel, new object[] { 1 });
            typeof(SettingsPanel).GetMethod("SetFullscreen", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(panel, new object[] { false });
            Assert.That(VideoSettings.ChosenResolution, Is.EqualTo(new Vector2Int(1280, 720)));
            Assert.That(VideoSettings.Fullscreen, Is.True);
            panel.ApplyDisplaySettings();
            Assert.That(VideoSettings.ChosenResolution, Is.EqualTo(new Vector2Int(1600, 900)));
            Assert.That(VideoSettings.Fullscreen, Is.False);
        }

        private CanvasGroup Group(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(root.transform, false);
            return go.GetComponent<CanvasGroup>();
        }
    }
}
