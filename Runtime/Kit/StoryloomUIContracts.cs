// Storyloom Unity Kit — presentation contracts.
// The director talks to its UI through these interfaces, not through the concrete widgets. The kit's uGUI widgets
// (DialogueUI, LocationBanner, PickupToast, InventoryHUD in StoryloomUI.cs) are the default implementations; to use your
// own UI stack (TextMeshPro, UI Toolkit, speech bubbles, comic panels), implement the matching interface on any
// MonoBehaviour and drop it into the director's "override" slot — the built-in widget is then ignored entirely.
// SpeechBubbleUI.cs is a complete working example.
//
// Contract notes:
//   · Say / Narrate / Choose are coroutines: the beat waits until they finish (Say/Narrate return once the player has
//     advanced past the line; Choose once `pick` has been called with the chosen option).
//   · ShowBark / ShowNarration are fire-and-forget one-shots that dismiss themselves.
//   · Hide is called when a beat ends — close whatever is open, cancel pending coroutine display state.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Storyloom
{
    /// <summary>The dialogue presenter: lines, narration, choices, one-shot barks.</summary>
    public interface IDialogueUI
    {
        /// <summary>Show one spoken line and finish when the player advances past it.</summary>
        IEnumerator Say(string speaker, string text, Sprite portrait, string emotion, AudioClip bark);
        /// <summary>Show narration (no speaker) and finish when the player advances. `ending` marks an ending's final text.</summary>
        IEnumerator Narrate(string title, string text, bool ending = false);
        /// <summary>Present the options and finish after calling `pick` with the player's choice. Locked options are
        /// included so they can be shown greyed with their lockReason; never pick one.</summary>
        IEnumerator Choose(List<StoryOption> options, Action<StoryOption> pick);
        /// <summary>One-shot line that dismisses itself ("...nothing to say yet").</summary>
        void ShowBark(string speaker, string text, Sprite portrait);
        /// <summary>One-shot narration that dismisses itself (signposts, examine text).</summary>
        void ShowNarration(string title, string text);
        /// <summary>A beat ended — close whatever is open.</summary>
        void Hide();
    }

    /// <summary>The location-arrival popup (name, region line, optional art, description).</summary>
    public interface ILocationBannerUI { void Show(string name, string region, Sprite art, string description); }

    /// <summary>The "Got X" popup.</summary>
    public interface IPickupToastUI { void Show(string message, Sprite icon); }

    /// <summary>The inventory panel. IsOpen gates player movement/look, so keep it truthful.</summary>
    public interface IInventoryUI { bool IsOpen { get; } void Toggle(); void Refresh(); }
}
