using System;
using System.Reflection;

namespace Sbl
{
    internal static class Reflect
    {
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        internal static T GetField<T>(object target, string name) where T : class
        {
            if (target == null) return null;
            try
            {
                FieldInfo field = target.GetType().GetField(name, Flags);
                if (field == null) return null;
                return field.GetValue(target) as T;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads a property. Several GameItemElement node references (inner / middle /
        /// rotator / background) are properties backed by auto-fields, so looking them up
        /// as fields silently returns null — which once sent the label to the wrong node.
        /// </summary>
        internal static T GetProperty<T>(object target, string name) where T : class
        {
            if (target == null) return null;
            try
            {
                PropertyInfo property = target.GetType().GetProperty(name, Flags);
                if (property == null || !property.CanRead) return null;
                return property.GetValue(target, null) as T;
            }
            catch
            {
                return null;
            }
        }

        internal static T GetPropThenField<T>(object target, string name) where T : class
        {
            return GetProperty<T>(target, name) ?? GetField<T>(target, name);
        }

        internal static void Invoke(object target, string method)
        {
            if (target == null) return;
            MethodInfo info = target.GetType().GetMethod(method, Flags);
            if (info == null) return;
            info.Invoke(target, null);
        }
    }
}
