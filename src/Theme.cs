using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal sealed class Theme
    {
        public string Name;
        public Color Background;
        public Color Panel;
        public Color Panel2;
        public Color Text;
        public Color Muted;
        public Color Accent;
        public Color Accent2;

        public static Theme Gilded() { return Make("Gilded", "#0c0d0a", "#171812", "#202115", "#f4ead1", "#a99d7c", "#d8a640", "#f4c767"); }
        public static Theme Ember() { return Make("Ember", "#120c09", "#1d1510", "#271b13", "#ffe6cd", "#b58d75", "#e28b3f", "#ffc06b"); }
        public static Theme Frost() { return Make("Frost", "#081018", "#101821", "#152334", "#e9f7ff", "#91a9ba", "#8cc7dd", "#bfefff"); }
        public static Theme Forest() { return Make("Forest", "#091109", "#101910", "#182718", "#eef5d8", "#98ad83", "#c0b15e", "#ece18c"); }

        // Custom theme: every palette slot is derived from the chosen accent so the whole UI shifts,
        // not just borders. Mirrors the built-ins (Frost/Forest also tint their backgrounds).
        public static Theme Custom(Color accent)
        {
            // Linear blend toward black for backgrounds, toward white for text.
            // Gives the panel a subtle hue-tinted dark instead of pure black.
            Color mix(Color a, Color b, float t) => Color.FromArgb(
                Math.Max(0, Math.Min(255, (int)(a.R * (1 - t) + b.R * t))),
                Math.Max(0, Math.Min(255, (int)(a.G * (1 - t) + b.G * t))),
                Math.Max(0, Math.Min(255, (int)(a.B * (1 - t) + b.B * t))));

            var black = Color.FromArgb(0, 0, 0);
            var white = Color.FromArgb(255, 255, 255);
            var midGray = Color.FromArgb(140, 140, 140);

            var bg = mix(accent, black, 0.94f);     // ~6% accent tint, near-black
            var panel = mix(accent, black, 0.91f);  // ~9%
            var panel2 = mix(accent, black, 0.86f); // ~14%
            var text = mix(accent, white, 0.85f);   // bright, slight accent tint
            var muted = mix(accent, midGray, 0.55f);// readable secondary text

            // Cap background luminance so a chosen pastel/yellow doesn't break readability.
            int lum(Color c) => c.R + c.G + c.B;
            if (lum(bg) > 60) bg = mix(bg, black, 0.6f);
            if (lum(panel) > 90) panel = mix(panel, black, 0.5f);
            if (lum(panel2) > 130) panel2 = mix(panel2, black, 0.4f);

            // Accent2 is the brighter highlight used for active tab text, top-bar pills, etc.
            var accent2 = mix(accent, white, 0.30f);

            return new Theme
            {
                Name = "Custom",
                Background = bg,
                Panel = panel,
                Panel2 = panel2,
                Text = text,
                Muted = muted,
                Accent = accent,
                Accent2 = accent2
            };
        }

        private static Theme Make(string name, string bg, string panel, string panel2, string text, string muted, string accent, string accent2)
        {
            return new Theme
            {
                Name = name,
                Background = ColorTranslator.FromHtml(bg),
                Panel = ColorTranslator.FromHtml(panel),
                Panel2 = ColorTranslator.FromHtml(panel2),
                Text = ColorTranslator.FromHtml(text),
                Muted = ColorTranslator.FromHtml(muted),
                Accent = ColorTranslator.FromHtml(accent),
                Accent2 = ColorTranslator.FromHtml(accent2)
            };
        }
    }
}
