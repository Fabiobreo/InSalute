using BusinessLogic;
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
using System.ComponentModel.DataAnnotations;
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

        private readonly List<UserExtended> EditedUsers = new List<UserExtended>();

        #endregion User Table

        #region UI buttons
        public DelegateCommand DeleteUsersCommand { get; set; }
        public DelegateCommand EditUsersCommand { get; set; }
        public DelegateCommand ReloadUsersCommand { get; set; }
        public ICommand CloseManageUserCommand { get; }
        #endregion UI buttons

        private static readonly ObservableCollection<string> _roles = new ObservableCollection<string>()
        { "user", "manager", "admin" };
        public static ObservableCollection<string> Roles { get => _roles; }

        private readonly UserStore UserStore;

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
                MessageBoxResult result = MessageBox.Show("Vuoi veramente eliminare " + usersToDelete.Count + " utente/i?\nQuesta scelta è definitiva.", "Elimina utenti", MessageBoxButton.YesNo, MessageBoxImage.Question);

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
                            MessageBox.Show("Errore durante la comunicazione con il server:\n" + ex.Message, "Errore del server", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        MessageBox.Show("Tutti gli utenti sono stati cancellati con successo.", "Eliminazione avvenuta con successo", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Ci sono stati dei problemi e si è riusciti a cancellare solamente " + eliminated + " di " + usersToDelete.Count + " utenti.\n" +
                            "Per favore, riprova a cancellarli più tardi.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    ReloadUsers();
                }
            }
            else
            {
                MessageBox.Show("Non ci sono utenti da eliminare.", "Nessun utente", MessageBoxButton.OK, MessageBoxImage.Information);
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
                        MessageBox.Show("Errore durante la comunicazione con il server:\n" + ex.Message, "Errore del server", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("Ci sono stati dei problemi ed è stato possibile salvare solamente " + edited.ToString() + " di " + total.ToString() + " utenti.\nPer favore, riprovare a salvare più tardi.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            
            if (UsersList.Any(user => user.Id == 0))
            {
                List<UserExtended> usersToAdd = UsersList.Where(user => user.Id == 0).ToList();
                usersToAdd.Reverse();
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
                        string message = "Impossibile aggiungere utente con email: " + user.Email + " e username: " + user.Username + "\n";
                        message += "Risolvi i problemi riguardanti: \n" + (emailError ? "- Email\n" : "") + (usernameError ? "- Username" : "");
                        MessageBox.Show(message, "Utente non aggiunto", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            MessageBox.Show("Errore durante la comunicazione con il server:\n" + ex.Message, "Errore del server", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("Ci sono stati dei problemi ed è stato possibile salvare solamente " + added.ToString() + " di " + total.ToString() + " utenti.\nPer favore, riprova a salvare più tardi.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show("La password di default per i nuovi utenti è 'Password'.\nPer favore ricorda di informarli di cambiarla in una sicura la prima volta che si autenticano.", "Password degli utenti", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }

            if (!errorOccurred)
            {
                MessageBox.Show("Tutti gli utenti son stati salvati con successo.", "Nessun errore", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            
        }

        private void ReloadUsers()
        {
            bool skipReload = false;
            if (EditedUsers.Count > 0 || UsersList.Any(user => user.Id == 0))
            {
                MessageBoxResult result = MessageBox.Show("Ci sono delle modifiche non salvate, vuoi veramente ricaricare gli utenti e perdere i cambiamenti effettuati?", "Modifiche pendenti", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
                MessageBox.Show("Errore durante la comunicazione con il server:\n" + ex.Message, "Errore del server", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (userDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                string result = userDetails.Result.Content.ReadAsStringAsync().Result;
                List<UserExtended> userExtended = JsonConvert.DeserializeObject<List<UserExtended>>(result);
                if (UsersList == null)
                {
                    UsersList = new ObservableCollection<UserExtended>();
                }
                UsersList.Clear();
                foreach (UserExtended user in userExtended)
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
                MessageBox.Show("Non si è riusciti a ricaricare della tabella.\nErrore: " + userDetails.Result.StatusCode, "Impossibile ricaricare", MessageBoxButton.OK, MessageBoxImage.Error);
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
                EmailAddressAttribute validator = new EmailAddressAttribute();
                return validator.IsValid(email);
            }
            catch
            {
                return false;
            }
        }
    }
}
