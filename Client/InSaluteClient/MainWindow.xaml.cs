using System.Globalization;
using System.Windows;
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
    }
}
