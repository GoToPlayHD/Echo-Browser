using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Bearbeitet eine Tab-Gruppe direkt (Name, Farbe) und meldet Aktionen an das Hauptfenster.</summary>
    public partial class TabGroupEditor : UserControl
    {
        private TabGroup? _group;

        /// <summary>Name oder Farbe geändert – die Sitzung sollte gesichert werden.</summary>
        public event Action? Changed;
        public event Action? NewTabRequested;
        public event Action? UngroupRequested;
        public event Action? CloseGroupRequested;
        /// <summary>Enter oder Esc im Namensfeld.</summary>
        public event Action? Done;

        public TabGroupEditor()
        {
            InitializeComponent();
        }

        public TextBox NameBox => txtName;

        public void Show(TabGroup group)
        {
            _group = null; // TextChanged beim Befüllen nicht als Änderung werten
            txtName.Text = group.Name;
            _group = group;
            BuildColorSwatches();
        }

        private void BuildColorSwatches()
        {
            panelColors.Children.Clear();
            if (_group == null) return;

            foreach (var color in TabGroupPalette.All)
            {
                bool selected = color == _group.Color;
                var swatch = new Button
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(0, 0, 5, 4),
                    Cursor = Cursors.Hand,
                    Tag = color,
                    ToolTip = Tr.Get("Group_Color_" + color),
                    Template = SwatchTemplate(selected),
                    Background = TabGroupPalette.BrushFor(color)
                };
                AutomationProperties.SetName(swatch, Tr.Get("Group_Color_" + color));
                swatch.Click += (s, e) =>
                {
                    if (_group == null) return;
                    _group.Color = color;
                    BuildColorSwatches();
                    Changed?.Invoke();
                };
                panelColors.Children.Add(swatch);
            }
        }

        /// <summary>Runder Farbknopf; der gewählte bekommt einen Ring in der Textfarbe.</summary>
        private static ControlTemplate SwatchTemplate(bool selected)
        {
            var template = new ControlTemplate(typeof(Button));
            var ring = new FrameworkElementFactory(typeof(Ellipse));
            ring.SetValue(Shape.StrokeThicknessProperty, selected ? 2.0 : 0.0);
            ring.SetResourceReference(Shape.StrokeProperty, "TextPrimaryBrush");
            ring.SetValue(Shape.FillProperty, Brushes.Transparent);

            var dot = new FrameworkElementFactory(typeof(Ellipse));
            dot.SetValue(MarginProperty, new Thickness(selected ? 4 : 2));
            dot.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var root = new FrameworkElementFactory(typeof(Grid));
            root.AppendChild(ring);
            root.AppendChild(dot);
            template.VisualTree = root;
            return template;
        }

        private void TxtName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_group == null) return;
            _group.Name = txtName.Text;
            Changed?.Invoke();
        }

        private void TxtName_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Done?.Invoke();
                e.Handled = true;
            }
        }

        private void MenuNewTab_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();
        private void MenuUngroup_Click(object sender, RoutedEventArgs e) => UngroupRequested?.Invoke();
        private void MenuCloseGroup_Click(object sender, RoutedEventArgs e) => CloseGroupRequested?.Invoke();
    }
}
