using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;

namespace InSalute
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
            InitializeComponent();
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
