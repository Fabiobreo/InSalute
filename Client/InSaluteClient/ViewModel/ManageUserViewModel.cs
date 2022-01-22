using DataAccessLayer;
using InSalute.Models;
using InSalute.Stores;
using InSalute.Utilities;
using MVVMEssentials.Commands;
using MVVMEssentials.Services;
using MVVMEssentials.ViewModels;
using Newtonsoft.Json;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace InSalute.ViewModel
{
    public class ManageUserViewModel : ViewModelBase
    {
        #region User Table
        private ObservableCollection<UserExtended> _usersList = new ObservableCollection<UserExtended>();

        public ObservableCollection<UserExtended> UsersList
        {
            get => _usersList;
            set
            {
                _usersList = value;
                OnPropertyChanged(nameof(UsersList));
            }
        }

        private CollectionViewSource _usersCollection = new CollectionViewSource();
        public CollectionViewSource UsersCollection
        {
            get
            {
                _usersCollection.Source = UsersList;
                return _usersCollection;
            }
            set
            {
                if (value != _usersCollection)
                {
                    _usersCollection = value;
                    OnPropertyChanged(nameof(UsersCollection));
                }
            }
        }

        private List<UserExtended> EditedUsers = new List<UserExtended>();

        #endregion User Table

        #region UI buttons
        public DelegateCommand DeleteUsersCommand { get; set; }
        public DelegateCommand EditUsersCommand { get; set; }
        public DelegateCommand ReloadUsersCommand { get; set; }
        public ICommand CloseManageUserCommand { get; }
        #endregion UI buttons

        private static ObservableCollection<string> _roles = new ObservableCollection<string>()
        { "user", "manager", "admin" };
        public static ObservableCollection<string> Roles { get => _roles; }

        private UserStore UserStore;

        public bool IsAdmin { get; }
        public bool IsManager { get; }

        public ManageUserViewModel(UserStore userStore, INavigationService closeModalService)
        {
            DeleteUsersCommand = new DelegateCommand(DeleteUsers);
            EditUsersCommand = new DelegateCommand(EditUsers);
            ReloadUsersCommand = new DelegateCommand(ReloadUsers);
            CloseManageUserCommand = new NavigateCommand(closeModalService);
            UsersCollection.SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Ascending));

            UserStore = userStore;
            IsAdmin = UserStore.CurrentUser.Role.ToLower() == "admin";
            IsManager = UserStore.CurrentUser.Role.ToLower() == "manager";

            ReloadUsers();
        }

        private void DeleteUsers()
        {
            List<UserExtended> usersToDelete = new List<UserExtended>();
            foreach (UserExtended user in UsersList)
            {
                if (user.IsChecked)
                {
                    usersToDelete.Add(user);
                }
            }

            if (usersToDelete.Count > 0)
            {
                MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show("Do you really want to delete " + usersToDelete.Count + " user(s)?\nThis choice is final.", "Delete users", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    int eliminated = 0;
                    foreach (UserExtended user in usersToDelete)
                    {
                        if (user.Id == 0) // Users that are yet to be added
                        {
                            eliminated++;
                            UsersList.Remove(user);
                            continue;
                        }

                        Dictionary<string, string> parameters = new Dictionary<string, string>()
                        {
                            ["id"] = user.Id.ToString()
                        };

                        Task<HttpResponseMessage> userDetails = null;
                        try
                        {
                            userDetails = WebAPI.DeleteCall(API_URIs.user, parameters, UserStore.CurrentUser.Token);
                        }
                        catch (Exception ex)
                        {
                            Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        if (userDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            eliminated++;
                            if (EditedUsers.Count > 0)
                            {
                                UserExtended toEliminate = EditedUsers.Find(us => us.Id == user.Id);
                                if (toEliminate != null)
                                {
                                    EditedUsers.Remove(toEliminate);
                                }

                            }
                        }
                    }

                    if (eliminated == usersToDelete.Count)
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show("All users were deleted successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show("There was some problems and we could delete only " + eliminated + " out of " + usersToDelete.Count + " users.\n" +
                            "Please retry to delete later.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    ReloadUsers();
                }
            }
            else
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("There are no users listed to be eliminated.", "No eliminated expenses", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditUsers()
        {
            bool errorOccurred = false;
            if (EditedUsers.Count > 0)
            {
                int total = EditedUsers.Count;
                int edited = 0;
                for (int i = EditedUsers.Count - 1; i >= 0; i--)
                {
                    UserExtended user = EditedUsers[i];
                    Dictionary<string, string> parameters = new Dictionary<string, string>()
                    {
                        ["id"] = user.Id.ToString()
                    };

                    Users editedUser = new Users()
                    {
                        id = user.Id,
                        email = user.Email,
                        username = user.Username,
                        creation_date = user.CreationDate,
                        role = user.Role
                    };

                    Task<HttpResponseMessage> userDetails = null;
                    try
                    {
                        userDetails = WebAPI.PutCall(API_URIs.user, parameters, editedUser, UserStore.CurrentUser.Token);
                    }
                    catch (Exception ex)
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (userDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        edited++;
                        EditedUsers.RemoveAt(i);
                    }
                }

                if (edited != total)
                {
                    errorOccurred = true;
                    Xceed.Wpf.Toolkit.MessageBox.Show("There was some problems and we could save only " + edited.ToString() + " out of " + total.ToString() + " users.\nPlease retry to save later.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            
            if (UsersList.Any(user => user.Id == 0))
            {
                List<UserExtended> usersToAdd = UsersList.Where(user => user.Id == 0).ToList();
                int total = usersToAdd.Count;
                int added = 0;
                for (int i = usersToAdd.Count - 1; i >= 0; i--)
                {
                    UserExtended user = usersToAdd[i];
                    bool emailError = false;
                    if (string.IsNullOrWhiteSpace(user.Email) || !IsValidEmail(user.Email))
                    {
                        emailError = true;
                    }

                    bool usernameError = false;
                    if (string.IsNullOrWhiteSpace(user.Username))
                    {
                        usernameError = true;
                    }

                    if (emailError || usernameError)
                    {
                        string message = "Could not add user with email: " + user.Email + " and username: " + user.Username + "\n";
                        message += "Fix problems with: \n" + (emailError ? "- Email\n" : "") + (usernameError ? "- Username" : "");
                        Xceed.Wpf.Toolkit.MessageBox.Show(message, "User not added", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }
                    else
                    {
                        Users newUser = new Users()
                        {
                            id = user.Id,
                            email = user.Email,
                            password = Convert.ToBase64String(Encoding.UTF8.GetBytes("Password")),
                            username = user.Username,
                            creation_date = user.CreationDate,
                            role = user.Role
                        };

                        Task<HttpResponseMessage> userDetails = null;
                        try
                        {
                            userDetails = WebAPI.PostCall(API_URIs.user, newUser, UserStore.CurrentUser.Token);
                        }
                        catch (Exception ex)
                        {
                            Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        if (userDetails.Result.StatusCode == System.Net.HttpStatusCode.Created)
                        {
                            added++;
                            usersToAdd.RemoveAt(i);
                            UsersList.Remove(user);
                        }
                    }
                }

                if (added != total)
                {
                    errorOccurred = true;
                    Xceed.Wpf.Toolkit.MessageBox.Show("There was some problems and we could save only " + added.ToString() + " out of " + total.ToString() + " users.\nPlease retry to save later.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                foreach (UserExtended user in usersToAdd)
                {
                    UsersList.Remove(user);
                }
                ReloadUsers();

                // Readd the ones that were not added to db
                foreach (UserExtended user in usersToAdd)
                {
                    UsersList.Add(user);
                }

                if (added > 0)
                {
                    Xceed.Wpf.Toolkit.MessageBox.Show("The default password is 'password'.\nPlease remember to tell the user to change it to a secure one when it logs in the first time.", "User password", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }

            if (!errorOccurred)
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("All users were saved successfully.", "No errors", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            
        }

        private void ReloadUsers()
        {
            bool skipReload = false;
            if (EditedUsers.Count > 0 || UsersList.Any(user => user.Id == 0))
            {
                MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show("You have unsaved edits, do you really want to reload your users and lose your changes?", "Pending changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    skipReload = true;
                }
                else
                {
                    EditedUsers.Clear();
                }
            }

            if (skipReload)
            {
                return;
            }

            Task<HttpResponseMessage> userDetails = null;
            try
            {
                userDetails = WebAPI.GetCall(API_URIs.user, null, UserStore.CurrentUser.Token);
            }
            catch (Exception ex)
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (userDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                string result = userDetails.Result.Content.ReadAsStringAsync().Result;
                List<UserExtended> userExpenses = JsonConvert.DeserializeObject<List<UserExtended>>(result);
                if (UsersList == null)
                {
                    UsersList = new ObservableCollection<UserExtended>();
                }
                UsersList.Clear();
                foreach (UserExtended user in userExpenses)
                {
                    if (user.Id != UserStore.CurrentUser.Id)
                    {
                        user.PropertyChanged += UserExtended_PropertyChanged;
                        UsersList.Add(user);
                    }
                }
            }
            else
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("Could not refresh users.\nError: " + userDetails.Result.StatusCode, "Could not refresh", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UserExtended_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is UserExtended editedUser)
            {
                if (!EditedUsers.Any(user => user.Id == editedUser.Id))
                {
                    EditedUsers.Add(editedUser);
                }
            }
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
