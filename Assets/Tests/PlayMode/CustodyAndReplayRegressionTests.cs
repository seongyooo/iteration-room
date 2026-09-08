using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IterationRoom.Tests
{
    public class CustodyAndReplayRegressionTests
    {
        private GameObject root;
        private PlayerHand hand;
        private CarryableItem item;
        private Vector3 origin;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Regression world");
            hand = Child("Player").AddComponent<PlayerHand>();
            hand.enabled = false; // Tests drive actions explicitly, not physical keyboard input.
            hand.holdAnchor = hand.transform;
            var prop = Child("RegressionKey");
            origin = new Vector3(0.2f, 0.3f, 0.4f);
            prop.transform.localPosition = origin;
            prop.transform.localScale = Vector3.one * 0.25f;
            prop.AddComponent<BoxCollider>().isTrigger = true;
            item = prop.AddComponent<CarryableItem>();
            item.enabled = false;
            item.itemId = "RegressionKey";
            ItemRegistry.Register(item);
            item.handLocalScale = Vector3.one;
        }

        [TearDown]
        public void TearDown()
        {
            // Items may have been reparented into a hand or a socket.
            hand.ReturnAll();
            foreach (var ghost in root.GetComponentsInChildren<GhostReplayer>()) ghost.ReleaseCarried();
            ItemRegistry.Unregister(item);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void FullHandRefusesSecondItem()
        {
            var second = Child("Second").AddComponent<BoxCollider>().gameObject.AddComponent<CarryableItem>();
            hand.Take(item);
            hand.Take(second);
            Assert.That(hand.Held, Is.SameAs(item));
            Assert.That(second.IsCarried, Is.False);
            Assert.That(second.transform.parent, Is.SameAs(root.transform));
        }

        [Test]
        public void LoopReturnRecoversSurrenderedSocketItemAndOriginalScale()
        {
            hand.Take(item);
            CarryableItem given = hand.Surrender(item.itemId);
            given.InsertInto(Child("Socket").transform);
            Assert.That(item.Seated, Is.True);
            Assert.That(hand.HandsFull, Is.False);

            hand.ReturnAll();
            AssertOrigin();
            hand.ReturnAll(); // Repeated cleanup must remain safe.
            AssertOrigin();
        }

        [Test]
        public void PlayerTakingFromGhostImmediatelyRevokesGhostToolUse()
        {
            GhostReplayer ghost = Ghost(new CarryEvent(0.1f, item.itemId, CarryKind.Take, item.name));
            ghost.Tick(0.2f);
            Assert.That(ghost.HoldingEquipped(item.itemId), Is.True);
            hand.Take(item);
            Assert.That(hand.Held, Is.SameAs(item));
            Assert.That(item.HeldByGhost, Is.Null);
            Assert.That(ghost.HoldingEquipped(item.itemId), Is.False);
            ghost.ReleaseCarried();
            Assert.That(item.transform.parent, Is.SameAs(hand.holdAnchor), "Ghost cleanup must not reclaim the player's item.");
        }

        [Test]
        public void GhostHandoverLeavesExactlyOneHolder()
        {
            GhostReplayer first = Ghost(new CarryEvent(0.1f, item.itemId, CarryKind.Take, item.name));
            GhostReplayer second = Ghost(new CarryEvent(0.2f, item.itemId, CarryKind.Take, item.name));
            first.Tick(0.15f);
            second.Tick(0.25f);
            Assert.That(item.HeldByGhost, Is.SameAs(second));
            Assert.That(first.HoldingEquipped(item.itemId), Is.False);
            Assert.That(second.HoldingEquipped(item.itemId), Is.True);
            first.ReleaseCarried();
            Assert.That(item.HeldByGhost, Is.SameAs(second));
        }

        [Test]
        public void SkippedFramesDrainCarryEventsInOrderAndResetReplaysThem()
        {
            GhostReplayer ghost = Ghost(
                new CarryEvent(0.1f, item.itemId, CarryKind.Take, item.name),
                new CarryEvent(0.2f, item.itemId, CarryKind.Drop));
            ghost.Tick(0.3f); // Both events must run, even without a tick between them.
            Assert.That(item.IsCarried, Is.False);
            Assert.That(item.Released, Is.True);
            Assert.That(ghost.HoldingEquipped(item.itemId), Is.False);
            item.ReturnToOrigin();
            ghost.ResetPlayback();
            ghost.Tick(0.15f);
            Assert.That(ghost.HoldingEquipped(item.itemId), Is.True, "Reset must rewind the event cursor.");
            ghost.ResetPlayback();
            AssertOrigin();
        }

        [UnityTest]
        public IEnumerator InteractionClaimExpiresOnNextFrame()
        {
            Assert.That(hand.InteractedThisFrame, Is.False);
            hand.Take(item);
            Assert.That(hand.InteractedThisFrame, Is.True);
            yield return null;
            Assert.That(hand.InteractedThisFrame, Is.False);
        }

        [Test]
        public void DestroyedLoopManagerClearsManagedSingletonReference()
        {
            var manager = Child("Loop").AddComponent<LoopManager>();
            manager.enabled = false; // Exercise lifetime without starting a game or changing saves.
            Assert.That(ReferenceEquals(LoopManager.Instance, manager), Is.True);
            Object.DestroyImmediate(manager.gameObject);
            Assert.That(ReferenceEquals(LoopManager.Instance, null), Is.True,
                "Unity fake-null does not protect callers using the ?. operator.");
        }

        [UnityTest]
        public IEnumerator EmptyHandsDoNotAdvertiseLockButHeldObjectsDo()
        {
            hand.gameObject.tag = "Player";
            hand.gameObject.AddComponent<BoxCollider>().isTrigger = true;
            Child("Eye").transform.SetParent(hand.transform, false);
            hand.transform.Find("Eye").gameObject.AddComponent<Camera>();
            var lockObject = Child("Lock");
            lockObject.transform.position = Vector3.forward;
            var reach = lockObject.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.size = Vector3.one * 4f;
            var keyLock = lockObject.AddComponent<KeyLock>();
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(keyLock.WantsInteractHint, Is.False);
            hand.Take(item); // Wrong items must still allow visible refusal feedback.
            Assert.That(keyLock.WantsInteractHint, Is.True);
            hand.ReturnAll();
            Assert.That(keyLock.WantsInteractHint, Is.False);
        }

        [Test]
        public void StandingTreeStopsBlockingWhenFelledAndBlocksAgainAfterReset()
        {
            var treeObject = Child("Tree");
            treeObject.AddComponent<BoxCollider>().isTrigger = true;
            var tree = treeObject.AddComponent<TreeTrunk>();
            tree.standingBlocker = Child("Standing trunk").AddComponent<CapsuleCollider>();
            tree.axeItemId = item.itemId;
            tree.chopsToFell = 1;
            tree.ResetTree();
            Assert.That(tree.standingBlocker.enabled, Is.True);
            GhostReplayer ghost = Ghost(new CarryEvent(0.1f, item.itemId, CarryKind.Take, item.name));
            ghost.Tick(0.2f);
            tree.SetGhostSignal(ghost, true);
            Assert.That(tree.HasFallen, Is.True);
            Assert.That(tree.standingBlocker.enabled, Is.False);
            tree.ResetTree();
            Assert.That(tree.HasFallen, Is.False);
            Assert.That(tree.standingBlocker.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator DropHintRetiresAfterFirstReleaseAcrossSubsequentPickups()
        {
            var display = Child("Inventory").AddComponent<CarriedItemsDisplay>();
            display.hand = hand;
            display.dropHint = Child("Hint").AddComponent<CanvasGroup>();
            hand.Take(item);
            yield return null;
            yield return null;
            Assert.That(display.dropHint.alpha, Is.GreaterThan(0f));
            hand.Surrender(item.itemId);
            yield return null;
            yield return null;
            hand.Take(item);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(display.dropHint.alpha, Is.Zero);
        }

        private GhostReplayer Ghost(params CarryEvent[] carries)
        {
            var ghost = Child("Ghost").AddComponent<GhostReplayer>();
            ghost.carryAnchor = ghost.transform;
            ghost.Init(new RecordedTimeline(new List<RecordedFrame>
            {
                new RecordedFrame(0f, Vector3.zero, 0f, 0u),
                new RecordedFrame(2f, Vector3.zero, 0f, 0u)
            }, new List<PopEvent>(), new List<CarryEvent>(carries)), new GhostInteractable[0]);
            return ghost;
        }

        private GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        private void AssertOrigin()
        {
            Assert.That(item.transform.parent, Is.SameAs(root.transform));
            Assert.That(item.transform.localPosition, Is.EqualTo(origin));
            Assert.That(item.transform.localScale, Is.EqualTo(Vector3.one * 0.25f));
            Assert.That(item.IsCarried, Is.False);
            Assert.That(item.Seated, Is.False);
            Assert.That(item.Released, Is.False);
            Assert.That(item.HeldByGhost, Is.Null);
            Assert.That(item.GetComponent<Collider>().enabled, Is.True);
        }
    }
}
