// HarmonyHelper — thin convenience wrapper around HarmonyX so mods write a single
// line to patch all methods in a class instead of remembering the boilerplate.

using System;
using System.Reflection;
using HarmonyLib;

namespace KingdomMod
{
    /// <summary>
    /// Convention helper.  In a MelonLoader mod, just call <c>HarmonyHelper.PatchAll(this)</c>
    /// in OnInitializeMelon and your <c>[HarmonyPatch]</c> classes are wired up.
    /// </summary>
    public static class HarmonyHelper
    {
        /// <summary>Apply every <c>[HarmonyPatch]</c> class in the mod's assembly.</summary>
        public static HarmonyLib.Harmony PatchAll(object mod, string id = null)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            var asm = mod.GetType().Assembly;
            id ??= $"kingdommod.{asm.GetName().Name}";
            var harmony = new HarmonyLib.Harmony(id);
            harmony.PatchAll(asm);
            return harmony;
        }

        /// <summary>Apply only patches from the given type (single class).</summary>
        public static HarmonyLib.Harmony PatchClass<T>(string id = null)
        {
            id ??= $"kingdommod.{typeof(T).FullName}";
            var harmony = new HarmonyLib.Harmony(id);
            new PatchClassProcessor(harmony, typeof(T)).Patch();
            return harmony;
        }

        /// <summary>Look up a private/internal method by name for ad-hoc patching.</summary>
        public static MethodInfo Method<T>(string name, params Type[] args)
            => AccessTools.Method(typeof(T), name, args);
    }
}
