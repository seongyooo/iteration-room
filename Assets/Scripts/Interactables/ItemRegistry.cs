using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Something that takes an item and keeps it: Room2's lock, and nothing else yet.
    //
    // An interface rather than a direct KeyLock reference on GhostReplayer, because a ghost's
    // recorded hand-over carries only an itemId - it has no idea what is on the other end, and it
    // should not. The second socket this game grows costs one AcceptFromGhost implementation.
    public interface IItemSocket
    {
        string AcceptedItemId { get; }

        // False means "not right now" and the item stays in the ghost's hands, which is a real
        // outcome rather than an error: Room2's lock refuses once its door is already open, and the
        // ghost then carries the key until its timeline runs out and drops it.
        bool AcceptFromGhost(CarryableItem item);
    }

    // Resolves an itemId to the objects that wear it - a SUPPLY of them, not one.
    //
    // Ghosts replay carries BY IDENTITY - the same decision balloon pops rest on, for the same
    // reason: an itemId means the same thing sixty seconds later, where a position does not. But an
    // id has to be turned back into an object somewhere, and a ghost cannot hold a reference it was
    // never given. Registration is self-service in OnEnable rather than wired by SceneBuilder,
    // because unlike `ghostInteractables` this is a lookup by name and has no bit positions to keep
    // stable - so nothing breaks if the order changes.
    //
    // WHY A LIST, when this used to reject a second claimant loudly. Popping a balloon requires the
    // pin IN HAND, for ghosts as for the player, and with one pin in the world that capped the whole
    // room at ONE popper at a time - so Room2's accumulation, the thing every iteration is supposed
    // to add to, did not survive its own rule. The accepted mitigation was always a supply rather
    // than a softer rule (docs/decisions.md), and a supply means several objects sharing an id.
    //
    // What the old error protected is worth being explicit about, because it is now gone: it caught
    // a NEW item accidentally named after an existing one. There is no way to tell that apart from a
    // deliberate pool from in here, so the check is replaced by a log line every time a pool grows
    // past one - an unintended collision shows up as an id you did not mean to have two of.
    //
    // "Exactly one object, never duplicated, never lost" is UNCHANGED by this. That invariant is
    // about each object, not about each id: three pins are three objects, each in exactly one of the
    // five states, each swept back to its own origin.
    //
    // Static state in a project with none elsewhere, so: entries are dropped in OnDisable, which is
    // what makes a scene reload clean.
    public static class ItemRegistry
    {
        private static readonly Dictionary<string, List<CarryableItem>> items =
            new Dictionary<string, List<CarryableItem>>();
        private static readonly Dictionary<string, IItemSocket> sockets =
            new Dictionary<string, IItemSocket>();

        public static void Register(CarryableItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return;

            if (!items.TryGetValue(item.itemId, out List<CarryableItem> pool))
            {
                pool = new List<CarryableItem>();
                items[item.itemId] = pool;
            }

            if (pool.Contains(item)) return;
            pool.Add(item);

            // The replacement for the duplicate-id error. Deliberately a plain log: a pool of three
            // pins is correct and says so, and an id that reports a size nobody intended is the
            // collision the old error existed to catch.
            if (pool.Count > 1)
                Debug.Log($"[ItemRegistry] id '{item.itemId}' is a supply of {pool.Count} "
                        + $"(added {item.name})");
        }

        public static void Unregister(CarryableItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return;
            if (!items.TryGetValue(item.itemId, out List<CarryableItem> pool)) return;

            pool.Remove(item);
            if (pool.Count == 0) items.Remove(item.itemId);
        }

        public static void RegisterSocket(IItemSocket socket)
        {
            if (socket == null || string.IsNullOrEmpty(socket.AcceptedItemId)) return;
            sockets[socket.AcceptedItemId] = socket;
        }

        public static void UnregisterSocket(IItemSocket socket)
        {
            if (socket == null || string.IsNullOrEmpty(socket.AcceptedItemId)) return;
            if (sockets.TryGetValue(socket.AcceptedItemId, out IItemSocket existing) && existing == socket)
                sockets.Remove(socket.AcceptedItemId);
        }

        // Every carryable back where SceneBuilder left it, whoever was holding it.
        //
        // THIS EXISTS BECAUSE PlayerHand.ReturnAll IS NOT ENOUGH ANY MORE, and the gap was a real
        // shipped bug: ReturnAll walks `taken`, which is what the PLAYER picked up. A key that a
        // GHOST fetched and put in the lock was never in that list, so nothing rewound it - it sat
        // in the socket with IsCarried still true, which made IsFreeForGhost false forever. The
        // ghost could never take it again, and Room2's door opened exactly once and then never
        // again. The symptom ("the ghost opens it, and next iteration it doesn't") points at the
        // replay; the cause was here, in the rewind.
        //
        // A sweep over the registry rather than another bookkeeping list, because the invariant is
        // about the objects themselves: at the top of an iteration every one of them is at origin,
        // regardless of which of the four places it spent the last sixty seconds in.
        public static void ReturnAllToOrigin()
        {
            foreach (List<CarryableItem> pool in items.Values)
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i] != null) pool[i].ReturnToOrigin();
        }

        // One this ghost may actually take: the first in the supply that nothing else has.
        //
        // Named for what it decides rather than left as a general Find, because with a pool the
        // question is no longer "which object is this" - it is "is there one going spare", and that
        // is the only question a replayed take has ever wanted. A ghost recorded taking "Tool" is
        // entitled to A pin, not to the pin it happened to hold sixty seconds ago; the recording
        // carries the id precisely so it does not depend on which physical object was involved.
        //
        // Returning null is a real answer, not a failure: the supply is finite, so a fifth ghost
        // reaching for a third pin gets nothing and its errand simply does not happen. That is the
        // same honest outcome as a ghost finding the key already taken.
        public static CarryableItem FindFreeForGhost(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (!items.TryGetValue(itemId, out List<CarryableItem> pool)) return null;

            for (int i = 0; i < pool.Count; i++)
            {
                CarryableItem item = pool[i];
                // Unity's fake-null: a destroyed object compares equal to null but is still a live
                // list entry, so each one has to be tested rather than trusted.
                if (item == null) continue;
                if (item.ghostCarryable && item.IsFreeForGhost) return item;
            }

            return null;
        }

        public static IItemSocket FindSocket(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            sockets.TryGetValue(itemId, out IItemSocket socket);
            return socket;
        }
    }
}
