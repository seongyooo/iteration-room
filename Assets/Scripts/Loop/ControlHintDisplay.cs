using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // A grey disc with a key glyph in it floats over whatever interactable the player has walked up
    // to, and a second one carrying a mouse button floats over the balloon a swing would burst.
    //
    // These used to retire permanently the first time the player performed the action each one
    // described - the reasoning being that the loop's whole texture is repetition, so an
    // instruction replaying every sixty seconds would become the most repeated thing in the
    // prototype. Play-testing overruled it, and decisively: testers never found the door button at
    // all, and the ones who did could not find it again an iteration later. A prompt that has been
    // shown once has not been taught, and a control the player cannot find is worse than a control
    // they are reminded of.
    //
    // What survives of the old reasoning is the restraint: the prompt still appears only when E
    // would actually do something here (WantsInteractHint, not mere proximity), only over the
    // nearest such thing, and at maxAlpha rather than full - so it reads as a label on the fixture
    // rather than as the game talking.
    //
    // The SWING prompt is the one exception, and retires for good after the first pop. That is not a
    // return of what play-testing overruled: the finding was that showing a prompt ONCE does not
    // teach, and this one stays up until the player has performed the action, not until they have
    // seen it. Nobody forgets which button they just clicked.
    public class ControlHintDisplay : MonoBehaviour
    {
        public Camera playerCamera;

        // Everything E does something to. Typed as MonoBehaviour rather than IInteractHintTarget
        // because Unity does not serialize interface fields - the cast back happens once in Awake.
        // SceneBuilder fills this the same way it fills LoopManager.ghostInteractables.
        public MonoBehaviour[] interactTargets;

        // The calibration step runs before the loop does, so AcceptsInput is false for all of it -
        // but the start button on that room's wall is an IInteractHintTarget and wants this prompt.
        // Letting it through here rather than giving that step a prompt of its own means the player
        // meets the game's own disc, in the game's own position, before the game.
        public SensitivityCalibration calibration;

        // The swing itself. Asked rather than re-derived: it owns both halves of this prompt's rule -
        // whether the click is live, and which balloon it would burst.
        public BalloonTool swingTool;

        // And the other thing the left button does. It shares the swing's disc rather than getting one
        // of its own, and cannot fight it for it: the pin and a chess piece are both items, PlayerHand
        // has exactly one out at a time, so at most one of the two ever wants the prompt.
        public ChessPlacer placer;
        // And the bucket's. Both live on the player, so both are core-scene references that survive
        // the per-cycle scene split.
        public BucketPlacer bucketPlacer;
        // And room3-2N's. The fourth thing on this disc, and the same crossing as the two above it:
        // the component is on the player and the cube it points at is in a cycle scene.
        public BedlamPlacer bedlamPlacer;
        // And room3-2N's ladder. The fifth thing on this disc and the same crossing as the rest.
        public LadderPlacer ladderPlacer;

        // The tree, which asks for the same disc as the balloon tool and for the same reason: a
        // left click is a SWING here too. It is a `TreeTrunk` rather than an interface because it is
        // the only fixture of its kind - if a second accumulation puzzle wants this, that is the
        // moment to give the three of them a shared one rather than now.
        public TreeTrunk treeTrunk;

        // The rect the screen positions are resolved against: a full-screen child of the canvas,
        // which is also both hints' parent, so a local point in it is an anchoredPosition.
        public RectTransform area;

        public CanvasGroup interactGroup;
        public RectTransform interactRect;
        public CanvasGroup swingGroup;
        public RectTransform swingRect;

        public float fadeSpeed = 6f;
        // Held short of opaque now that these are permanent rather than one-time. At full alpha a
        // prompt that is always there reads as part of the HUD; at 0.75 it reads as something
        // stencilled on the fixture.
        public float maxAlpha = 0.75f;

        private IInteractHintTarget[] targets;
        private float interactAlpha;
        private float swingAlpha;

        private void Awake()
        {
            CacheTargets();

            if (interactGroup != null) interactGroup.alpha = 0f;
            if (swingGroup != null) swingGroup.alpha = 0f;
        }

        // ASSIGN THROUGH THIS, NEVER BY WRITING THE FIELD, once anything sets it after build time.
        // The cast to the interface happens once in `Awake`, so a later write to `interactTargets`
        // alone changes nothing at all - and Unity does not define the order of two `Awake`s, so
        // whether a rebind is seen would be a coin toss decided per build. `CycleBinding` rebuilds
        // this list at startup as the groundwork for the per-cycle scene split.
        public void SetTargets(MonoBehaviour[] value)
        {
            interactTargets = value;
            CacheTargets();
        }

        // ...and the arbiter gets the same list. It decides which fixture a press is for, and it must
        // never be judging a different set from the one the disc is drawn over.
        private void PublishTargets() => PlayerLookup.SetHintTargets(targets);

        private void CacheTargets()
        {
            int count = interactTargets != null ? interactTargets.Length : 0;
            targets = new IInteractHintTarget[count];
            for (int i = 0; i < count; i++) targets[i] = interactTargets[i] as IInteractHintTarget;
            PublishTargets();
        }

        private void Update()
        {
            // Silent through the wake-up, like everything else the facility does: the player has no
            // control then, so a prompt would be describing a button that does nothing.
            // Room4 used to need an exception here, because its plate was live while AcceptsInput
            // was false. The clock runs through that room now, so the ordinary rule covers it and
            // the calibration step is the only moment left that the loop does not.
            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput
                        || (calibration != null && calibration.Active);

            Show(interactGroup, interactRect, running ? AimedWantingHint() : null, ref interactAlpha);
            Show(swingGroup, swingRect, running ? SwingAnchor() : null, ref swingAlpha);
        }

        // THE ONE BEING LOOKED AT rather than the one nearest the face - and NOT computed here.
        //
        // The disc used to run its own scan, which was correct for as long as the press ran the same
        // one over the same list. It is one answer now (`PlayerLookup.AimedAnchor`), asked once a
        // frame and shared, because two scans that agree today are two scans that can be changed
        // apart tomorrow - and a disc drawn on one fixture while E operates another is the single
        // worst outcome this family of rules has.
        private Transform AimedWantingHint() =>
            playerCamera != null ? PlayerLookup.AimedAnchor : null;

        // On the BALLOON, not on the pin. Hung on the held tool this rode the view, so it was up the
        // whole time the pin was in hand and sat on the one thing the player was not being asked to
        // click - which made it a HUD element describing a control rather than a label on what the
        // control acts on. Anchored to the balloon a swing would actually burst, it appears only when
        // clicking would do something and points at what that something is.
        private Transform SwingAnchor()
        {
            // The board's lit square, on the same principle: the prompt goes on the thing the click
            // acts on. Unlike the swing's it does NOT retire after the first use - a piece's square is
            // a different square every time, so this is not repeating an instruction the player has
            // learned, it is answering "which one is this piece's" - and the placer stops asking for
            // it once the button has clearly been learned.
            if (placer != null && placer.WantsPlaceHint) return placer.PlaceAnchor;

            // And the bucket's stand or the tank it is being carried to - the same disc for the same
            // reason: the prompt goes on the thing the click acts on, and only one of these can want
            // it at a time because the hand holds one object.
            if (bucketPlacer != null && bucketPlacer.WantsPlaceHint) return bucketPlacer.PlaceAnchor;

            // And the Bedlam cube, on the same principle again: the disc hangs on the middle of the
            // cube a click would add to, so it says "left click" and "over there" in one mark. It
            // does NOT retire, for the reason the chess board's does not - the half of the message
            // that keeps mattering is WHETHER THIS BLOCK GOES ON YET, which is a new answer for
            // every one of the twelve.
            if (bedlamPlacer != null && bedlamPlacer.WantsPlaceHint) return bedlamPlacer.PlaceAnchor;

            // And the ladder's mark on deck B. The one prompt in the game that is answering "this is
            // the missing piece of the room" rather than "put the thing here".
            if (ladderPlacer != null && ladderPlacer.WantsPlaceHint) return ladderPlacer.PlaceAnchor;

            // The tree BEFORE the balloon tool, because the two can want the disc at the same
            // moment - a past self can be holding a pin in the same room - and the tree is the one
            // the player is standing at.
            if (treeTrunk != null && treeTrunk.WantsSwingHint) return treeTrunk.HintAnchor;

            if (swingTool == null || !swingTool.WantsSwingHint) return null;

            Balloon target = swingTool.FindTarget();
            return target != null ? target.transform : null;
        }

        private void Show(CanvasGroup group, RectTransform rect, Transform anchor, ref float alpha)
        {
            if (group == null || rect == null) return;

            bool visible = anchor != null && Place(rect, anchor);
            // Unscaled, so the prompt still fades away when the pause menu freezes the game. On
            // scaled time it would sit there at whatever alpha it had, frozen under the overlay.
            alpha = Mathf.MoveTowards(alpha, visible ? maxAlpha : 0f, fadeSpeed * Time.unscaledDeltaTime);
            group.alpha = alpha;
        }

        // False when the anchor is behind the camera: WorldToScreenPoint happily returns a mirrored
        // on-screen position for anything behind the eye, so without the z test a prompt for
        // something at your back appears in front of you.
        private bool Place(RectTransform rect, Transform anchor)
        {
            if (playerCamera == null || area == null) return false;

            Vector3 screenPoint = playerCamera.WorldToScreenPoint(anchor.position);
            if (screenPoint.z <= 0f) return false;

            // Null camera: the canvas is ScreenSpaceOverlay, where screen points are already in the
            // canvas's own space and passing a camera would skew the result.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPoint, null, out Vector2 local))
                return false;

            rect.anchoredPosition = local;
            return true;
        }
    }
}
