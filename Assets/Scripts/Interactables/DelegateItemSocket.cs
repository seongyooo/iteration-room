using System;

namespace IterationRoom
{
    // One IItemSocket, backed by a delegate rather than a class of its own.
    //
    // ChessBoard.HomeSquare and CubeRoom.CubeSocket used to be the same six lines twice: a private
    // sealed class whose only job was "carry this itemId, and hand a ghost's surrender to my owner's
    // Seat method". Every accumulation room needs exactly this - one socket per carryable object,
    // registered so ItemRegistry can turn a ghost's recorded itemId back into a place to put it -
    // and the room-specific part is entirely the Seat call at the end. This is that adapter, once,
    // so the next room reuses it instead of writing a third copy.
    public sealed class DelegateItemSocket : IItemSocket
    {
        private readonly string itemId;
        private readonly Func<CarryableItem, bool> seat;

        // `seat` is typically a room's own Seat(CarryableItem) method, passed as a method group - a
        // bound delegate that closes over the room instance, so this socket needs no owner reference
        // of its own to null-check.
        public DelegateItemSocket(string itemId, Func<CarryableItem, bool> seat)
        {
            this.itemId = itemId;
            this.seat = seat;
        }

        public string AcceptedItemId => itemId;

        // False means the ghost keeps carrying it - the outcome every socket in this game gives when
        // its seat is already taken: the errand this ghost recorded was finished by someone else, and
        // it simply holds the item until its own timeline ends.
        public bool AcceptFromGhost(CarryableItem item) => seat != null && seat(item);
    }
}
