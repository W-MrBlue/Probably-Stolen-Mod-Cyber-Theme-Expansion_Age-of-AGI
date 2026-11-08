using System;
using AgeOfAgi.Npcs;

internal static class DialogueTimingChecks
{
    internal static void Run()
    {
        var line = new Dialogue();
        Dialogue tail = ClientDialogueText.SetText(line, "CLIENT", @"呼......\p好了；... ...；\p继续");
        Assert(line.plainText == "呼......<waitfor=1>好了" && line.nextDialogue.plainText == "... ..." &&
            tail.plainText == "<waitfor=1>继续" && tail.nextDialogue == null,
            "Explicit pauses compile to native actions without splitting ellipses or adding dialogue nodes.");
        Action callback = () => { };
        var explicitEnd = new Dialogue().SetEndAction(callback);
        float originalDelay = explicitEnd.endActionDelay;
        ClientDialogueText.SetText(explicitEnd, "CLIENT", @"呼......\p");
        Assert(explicitEnd.plainText == "呼......<waitfor=1>" && explicitEnd.endActionDelay == originalDelay &&
            explicitEnd.endAction == callback,
            "A trailing explicit pause preserves the authored end action and native scheduling delay.");
        var onlyPause = new Dialogue();
        ClientDialogueText.SetText(onlyPause, "CLIENT", @"\p");
        Assert(onlyPause.plainText == "\u200B<waitfor=1>" && onlyPause.nextDialogue == null,
            "A pause-only bubble retains an invisible character to start the native typewriter action.");
        Console.WriteLine("PASS: explicit native pause commands, intact ellipses and unchanged native end-action delay.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
