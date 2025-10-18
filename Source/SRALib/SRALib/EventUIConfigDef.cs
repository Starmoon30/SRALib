using UnityEngine;
using Verse;

namespace SRA
{
    public class EventUIConfigDef : Def
    {
        // General Style
        public GameFont labelFont = GameFont.Small;
        public bool drawBorders = true;
        public bool showDefName = true;
        public bool showLabel = true;
        public string defaultBackgroundImagePath;
        public Vector2 defaultWindowSize = new Vector2(1600f, 900f);

        // Virtual Layout Dimensions
        public Vector2 portraitSize = new Vector2(500f, 800f);
        public Vector2 nameSize = new Vector2(260f, 130f);
        public Vector2 textSize = new Vector2(650f, 500f);
        public float optionsWidth = 610f;
        
        // Virtual Layout Offsets
        public float textNameOffset = 20f;
        public float optionsTextOffset = 20f;
        // New Layout Dimensions
        public Vector2 newLayoutNameSize = new Vector2(200f, 50f);
        public Vector2 newLayoutportraitSize = new Vector2(300f, 400f);
        public Vector2 newLayoutTextSize = new Vector2(600f, 200f);
        public float newLayoutOptionsWidth = 600f;
        public float newLayoutPadding = 20f;
        public float newLayoutTextNameOffset = 20f;
        public float newLayoutOptionsTextOffset = 20f;
    }
}
