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

    // Resolves an itemId to the one object that wears it.
    //
    // Ghosts replay carries BY IDENTITY - the same decision balloon pops rest on, for the same
    // reason: an itemId means the same thing sixty seconds later, where a position does not. But an
    // id has to be turned back into an object somewhere, and a ghost cannot hold a reference it was
    // never given. Registration is self-service in OnEnable rather than wired by SceneBuilder,
    // because unlike `ghostInteractables` this is a lookup by name and has no bit positions to keep
    // stable - so nothing breaks if the order changes.
    //
    // Static state in a project with none elsewhere, so: it is cleared on registration failure and
    // both dictionaries drop their entry in OnDisable, which is what makes a scene reload clean.
    public static class ItemRegistry
    {
        private static readonly Dictionary<string, CarryableItem> items =
            new Dictionary<string, CarryableItem>();
        private static readonly Dictionary<string, IItemSocket> sockets =
            new Dictionary<string, IItemSocket>();

        public static void Register(CarryableItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return;
            // Two objects claiming one id would make "there is exactly one of each" false, which is
            // the invariant the whole possession design rests on. Loud, because the symptom - a
            // ghost carrying the wrong object - would be baffling to track down from the outside.
            if (items.TryGetValue(item.itemId, out CarryableItem existing) && existing != null && existing != item)
            {
                Debug.LogError($"[ItemRegistry] two items claim id '{item.itemId}': "
                    + $"{existing.name} and {item.name}. Ghost carries will pick one arbitrarily.");
                return;
            }
            items[item.itemId] = item;
        }

        public static void Unregister(CarryableItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) return;
            if (items.TryGetValue(item.itemId, out CarryableItem existing) && existing == item)
                items.Remove(item.itemId);
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
            foreach (CarryableItem item in items.Values)
                if (item != null) item.ReturnToOrigin();
        }

        public static CarryableItem Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            items.TryGetValue(itemId, out CarryableItem item);
            // Unity's fake-null: a destroyed object compares equal to null but is still a live
            // dictionary value, so the entry has to be tested rather than trusted.
            return item != null ? item : null;
        }

        public static IItemSocket FindSocket(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            sockets.TryGetValue(itemId, out IItemSocket socket);
            return socket;
        }
    }
}
