using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace F95ZoneMetadataProvider
{
    public partial class F95ZoneMetadataProviderSettingsView : UserControl
    {
        public F95ZoneMetadataProviderSettingsView()
        {
            InitializeComponent();
        }

        private Settings GetSettings()
        {
            if (DataContext is not Settings settings)
                throw new InvalidDataException();
            return settings;
        }

        /// <summary>
        /// Login button click handler.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            var settings = GetSettings();
            settings.DoLogin();
        }

        private void TextBoxBase_OnTextChanged(object sender, TextChangedEventArgs args)
        {
            if (sender is not TextBox textBox) throw new NotImplementedException();

            var text = textBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                textBox.Text = "0";
                return;
            }

            if (int.TryParse(text, out _)) return;

            if (!args.Changes.Any())
            {
                textBox.Text = "0";
                return;
            }

            var change = args.Changes.First();
            textBox.Text = text.Remove(change.Offset, change.AddedLength);
        }
    }
}