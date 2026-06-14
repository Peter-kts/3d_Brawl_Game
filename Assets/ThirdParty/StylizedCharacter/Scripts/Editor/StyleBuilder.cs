using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts.Editor
{
    public class StyleBuilder
    {
        private GUIStyle style = new GUIStyle();
        private Color bgColor;

        public StyleBuilder Background(float r, float g, float b, float alpha)
        {
            bgColor = new Color(r / 255, g / 255, b / 255, alpha / 255);
            return this;
        }
        public StyleBuilder Background(float rgb, float alpha = 255)
        {
            bgColor = new Color(rgb / 255, rgb / 255, rgb / 255, alpha / 255);
            return this;
        }

        public StyleBuilder Margin(int left, int right, int top, int bottom)
        {
            style.margin = new RectOffset(left, right, top, bottom);
            return this;
        }

        public StyleBuilder Margin(int horizontal, int vertical)
        {
            style.margin = new RectOffset(horizontal, horizontal, vertical, vertical);
            return this;
        }

        public StyleBuilder Margin(int all)
        {
            style.margin = new RectOffset(all, all, all, all);
            return this;
        }

        public StyleBuilder Margin(RectOffset offset)
        {
            style.margin = offset;
            return this;
        }

        public StyleBuilder Alignment(TextAnchor alignment)
        {
            style.alignment = alignment;
            return this;
        }

        public StyleBuilder FontSize(int size)
        {
            style.fontSize = size;
            return this;
        }

        public StyleBuilder FontStyle(FontStyle fstyle)
        {
            style.fontStyle = fstyle;
            return this;
        }

        public StyleBuilder FontColor(float r, float g, float b, float alpha)
        {
            style.normal.textColor = new Color(r / 255, g / 255, b / 255, alpha / 255);
            return this;
        }

        public StyleBuilder FontColor(Color color)
        {
            style.normal.textColor = color;
            return this;
        }

        public StyleBuilder Padding(int left, int right, int top, int bottom)
        {
            style.padding = new RectOffset(left, right, top, bottom);
            return this;
        }

        public StyleBuilder Padding(int horizontal, int vertical)
        {
            style.padding = new RectOffset(horizontal, horizontal, vertical, vertical);
            return this;
        }

        public StyleBuilder Padding(int all)
        {
            style.padding = new RectOffset(all, all, all, all);
            return this;
        }

        public StyleBuilder Padding(RectOffset offset)
        {
            style.padding = offset;
            return this;
        }

        public GUIStyle Build()
        {
            if (bgColor != new Color(0, 0, 0, 0))
            {
                var t = MakeTex(600, 1, bgColor);
                style.normal.background = t;
            }
            
            return style;
        }

        public StyleBuilder OnHover(GUIStyleState state)
        {
            style.onActive = state;
            return this;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];

            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();

            return result;
        }
    }
}