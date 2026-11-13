using System;
using System.Reflection;

namespace EarthMap.Core
{
    /// <summary>
    /// Suggests a setting to another mod by reflection only, without referencing its assembly: calls a public instance
    /// method bool SuggestSetting(string name, string value) on the given object, or reports that it has none. Used for
    /// AstraTerra (AGPL-3.0, optional), which takes a suggestion only for a drawing setting the player left at its default
    /// and never saves it. Never throws.
    /// </summary>
    public static class SettingSuggestionBinding
    {
        public const string MethodName = "SuggestSetting";

        public enum Result
        {
            /// <summary>The suggestion now holds.</summary>
            Applied,
            /// <summary>The target kept its own value (the player set it, or turned suggestions off).</summary>
            Kept,
            /// <summary>No target, or the target has no bool SuggestSetting(string, string).</summary>
            NoMethod,
            /// <summary>The method exists but threw (an unknown setting or value, or called before that mod started).</summary>
            Failed,
        }

        /// <summary>The method on a type, or null.</summary>
        public static MethodInfo Find(Type type)
        {
            if (type == null) return null;
            try
            {
                var method = type.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null);
                return method?.ReturnType == typeof(bool) ? method : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Calls target.SuggestSetting(name, value). The error is set when Failed.</summary>
        public static Result TrySuggest(object target, string name, string value, out string error)
        {
            error = null;
            var method = Find(target?.GetType());
            if (method == null) return Result.NoMethod;
            try
            {
                return (bool)method.Invoke(target, new object[] { name, value }) ? Result.Applied : Result.Kept;
            }
            catch (TargetInvocationException e)
            {
                error = (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message;
                return Result.Failed;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return Result.Failed;
            }
        }
    }
}
