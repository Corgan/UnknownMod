using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnknownMod.Scripting;

namespace UnknownMod.Core
{
    public enum FindingLevel { Caution, Danger }

    public sealed class SandboxFinding
    {
        public FindingLevel Level;
        public string Why;
        public string Member;   // what the script references
        public string Where;    // Type::Method of the first use
        public int Count = 1;
    }

    public sealed class SandboxReport
    {
        public List<SandboxFinding> Findings = new();
        public int ApiVersion;
        public string Error;
        public byte[] Bytes;
        public string AssemblyName;
        public bool HasDanger => Findings.Any(f => f.Level == FindingLevel.Danger);
    }

    /// <summary>
    /// Reads a scripts.dll without running it and lists what it reaches for: other programs, the network, native code,
    /// runtime code generation, files. It also reads the script API version and gives the assembly a unique identity so
    /// a rebuilt scripts.dll can be loaded next to the old copy.
    /// This catches accidents and careless code. It is not a security boundary: code running inside the game process
    /// can always get around a static scan, so only install mods you trust.
    /// </summary>
    public static class ScriptSandbox
    {
        private sealed class Rule
        {
            public string Type;          // matched as a prefix of the declaring type's full name
            public string[] Members;     // null = any member; otherwise member name prefixes
            public FindingLevel Level;
            public string Why;
        }

        private static Rule R(string type, FindingLevel level, string why, params string[] members)
            => new Rule { Type = type, Level = level, Why = why, Members = members.Length == 0 ? null : members };

        private static readonly Rule[] Rules =
        {
            R("System.Diagnostics.Process", FindingLevel.Danger, "starts or controls other programs"),
            R("System.Net.", FindingLevel.Danger, "uses the network"),
            R("UnityEngine.Networking.", FindingLevel.Danger, "uses the network"),
            R("System.Reflection.Emit.", FindingLevel.Danger, "generates code at runtime"),
            R("System.Reflection.Assembly", FindingLevel.Danger, "loads other assemblies", "Load", "UnsafeLoad", "ReflectionOnlyLoad"),
            R("System.AppDomain", FindingLevel.Danger, "works with application domains"),
            R("Microsoft.Win32.", FindingLevel.Danger, "uses the Windows registry or Win32 APIs"),
            R("System.Runtime.InteropServices.Marshal", FindingLevel.Danger, "calls into native code", "GetDelegateForFunctionPointer", "GetFunctionPointerForDelegate", "GetExceptionPointers"),
            R("System.Runtime.InteropServices.NativeLibrary", FindingLevel.Danger, "calls into native code"),
            R("System.Environment", FindingLevel.Danger, "closes the game", "Exit", "FailFast"),
            R("UnityEngine.Application", FindingLevel.Caution, "quits the game or opens a web page", "Quit", "OpenURL"),
            R("System.IO.File", FindingLevel.Caution, "reads or writes files directly (ScriptContext keeps files inside the mod's own folder)"),
            R("System.IO.Directory", FindingLevel.Caution, "reads or writes folders directly"),
            R("System.IO.DirectoryInfo", FindingLevel.Caution, "reads or writes folders directly"),
            R("System.IO.FileInfo", FindingLevel.Caution, "reads or writes files directly"),
            R("System.IO.FileStream", FindingLevel.Caution, "reads or writes files directly"),
            R("HarmonyLib.", FindingLevel.Caution, "patches game code itself"),
            R("System.Threading.Thread", FindingLevel.Caution, "runs code on other threads; the game's objects are not safe to use there"),
            R("System.Threading.Tasks.", FindingLevel.Caution, "runs code on other threads; the game's objects are not safe to use there"),
            R("System.Type", FindingLevel.Caution, "looks types up by name, which hides what the script uses", "GetType"),
            R("System.Activator", FindingLevel.Caution, "creates objects from type names", "CreateInstance"),
        };

        private static readonly string[] SuspiciousText =
        {
            "cmd.exe", "powershell", "System.Diagnostics.Process", "http://", "https://", "WScript", "regsvr32", "rundll32"
        };

        public static SandboxReport Analyze(byte[] dll, ISet<string> usedAssemblyNames)
        {
            var report = new SandboxReport { Bytes = dll };
            AssemblyDefinition asm;
            try
            {
                asm = AssemblyDefinition.ReadAssembly(new MemoryStream(dll), new ReaderParameters { InMemory = true, ReadSymbols = false });
            }
            catch (Exception ex)
            {
                report.Error = "Not a readable .NET assembly: " + ex.Message;
                return report;
            }

            using (asm)
            {
                report.AssemblyName = asm.Name.Name;
                report.ApiVersion = ReadApiVersion(asm);

                var seen = new Dictionary<string, SandboxFinding>();
                foreach (var module in asm.Modules)
                    foreach (var type in module.GetTypes())
                        foreach (var method in type.Methods)
                            Scan(method, seen);
                report.Findings = seen.Values.OrderByDescending(f => f.Level).ThenBy(f => f.Why).ThenBy(f => f.Member).Take(300).ToList();

                if (usedAssemblyNames != null && usedAssemblyNames.Contains(asm.Name.Name))
                {
                    string unique = asm.Name.Name + "_r" + (usedAssemblyNames.Count + 1);
                    asm.Name.Name = unique;
                    asm.Name.HasPublicKey = false;
                    asm.Name.PublicKey = Array.Empty<byte>();
                    asm.MainModule.Attributes &= ~ModuleAttributes.StrongNameSigned;
                    try
                    {
                        using var ms = new MemoryStream();
                        asm.Write(ms);
                        report.Bytes = ms.ToArray();
                        report.AssemblyName = unique;
                    }
                    catch (Exception ex)
                    {
                        report.Error = "Could not prepare the assembly for reloading: " + ex.Message;
                    }
                }
            }
            usedAssemblyNames?.Add(report.AssemblyName);
            return report;
        }

        private static int ReadApiVersion(AssemblyDefinition asm)
        {
            foreach (var attr in asm.CustomAttributes)
                if (attr.AttributeType.FullName == typeof(ScriptApiAttribute).FullName && attr.ConstructorArguments.Count > 0 && attr.ConstructorArguments[0].Value is int v)
                    return v;
            return 0;
        }

        private static void Scan(MethodDefinition method, Dictionary<string, SandboxFinding> seen)
        {
            string where = method.DeclaringType.Name + "::" + method.Name;
            if (method.HasPInvokeInfo)
                Add(seen, FindingLevel.Danger, "calls into native code", method.PInvokeInfo?.Module?.Name + "!" + method.PInvokeInfo?.EntryPoint, where);
            if (!method.HasBody) return;

            foreach (var ins in method.Body.Instructions)
            {
                switch (ins.Operand)
                {
                    case MethodReference mr:
                        Check(mr.DeclaringType, mr.Name, seen, where);
                        break;
                    case FieldReference fr:
                        Check(fr.DeclaringType, fr.Name, seen, where);
                        break;
                    case TypeReference tr:
                        Check(tr, null, seen, where);
                        break;
                    case string s when ins.OpCode == OpCodes.Ldstr:
                        foreach (var word in SuspiciousText)
                            if (s.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                                Add(seen, FindingLevel.Caution, "contains text that looks like a program name or web address", word, where);
                        break;
                }
            }
        }

        private static void Check(TypeReference type, string member, Dictionary<string, SandboxFinding> seen, string where)
        {
            if (type == null) return;
            string full;
            try { full = type.GetElementType()?.FullName ?? type.FullName; }
            catch { full = type.FullName; }
            if (string.IsNullOrEmpty(full)) return;

            foreach (var rule in Rules)
            {
                if (!full.StartsWith(rule.Type, StringComparison.Ordinal)) continue;
                if (rule.Members != null && (member == null || !rule.Members.Any(m => member.StartsWith(m, StringComparison.Ordinal)))) continue;
                Add(seen, rule.Level, rule.Why, member == null ? full : full + "." + member, where);
            }
        }

        private static void Add(Dictionary<string, SandboxFinding> seen, FindingLevel level, string why, string member, string where)
        {
            string key = level + "|" + why + "|" + member;
            if (seen.TryGetValue(key, out var f)) { f.Count++; return; }
            seen[key] = new SandboxFinding { Level = level, Why = why, Member = member, Where = where };
        }
    }
}
