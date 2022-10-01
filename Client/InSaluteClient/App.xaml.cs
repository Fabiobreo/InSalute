using InSalute.Stores;
using InSalute.ViewModel;
using MVVMEssentials.Services;
using MVVMEssentials.Stores;
using MVVMEssentials.ViewModels;
using System.Windows;

namespace InSalute
{
    /// <summary>
    /// The application class
    /// </summary>
    public partial class App : Application
    {
        #region Navigation objects
        private NavigationStore _navigationStore;
        public NavigationStore NavigationStore
        {
            get => _navigationStore;
            set
            {
                _navigationStore = value;
            }
        }

        private ModalNavigationStore _modalNavigationStore;
        public ModalNavigationStore ModalNavigationStore
        {
            get => _modalNavigationStore;
            set
            {
                _modalNavigationStore = value;
            }
        }
        #endregion Navigation objects

        #region Store objects
        private UserStore _userStore;
        public UserStore UserStore
        {
            get => _userStore;
            set
            {
                _userStore = value;
            }
        }
        #endregion Store objects

        public bool IsOpen => ModalNavigationStore.IsOpen;

        public App()
        {
            NavigationStore = new NavigationStore();
            ModalNavigationStore = new ModalNavigationStore();
            UserStore = new UserStore();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            INavigationService navigationService = CreateHomeNavigationService();
            navigationService.Navigate();

            MainWindow = new MainWindow()
            {
                DataContext = new MainViewModel(_navigationStore, _modalNavigationStore)
            };
            MainWindow.Show();

            base.OnStartup(e);
        }

        #region Create HomeView
        private INavigationService CreateHomeNavigationService()
        {
            return new NavigationService<HomeViewModel>(NavigationStore, CreateHomeViewModel);
        }

        private HomeViewModel CreateHomeViewModel()
        {
            return new HomeViewModel(UserStore, CreateLoginNavigationService(), CreateExpensesNavigationService());
        }
        #endregion Create HomeView

        #region Create ExpensesView
        private INavigationService CreateExpensesNavigationService()
        {
            return new NavigationService<CoreViewModel>(NavigationStore, CreateExpensesViewModel);
        }

        private CoreViewModel CreateExpensesViewModel()
        {
            return new CoreViewModel(UserStore, CreateHomeNavigationService(),
                CreateLoginNavigationService(), CreateUserNavigationService(), CreateManageUserNavigationService(), CreateLogNavigationService());
        }
        #endregion Create ExpensesView

        #region Create LoginView
        private INavigationService CreateLoginNavigationService()
        {
            return new NavigationService<LoginViewModel>(ModalNavigationStore, CreateLoginViewModel);
        }

        private LoginViewModel CreateLoginViewModel()
        {
            return new LoginViewModel(UserStore, new CloseModalNavigationService(ModalNavigationStore));
        }
        #endregion Create LoginView

        #region Create UserView
        private INavigationService CreateUserNavigationService()
        {
            return new NavigationService<UserViewModel>(ModalNavigationStore, CreateUserViewModel);
        }

        private UserViewModel CreateUserViewModel()
        {
            return new UserViewModel(UserStore, CreateLoginNavigationService(), new CloseModalNavigationService(ModalNavigationStore));
        }
        #endregion Create UserView

        #region Create ManageUserView
        private INavigationService CreateManageUserNavigationService()
        {
            return new NavigationService<ManageUserViewModel>(ModalNavigationStore, CreateManageUserViewModel);
        }

        private ManageUserViewModel CreateManageUserViewModel()
        {
            return new ManageUserViewModel(UserStore, new CloseModalNavigationService(ModalNavigationStore));
        }
        #endregion Create ManageUserView

        #region Create LogView
        private INavigationService CreateLogNavigationService()
        {
            return new NavigationService<LogViewModel>(ModalNavigationStore, CreateLogViewModel);
        }

        private LogViewModel CreateLogViewModel()
        {
            return new LogViewModel(UserStore, new CloseModalNavigationService(ModalNavigationStore));
        }
        #endregion Create LogView
    }
}
