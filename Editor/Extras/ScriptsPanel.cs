using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnknownMod.Core;

namespace UnknownMod.Editor.Tabs
{
    /// <summary>What each mod's scripts.dll did when it loaded, the handlers it registered, how they behave, and a starter project generator.</summary>
    public sealed class ScriptsPanel
    {
        private readonly ModEditor _editor;
        private Vector2 _scroll;
        private string _expandedMod = "";
        private string _lastScaffold = "";

        private static readonly string[] Modes = { "Block", "Warn", "Off" };

        public ScriptsPanel(ModEditor editor) { _editor = editor; }

        public void DrawPanel()
        {
            GUILayout.Label("<color=#888>scripts.dll adds behaviour that data alone cannot express. The scan below is a quick check for things a script has no business doing (starting programs, using the network). It is not a security boundary: only run scripts from people you trust.</color>", EditorStyles.RichLabel);

            DrawMode();
            EditorStyles.Separator();
            DrawScaffold();
            EditorStyles.Separator();

            _scroll = GUILayout.BeginScrollView(_scroll);
            var sets = ScriptLoader.Sets;
            if (sets.Count == 0)
                GUILayout.Label("<color=#888>No mod has loaded a scripts.dll yet.</color>", EditorStyles.RichLabel);
            foreach (var set in sets.ToList()) DrawSet(set);
            GUILayout.EndScrollView();
        }

        private static void DrawMode()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Safety scan", GUILayout.Width(90));
            int cur = Math.Max(0, Array.IndexOf(Modes, ScriptLoader.SandboxMode));
            int sel = GUILayout.Toolbar(cur, Modes, GUILayout.Width(240));
            if (sel != cur && Plugin.ScriptSandbox != null) Plugin.ScriptSandbox.Value = Modes[sel];
            GUILayout.EndHorizontal();
            string help = ScriptLoader.SandboxMode switch
            {
                "Off" => "<color=#e64d4d>Off:</color> scripts load without being checked.",
                "Warn" => "<color=#e6b84d>Warn:</color> findings are shown here and logged, and every script still loads.",
                _ => "<b>Block:</b> a script with a serious finding does not load unless you trust that mod below. Takes effect on the next load or reload.",
            };
            GUILayout.Label(help, EditorStyles.RichLabel);
        }

        private void DrawScaffold()
        {
            var p = ModManagerPanel.ActiveProject;
            GUILayout.BeginHorizontal();
            GUI.enabled = p != null;
            if (GUILayout.Button(p == null ? "Create script project (open a mod first)" : $"Create script project for {p.ModId}", GUILayout.Width(320)))
                _lastScaffold = Scaffold(p.ModId);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_lastScaffold))
                GUILayout.Label(_lastScaffold, EditorStyles.RichLabel);
        }

        private void DrawSet(ScriptSet set)
        {
            string color = set.Status switch
            {
                ScriptStatus.Loaded => "#66cc66",
                ScriptStatus.Blocked => "#e6b84d",
                ScriptStatus.Failed => "#e64d4d",
                _ => "#888888",
            };
            bool open = string.Equals(_expandedMod, set.ModId, StringComparison.OrdinalIgnoreCase);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(open ? "v" : ">", EditorStyles.MiniButton, GUILayout.Width(24))) _expandedMod = open ? "" : set.ModId;
            GUILayout.Label($"<b>{set.ModId}</b>  <color={color}>{set.Status}</color>  <color=#888>{set.Message}</color>", EditorStyles.RichLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload", EditorStyles.MiniButton, GUILayout.Width(60)))
            {
                var r = ScriptLoader.ReloadMod(set.ModId);
                EditorStatus.Toast(r == null ? "No scripts.dll." : $"{set.ModId}: {r.Status.ToString().ToLowerInvariant()}, {r.Message}", r != null && r.Status != ScriptStatus.Loaded);
            }
            GUILayout.EndHorizontal();

            if (open) DrawSetDetails(set);
            GUILayout.EndVertical();
        }

        private static void DrawSetDetails(ScriptSet set)
        {
            GUILayout.Label($"<color=#888>{set.Path}\nfingerprint {Short(set.Hash)}   script API {(set.ApiVersion == 0 ? "not declared" : set.ApiVersion.ToString())}   loaded {set.LoadedAt:HH:mm:ss}</color>", EditorStyles.RichLabel);

            bool trusted = ScriptLoader.IsTrusted(set.ModId);
            GUILayout.BeginHorizontal();
            bool t = GUILayout.Toggle(trusted, " Trust this mod's scripts (skips the Block rule for it)");
            if (t != trusted)
            {
                ScriptLoader.SetTrusted(set.ModId, t);
                if (t && set.Status == ScriptStatus.Blocked) ScriptLoader.ReloadMod(set.ModId);
            }
            GUILayout.EndHorizontal();

            if (set.Findings.Count > 0)
            {
                GUILayout.Label("<b>Scan findings</b>", EditorStyles.RichLabel);
                foreach (var f in set.Findings.OrderByDescending(x => x.Level))
                {
                    string c = f.Level == FindingLevel.Danger ? "#e64d4d" : "#e6b84d";
                    GUILayout.Label($"  <color={c}>{f.Level}</color> {f.Why}  <color=#888>{f.Member} in {f.Where}{(f.Count > 1 ? $" (+{f.Count - 1} more)" : "")}</color>", EditorStyles.RichLabel);
                }
            }
            else if (set.Status == ScriptStatus.Loaded)
                GUILayout.Label("<color=#888>The scan found nothing unusual.</color>", EditorStyles.RichLabel);

            if (set.Handlers.Count > 0)
            {
                GUILayout.Label("<b>Handlers</b>", EditorStyles.RichLabel);
                foreach (var h in set.Handlers) DrawHandler(h);
            }
        }

        private static void DrawHandler(ScriptHandlerInfo h)
        {
            GUILayout.BeginHorizontal();
            string state = h.Disabled ? "<color=#e64d4d>off</color>" : "<color=#66cc66>on</color>";
            string stats = $"{h.Calls} calls, {h.AverageMs:0.000} ms avg" + (h.SlowCalls > 0 ? $", {h.SlowCalls} slow" : "") + (h.Errors > 0 ? $", <color=#e64d4d>{h.Errors} errors</color>" : "");
            GUILayout.Label($"  {state} {h.Label}  <color=#888>{HookText(h.Hooks)}  {stats}</color>", EditorStyles.RichLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(h.Disabled ? "Enable" : "Disable", EditorStyles.MiniButton, GUILayout.Width(60)))
                ScriptHost.SetDisabled(h, !h.Disabled);
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(h.LastError))
                GUILayout.Label($"      <color=#e64d4d>{h.LastError}</color>", EditorStyles.RichLabel);
        }

        private static string HookText(ScriptHook hooks) => hooks == 0 ? "" : hooks.ToString().Replace(", ", " ");

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "-" : s.Length > 12 ? s.Substring(0, 12) : s;

        // ---------------------------------------------------------------- scaffold

        private static string Scaffold(string modId)
        {
            try
            {
                string mod = ModProjectLoader.ModFolder(modId);
                string dir = Path.Combine(mod, "scripts");
                string csproj = Path.Combine(dir, "scripts.csproj");
                string cs = Path.Combine(dir, "MyScript.cs");
                if (File.Exists(csproj)) return $"<color=#e6b84d>{dir} already has a scripts.csproj; nothing was changed.</color>";

                Directory.CreateDirectory(dir);
                string game = BepInEx.Paths.GameRootPath;
                string managed = Path.Combine(game, "AcrossTheObelisk_Data", "Managed");
                string plugin = typeof(Plugin).Assembly.Location;
                string ns = Sanitize(modId);

                File.WriteAllText(csproj, ProjectTemplate.Replace("%NS%", ns).Replace("%MANAGED%", managed).Replace("%PLUGIN%", plugin));
                File.WriteAllText(cs, SourceTemplate.Replace("%NS%", ns).Replace("%MOD%", modId));
                return $"<color=#66cc66>Created {dir}</color>\nOpen it in Visual Studio or run <b>dotnet build</b> inside it. The build writes scripts.dll into the mod folder, and the game reloads it by itself.";
            }
            catch (Exception e)
            {
                return $"<color=#e64d4d>Could not create the project: {e.Message}</color>";
            }
        }

        private static string Sanitize(string id)
        {
            var chars = id.Where(char.IsLetterOrDigit).ToArray();
            string s = new string(chars);
            if (s.Length == 0 || char.IsDigit(s[0])) s = "Mod" + s;
            return s;
        }

        private const string ProjectTemplate = @"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>scripts</AssemblyName>
    <RootNamespace>%NS%</RootNamespace>
    <LangVersion>latest</LangVersion>
    <!-- scripts.dll goes into the mod folder, next to the mod's json -->
    <OutputPath>..\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <DebugType>none</DebugType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.NETFramework.ReferenceAssemblies"" Version=""1.0.3"" PrivateAssets=""all"" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include=""Assembly-CSharp""><HintPath>%MANAGED%\Assembly-CSharp.dll</HintPath><Private>false</Private></Reference>
    <Reference Include=""UnityEngine""><HintPath>%MANAGED%\UnityEngine.dll</HintPath><Private>false</Private></Reference>
    <Reference Include=""UnityEngine.CoreModule""><HintPath>%MANAGED%\UnityEngine.CoreModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include=""com.corgan.UnknownMod""><HintPath>%PLUGIN%</HintPath><Private>false</Private></Reference>
  </ItemGroup>

</Project>
";

        private const string SourceTemplate = @"using UnknownMod.Scripting;
using Cards;

// Tells UnknownMod which script API this was built against, so a game update can warn instead of crashing.
[assembly: ScriptApi(2)]

namespace %NS%
{
    // Override only the hooks you need. A hook that throws is logged; one that keeps throwing is switched off.
    public class MyScript : ModScript
    {
        public override void OnLoad()
        {
            Context.Log(""scripts loaded for %MOD%"");
        }

        public override void OnCombatEvent(Enums.EventActivation activation, Character owner, Character target, int auxInt, string auxString, Character caster)
        {
            if (activation == Enums.EventActivation.BeginCombat && owner != null && owner.IsHero)
                Context.Log($""{owner.SourceName} enters the fight"");
        }

        public override void OnCombatEnd(bool won)
        {
            Context.Log(won ? ""Victory"" : ""Defeat"");
        }
    }
}
";
    }
}
