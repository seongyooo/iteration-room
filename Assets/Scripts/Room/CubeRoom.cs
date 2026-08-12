using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Room2East's puzzle: six cubes scattered across the floor, each carrying a symbol, and six
    // recesses in the walls carrying the same six. Put every cube in the recess that matches it and
    // a plinth rises in the middle of the room with the blue sphere on it.
    //
    // SYMBOLS, NOT COLOURS, and that is the room's whole design rule. A puzzle matched by colour is a
    // puzzle a colour-blind player cannot see and one a dim room cannot show; a spade is a spade at
    // any brightness. The cubes and the plates are the same six glyphs in the same near-black ink.
    //
    // THE SECOND ACCUMULATION ROOM, and deliberately the same shape as the first (`ChessBoard`): the
    // work is errands, the loop divides them, and every delivery a past self made replays. What it is
    // NOT is a memory test - each cube says on its face where it goes, and its recess lights up while
    // it is in your hands, so the cost is the walk.
    //
    // GHOSTS TIDY IT FOR FREE, by the same route chess uses and for the same reason: one `IItemSocket`
    // per CUBE, each of which is that cube's own recess. A ghost's recorded hand-over carries an
    // itemId, `ItemRegistry` turns it into a socket, `GhostReplayer` puts the cube in it and learns
    // nothing about symbols.
    public class CubeRoom : MonoBehaviour
    {
        // Parallel arrays filled by SceneBuilder: cubes[i] belongs in slots[i]. Two arrays rather
        // than a pair type because that is what the rest of this project does with wire data.
        public CarryableItem[] cubes;
        public SymbolSlot[] slots;

        // What the room pays out. Driven rather than watched: this class owns the rule, and
        // `RewardPlinth` owns what rising looks like.
        public RewardPlinth reward;

        private readonly Dictionary<string, SymbolSlot> slotById = new Dictionary<string, SymbolSlot>();
        private readonly List<IItemSocket> sockets = new List<IItemSocket>();
        private readonly HashSet<string> seated = new HashSet<string>();

        public int CubeCount => cubes != null ? cubes.Length : 0;
        public int SeatedCount => seated.Count;
        public bool IsSolved => CubeCount > 0 && seated.Count >= CubeCount;

        private void Awake()
        {
            int count = Mathf.Min(CubeCount, slots != null ? slots.Length : 0);
            for (int i = 0; i < count; i++)
            {
                CarryableItem cube = cubes[i];
                SymbolSlot slot = slots[i];
                if (cube == null || slot == null || string.IsNullOrEmpty(cube.itemId)) continue;
                if (slotById.ContainsKey(cube.itemId)) continue;

                slotById[cube.itemId] = slot;
                sockets.Add(new DelegateItemSocket(cube.itemId, Seat));
            }
        }

        private void OnEnable()
        {
            foreach (IItemSocket socket in sockets) ItemRegistry.RegisterSocket(socket);
        }

        private void OnDisable()
        {
            foreach (IItemSocket socket in sockets) ItemRegistry.UnregisterSocket(socket);
        }

        // -1 style answer for anything that is not a cube of this room: false. It is what SymbolSlot
        // and the ghost path both branch on.
        public bool Accepts(string itemId) => !string.IsNullOrEmpty(itemId) && slotById.ContainsKey(itemId);

        // Asked BEFORE the cube leaves anyone's hands, so a hand-over is only ever begun when it can
        // be finished. `PlayerHand.Surrender` writes the event into the recording, and a recorded
        // surrender that did not happen is a past self doing something the player did not.
        public bool CanSeat(string itemId) => Accepts(itemId) && !seated.Contains(itemId);

        public bool IsSeated(string itemId) => seated.Contains(itemId);

        // The cube goes in its recess. InsertInto, not DropAt: a seated cube is OUT OF PLAY like the
        // key in Room2's lock - not takeable, not free for a ghost, and rewound only by the loop.
        // Without that a past self could pull one back out and the room would go backwards while the
        // player watched.
        public bool Seat(CarryableItem cube)
        {
            if (cube == null || !CanSeat(cube.itemId)) return false;

            SymbolSlot slot = slotById[cube.itemId];
            slot.Accept(cube);
            seated.Add(cube.itemId);

            // Set rather than toggled, so the answer is always the rule and never a running tally
            // that could drift out of step with it.
            if (reward != null) reward.Requested = IsSolved;
            return true;
        }

        // The top of an iteration. The cubes themselves are already back on the floor by the time this
        // runs (`ItemRegistry.ReturnAllToOrigin`), so this only forgets who was home and takes the
        // reward's request back - the one piece of state here the sweep cannot reach. Clearing
        // `seated` is enough on its own: SymbolSlot.Filled reads it directly rather than keeping a
        // second copy, so every slot is already unfilled the instant this line runs.
        public void ResetRoom()
        {
            seated.Clear();
            if (reward != null) reward.Requested = false;
        }
    }
}
