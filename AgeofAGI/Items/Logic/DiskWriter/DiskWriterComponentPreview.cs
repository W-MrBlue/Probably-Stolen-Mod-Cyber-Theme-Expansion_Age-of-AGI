using System;
using UnityEngine;

namespace AgeOfAgi.Items.Logic.DiskWriter
{
    // A sprite-only widget: no inventory, item labels, grid or input handlers.
    internal sealed class DiskWriterComponentPreview : PixelElement, MachineryWidget
    {
        private const int Size = 48;
        private readonly TreeNodeRender imageNode;
        public bool canResize => false;
        public bool fillWidth => false;
        public int widthPixels => Size;
        public int heightPixels => Size;
        public TreeNodeRender handler { get; }
        public RectTransform rectTransform => handler.rectTransform;
        public TreeNodeRender inner => handler;
        internal string ItemId { get; private set; }

        internal DiskWriterComponentPreview()
        {
            handler = TreeNodeRender.Instantiate(this, "DiskWriterComponentPreview");
            handler.image.enabled = false;
            handler.image.raycastTarget = false;
            imageNode = TreeNodeRender.Instantiate().SetParent(handler);
            imageNode.image.enabled = false;
            imageNode.image.raycastTarget = false;
            Validate();
        }

        internal void SetSprite(string itemId, Sprite sprite)
        {
            ItemId = itemId;
            if (sprite != null) MachineryStyle.SetSimple(imageNode, sprite, Color.white);
            else imageNode.image.sprite = null;
            imageNode.image.enabled = sprite != null;
            imageNode.image.raycastTarget = false;
            Validate();
        }

        public void Validate()
        {
            rectTransform.sizeDelta = new Vector2(Size, Size);
            Sprite sprite = imageNode.image.sprite;
            if (sprite == null) return;
            float scale = Math.Min(1, Math.Min(Size / sprite.rect.width, Size / sprite.rect.height));
            int width = (int)Math.Round(sprite.rect.width * scale);
            int height = (int)Math.Round(sprite.rect.height * scale);
            MachineryStyle.Place(imageNode, (Size - width) / 2, (Size - height) / 2, width, height);
        }

        public void ResizePixels(int width, int height) => Validate();
        public bool Attach(PixelElement element) => false;
        public bool Detach(PixelElement element) => false;
        public int WidthPixelsClamp(int width) => Size;
        public int HeightPixelsClamp(int height) => Size;
        public int XClamp(int x) => 0;
        public int YClamp(int y) => 0;
    }
}
