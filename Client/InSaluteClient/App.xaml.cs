using System.Windows;
using InSalute.ViewModel;
using InSalute.Stores;
using MVVMEssentials.Services;
using MVVMEssentials.Stores;
using MVVMEssentials.ViewModels;

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

        private FilterStore _filterStore;

        public FilterStore FilterStore
        {
            get => _filterStore;
            set
            {
                _filterStore = value;
            }
        }
        #endregion Store objects

        public bool IsOpen => ModalNavigationStore.IsOpen;

        public App()
        {
            NavigationStore = new NavigationStore();
            ModalNavigationStore = new ModalNavigationStore();
            UserStore = new UserStore();
            FilterStore = new FilterStore();
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
            return new CoreViewModel(UserStore, FilterStore, CreateHomeNavigationService(),
                CreateLoginNavigationService(), CreateFilterNavigationService(),
                CreateUserNavigationService(), CreateManageUserNavigationService());
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

        #region Create FilterView
        private INavigationService CreateFilterNavigationService()
        {
            return new NavigationService<FilterViewModel>(ModalNavigationStore, CreateFilterViewModel);
        }

        private FilterViewModel CreateFilterViewModel()
        {
            return new FilterViewModel(FilterStore, new CloseModalNavigationService(ModalNavigationStore));
        }
        #endregion Create FilterView

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
    }
}
