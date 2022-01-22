using DataAccessLayer;
using InSalute.Models;
using InSalute.Stores;
using InSalute.Utilities;
using MVVMEssentials.Services;
using MVVMEssentials.ViewModels;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.ObjectModel;
using System.Windows.Data;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Globalization;
using System.Windows.Input;
using MVVMEssentials.Commands;

namespace InSalute.ViewModel
{
    class CoreViewModel : ViewModelBase
    {
        #region Expenses Table
        private ObservableCollection<ExpenseExtended> _expensesList;

        public ObservableCollection<ExpenseExtended> ExpensesList
        {
            get => _expensesList;
            set
            {
                _expensesList = value;
                OnPropertyChanged(nameof(ExpensesList));
            }
        }

        private CollectionViewSource _expensesCollection = new CollectionViewSource();
        public CollectionViewSource ExpensesCollection
        {
            get
            {
                _expensesCollection.Source = ExpensesList;
                return _expensesCollection;
            }
            set
            {
                if (value != _expensesCollection)
                {
                    _expensesCollection = value;
                    OnPropertyChanged(nameof(ExpensesCollection));
                }
            }
        }

        private List<ExpenseExtended> EditedExpenses = new List<ExpenseExtended>();
        #endregion Expenses Table

        #region Insert Expense
        private DateTime _newExpenseDate = DateTime.Now;

        public DateTime NewExpenseDate
        {
            get => _newExpenseDate;
            set
            {
                _newExpenseDate = value;
                OnPropertyChanged(nameof(NewExpenseDate));
            }
        }

        private DateTime _newExpenseTime = DateTime.Now;

        public DateTime NewExpenseTime
        {
            get => _newExpenseTime;
            set
            {
                _newExpenseTime = value;
                OnPropertyChanged(nameof(NewExpenseTime));
            }
        }

        private string _newExpenseDescription;

        public string NewExpenseDescription
        {
            get => _newExpenseDescription;
            set
            {
                _newExpenseDescription = value;
                OnPropertyChanged(nameof(NewExpenseDescription));
            }
        }

        private decimal _newExpenseAmount;

        public decimal NewExpenseAmount
        {
            get => _newExpenseAmount;
            set
            {
                _newExpenseAmount = value;
                OnPropertyChanged(nameof(NewExpenseAmount));
            }
        }

        private long _newExpenseUserId;

        public long NewExpenseUserId
        {
            get => _newExpenseUserId;
            set
            {
                _newExpenseUserId = value;
                OnPropertyChanged(nameof(NewExpenseUserId));
            }
        }

        private bool _newExpenseIdEnable;

        public bool NewExpenseIdEnable
        {
            get => _newExpenseIdEnable;
            set
            {
                _newExpenseIdEnable = value;
                OnPropertyChanged(nameof(NewExpenseIdEnable));
            }
        }

        private string _newExpenseComment;

        public string NewExpenseComment
        {
            get => _newExpenseComment;
            set
            {
                _newExpenseComment = value;
                OnPropertyChanged(nameof(NewExpenseComment));
            }
        }

        #endregion Insert Expense

        #region Statistic Table
        private ObservableCollection<WeeklyExpense> _statisticsList;

        public ObservableCollection<WeeklyExpense> StatisticsList
        {
            get => _statisticsList;
            set
            {
                _statisticsList = value;
                OnPropertyChanged(nameof(StatisticsList));
            }
        }

        private CollectionViewSource _statisticsCollection = new CollectionViewSource();
        public CollectionViewSource StatisticsCollection
        {
            get
            {
                _statisticsCollection.Source = StatisticsList;
                return _statisticsCollection;
            }
            set
            {
                if (value != _statisticsCollection)
                {
                    _statisticsCollection = value;
                    OnPropertyChanged(nameof(StatisticsCollection));
                }
            }
        }
        #endregion

        #region UI buttons
        public DelegateCommand DeleteExpensesCommand { get; set; }
        public DelegateCommand EditExpensesCommand { get; set; }
        public DelegateCommand ReloadExpensesCommand { get; set; }
        public DelegateCommand AddExpenseCommand { get; set; }
        public DelegateCommand FilterExpensesCommand { get; set; }
        public DelegateCommand LogoutCommand { get; set; }
        public ICommand NavigateHomeCommand { get; }
        public ICommand NavigateLoginCommand { get; }
        public ICommand NavigateFilterCommand { get; }
        public ICommand NavigateUserCommand { get; }
        public ICommand NavigateManageUserCommand { get; }

        #endregion UI buttons

        #region UI utilities

        private bool _isManageAllAccountVisible;
        public bool IsManageAllAccountVisible
        {
            get => _isManageAllAccountVisible;
            set
            {
                _isManageAllAccountVisible = value;
                OnPropertyChanged(nameof(IsManageAllAccountVisible));
            }
        }

        private bool _isNotAdmin;
        public bool IsNotAdmin
        {
            get => _isNotAdmin;
            set
            {
                _isNotAdmin = value;
                OnPropertyChanged(nameof(IsNotAdmin));
            }
        }

        private string _displayedUsername = "";
        public string DisplayedUsername
        {
            get => _displayedUsername;
            set
            {
                _displayedUsername = value;
                OnPropertyChanged(nameof(DisplayedUsername));
            }
        }
        #endregion UI utilities

        private readonly UserStore UserStore;
        private readonly FilterStore FilterStore;

        public CoreViewModel(UserStore userStore, FilterStore filterStore, INavigationService homeNavigationService,
            INavigationService loginNavigationService, INavigationService filterNavigationService,
            INavigationService userNavigationService, INavigationService manageUserNavigationService)
        {
            DeleteExpensesCommand = new DelegateCommand(DeleteExpenses);
            EditExpensesCommand = new DelegateCommand(EditExpenses);
            ReloadExpensesCommand = new DelegateCommand(ReloadExpenses);
            AddExpenseCommand = new DelegateCommand(AddExpense);
            FilterExpensesCommand = new DelegateCommand(FilterExpenses);
            LogoutCommand = new DelegateCommand(Logout);

            NavigateHomeCommand = new NavigateCommand(homeNavigationService);
            NavigateLoginCommand = new NavigateCommand(loginNavigationService);
            NavigateFilterCommand = new NavigateCommand(filterNavigationService);
            NavigateUserCommand = new NavigateCommand(userNavigationService);
            NavigateManageUserCommand = new NavigateCommand(manageUserNavigationService);

            NavigateLoginCommand.Execute(null);

            ExpensesCollection.SortDescriptions.Add(new SortDescription("Date", ListSortDirection.Descending));
            ExpensesCollection.SortDescriptions.Add(new SortDescription("Time", ListSortDirection.Descending));
            StatisticsCollection.SortDescriptions.Add(new SortDescription("WeeksAgo", ListSortDirection.Ascending));
            StatisticsCollection.SortDescriptions.Add(new SortDescription("UserId", ListSortDirection.Ascending));

            UserStore = userStore;
            FilterStore = filterStore;
            UserStore.CurrentUserChanged += UserStore_CurrentUserChanged;
            FilterStore.CurrentFilterChanged += FilterStore_CurrentFilterChanged;

            ExpensesList = new ObservableCollection<ExpenseExtended>();
            StatisticsList = new ObservableCollection<WeeklyExpense>();
        }

        private void Logout()
        {
            UserStore.CurrentUser = null;
            UserStore.CurrentUserChanged -= UserStore_CurrentUserChanged;
            NavigateHomeCommand.Execute(null);
            NavigateLoginCommand.Execute(null);
        }

        private void FilterStore_CurrentFilterChanged()
        {
            ReloadExpenses();
        }

        private void UserStore_CurrentUserChanged()
        {
            if (UserStore.CurrentUser != null)
            {
                NewExpenseUserId = UserStore.CurrentUser.Id;
                NewExpenseIdEnable = UserStore.CurrentUser.Role.ToLower() == "admin";
                IsManageAllAccountVisible = UserStore.CurrentUser.Role.ToLower() == "admin" || UserStore.CurrentUser.Role.ToLower() == "manager";
                IsNotAdmin = UserStore.CurrentUser.Role != "admin";
                DisplayedUsername = UserStore.CurrentUser.Username + " (" + UserStore.CurrentUser.Role + ")";
            }
            else
            {
                NewExpenseUserId = 0;
                NewExpenseIdEnable = false;
                IsManageAllAccountVisible = false;
                IsNotAdmin = true;
                DisplayedUsername = "";
            }
            ReloadExpenses();
        }

        private void DeleteExpenses()
        {
            List<ExpenseExtended> expenseToDelete = new List<ExpenseExtended>();
            foreach (ExpenseExtended expense in ExpensesList)
            {
                if (expense.IsChecked)
                {
                    expenseToDelete.Add(expense);
                }
            }
            
            if (expenseToDelete.Count > 0)
            {
                MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show("Do you really want to delete " + expenseToDelete.Count + " expenses?\nThis choice is final.", "Delete expenses", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    int eliminated = 0;
                    foreach (ExpenseExtended expense in expenseToDelete)
                    {
                        Dictionary<string, string> parameters = new Dictionary<string, string>()
                        {
                            ["id"] = expense.Id.ToString()
                        };

                        Task<HttpResponseMessage> expenseDetails = null;
                        try
                        {
                            expenseDetails = WebAPI.DeleteCall(API_URIs.expense, parameters, UserStore.CurrentUser.Token);
                        }
                        catch (Exception ex)
                        {
                            Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        if (expenseDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            eliminated++;
                            if (EditedExpenses.Count > 0)
                            {
                                ExpenseExtended toEliminate = EditedExpenses.Find(exp => exp.Id == expense.Id);
                                if (toEliminate != null)
                                {
                                    EditedExpenses.Remove(toEliminate);
                                }
                                
                            }
                        }
                    }

                    if (eliminated == expenseToDelete.Count)
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show("All expenses were deleted successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show("There was some problems and we could delete only " + eliminated + " out of " + expenseToDelete.Count + " expenses.\n" +
                            "Please retry to delete later.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    ReloadExpenses();
                }
            }
            else
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("There are no expenses listed to be eliminated.", "No eliminated expenses", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditExpenses()
        {
            if (EditedExpenses.Count > 0)
            {
                int total = EditedExpenses.Count;
                int edited = 0;
                for (int i = EditedExpenses.Count - 1; i >= 0; i--)
                {
                    ExpenseExtended expense = EditedExpenses[i];
                    Dictionary<string, string> parameters = new Dictionary<string, string>()
                    {
                        ["id"] = expense.Id.ToString()
                    };

                    Expenses editExpense = new Expenses()
                    {
                        id = expense.Id,
                        description = expense.Description,
                        amount = expense.Amount,
                        comment = expense.Comment,
                        date = expense.Date,
                        time = expense.Time,
                        user_id = expense.UserId
                    };

                    Task<HttpResponseMessage> expenseDetails = null;
                    try
                    {
                        expenseDetails = WebAPI.PutCall(API_URIs.expense, parameters, editExpense, UserStore.CurrentUser.Token);
                    }
                    catch (Exception ex)
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (expenseDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        edited++;
                        EditedExpenses.RemoveAt(i);
                    }
                }

                if (edited == total)
                {
                    Xceed.Wpf.Toolkit.MessageBox.Show("All expenses were saved successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Xceed.Wpf.Toolkit.MessageBox.Show("There was some problems and we could save only " + edited.ToString() + " out of " + total.ToString() + " expenses.\nPlease retry to save later.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                ReloadExpenses();
            }
            else
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("All expenses are already saved.", "No changes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ReloadExpenses()
        {
            if (UserStore.CurrentUser == null)
            {
                StatisticsList.Clear();
                ExpensesList.Clear();
                return;
            }

            bool skipReload = false;
            if (EditedExpenses.Count > 0)
            {
                MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show("You have unsaved edits, do you really want to reload your expense and lose your changes?", "Pending changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    skipReload = true;
                }
                else
                {
                    EditedExpenses.Clear();
                }
            }

            if (skipReload)
            {
                return;
            }

            Dictionary<string, string> parameters = new Dictionary<string, string>()
            {
                ["fromDate"] = FilterStore.CurrentFilter.FromDate.ToString(CultureInfo.InvariantCulture),
                ["toDate"] = FilterStore.CurrentFilter.ToDate.ToString(CultureInfo.InvariantCulture),
                ["minAmount"] = FilterStore.CurrentFilter.MinAmount.ToString(),
                ["maxAmount"] = FilterStore.CurrentFilter.MaxAmount.ToString()
            };

            Task<HttpResponseMessage> expenseDetails = null;
            try
            {
                expenseDetails = WebAPI.GetCall(API_URIs.expense, parameters, UserStore.CurrentUser.Token);
            }
            catch (Exception ex)
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (expenseDetails.Result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                string format = "dd/MM/yyyy"; // your datetime format
                IsoDateTimeConverter dateTimeConverter = new IsoDateTimeConverter { DateTimeFormat = format };
                string result = expenseDetails.Result.Content.ReadAsStringAsync().Result;
                List<UserExpenses> userExpenses = JsonConvert.DeserializeObject<List<UserExpenses>>(result, dateTimeConverter);
                if (StatisticsList == null)
                {
                    StatisticsList = new ObservableCollection<WeeklyExpense>();
                }
                StatisticsList.Clear();

                if (ExpensesList == null)
                {
                    ExpensesList = new ObservableCollection<ExpenseExtended>();
                }
                ExpensesList.Clear();
                
                foreach (UserExpenses userExpense in userExpenses)
                {
                    foreach (WeeklyExpense weeklyExpense in userExpense.WeeklyExpenses)
                    {
                        StatisticsList.Add(weeklyExpense);
                        foreach (Expenses expense in weeklyExpense.Expenses)
                        {
                            ExpenseExtended expenseExtended = new ExpenseExtended(expense);
                            expenseExtended.PropertyChanged += ExpenseExtended_PropertyChanged;
                            ExpensesList.Add(expenseExtended);
                        }
                    }
                }
            }
            else if (expenseDetails.Result.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return; // No expense for the user
            }
            else
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("Could not refresh expenses.\nError: " + expenseDetails.Result.StatusCode, "Could not refresh", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExpenseExtended_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is ExpenseExtended editedExpense)
            {
                if (!EditedExpenses.Any(expense => expense.Id == editedExpense.Id))
                {
                    EditedExpenses.Add(editedExpense);
                }
            }
        }

        private void AddExpense()
        {
            DateTime date = NewExpenseDate.Date.Add(new TimeSpan(NewExpenseTime.Hour, NewExpenseTime.Minute, NewExpenseTime.Second));
            if (date <= DateTime.Now)
            {
                if (!string.IsNullOrWhiteSpace(NewExpenseDescription))
                {
                    if (NewExpenseAmount != 0)
                    {
                        if (NewExpenseUserId > 0)
                        {
                            Expenses newExpense = new Expenses()
                            {
                                date = NewExpenseDate,
                                time = new TimeSpan(NewExpenseTime.Hour, NewExpenseTime.Minute, NewExpenseTime.Second),
                                description = NewExpenseDescription,
                                amount = NewExpenseAmount,
                                comment = NewExpenseComment,
                                user_id = NewExpenseUserId
                            };
                            Task<HttpResponseMessage> expenseDetails = null;
                            try
                            {
                                expenseDetails = WebAPI.PostCall(API_URIs.expense, newExpense, UserStore.CurrentUser.Token);
                            }
                            catch (Exception ex)
                            {
                                Xceed.Wpf.Toolkit.MessageBox.Show("Error during the comunication with the server:\n" + ex.Message, "Server error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }

                            if (expenseDetails.Result.StatusCode == System.Net.HttpStatusCode.Created)
                            {
                                Xceed.Wpf.Toolkit.MessageBox.Show("Expense successfully added to list.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                                ReloadExpenses();

                                // Reset to default
                                NewExpenseDate = DateTime.Now;
                                NewExpenseTime = DateTime.Now;
                                NewExpenseAmount = 0;
                                NewExpenseUserId = UserStore.CurrentUser.Id;
                                NewExpenseDescription = string.Empty;
                                NewExpenseComment = string.Empty;
                            }
                            else
                            {
                                Xceed.Wpf.Toolkit.MessageBox.Show("Could not add expense.\nError: " + expenseDetails.Result.StatusCode, "Error adding expense", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                        {
                            Xceed.Wpf.Toolkit.MessageBox.Show("There are no users with a negative or zero id, please change it.", "Wrong id", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show("Please add an amount to your expense.", "No amount", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    Xceed.Wpf.Toolkit.MessageBox.Show("Please add a description to your expense.", "Empty description", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("You can't add a future expense.", "Wrong date", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FilterExpenses()
        {
            NavigateFilterCommand.Execute(null);
            ReloadExpenses();
        }

    }
}
