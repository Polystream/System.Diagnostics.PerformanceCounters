using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using PerformanceCounters;

namespace System.Diagnostics.Linux
{
    class PerformanceCounterLib
    {
        static readonly Dictionary<string, CategoryBase> CategoryTable = new Dictionary<string, CategoryBase>(StringComparer.OrdinalIgnoreCase);

        static PerformanceCounterLib()
        {
            var assembly = typeof(PerformanceCounterLib).GetTypeInfo().Assembly;
            var types = assembly.DefinedTypes.Where(t => t.IsClass && !t.IsAbstract && typeof(CategoryBase).GetTypeInfo().IsAssignableFrom(t));

            foreach (var type in types)
            {
                var category = (CategoryBase)Activator.CreateInstance(type.AsType());

                CategoryTable.Add(category.CategoryName, category);
            }
        }

        public static bool CategoryExists(string category)
        {
            return CategoryTable.ContainsKey(category);
        }

        public static void CloseAllLibraries()
        {
        }

        public static bool CounterExists(string category, string counter)
        {
            if (!CategoryTable.TryGetValue(category, out var entry))
                throw new InvalidOperationException(SR.GetString("Category does not exist."));

            return entry.CounterExists(counter);
        }

        public static string[] GetCategories()
        {
            ICollection keys = CategoryTable.Keys;
            var categories = new string[keys.Count];
            keys.CopyTo(categories, 0);
            return categories;
        }

        public static string GetCategoryHelp(string category)
        {
            if (!CategoryTable.TryGetValue(category, out var entry) || entry == null)
                return null;

            return entry.GetHelp();
        }

        public static CategorySample GetCategorySample(string category)
        {
            if (!CategoryTable.TryGetValue(category, out var entry))
                throw new InvalidOperationException(SR.GetString("Category does not exist."));

            return entry.GetSample();
        }

        public static string[] GetCounters(string category)
        {
            if (!CategoryTable.TryGetValue(category, out var entry))
                throw new InvalidOperationException(SR.GetString("Category does not exist."));

            return entry.GetCounters();
        }

        public static string GetCounterHelp(string category, string counter)
        {
            if (!CategoryTable.TryGetValue(category, out var entry) || entry == null)
                return null;

            return entry.GetHelp(counter);
        }

        public static bool IsCustomCategory(string category)
        {
            return false;
        }

        public static bool IsBaseCounter(int type)
        {
            return false;
        }

        public static PerformanceCounterCategoryType GetCategoryType(string category)
        {
            if (!CategoryTable.TryGetValue(category, out var entry))
                throw new InvalidOperationException(SR.GetString("Category does not exist."));

            return entry.Type;
        }

        public static void RegisterCategory(string categoryName, PerformanceCounterCategoryType categoryType, string categoryHelp, List<CounterCreationData> creationData)
        {
            throw new NotSupportedException();
        }
        public static void UnregisterCategory(string categoryName)
        {
            throw new NotSupportedException();
        }
        public static CustomPerformanceCounter CreateCustomPerformanceCounter(string categoryName, string counterName, string instanceName, PerformanceCounterInstanceLifetime lifetime)
        {
            throw new NotSupportedException();
        }
        public static void RemoveAllCustomInstances(string categoryName)
        {
            throw new NotSupportedException();
        }
    }
}
