using System;
using System.Runtime.CompilerServices;

namespace LightGreen.Core
{
    /// <summary>
    /// Puts LightGreen's short messages ("Filter: Noir") into the game's own chat box, on this screen only. The game posts its
    /// own local lines the same way (the chat-muted notice, the cheats warning, vote-kick results): TextChatUi.ShowMessage
    /// adds a line to this player's chat and sends nothing over the network.
    /// </summary>
    internal static class ChatNotices
    {
        private static bool _broken;

        /// <summary>Posts the line to the chat box; false when there is no chat box right now, or the game's chat has
        /// changed (then never again this session, with one warning in the log).</summary>
        internal static bool TryPost(string text)
        {
            if (_broken) return false;
            try
            {
                return Post(Format(text));
            }
            catch (Exception e)
            {
                // A game update renamed or changed the chat: the caller shows the message on screen instead.
                _broken = true;
                Plugin.Log.LogWarning($"Couldn't post to the game's chat ({e.GetType().Name}: {e.Message}); " +
                                      "LightGreen's messages show in the top-left corner instead.");
                return false;
            }
        }

        // Kept apart so a missing type or method fails when this is compiled, inside TryPost's try, not TryPost itself.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Post(string line)
        {
            if (!TextChatUi.HasInstance) return false;
            TextChatUi.ShowMessage(line);
            return true;
        }

        /// <summary>"LightGreen · " in bold green, then the message as plain text: a name with "&lt;" in it stays as typed.</summary>
        internal static string Format(string text) =>
            "<b><color=#6FD98A>LightGreen</color></b> · <noparse>" + (text ?? "").Replace("</noparse>", "") + "</noparse>";
    }
}
