using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace EchoBrowser.Services
{
    /// <summary>
    /// XAML-Markup-Erweiterung für übersetzte Texte: Text="{loc:Loc Nav_Back}".
    /// Bindet an den Indexer des LocalizationService, ein Sprachwechsel aktualisiert die Oberfläche sofort.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public class LocExtension : MarkupExtension
    {
        public LocExtension() { }

        public LocExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationService.Instance,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
