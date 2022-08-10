using InSalute.Stores;
using MVVMEssentials.Commands;
using MVVMEssentials.Services;
using MVVMEssentials.ViewModels;
using Prism.Commands;
using System.Windows.Input;

namespace InSalute.ViewModel
{
    class HomeViewModel : ViewModelBase
    {
        public ICommand NavigateMainCommand { get; }
        public ICommand NavigateLoginCommand { get; }

        private readonly UserStore m_userStore;

        public HomeViewModel(UserStore userStore, INavigationService loginNavigationService, INavigationService mainService)
        {
            m_userStore = userStore;
            NavigateMainCommand = new NavigateCommand(mainService);
            NavigateLoginCommand = new NavigateCommand(loginNavigationService);
            NavigateLoginCommand.Execute(null);

            m_userStore.CurrentUserChanged += HomeViewModel_CurrentUserChanged;
        }

        private void HomeViewModel_CurrentUserChanged()
        {
            m_userStore.CurrentUserChanged -= HomeViewModel_CurrentUserChanged;
            NavigateMainCommand.Execute(null);
        }
    }
}
