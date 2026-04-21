using System.Collections.Generic;
using UnityEngine;

namespace UnknownMod.Runtime
{
    /// <summary>
    /// Synchronizes a puppet Animator's state machine to a host Animator.
    /// Runs in Update (before Animator evaluation) so the puppet's Animator
    /// starts each frame at the same state + normalized time as the host.
    ///
    /// Because each AnimatorController names its states differently
    /// (e.g. sylvieIdle vs buhomago_idle), we build a hash→hash mapping
    /// at init time by matching semantic suffixes (idle, attack, cast, hit…).
    /// </summary>
    public class AnimatorStateMirror : MonoBehaviour
    {
        private Animator _host;
        private Animator _puppet;
        private int _layer = 0;
        private int _diagFrames = 0;
        private const int DiagInterval = 300;

        // hostStateHash → puppetStateHash
        private Dictionary<int, int> _stateMap;
        // hostClipName → puppetStateHash (for IsName fallback)
        private Dictionary<string, int> _clipNameMap;
        // Puppet's default (idle) state hash, used as fallback
        private int _puppetDefaultState;

        // Known semantic suffixes in order of priority for matching
        private static readonly string[] KnownSuffixes = {
            "idle", "attack", "cast", "hit", "hardmovement", "movement", "death", "stun"
        };

        /// <summary>Initialize with host and puppet Animators.</summary>
        public void Init(Animator host, Animator puppet)
        {
            _host = host;
            _puppet = puppet;
            if (_puppet != null)
            {
                _puppet.enabled = true;
                _puppet.speed = 0f;
            }
            BuildStateMap();
        }

        private void BuildStateMap()
        {
            _stateMap = new Dictionary<int, int>();
            _clipNameMap = new Dictionary<string, int>();
            if (_host == null || _puppet == null) return;

            var hostCtrl = _host.runtimeAnimatorController;
            var puppetCtrl = _puppet.runtimeAnimatorController;
            if (hostCtrl == null || puppetCtrl == null) return;

            // Build suffix → stateHash for puppet clips
            var puppetSuffixToHash = new Dictionary<string, int>();
            foreach (var clip in puppetCtrl.animationClips)
            {
                string suffix = ExtractSuffix(clip.name);
                if (suffix != null)
                {
                    int hash = Animator.StringToHash(clip.name);
                    if (puppetSuffixToHash.ContainsKey(suffix))
                        Plugin.Log.LogWarning($"[AnimMirror] Duplicate puppet suffix '{suffix}': " +
                            $"'{clip.name}' overwrites previous mapping");
                    puppetSuffixToHash[suffix] = hash;
                }
            }

            // Store puppet default (idle) state
            if (puppetSuffixToHash.ContainsKey("idle"))
                _puppetDefaultState = puppetSuffixToHash["idle"];
            else if (_puppet.isInitialized)
                _puppetDefaultState = _puppet.GetCurrentAnimatorStateInfo(_layer).shortNameHash;

            // Map each host clip → matching puppet clip by suffix
            foreach (var clip in hostCtrl.animationClips)
            {
                string suffix = ExtractSuffix(clip.name);
                if (suffix != null)
                {
                    int hostHash = Animator.StringToHash(clip.name);
                    int puppetHash;
                    if (puppetSuffixToHash.TryGetValue(suffix, out puppetHash))
                    {
                        _stateMap[hostHash] = puppetHash;
                        _clipNameMap[clip.name] = puppetHash;
                    }
                }
            }

            Plugin.Log.LogInfo($"[AnimMirror] Built state map: {_stateMap.Count} mappings " +
                $"(host clips={hostCtrl.animationClips.Length}, puppet clips={puppetCtrl.animationClips.Length})");
            foreach (var kvp in _stateMap)
                Plugin.Log.LogInfo($"[AnimMirror]   {kvp.Key} → {kvp.Value}");
        }

        /// <summary>
        /// Extract the semantic suffix from a clip/state name.
        /// E.g. "sylvieIdle" → "idle", "buhomago_attack" → "attack", "sylvieHardMovement" → "hardmovement"
        /// Requires a word boundary before the suffix: start-of-string, underscore/digit,
        /// or a camelCase boundary (lowercase→uppercase transition).
        /// This prevents e.g. "counterattack" from matching as "attack".
        /// </summary>
        private static string ExtractSuffix(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            string lower = clipName.ToLowerInvariant();
            // Check longest suffixes first to avoid "movement" matching before "hardmovement"
            for (int i = 0; i < KnownSuffixes.Length; i++)
            {
                string s = KnownSuffixes[i];
                if (!lower.EndsWith(s)) continue;
                int boundary = clipName.Length - s.Length;
                // Accept if suffix starts at index 0, or the preceding char is a word boundary
                if (boundary == 0
                    || clipName[boundary - 1] == '_'
                    || char.IsDigit(clipName[boundary - 1])
                    || char.IsUpper(clipName[boundary]))
                    return s;
            }
            return null;
        }

        private void Update()
        {
            if (_host == null || _puppet == null) return;
            if (!_host.isActiveAndEnabled || !_puppet.isInitialized) return;

            var hostState = _host.GetCurrentAnimatorStateInfo(_layer);
            int hostHash = hostState.shortNameHash;
            float normalizedTime = hostState.normalizedTime;

            // During triggered transitions (attack, cast, hit…), the Animator
            // blends between source and destination states.  GetCurrentAnimatorStateInfo
            // returns the SOURCE state during this blend, so the puppet would stay
            // in the old state until the blend finishes — causing a visible pop/flicker.
            // Snap to the DESTINATION state immediately so the puppet transitions
            // at the same time the host starts blending.
            AnimatorStateInfo stateForFallback = hostState;
            if (_host.IsInTransition(_layer))
            {
                var nextState = _host.GetNextAnimatorStateInfo(_layer);
                if (nextState.fullPathHash != 0)
                {
                    hostHash = nextState.shortNameHash;
                    normalizedTime = nextState.normalizedTime;
                    stateForFallback = nextState;
                }
            }

            // Look up the matching puppet state by hash first, then IsName fallback
            int targetHash;
            if (_stateMap == null || !_stateMap.TryGetValue(hostHash, out targetHash))
            {
                // Hash miss — try IsName fallback (catches states renamed differently from clips)
                targetHash = _puppetDefaultState;
                if (_clipNameMap != null)
                {
                    foreach (var kvp in _clipNameMap)
                    {
                        if (stateForFallback.IsName(kvp.Key))
                        {
                            targetHash = kvp.Value;
                            // Cache for future frames
                            _stateMap[hostHash] = targetHash;
                            break;
                        }
                    }
                }
                // Cache the miss so we don't iterate _clipNameMap every frame
                // for permanently unmapped states
                if (targetHash == _puppetDefaultState)
                    _stateMap[hostHash] = _puppetDefaultState;
            }

            _puppet.Play(targetHash, _layer, normalizedTime);
            _puppet.speed = 0f;
            _puppet.Update(0f);

            // Periodic diagnostic
            if (++_diagFrames >= DiagInterval)
            {
                _diagFrames = 0;
                var ps = _puppet.GetCurrentAnimatorStateInfo(_layer);
                bool mapped = _stateMap != null && _stateMap.ContainsKey(hostHash);
                Plugin.Log.LogInfo($"[AnimMirror] host={_host.name} hostState={hostHash} " +
                    $"hostNorm={hostState.normalizedTime:F2} → puppetTarget={targetHash} " +
                    $"puppetActual={ps.shortNameHash} mapped={mapped}");
            }
        }
    }
}
