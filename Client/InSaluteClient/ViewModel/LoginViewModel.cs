using DataAccessLayer;
using InSalute.Models;
using InSalute.Stores;
using InSalute.Utilities;
using MVVMEssentials.Commands;
using MVVMEssentials.Services;
using MVVMEssentials.ViewModels;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace InSalute.ViewModel
{
    public class LoginViewModel : ViewModelBase
    {

        #region Login User Properties

        private string _loginEmail;

        public string LoginUserName
        {
            get => _loginEmail;
            set
            {
                _loginEmail = value;
                OnPropertyChanged(nameof(LoginUserName));
            }
        }

        private string _loginPassword;

        public string LoginPassword
        {
            get => _loginPassword;
            set
            {
                _loginPassword = value;
                OnPropertyChanged(nameof(LoginPassword));
            }
        }

        private bool _rememberMe;

        public bool RememberMe
        {
            get => _rememberMe;
            set
            {
                _rememberMe = value;
                OnPropertyChanged(nameof(RememberMe));
            }
        }

        private bool _isDefaultLogin = true;
        public bool IsDefaultLogin
        {
            get => _isDefaultLogin;
            set
            {
                _isDefaultLogin = value;
                OnPropertyChanged(nameof(IsDefaultLogin));
            }
        }

        #endregion Login User Properties

        #region ICommands
        public DelegateCommand LoginButtonClicked { get; set; }

        public DelegateCommand SignUpButtonClicked { get; set; }

        public DelegateCommand SwitchToLoginFormCommand { get; set; }

        public DelegateCommand SwitchToRegisterFormCommand { get; set; }

        public ICommand CloseLoginCommand { get; }
        #endregion

        #region Store and services
        private readonly UserStore UserStore;
        #endregion

        public LoginViewModel(UserStore userStore, INavigationService closeModalService)
        {
            UserStore = userStore;
            LoginButtonClicked = new DelegateCommand(LoginUser);
            CloseLoginCommand = new NavigateCommand(closeModalService);
        }

        private void LoginUser()
        {
            if (!string.IsNullOrWhiteSpace(LoginUserName) && !string.IsNullOrWhiteSpace(LoginPassword))
            {
                Dictionary<string, string> parameters = new Dictionary<string, string>()
                {
                    ["username"] = LoginUserName,
                    ["password"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(LoginPassword))
                };
            
                Task<HttpResponseMessage> userDetails;
                try
                {
                    userDetails = WebAPI.GetCall(API_URIs.login, parameters, "");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            
                if (userDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    UserExtended user = userDetails.Result.Content.ReadAsAsync<UserExtended>().Result;
                    if (user != null)
                    {
                        UserStore.CurrentUser = user;
                        CloseLoginCommand.Execute(null);
                    }
                    else
                    {
                        MessageBox.Show("There was an error retrieving the user information.", "Retrieving error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("The login failed with error: " + userDetails.Result.StatusCode + "\nPlease retry.", "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Please fill the form before clicking the Login button.", "Empty fields", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}