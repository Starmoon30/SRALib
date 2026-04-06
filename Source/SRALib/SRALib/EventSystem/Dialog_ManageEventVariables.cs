using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SRA
{
    public class Dialog_ManageEventVariables : Window
    {
        private Vector2 scrollPosition;
        private Dictionary<string, string> editBuffers = new Dictionary<string, string>();
        private EventVariableManager manager;

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        public Dialog_ManageEventVariables()
        {
            forcePause = true;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            manager = Find.World.GetComponent<EventVariableManager>();
            RefreshBuffers();
        }

        private void RefreshBuffers()
        {
            editBuffers.Clear();
            foreach (var kvp in manager.GetAllVariables())
            {
                editBuffers[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            if (listing.ButtonText("Refresh"))
            {
                RefreshBuffers();
            }
            if (listing.ButtonText("Clear All Variables"))
            {
                manager.ClearAll();
                RefreshBuffers();
            }

            listing.GapLine();

            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, manager.GetAllVariables().Count * 32f);
            Widgets.BeginScrollView(listing.GetRect(inRect.height - 100f), ref scrollPosition, viewRect);

            Listing_Standard varListing = new Listing_Standard();
            varListing.Begin(viewRect);

            var allVars = manager.GetAllVariables().OrderBy(kvp => kvp.Key).ToList();

            foreach (var kvp in allVars)
            {
                Rect rowRect = varListing.GetRect(30f);
                string key = kvp.Key;
                object value = kvp.Value;
                string typeName = value?.GetType().Name ?? "null";

                Widgets.Label(rowRect.LeftPart(0.4f).Rounded(), $"{key} ({typeName})");

                string buffer = editBuffers[key];
                string newValue = Widgets.TextField(rowRect.RightPart(0.6f).LeftPart(0.8f).Rounded(), buffer);
                editBuffers[key] = newValue;

                if (Widgets.ButtonText(rowRect.RightPart(0.1f).Rounded(), "Set"))
                {
                    // Attempt to parse and set the variable
                    if (value is int)
                    {
                        if (int.TryParse(newValue, out int intVal)) manager.SetVariable(key, intVal);
                    }
                    else if (value is float)
                    {
                        if (float.TryParse(newValue, out float floatVal)) manager.SetVariable(key, floatVal);
                    }
                    else
                    {
                         manager.SetVariable(key, newValue);
                    }
                }
            }

            varListing.End();
            Widgets.EndScrollView();
            listing.End();
        }
    }
}