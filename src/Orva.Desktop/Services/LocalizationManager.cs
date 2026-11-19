using System;
using System.Linq;
using System.Windows;

namespace Orva.Services
{
    public static class LocalizationManager
    {
        private static ResourceDictionary _activeDictionary;
        public static string CurrentLanguage { get; private set; } = "en";

        public static void SetLanguage(string languageCode)
        {
            var language = string.Equals(languageCode, "bn", StringComparison.OrdinalIgnoreCase) ? "bn" : "en";
            var uri = new Uri("pack://application:,,,/ORVA;component/Resources/Strings." + language + ".xaml", UriKind.Absolute);
            var dictionary = new ResourceDictionary { Source = uri };

            var merged = Application.Current.Resources.MergedDictionaries;
            if (_activeDictionary != null) merged.Remove(_activeDictionary);
            else
            {
                var old = merged.FirstOrDefault(d => d.Contains("LanguageMarker"));
                if (old != null) merged.Remove(old);
            }
            merged.Add(dictionary);
            _activeDictionary = dictionary;
            CurrentLanguage = language;
        }

        public static string Get(string key)
        {
            if (Application.Current == null) return key;
            var value = Application.Current.TryFindResource(key);
            return value == null ? key : Convert.ToString(value);
        }
    }
}