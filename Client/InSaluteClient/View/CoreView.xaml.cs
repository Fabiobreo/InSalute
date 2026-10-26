using InSalute.ViewModel;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace InSalute.View
{
    /// <summary>
    /// Interaction logic for ExpenseView.xaml
    /// </summary>
    public partial class CoreView : UserControl
    {
        public CoreViewModel ViewModel { get; set; }

        public CoreView()
        {
            InitializeComponent();
        }

        private void UserProfileButton_LostFocus(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (!UserProfilePopup.IsKeyboardFocusWithin)
                {
                    //ViewModel.IsUserProfileOpen = false;
                }
            }), DispatcherPriority.Background);
        }
    }
}
