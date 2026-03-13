using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public enum AnimationEventParameterKind
{
    None = 0,
    Int = 1,
    Float = 2,
    String = 3,
    Object = 4
}

public static class AnimationEventMethodCatalog
{
    public struct MethodOption
    {
        public string displayName;
        public string functionName;
        public AnimationEventParameterKind parameterKind;
        public bool isCurated;
    }

    private struct CuratedEntry
    {
        public Type componentType;
        public string functionName;
        public AnimationEventParameterKind parameterKind;
        public string label;
    }

    private static readonly CuratedEntry[] CuratedEntries =
    {
        new CuratedEntry { componentType = typeof(WeaponCombat), functionName = "BeginHitbox", parameterKind = AnimationEventParameterKind.Int, label = "WeaponCombat.BeginHitbox(int)" },
        new CuratedEntry { componentType = typeof(WeaponCombat), functionName = "EndHitbox", parameterKind = AnimationEventParameterKind.Int, label = "WeaponCombat.EndHitbox(int)" },
        new CuratedEntry { componentType = typeof(WeaponCombat), functionName = "BeginWeaponTipActiveFrames", parameterKind = AnimationEventParameterKind.None, label = "WeaponCombat.BeginWeaponTipActiveFrames()" },
        new CuratedEntry { componentType = typeof(WeaponCombat), functionName = "EndWeaponTipActiveFrames", parameterKind = AnimationEventParameterKind.None, label = "WeaponCombat.EndWeaponTipActiveFrames()" },
        new CuratedEntry { componentType = typeof(WeaponCombat), functionName = "EndAllHitboxes", parameterKind = AnimationEventParameterKind.None, label = "WeaponCombat.EndAllHitboxes()" },
        new CuratedEntry { componentType = typeof(AttackAnimationEventForwarder), functionName = "OnBeginHitbox", parameterKind = AnimationEventParameterKind.Int, label = "AttackAnimationEventForwarder.OnBeginHitbox(int)" },
        new CuratedEntry { componentType = typeof(AttackAnimationEventForwarder), functionName = "OnEndHitbox", parameterKind = AnimationEventParameterKind.Int, label = "AttackAnimationEventForwarder.OnEndHitbox(int)" },
        new CuratedEntry { componentType = typeof(AttackAnimationEventForwarder), functionName = "OnBeginHitbox", parameterKind = AnimationEventParameterKind.None, label = "AttackAnimationEventForwarder.OnBeginHitbox()" },
        new CuratedEntry { componentType = typeof(AttackAnimationEventForwarder), functionName = "OnEndHitbox", parameterKind = AnimationEventParameterKind.None, label = "AttackAnimationEventForwarder.OnEndHitbox()" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnChargeWindowStart", parameterKind = AnimationEventParameterKind.None, label = "Combat.OnChargeWindowStart()" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnChargeWindowEnd", parameterKind = AnimationEventParameterKind.None, label = "Combat.OnChargeWindowEnd()" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnAttackChargeWindowStart", parameterKind = AnimationEventParameterKind.None, label = "Combat.OnAttackChargeWindowStart()" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnAttackChargeWindowEnd", parameterKind = AnimationEventParameterKind.None, label = "Combat.OnAttackChargeWindowEnd()" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnAttackSfxEvent", parameterKind = AnimationEventParameterKind.None, label = "Combat.OnAttackSfxEvent()" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnAttackSfxEvent", parameterKind = AnimationEventParameterKind.Int, label = "Combat.OnAttackSfxEvent(int)" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnThrowSfxEvent", parameterKind = AnimationEventParameterKind.None, label = "Combat.OnThrowSfxEvent()" },
        new CuratedEntry { componentType = typeof(Combat), functionName = "OnThrowSfxEvent", parameterKind = AnimationEventParameterKind.Int, label = "Combat.OnThrowSfxEvent(int)" },
    };

    public static List<MethodOption> GetCuratedOptions(GameObject root)
    {
        List<MethodOption> results = new List<MethodOption>();
        if (root == null) return results;

        for (int i = 0; i < CuratedEntries.Length; i++)
        {
            CuratedEntry entry = CuratedEntries[i];
            if (entry.componentType == null) continue;
            if (root.GetComponentInChildren(entry.componentType, true) == null) continue;
            if (!ContainsMatchingMethod(root, entry.functionName, entry.parameterKind)) continue;

            results.Add(new MethodOption
            {
                displayName = entry.label,
                functionName = entry.functionName,
                parameterKind = entry.parameterKind,
                isCurated = true
            });
        }

        return results;
    }

    public static List<MethodOption> GetReflectionOptions(GameObject root)
    {
        List<MethodOption> results = new List<MethodOption>();
        if (root == null) return results;

        Component[] components = root.GetComponents<Component>();
        for (int c = 0; c < components.Length; c++)
        {
            Component component = components[c];
            if (component == null) continue;

            MethodInfo[] methods = component.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.IsSpecialName) continue;
                if (method.ReturnType != typeof(void)) continue;

                AnimationEventParameterKind kind;
                if (!TryGetAnimationEventParameterKind(method, out kind)) continue;

                results.Add(new MethodOption
                {
                    displayName = component.GetType().Name + "." + method.Name + SignatureFromKind(kind),
                    functionName = method.Name,
                    parameterKind = kind,
                    isCurated = false
                });
            }
        }

        results.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.Ordinal));
        return RemoveDuplicates(results);
    }

    public static string SignatureFromKind(AnimationEventParameterKind kind)
    {
        switch (kind)
        {
            case AnimationEventParameterKind.Int: return "(int)";
            case AnimationEventParameterKind.Float: return "(float)";
            case AnimationEventParameterKind.String: return "(string)";
            case AnimationEventParameterKind.Object: return "(Object)";
            default: return "()";
        }
    }

    private static List<MethodOption> RemoveDuplicates(List<MethodOption> input)
    {
        List<MethodOption> output = new List<MethodOption>();
        HashSet<string> seen = new HashSet<string>();
        for (int i = 0; i < input.Count; i++)
        {
            MethodOption item = input[i];
            string key = item.functionName + "|" + (int)item.parameterKind + "|" + item.displayName;
            if (seen.Contains(key)) continue;
            seen.Add(key);
            output.Add(item);
        }
        return output;
    }

    private static bool ContainsMatchingMethod(GameObject root, string functionName, AnimationEventParameterKind parameterKind)
    {
        Component[] components = root.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) continue;
            MethodInfo[] methods = component.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (int m = 0; m < methods.Length; m++)
            {
                MethodInfo method = methods[m];
                if (method.Name != functionName) continue;
                AnimationEventParameterKind methodKind;
                if (!TryGetAnimationEventParameterKind(method, out methodKind)) continue;
                if (methodKind != parameterKind) continue;
                return true;
            }
        }
        return false;
    }

    private static bool TryGetAnimationEventParameterKind(MethodInfo method, out AnimationEventParameterKind kind)
    {
        kind = AnimationEventParameterKind.None;
        if (method == null) return false;

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            kind = AnimationEventParameterKind.None;
            return true;
        }

        if (parameters.Length != 1) return false;

        Type paramType = parameters[0].ParameterType;
        if (paramType == typeof(int))
        {
            kind = AnimationEventParameterKind.Int;
            return true;
        }
        if (paramType == typeof(float))
        {
            kind = AnimationEventParameterKind.Float;
            return true;
        }
        if (paramType == typeof(string))
        {
            kind = AnimationEventParameterKind.String;
            return true;
        }
        if (typeof(UnityEngine.Object).IsAssignableFrom(paramType))
        {
            kind = AnimationEventParameterKind.Object;
            return true;
        }

        return false;
    }
}
