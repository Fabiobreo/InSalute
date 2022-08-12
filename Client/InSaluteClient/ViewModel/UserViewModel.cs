using BusinessLogic;
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
    public class UserViewModel : ViewModelBase
    {
        #region Ui Bindings
        public string Id { get; set; }
        
        public string Role { get; set; }

        public string Date { get; set; }

        private string _email;

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged(nameof(Email));
            }
        }

        private string _username;

        public string Username
        {
            get => _username;
            set
            {
                _username = value;
                OnPropertyChanged(nameof(Username));
            }
        }

        private string _password;

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged(nameof(Password));
            }
        }
        #endregion Ui Bindings

        #region UI Commands
        public DelegateCommand SaveCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }
        public DelegateCommand DeleteCommand { get; set; }
        public ICommand NavigateLoginCommand { get; }
        public ICommand CloseUserCommand { get; }
        #endregion UI Commands

        private readonly UserStore UserStore;

        public UserViewModel(UserStore userStore, INavigationService loginNavigationService, INavigationService closeModalService)
        {
            UserStore = userStore;

            SaveCommand = new DelegateCommand(Save);
            CancelCommand = new DelegateCommand(Cancel);
            DeleteCommand = new DelegateCommand(DeleteAccount);
            NavigateLoginCommand = new NavigateCommand(loginNavigationService);
            CloseUserCommand = new NavigateCommand(closeModalService);

            Id = UserStore.CurrentUser.Id.ToString();
            Role = UserStore.CurrentUser.Role;
            Date = UserStore.CurrentUser.CreationDate.ToString("dd/MM/yyyy");
            Email = UserStore.CurrentUser.Email;
            Username = UserStore.CurrentUser.Username;
            Password = "";
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Email) || !IsValidEmail(Email))
            {
                MessageBox.Show("Please enter a valid email.", "Wrong email", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Please enter a valid username", "Wrong username", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Dictionary<string, string> parameters = new Dictionary<string, string>()
            {
                ["id"] = Id
            };

            Users userToEdit = new Users()
            {
                id = UserStore.CurrentUser.Id,
                creation_date = UserStore.CurrentUser.CreationDate,
                role = UserStore.CurrentUser.Role,
                username = Username
            };

            if (!string.IsNullOrWhiteSpace(Password))
            {
                userToEdit.password = Convert.ToBase64String(Encoding.UTF8.GetBytes(Password));
            }

            Task<HttpResponseMessage> userDetails;
            try
            {
                userDetails = WebAPI.PutCall(API_URIs.user, parameters, userToEdit, UserStore.CurrentUser.Token);
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
                    UserStore.CurrentUser.Email = Email;
                    UserStore.CurrentUser.Username = Username;
                    if (!string.IsNullOrWhiteSpace(user.Token))
                    {
                        UserStore.CurrentUser.Token = user.Token;
                    }
                }
                Password = string.Empty;
                MessageBox.Show("User details updated successfully.", "Update successful", MessageBoxButton.OK, MessageBoxImage.Information);
                UserStore.CurrentUser = UserStore.CurrentUser; // To trigger ui refresh
                CloseUserCommand.Execute(null);
            }
            else
            {
                Password = string.Empty;
                MessageBox.Show("There was an error during the update.\nError: " + userDetails.Result.StatusCode, "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteAccount()
        {
            MessageBoxResult result = MessageBox.Show("Are you REALLY sure that you want to delete your account? This choice is final!", "No going back", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            
            Dictionary<string, string> parameters = new Dictionary<string, string>()
            {
                ["id"] = Id
            };

            Task<HttpResponseMessage> userDetails;
            try
            {
                userDetails = WebAPI.DeleteCall(API_URIs.user, parameters, UserStore.CurrentUser.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (userDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                UserStore.CurrentUser = null;
            
                MessageBox.Show("User successfully deleted. We hope to see you again soon.", "User deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                NavigateLoginCommand.Execute(null);
            }
            else
            {
                MessageBox.Show("There was an error during the deletion.\nError: " + userDetails.Result.StatusCode, "Deletion failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel()
        {
            CloseUserCommand.Execute(null);
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                System.Net.Mail.MailAddress addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}
