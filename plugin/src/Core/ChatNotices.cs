using System;
using System.Runtime.CompilerServices;

namespace LightGreen.Core
{
    /// <summary>
    /// Puts LightGreen's short messages into the game's own chat box, on this screen only, looking like a player's line:
    /// "LightGreen: Filter: Noir". The game posts its own local lines the same way (the chat-muted notice, the cheats
    /// warning, vote-kick results): TextChatUi.ShowMessage adds a line to this player's chat and sends nothing.
    /// </summary>
    internal static class ChatNotices
    {
        internal const string Name = "LightGreen";
        private static bool _broken;

        /// <summary>Posts the line to the chat box; false when there is no chat box right now, or the game's chat has
        /// changed (then never again this session, with one warning in the log).</summary>
        internal static bool TryPost(string text)
        {
            if (_broken) return false;
            try
            {
                return Post(text ?? "");
            }
            catch (Exception e) when (e is MissingMemberException || e is TypeLoadException)
            {
                // A game update renamed or changed the chat: the caller shows the message on screen instead.
                _broken = true;
                Plugin.Log.LogWarning($"Couldn't post to the game's chat ({e.GetType().Name}: {e.Message}); " +
                                      "LightGreen's messages show in the top-left corner instead.");
                return false;
            }
            catch (Exception)
            {
                // Something not ready yet (say, while a scene loads): the top-left corner this time.
                return false;
            }
        }

        // Kept apart so a missing type or method fails when this is compiled, inside TryPost's try, not TryPost itself.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Post(string text)
        {
            if (!TextChatUi.HasInstance) return false;
            TextChatUi.ShowMessage(Line(text));
            return true;
        }

        /// <summary>
        /// The line as the game builds a player's message: the name in the game's highlight colour, ": ", then the
        /// text with formatting switched off (so a name with "&lt;" in it shows as typed).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Line(string text)
        {
            UiSettings ui = GameManager.UiSettings;
            string name = ui != null ? ui.ApplyColorTag(Name, TextHighlight.Regular) : Name;
            return name + ": " + GameManager.RichTextNoParse(text);
        }
    }
}
