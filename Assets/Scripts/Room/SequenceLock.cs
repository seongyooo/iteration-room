using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // ROOM6'S RULE: three plates, pressed IN ORDER, each within a few seconds of the last.
    //
    // The chorus asks three people to act at the same moment. This asks them to act one after
    // another, at times they cannot see and did not agree on - which is a different and harder
    // thing, because a past self cannot react. It did what it did when it did it, and the living
    // player has to fit into the gap that leaves.
    //
    // THE WINDOW IS DELIBERATELY GENEROUS, and CLAUDE.md §1.5 is why. A ghost advances by elapsed
    // time and can cross several recorded frames in a single tick, so it lands on a moment
    // approximately rather than exactly. A tight window would be a puzzle whose failures are the
    // replay's rounding rather than the player's planning - the same trap `Drawer` stretches its
    // pulse to avoid, one level up.
    //
    // FAILING IS FREE. A wrong press resets the chain and nothing else; there is no lockout and no
    // cost beyond the seconds it took. What the room asks for is a plan, and a plan that has to be
    // re-attempted is still a plan.
    public class SequenceLock : RoomCondition
    {
        public SequenceNode[] nodes;

        // How long the chain will wait for the next plate. Three seconds is about seven metres of
        // walking, so the plates are placed nearer than that to each other and further than that
        // from any one standing point.
        public float windowSeconds = 3f;

        public RewardPlinth payout;

        private int reached;
        private float expiresAt;
        private bool latched;

        public override bool Satisfied => latched;

        // How far along the chain the room is, for its own readout. -1 while nothing is armed.
        public int Reached => latched ? (nodes?.Length ?? 0) : reached;

        private void Update()
        {
            if (latched || reached <= 0) return;
            if (Time.time <= expiresAt) return;
            // The window closed. Quietly - a chain that announced every lapse would be nagging.
            reached = 0;
            Refresh();
        }

        // Called by a node when it is stepped on. Returns whether it was the RIGHT one, so the node
        // can say so - a plate that reacts identically to a correct and a wrong press teaches
        // nothing about the order it is asking for.
        public bool Press(SequenceNode node)
        {
            if (latched || nodes == null || nodes.Length == 0) return false;

            int index = System.Array.IndexOf(nodes, node);
            if (index < 0) return false;

            bool inTime = reached == 0 || Time.time <= expiresAt;
            if (index != reached || !inTime)
            {
                // Wrong plate, or too late. Start again - and if the plate pressed happens to be the
                // FIRST, this press is that first press rather than a wasted one.
                reached = index == 0 ? 1 : 0;
                expiresAt = Time.time + windowSeconds;
                Refresh();
                return index == 0;
            }

            reached++;
            expiresAt = Time.time + windowSeconds;
            if (reached >= nodes.Length)
            {
                latched = true;
                if (payout != null) payout.Requested = true;
            }
            Refresh();
            return true;
        }

        public override void ResetCondition()
        {
            latched = false;
            reached = 0;
            expiresAt = 0f;
            if (payout != null) payout.Requested = false;
            if (nodes == null) return;
            foreach (SequenceNode n in nodes) n?.ResetNode();
            Refresh();
        }

        private void Refresh()
        {
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length; i++)
                nodes[i]?.SetState(latched || i < reached, !latched && i == reached);
        }
    }

    // One plate in the chain. Stood on rather than pressed, so it is a hold the recorder samples as
    // a level and the chain acts on the rising edge - the same shape `CountPad` uses, and for the
    // same §1.5 reason.
    public class SequenceNode : GhostInteractable
    {
        public SequenceLock chain;
        public float activationRadius = 0.6f;
        public float footClearance = 0.4f;

        public Renderer faceRenderer;
        public Color idleColour = new Color(0.42f, 0.44f, 0.5f);
        public Color nextColour = new Color(1f, 0.78f, 0.2f);
        public Color doneColour = new Color(0.25f, 1f, 0.5f);

        public AudioSource audioSource;
        public AudioClip goodClip;
        public AudioClip badClip;

        private bool playerOn;
        private readonly HashSet<GhostReplayer> ghostsOn = new HashSet<GhostReplayer>();
        private Color shown;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public override bool PlayerSignal => playerOn;

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            // Add() answering true IS the rising edge, and it means a ghost re-asserting a signal it
            // already holds cannot press twice.
            if (active) { if (ghostsOn.Add(ghost)) Fire(); }
            else ghostsOn.Remove(ghost);
        }

        public void ResetNode()
        {
            playerOn = false;
            ghostsOn.Clear();
        }

        public void SetState(bool done, bool isNext)
        {
            shown = done ? doneColour : isNext ? nextColour : idleColour;
            if (faceRenderer == null) return;
            var block = new MaterialPropertyBlock();
            faceRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, shown);
            block.SetColor(EmissionId, shown * (done || isNext ? 2.2f : 0.35f));
            faceRenderer.SetPropertyBlock(block);
        }

        private void FixedUpdate()
        {
            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;
            bool on = running && OnPlate;
            if (on == playerOn) return;
            playerOn = on;
            if (on) Fire();
        }

        private void Fire()
        {
            if (chain == null) return;
            bool right = chain.Press(this);

            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;
            AudioClip clip = right ? goodClip : badClip;
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        private bool OnPlate
        {
            get
            {
                Collider p = PlayerLookup.Collider;
                if (p == null || !p.enabled) return false;
                Bounds b = p.bounds;
                float dx = b.center.x - transform.position.x, dz = b.center.z - transform.position.z;
                if (dx * dx + dz * dz > activationRadius * activationRadius) return false;
                float feet = b.min.y;
                return feet >= transform.position.y - footClearance
                    && feet <= transform.position.y + footClearance;
            }
        }
    }
}
