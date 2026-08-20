using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // THE CONTROLS LIST ON THE SETTINGS PAGE: one row per verb, click the key to change it.
    //
    // It owns the *listening* state and nothing else. Which key a verb is on, what happens when the
    // new key is already taken, and what the defaults are all belong to `InputBindings` - this is the
    // panel that asks it, exactly the way `PauseMenu`'s slider owns no part of what sensitivity means.
    //
    // **ONE ROW LISTENS AT A TIME**, and that is not a nicety: two rows listening would both take the
    // same press, and which one won would be a coin toss on the order the rows happened to be in.
    public class KeyBindingPanel : MonoBehaviour
    {
        [System.Serializable]
        public class Row
        {
            public GameAction action;
            public Button button;
            public Text keyLabel;
        }

        public Row[] rows;
        public Button resetButton;
        public Text hint;

        // The page this list is on. `Update` is not stopped by an alpha of zero, so without this the
        // panel would keep listening behind a screen the player has left - and a press meant for the
        // title screen would land on a binding.
        public CanvasGroup group;

        // Through `Loc` rather than a `LocalizedText` on the hint, because this label has two
        // states and the component only knows one key. Subscribing to `Loc.Changed` below is what
        // keeps it honest when the language button is clicked with this page already open.
        private const string IdleHintKey = "set.bindHintIdle";
        private const string ListeningHintKey = "set.bindHintArmed";

        private int listening = -1;
        // The frame the listen began. The click that started it is a Mouse0 press, and without this
        // guard a row armed by clicking it would bind itself to the left mouse button on the spot.
        private int listeningFrame = -1;

        private void Awake()
        {
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                {
                    int index = i;
                    if (rows[i]?.button != null)
                        rows[i].button.onClick.AddListener(() => Listen(index));
                }

            if (resetButton != null)
                resetButton.onClick.AddListener(() =>
                {
                    InputBindings.ResetToDefaults();
                    Cancel();
                });

            Refresh();
        }

        private void OnEnable()
        {
            Loc.Changed += Refresh;
            Refresh();
        }

        private void OnDisable() => Loc.Changed -= Refresh;

        // Called by MainMenu when the settings page closes, so a row left armed does not survive into
        // the next visit to the page.
        public void Cancel()
        {
            listening = -1;
            Refresh();
        }

        // Read by MainMenu: a rebind in progress must swallow the whole keyboard, or ENTER assigns a
        // binding AND starts the game in the same frame.
        public bool Listening => listening >= 0;

        private void Listen(int index)
        {
            listening = index;
            listeningFrame = Time.frameCount;
            Refresh();
        }

        private void Update()
        {
            bool onScreen = group == null || group.blocksRaycasts;
            if (!onScreen)
            {
                if (Listening) Cancel();
                return;
            }

            if (!Listening) return;
            // The press that armed this row is still going down in the frame Listen ran.
            if (Time.frameCount == listeningFrame) return;

            // ESCAPE CANCELS AND NEVER BINDS. It is the one key that always opens the pause menu
            // (see `GameInput.PausePressed`), so letting it be assigned to something else would put
            // two meanings on the key a stuck player reaches for.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }

            foreach (KeyCode key in InputBindings.Listenable)
            {
                if (!Input.GetKeyDown(key)) continue;
                InputBindings.Set(rows[listening].action, key);
                Cancel();
                return;
            }
        }

        // Every row redrawn, not just the one that changed - because `InputBindings.Set` SWAPS on a
        // conflict, so assigning one key can change a second row the player was not looking at. A
        // partial refresh would leave that row displaying a key it no longer has.
        private void Refresh()
        {
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                {
                    Row row = rows[i];
                    if (row?.keyLabel == null) continue;
                    row.keyLabel.text = i == listening
                        ? "..."
                        : InputBindings.KeyLabel(InputBindings.Get(row.action));
                }

            if (hint != null)
            {
                hint.text = Loc.Get(Listening ? ListeningHintKey : IdleHintKey);
                Font korean = LocFont.Korean();
                if (Loc.Current == GameLanguage.Korean && korean != null) hint.font = korean;
            }
        }
    }
}
