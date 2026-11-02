using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using HarmonyLib;
using Verse;

namespace RimSiege.Integration
{
    internal static class CeAccess
    {
        internal static IEnumerable<MethodBase> Methods(Type baseType, params string[] names) =>
            GenTypes.AllTypes.Where(t => !t.ContainsGenericParameters && baseType.IsAssignableFrom(t))
                .SelectMany(AccessTools.GetDeclaredMethods)
                .Where(m => !m.IsStatic && !m.IsAbstract && names.Contains(m.Name));
    }
}
