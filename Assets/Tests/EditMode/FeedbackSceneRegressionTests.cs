using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IterationRoom.Tests
{
    public class FeedbackSceneRegressionTests
    {
        private Scene scene;

        [TearDown]
        public void ClosePreview()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        [TestCase("MainMenu")]
        [TestCase("IterationRoom")]
        public void GeneratedMenusHaveApplyButtonAndAspectFraming(string name)
        {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/" + name + ".unity");
            var panels = All<SettingsPanel>();
            Assert.That(panels.Length, Is.GreaterThan(0));
            foreach (SettingsPanel panel in panels) Assert.That(panel.applyDisplayButton, Is.Not.Null);
            var cameras = All<Camera>().Where(c => c.CompareTag("MainCamera")).ToArray();
            Assert.That(cameras.Length, Is.GreaterThan(0));
            foreach (Camera camera in cameras) Assert.That(camera.GetComponent<FixedAspectPresentation>(), Is.Not.Null);
            foreach (Canvas canvas in All<Canvas>().Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay && c.isRootCanvas))
                Assert.That(canvas.GetComponent<FixedAspectPresentation>(), Is.Not.Null);
        }

        [Test]
        public void GeneratedTreeHasSeparateStandingCollision()
        {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Cycle2.unity");
            var trees = All<TreeTrunk>();
            Assert.That(trees.Length, Is.GreaterThan(0));
            foreach (TreeTrunk tree in trees)
            {
                Assert.That(tree.standingBlocker, Is.Not.Null);
                Assert.That(tree.standingBlocker.isTrigger, Is.False);
                Assert.That(tree.standingBlocker.enabled, Is.True);
                var stump = tree.fallPivot.parent.Find("StumpBlocker").GetComponent<BoxCollider>();
                Assert.That(((CapsuleCollider)tree.standingBlocker).radius,
                    Is.EqualTo(Mathf.Min(stump.size.x, stump.size.z) * 0.5f).Within(0.001f),
                    "Branches must not expand the standing trunk into an invisible wall.");
                Assert.That(tree.branchColliders, Has.No.Member(tree.standingBlocker));
            }
        }

        [Test]
        public void GeneratedFinalRoomsHaveSeamBackingCableConnectionAndVerticalLadder()
        {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Cycle3.unity");
            var transforms = All<Transform>();
            Assert.That(transforms.Any(t => t.name == "FloorSeamBacking"), Is.True);
            Transform connection = transforms.FirstOrDefault(t => t.name == "StationConnection");
            Assert.That(connection, Is.Not.Null);
            Assert.That(connection.lossyScale.y * 2f, Is.LessThanOrEqualTo(1.01f),
                "The station cable must reach the wall without protruding into room3-2N.");

            Transform roomOne = transforms.Single(t => t.name == "Room3_1");
            foreach (string side in new[] { "South", "East", "West" })
            {
                Transform threshold = roomOne.Find("GateThreshold_" + side);
                Transform backing = roomOne.Find("GateThresholdBacking_" + side);
                Assert.That(threshold, Is.Not.Null, $"Room3-1's {side} gate has no floor threshold.");
                Assert.That(backing, Is.Not.Null, $"Room3-1's {side} gate has no seam backing.");
                Assert.That(threshold.position.y + threshold.lossyScale.y * 0.5f,
                    Is.EqualTo(roomOne.position.y).Within(0.001f));
            }

            EndingDeparture departure = All<EndingDeparture>().Single();
            Transform lid = departure.shaftLid;
            Assert.That(lid, Is.Not.Null);
            Assert.That(lid.localScale.x, Is.GreaterThan(1.75f),
                "The closed shaft lid must overlap the opening instead of leaving a perimeter slot.");
            float sealedCentre = lid.localPosition.y + departure.shaftLidRise;
            Assert.That(sealedCentre + lid.localScale.y * 0.5f, Is.EqualTo(-0.01f).Within(0.001f));
            Assert.That(sealedCentre - lid.localScale.y * 0.5f, Is.EqualTo(-0.11f).Within(0.001f));

            FacilityFailure failure = All<FacilityFailure>().Single();
            Assert.That(failure.movers.Any(m => HasAncestor(m.what, "LadderShaft")), Is.False,
                "The ladder shaft is part of the room shell and must remain during the teardown.");
            var mounts = All<LadderMount>();
            Assert.That(mounts.Length, Is.GreaterThan(0));
            foreach (LadderMount mount in mounts)
            {
                Assert.That(Vector3.Dot(mount.climbDirection.normalized, Vector3.up), Is.GreaterThan(0.999f));
                Vector3 low = mount.AxisAt(mount.bottomY), high = mount.AxisAt(mount.topY);
                Assert.That(low.x, Is.EqualTo(high.x).Within(0.001f));
                Assert.That(low.z, Is.EqualTo(high.z).Within(0.001f));
                Assert.That(mount.climbOffset.magnitude, Is.GreaterThan(0.3f));
                var ladder = All<CarryableItem>().First(i => i.itemId == mount.acceptedItemId);
                float length = ladder.transform.TransformVector(ladder.heldSpan).magnitude;
                Assert.That(mount.seat.localPosition.y, Is.EqualTo(length * 0.5f).Within(0.001f),
                    "The installed ladder's foot must stay on the deck after changing its angle.");
            }
        }

        [Test]
        public void GeneratedEnglishRideNarrationHasEveryLine()
        {
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/IterationRoom.unity");
            NarrationDirector narration = All<NarrationDirector>().Single();

            Assert.That(narration.english.rideLines, Has.Length.EqualTo(13));
            for (int i = 0; i < narration.english.rideLines.Length; i++)
            {
                AudioClip clip = narration.english.rideLines[i];
                Assert.That(clip, Is.Not.Null, $"English cable-car announcement {i} is missing.");
                Assert.That(clip.length, Is.GreaterThan(0f),
                    $"English cable-car announcement {i} contains no playable audio.");
            }
        }

        [Test]
        public void EndingExteriorDisablesAndRestoresOcclusionForEveryPlayerCamera()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var player = new GameObject("TestPlayer");
            SceneManager.MoveGameObjectToScene(player, scene);
            var departure = player.AddComponent<EndingDeparture>();
            departure.player = player.transform;

            Camera gameplay = new GameObject("GameplayCamera").AddComponent<Camera>();
            gameplay.transform.SetParent(player.transform);
            gameplay.useOcclusionCulling = true;
            Camera capture = new GameObject("CaptureCamera").AddComponent<Camera>();
            capture.transform.SetParent(player.transform);
            capture.useOcclusionCulling = false;

            InvokePrivate(departure, "DisableOcclusionForExteriorView");
            Assert.That(gameplay.useOcclusionCulling, Is.False);
            Assert.That(capture.useOcclusionCulling, Is.False);

            InvokePrivate(departure, "RestoreOcclusionAfterExteriorView");
            Assert.That(gameplay.useOcclusionCulling, Is.True);
            Assert.That(capture.useOcclusionCulling, Is.False,
                "A camera that had occlusion disabled before the ride must stay disabled.");
        }

        private T[] All<T>() where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        private static void InvokePrivate(object target, string method)
        {
            target.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(target, null);
        }

        private static bool HasAncestor(Transform transform, string name)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (current.name == name) return true;
            return false;
        }
    }
}
