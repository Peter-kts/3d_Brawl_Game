using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts.Editor
{
    public class StyleStateBuilder
    {
        private GUIStyleState state = new GUIStyleState(); 
        private Color bgColor;

        public StyleStateBuilder Background(float r, float g, float b, float alpha)
        {
            bgColor = new Color(r / 255, g / 255, b / 255, alpha / 255);
            return this;
        }
        public StyleStateBuilder Background(float rgb, float alpha = 255)
        {
            bgColor = new Color(rgb / 255, rgb / 255, rgb / 255, alpha / 255);
            return this;
        }
        public StyleStateBuilder FontColor(float r, float g, float b, float alpha)
        {
            state.textColor = new Color(r / 255, g / 255, b / 255, alpha / 255);
            return this;
        }

        public StyleStateBuilder FontColor(Color color)
        {
            state.textColor = color;
            return this;
        }
        public GUIStyleState Build()
        {
            if (bgColor != new Color(0, 0, 0, 0))
            {
                var t = MakeTex(600, 1, bgColor);
                state.background = t;
            }
                
            return state;
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