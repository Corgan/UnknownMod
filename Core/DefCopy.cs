using System.Reflection;
using Newtonsoft.Json;

namespace UnknownMod.Core
{
    /// <summary>
    /// Writes one definition's state into a live instance of the same type (other code holds references to it).
    /// Every serialisable member is copied, so values hidden by ShouldSerialize predicates are reset too, which
    /// JsonConvert.PopulateObject would leave untouched.
    /// </summary>
    public static class DefCopy
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

        public static void CopyInto(object target, object source)
        {
            if (target == null || source == null) return;
            var type = target.GetType();
            if (!type.IsInstanceOfType(source)) return;
            foreach (var f in type.GetFields(Flags))
            {
                if (f.IsInitOnly || f.IsLiteral || f.IsDefined(typeof(JsonIgnoreAttribute), true)) continue;
                f.SetValue(target, f.GetValue(source));
            }
            foreach (var p in type.GetProperties(Flags))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0 || p.IsDefined(typeof(JsonIgnoreAttribute), true)) continue;
                p.SetValue(target, p.GetValue(source));
            }
        }

        /// <summary>Deserialises a state snapshot and copies it over the live instance.</summary>
        public static bool RestoreFromJson(object target, string json)
        {
            if (target == null) return false;
            var fresh = JsonConvert.DeserializeObject(json, target.GetType());
            if (fresh == null) return false;
            CopyInto(target, fresh);
            return true;
        }
    }
}
