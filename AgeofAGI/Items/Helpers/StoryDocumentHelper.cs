using System;
using AgeOfAgi.Items.Logic.StoryDocuments;

namespace AgeOfAgi.Items.Helpers
{
    internal static class StoryDocumentHelper
    {
        internal static void Enable() => QuorraDocuments.Enable();
        internal static void Shutdown() => QuorraDocuments.Shutdown();

        internal static void InitWanted(GameItem item, Action<GameItem> initializeAudio) => Init(item, initializeAudio, QuorraDocuments.Wanted);
        internal static void InitPhoneCard(GameItem item, Action<GameItem> initializeAudio) => Init(item, initializeAudio, QuorraDocuments.PhoneCard);

        private static void Init(GameItem item, Action<GameItem> initializeAudio, QuorraDocuments.Document document)
        {
            initializeAudio(item);
            QuorraDocuments.Bind(item, document);
            item.onLoaded += loaded => QuorraDocuments.Bind(loaded, document);
        }
    }
}
