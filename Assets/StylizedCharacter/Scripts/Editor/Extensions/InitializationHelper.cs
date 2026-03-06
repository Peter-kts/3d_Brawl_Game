using System;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts;
using NHance.Assets.Scripts.Enums;

public static class InitializationHelper
{
    public static void InitializeWrappers(ref List<BodypartWrapper> wrappers)
    {
        var values = Enum.GetValues(typeof(TargetBodyparts));
        var toRemove = new List<BodypartWrapper>();
        var toAdd = new List<BodypartWrapper>();

        foreach (var v in values)
        {
            var found = false;
            var value = (TargetBodyparts) v;
            foreach (var w in wrappers)
            {
                if (value == w.Type)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                toAdd.Add(new BodypartWrapper(value, false));
        }

        foreach (var w in wrappers)
        {
            var found = false;

            foreach (var v in values)
            {
                if ((TargetBodyparts) v == w.Type)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                toRemove.Add(w);
        }

        wrappers = wrappers.Where(w => !toRemove.Contains(w)).ToList();
        wrappers.AddRange(toAdd);
    }
}