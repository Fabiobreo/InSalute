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
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace InSalute.ViewModel
{
    public class LogViewModel : ViewModelBase
    {
        #region Log Table
        private DateTime _fromDate = new DateTime(2022, 01, 01);

        public DateTime FromDate
        {
            get => _fromDate;
            set
            {
                _fromDate = value;
                OnPropertyChanged(nameof(FromDate));
            }
        }

        private DateTime _toDate = DateTime.Now;

        public DateTime ToDate
        {
            get => _toDate;
            set
            {
                _toDate = value;
                OnPropertyChanged(nameof(ToDate));
            }
        }

        private ObservableCollection<LogExtended> _logList = new ObservableCollection<LogExtended>();

        public ObservableCollection<LogExtended> LogList
        {
            get => _logList;
            set
            {
                _logList = value;
                OnPropertyChanged(nameof(LogList));
            }
        }

        private CollectionViewSource _logCollection = new CollectionViewSource();
        public CollectionViewSource LogCollection
        {
            get
            {
                _logCollection.Source = LogList;
                return _logCollection;
            }
            set
            {
                if (value != _logCollection)
                {
                    _logCollection = value;
                    OnPropertyChanged(nameof(LogCollection));
                }
            }
        }

        #endregion Log Table

        #region UI buttons
        public DelegateCommand ReloadUsersCommand { get; set; }
        public ICommand CloseManageUserCommand { get; }
        #endregion UI buttons

        private readonly UserStore UserStore;

        public bool IsAdmin { get; }

        public LogViewModel(UserStore userStore, INavigationService closeModalService)
        {
            ReloadUsersCommand = new DelegateCommand(ReloadUsers);
            CloseManageUserCommand = new NavigateCommand(closeModalService);
            LogCollection.SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Ascending));

            UserStore = userStore;
            IsAdmin = UserStore.CurrentUser.Role.ToLower() == "admin";

            ReloadUsers();
        }

        private void ReloadUsers()
        {
            bool skipReload = false;

            if (skipReload)
            {
                return;
            }

            Dictionary<string, string> parameters = new Dictionary<string, string>()
            {
                ["fromDate"] = FromDate.ToString(CultureInfo.InvariantCulture),
                ["toDate"] = ToDate.ToString(CultureInfo.InvariantCulture)
            };

            Task<HttpResponseMessage> logDetails;
            try
            {
                logDetails = WebAPI.GetCall(API_URIs.log, parameters, UserStore.CurrentUser.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante la comunicazione con il server:\n" + ex.Message, "Errore del server", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (logDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                string result = logDetails.Result.Content.ReadAsStringAsync().Result;
                List<LogExtended> logExtended = JsonConvert.DeserializeObject<List<LogExtended>>(result);
                if (LogList == null)
                {
                    LogList = new ObservableCollection<LogExtended>();
                }
                LogList.Clear();
                foreach (LogExtended log in logExtended)
                {
                    LogList.Add(log);
                }
            }
            else
            {
                MessageBox.Show("Non si è riusciti a ricaricare della tabella.\nErrore: " + logDetails.Result.StatusCode, "Impossibile ricaricare", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }
    }
}
