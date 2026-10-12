using System;
using UnityEngine;

namespace UnknownMod.Editor
{
    /// <summary>
    /// Editor-wide feedback: when the last file write happened, whether a write failed, and short-lived toast messages.
    /// Every JSON write goes through ModProjectLoader.AtomicWrite, so autosave in every editor reports here automatically.
    /// </summary>
    public static class EditorStatus
    {
        private static float _lastSaveTime = -1000f;
        private static string _lastSavedFile;
        private static int _saveCount;
        private static string _lastError;
        private static float _lastErrorTime = -1000f;
        private static string _toast;
        private static bool _toastWarn;
        private static float _toastTime = -1000f;

        private const float ToastSeconds = 5f;
        private const float ErrorSeconds = 30f;

        public static void Saved(string path)
        {
            _lastSaveTime = Time.realtimeSinceStartup;
            _lastSavedFile = System.IO.Path.GetFileNameWithoutExtension(path);
            _saveCount++;
        }

        public static void Failed(string path, Exception ex)
        {
            _lastErrorTime = Time.realtimeSinceStartup;
            _lastError = $"{System.IO.Path.GetFileName(path)}: {ex.Message}";
        }

        /// <summary>Short message shown in the header for a few seconds.</summary>
        public static void Toast(string message, bool warn = false)
        {
            _toast = message;
            _toastWarn = warn;
            _toastTime = Time.realtimeSinceStartup;
        }

        /// <summary>Rich-text status for the right side of the header: a toast, a recent save error, or the age of the last save.</summary>
        public static string HeaderText()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _toastTime < ToastSeconds && !string.IsNullOrEmpty(_toast))
                return _toastWarn ? $"<color=#ffcc66>{_toast}</color>" : $"<color=#9fe39f>{_toast}</color>";
            if (_lastError != null && now - _lastErrorTime < ErrorSeconds)
                return $"<color=#ff6666>\u2716 SAVE FAILED  {_lastError}</color>";
            if (_saveCount == 0) return "<color=#777>no changes yet</color>";
            float age = now - _lastSaveTime;
            string when = age < 2f ? "just now" : age < 60f ? $"{(int)age}s ago" : $"{(int)(age / 60f)}m ago";
            return $"<color=#88cc88>\u2714 saved {when}</color> <color=#777>{_lastSavedFile}</color>";
        }
    }

    public enum EditorCommand { None, Duplicate, Undo, Redo }

    /// <summary>What the shell (header buttons, Ctrl+D / Ctrl+Z / Ctrl+Y) can ask the active entity editor to do.</summary>
    public interface IEntityActions
    {
        bool CanDuplicate { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        void Run(EditorCommand command);
    }

    /// <summary>
    /// Routes shell commands to whichever entity editor drew the panel this frame.
    /// A command is queued from an input event and run at the next Layout event, because changing the control set
    /// in the middle of an input event desyncs IMGUI layout.
    /// </summary>
    public static class EditorActions
    {
        /// <summary>Editor that drew the panel in the current OnGUI pass.</summary>
        public static IEntityActions Current;

        /// <summary>Editor that drew the panel in the previous pass; used by header buttons so their enabled state is stable.</summary>
        public static IEntityActions Previous;

        public static EditorCommand Pending;

        /// <summary>A structural change (e.g. delete) requested mid-draw; runs at the next Layout, before the panel is built.</summary>
        public static Action PendingAction;

        /// <summary>Registers the editor as current and runs a queued command on it. Call at the top of the editor's DrawPanel.</summary>
        public static void Register(IEntityActions editor)
        {
            Current = editor;
            if (Event.current.type != EventType.Layout) return;
            var action = PendingAction;
            if (action != null)
            {
                PendingAction = null;
                action();
            }
            if (Pending == EditorCommand.None) return;
            var command = Pending;
            Pending = EditorCommand.None;
            editor.Run(command);
        }
    }
}
