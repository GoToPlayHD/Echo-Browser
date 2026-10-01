using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EchoBrowser.Services;

namespace EchoBrowser.Views
{
    public partial class LanguageSetupWindow : Window
    {
        public string SelectedLanguage { get; private set; } = "de";

        private readonly List<(string Word, string Language)> _animatedWords = new()
        {
            ("Sprache", "Deutsch"),
            ("Language", "English"),
            ("Langue", "Français"),
            ("Idioma", "Español"),
            ("Linguaggio", "Italiano"),
            ("Språk", "Svenska"),
            ("Język", "Polski"),
            ("Taal", "Nederlands"),
            ("言語", "日本語"),
            ("Hallo", "Willkommen"),
            ("Hello", "Welcome"),
            ("Bonjour", "Bienvenue")
        };

        private int _currentWordIndex = 0;
        private readonly DispatcherTimer _timer;

        public LanguageSetupWindow()
        {
            InitializeComponent();

            string current = AppSettingsService.Instance.Settings.Language ?? "de";
            SelectLanguage(current);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.2)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            Closed += (s, e) => _timer.Stop();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            SaveAndClose();
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string lang)
            {
                SelectLanguage(lang);
            }
        }

        private void SelectLanguage(string lang)
        {
            SelectedLanguage = lang;

            // Reset all cards
            ResetCard(cardDe, checkDe);
            ResetCard(cardEn, checkEn);
            ResetCard(cardFr, checkFr);
            ResetCard(cardEs, checkEs);

            // Highlight selected
            switch (lang)
            {
                case "en":
                    HighlightCard(cardEn, checkEn);
                    btnContinue.Content = "Continue →";
                    txtSubtitle.Text = "Select your preferred language for Echo-Browser";
                    break;
                case "fr":
                    HighlightCard(cardFr, checkFr);
                    btnContinue.Content = "Continuer →";
                    txtSubtitle.Text = "Choisissez votre langue préférée pour Echo-Browser";
                    break;
                case "es":
                    HighlightCard(cardEs, checkEs);
                    btnContinue.Content = "Continuar →";
                    txtSubtitle.Text = "Elige tu idioma preferido para Echo-Browser";
                    break;
                default:
                    HighlightCard(cardDe, checkDe);
                    btnContinue.Content = "Fortfahren →";
                    txtSubtitle.Text = "Wähle deine bevorzugte Sprache für Echo-Browser";
                    break;
            }
        }

        private void HighlightCard(Border card, System.Windows.Shapes.Path check)
        {
            card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#232731"));
            card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
            check.Visibility = Visibility.Visible;
        }

        private void ResetCard(Border card, System.Windows.Shapes.Path check)
        {
            card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1C22"));
            card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2E38"));
            check.Visibility = Visibility.Collapsed;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _currentWordIndex = (_currentWordIndex + 1) % _animatedWords.Count;
            var next = _animatedWords[_currentWordIndex];

            // Apple Style Animation: Slide up and fade out
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(220));
            var slideUp = new DoubleAnimation(0.0, -12.0, TimeSpan.FromMilliseconds(220));

            fadeOut.Completed += (s, ev) =>
            {
                txtAnimatedWord.Text = next.Word;
                txtLanguageSubLabel.Text = next.Language;

                transAnimatedWord.Y = 14.0;

                // Slide up and fade in
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(280));
                var slideIn = new DoubleAnimation(14.0, 0.0, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                txtAnimatedWord.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                transAnimatedWord.BeginAnimation(TranslateTransform.YProperty, slideIn);
            };

            txtAnimatedWord.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            transAnimatedWord.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            SaveAndClose();
        }

        private void SaveAndClose()
        {
            AppSettingsService.Instance.Settings.Language = SelectedLanguage;
            AppSettingsService.Instance.Settings.HasCompletedFirstRunLanguageSetup = true;
            AppSettingsService.Instance.Save();

            LocalizationService.Instance.SetLanguage(SelectedLanguage);

            DialogResult = true;
            Close();
        }
    }
}
