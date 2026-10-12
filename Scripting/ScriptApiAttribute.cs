using System;

namespace UnknownMod.Scripting
{
    /// <summary>
    /// Declares which version of the UnknownMod script API a scripts.dll was written against.
    /// Put <c>[assembly: ScriptApi(2)]</c> in the script project; a scripts.dll that asks for a newer API than the
    /// installed UnknownMod provides is refused with a clear message instead of failing in the middle of a fight.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class ScriptApiAttribute : Attribute
    {
        /// <summary>The API version this build of UnknownMod provides.</summary>
        public const int Current = 2;

        public int MinVersion { get; }
        public ScriptApiAttribute(int minVersion) { MinVersion = minVersion; }
    }
}
