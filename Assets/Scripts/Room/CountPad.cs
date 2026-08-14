using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // A floor pad that COUNTS the times it is stepped on, and shows the count as a single digit.
    //
    // Unlike `FloorButton`, which is a hold - active exactly while somebody is on it - this is an
    // instant, and it accumulates. Standing on it forever is one press. That difference is the whole
    // of what makes the number lock a puzzle iterations divide: a past self can leave a pad reading
    // 3 and walk away, and it is still reading 3 when the living player arrives.
    //
    // **IT WRAPS AT TEN, AND THAT IS WHAT MAKES THE ROOM SURVIVABLE.** Ghosts replay their presses
    // exactly, so a miscount is permanent - a pad overshot by a past self could never be brought back
    // down, and the room would be unwinnable for the rest of the cycle with no way to know until the
    // door refused. A counter that rolls over means overshoot is not a state at all: whatever a pad
    // reads, there is always some number of further presses that reaches the target. The player can
    // always dig out.
    //
    // The alternative on the table was a button that wipes accumulated ghosts. That was rejected:
    // accumulated past selves are the visible record of the player's work, and an undo for them makes
    // every iteration provisional. The wrap keeps the problem inside this room.
    public class CountPad : GhostInteractable
    {
        // What the wall says this pad should read. Set by SceneBuilder from the same array that
        // paints the graffiti, so the two cannot disagree.
        public int target = 1;

        // One decimal digit. Ten is not a tuning value - it is what makes the digit on the face and
        // the digit on the wall the same kind of thing.
        public int wrapAt = 10;

        // Standing on the pad means the player's own centre is over the disc, and their feet are at
        // its height. Same test `FloorButton` uses, and derived from the visual radius by
        // SceneBuilder so the two cannot drift apart.
        public float activationRadius = 0.4f;
        public float footClearance = 0.35f;

        // The face, which reads met or unmet, and the digit quad over it.
        public Renderer faceRenderer;
        public Color unmetColour = new Color(0.55f, 0.60f, 0.68f);
        public Color metColour = new Color(0.25f, 1f, 0.5f);
        public float faceEmission = 1.6f;

        public Renderer digitRenderer;
        // Index is the digit. Swapped through a property block, so all ten pads share one material.
        public Texture2D[] digitTextures;

        // Sinks a few millimetres while somebody is on it. Small, because a pad that visibly travels
        // reads as a lever rather than as something you tread on.
        public Transform plunger;
        public float plungerDrop = 0.012f;

        public AudioSource audioSource;
        public AudioClip stepClip;

        public int Count { get; private set; }
        public bool Met => Count == target;

        // A HOLD, recorded as a level, exactly like FloorButton - the RISING EDGE is what counts, and
        // it is taken on this side rather than recorded as an event.
        //
        // That is deliberate and it follows CLAUDE.md §1.5: a ghost can skip several recorded frames
        // in one tick, so an instant has to be derived from a level that was held long enough to be
        // sampled. Recording "pressed" as a one-frame pulse would let a ghost step straight over it.
        public override bool PlayerSignal => playerOn;

        private bool playerOn;
        private readonly HashSet<GhostReplayer> ghostsOn = new HashSet<GhostReplayer>();

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Start() => UpdateVisual();

        // Polled rather than driven by trigger callbacks, for the reason FloorButton documents: the
        // loop teleports the player by disabling the controller inside one frame, so an exit callback
        // can simply never arrive.
        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;
            bool on = playerCollider != null && playerCollider.enabled && IsStandingOn(playerCollider);
            if (on == playerOn) return;

            playerOn = on;
            if (on) Step();
            UpdateVisual();
        }

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            // Add() answers whether it was new, which is exactly the rising edge - and it means a
            // ghost re-asserting a signal it already holds cannot count twice.
            if (active) { if (ghostsOn.Add(ghost)) Step(); }
            else ghostsOn.Remove(ghost);

            UpdateVisual();
        }

        // WORLD STATE THE LOOP REWINDS, like a door or a drawer. Without this the counts would carry
        // across the iteration boundary and every pad would be somewhere unpredictable by the time
        // the ghosts replayed into it.
        public void ResetCount()
        {
            Count = 0;
            playerOn = false;
            ghostsOn.Clear();
            UpdateVisual();
        }

        private void Step()
        {
            Count = (Count + 1) % Mathf.Max(1, wrapAt);

            // Silent unless the clock is running, for the reason FloorButton's is: the loop releases
            // every ghost during the reset, and a rack of pads clicking behind the closed eyelids is
            // the machinery showing through rather than a sound the room makes.
            if (audioSource == null || stepClip == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;
            audioSource.PlayOneShot(stepClip);
        }

        private bool IsStandingOn(Collider playerCollider)
        {
            Bounds player = playerCollider.bounds;
            Vector3 pad = transform.position;

            float dx = player.center.x - pad.x;
            float dz = player.center.z - pad.z;
            if (dx * dx + dz * dz > activationRadius * activationRadius) return false;

            float feet = player.min.y;
            return feet >= pad.y - footClearance && feet <= pad.y + footClearance;
        }

        private void UpdateVisual()
        {
            bool occupied = playerOn || ghostsOn.Count > 0;

            if (plunger != null)
                plunger.localPosition = new Vector3(0f, occupied ? -plungerDrop : 0f, 0f);

            if (faceRenderer != null)
            {
                Color c = Met ? metColour : unmetColour;
                var block = new MaterialPropertyBlock();
                faceRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(EmissionId, c * faceEmission);
                faceRenderer.SetPropertyBlock(block);
            }

            if (digitRenderer == null || digitTextures == null) return;
            int index = Mathf.Clamp(Count, 0, digitTextures.Length - 1);
            if (digitTextures[index] == null) return;

            var digitBlock = new MaterialPropertyBlock();
            digitRenderer.GetPropertyBlock(digitBlock);
            digitBlock.SetTexture(BaseMapId, digitTextures[index]);
            digitRenderer.SetPropertyBlock(digitBlock);
        }
    }
}
