using System;
using System.Reflection;

namespace EarthMap.Core
{
    /// <summary>
    /// Hands a longitude provider to another mod by reflection only, without referencing its assembly: calls a public
    /// instance method SetLongitudeProvider(Func&lt;double, double?&gt;) on the given object, or reports that it has none.
    /// Used for AstraTerra (AGPL-3.0, optional), whose sky reads every longitude through that provider; a version
    /// without the method is simply left alone. Never throws.
    /// </summary>
    public static class LongitudeProviderBinding
    {
        public const string MethodName = "SetLongitudeProvider";

        public enum Result
        {
            /// <summary>The provider (or null, clearing it) was handed over.</summary>
            Bound,
            /// <summary>No target, or the target has no SetLongitudeProvider(Func&lt;double, double?&gt;).</summary>
            NoMethod,
            /// <summary>The method exists but threw (for example, called before that mod started on this side).</summary>
            Failed,
        }

        /// <summary>The method on a type, or null.</summary>
        public static MethodInfo Find(Type type)
        {
            if (type == null) return null;
            try
            {
                return type.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Func<double, double?>) }, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Calls target.SetLongitudeProvider(provider); null clears it. The error is set when Failed.</summary>
        public static Result TrySet(object target, Func<double, double?> provider, out string error)
        {
            error = null;
            var method = Find(target?.GetType());
            if (method == null) return Result.NoMethod;
            try
            {
                method.Invoke(target, new object[] { provider });
                return Result.Bound;
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
