using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal sealed partial class ManagerForm
    {
        private Theme ResolveSavedTheme()
        {
            var name = ConfigString("theme");
            switch ((name ?? "").ToLowerInvariant())
            {
                case "ember": return Theme.Ember();
                case "frost": return Theme.Frost();
                case "forest": return Theme.Forest();
                case "custom":
                    var hex = ConfigString("customAccent");
                    if (!string.IsNullOrEmpty(hex))
                    {
                        try { return Theme.Custom(ColorTranslator.FromHtml(hex)); } catch { }
                    }
                    return Theme.Gilded();
                default: return Theme.Gilded();
            }
        }

        private void EnsureLayout()
        {
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(enabledDir);
            Directory.CreateDirectory(asiDir);
            Directory.CreateDirectory(Path.Combine(modsDir, "_enabled"));
            Directory.CreateDirectory(Path.Combine(modsDir, "_lang"));
            Directory.CreateDirectory(backupsDir);
            Directory.CreateDirectory(cacheDir);

            var state = Path.Combine(backupsDir, "state.json");
            if (!File.Exists(state))
            {
                File.WriteAllText(state, "{\r\n  \"overlay_groups\": [],\r\n  \"applied_mods\": []\r\n}", Encoding.UTF8);
            }

            var loadOrder = Path.Combine(enabledDir, "_load_order.json");
            if (!File.Exists(loadOrder))
            {
                File.WriteAllText(loadOrder, "[]\r\n", Encoding.UTF8);
            }
        }

        private Dictionary<string, object> LoadConfig()
        {
            if (!File.Exists(configPath))
            {
                var fresh = DefaultConfig();
                SaveConfig(fresh);
                return fresh;
            }

            try
            {
                var parsed = json.DeserializeObject(File.ReadAllText(configPath, Encoding.UTF8)) as Dictionary<string, object>;
                var merged = DefaultConfig();
                if (parsed != null)
                {
                    foreach (var pair in parsed)
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
                merged["modsPath"] = modsDir;
                return merged;
            }
            catch
            {
                return DefaultConfig();
            }
        }

        private Dictionary<string, object> DefaultConfig()
        {
            return new Dictionary<string, object>
            {
                ["gamePath"] = "",
                ["modsPath"] = modsDir,
                ["activeMods"] = new object[0],
                ["activeAsiMods"] = new object[0],
                ["activeLangMod"] = null,
                ["selectedLanguage"] = "english",
                ["uiLang"] = "en",
                ["modsApplied"] = false,
                ["modsInstalled"] = false,
                ["devMode"] = true,
                ["windowWidth"] = 1320,
                ["windowHeight"] = 820,
                ["windowLeft"] = null,
                ["windowTop"] = null,
                ["windowMaximized"] = false,
                ["colModsWidth"] = 320,
                ["colPatchesWidth"] = 700,
                ["colLogWidth"] = 360,
                ["theme"] = "Gilded",
                ["selectedGroups"] = new Dictionary<string, object>(),
                ["modOrder"] = new object[0]
            };
        }

        private string ConfigString(string key)
        {
            return config != null && config.ContainsKey(key) && config[key] != null ? Convert.ToString(config[key]) : "";
        }

        private bool ConfigBool(string key)
        {
            if (config == null || !config.ContainsKey(key) || config[key] == null) return false;
            try { return Convert.ToBoolean(config[key]); } catch { return false; }
        }

        private HashSet<string> ConfigStringSet(string key)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!config.ContainsKey(key) || config[key] == null)
            {
                return set;
            }

            if (config[key] is object[] array)
            {
                foreach (var item in array) set.Add(Convert.ToString(item));
            }
            else if (config[key] is System.Collections.ArrayList list)
            {
                foreach (var item in list) set.Add(Convert.ToString(item));
            }
            return set;
        }

        private List<string> ConfigStringList(string key)
        {
            var listOut = new List<string>();
            if (!config.ContainsKey(key) || config[key] == null)
            {
                return listOut;
            }

            if (config[key] is object[] array)
            {
                foreach (var item in array)
                {
                    var text = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(text)) listOut.Add(text);
                }
            }
            else if (config[key] is System.Collections.ArrayList list)
            {
                foreach (var item in list)
                {
                    var text = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(text)) listOut.Add(text);
                }
            }
            return listOut;
        }

        private Dictionary<string, object> ConfigDict(string key)
        {
            if (config.ContainsKey(key) && config[key] is Dictionary<string, object> dict)
            {
                return dict;
            }
            return new Dictionary<string, object>();
        }

        private void SaveConfig(Dictionary<string, object> data)
        {
            data["modsPath"] = modsDir;
            File.WriteAllText(configPath, json.Serialize(data), Encoding.UTF8);
        }

        // ---------------------------------------------------------- UI BUILD

    }
}
