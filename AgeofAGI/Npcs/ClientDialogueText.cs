using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AgeOfAgi.Npcs
{
    internal static class ClientDialogueText
    {
        // Own the complete text chain; return its last line so callers attach
        // delivery/dismissal actions only after every bubble has been shown.
        internal static Dialogue SetText(Dialogue dialogue, string title, string text)
        {
            var lines = new List<string>();
            foreach (string segment in (text ?? string.Empty).Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
                AddLine(lines, segment);
            dialogue.nextDialogue = null;
            dialogue.SetText(title, CompilePauses(lines.Count > 0 ? lines[0] : string.Empty));
            for (int i = 1; i < lines.Count; i++)
            {
                dialogue = dialogue.NextDialogue();
                dialogue.SetText(title, CompilePauses(lines[i]));
            }
            return dialogue;
        }

        private static void AddLine(List<string> lines, string text)
        {
            string line = text.Trim();
            if (line.Length > 0) lines.Add(line);
        }

        private static string CompilePauses(string source)
        {
            string visible = Regex.Replace(source.Replace(@"\p", ""), "<[^>]*>", "");
            bool pauseOnly = string.IsNullOrWhiteSpace(visible) && source.Contains(@"\p");
            // The native typewriter skips zero-character text. Keep an invisible
            // character so a pause-only bubble can execute its native action.
            return (pauseOnly ? "\u200B" : "") + source.Replace(@"\p", "<waitfor=1>");
        }
    }
}
