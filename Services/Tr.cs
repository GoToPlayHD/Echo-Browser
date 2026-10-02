namespace EchoBrowser.Services
{
    /// <summary>Kurzschreibweise für übersetzte Texte im C#-Code.</summary>
    public static class Tr
    {
        /// <summary>Übersetzter Text zum Key.</summary>
        public static string Get(string key) => LocalizationService.Instance.GetString(key);

        /// <summary>Übersetzter Text mit Platzhaltern ({0}, {1}, ...).</summary>
        public static string Format(string key, params object?[] args) => LocalizationService.Instance.Format(key, args);
    }
}
