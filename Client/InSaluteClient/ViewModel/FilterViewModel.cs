using InSalute.Models;
using InSalute.Stores;
using MVVMEssentials.Commands;
using MVVMEssentials.Services;
using MVVMEssentials.ViewModels;
using Prism.Commands;
using System;
using System.Windows;
using System.Windows.Input;

namespace InSalute.ViewModel
{
    public class FilterViewModel : ViewModelBase
    {
        #region Defaults
        private DateTime _defaultFromDate = default;

        private DateTime _defaultToDate = DateTime.Now;

        private decimal _defaultMinAmount = decimal.MinValue;

        private decimal _defaultMaxAmount = decimal.MaxValue;
        #endregion Defaults

        #region Binding variables
        private DateTime _fromDate = default;

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

        private decimal _minAmount = decimal.MinValue;

        public decimal MinAmount
        {
            get => _minAmount;
            set
            {
                _minAmount = value;
                OnPropertyChanged(nameof(MinAmount));
            }
        }

        private decimal _maxAmount = decimal.MaxValue;

        public decimal MaxAmount
        {
            get => _maxAmount;
            set
            {
                _maxAmount = value;
                OnPropertyChanged(nameof(MaxAmount));
            }
        }
        #endregion Binding variables

        private FilterStore FilterStore;

        #region UI buttons
        public DelegateCommand SetFiltersCommand { get; set; }
        public DelegateCommand ClearFiltersCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }
        public ICommand CloseFilterCommand { get; }
        #endregion UI buttons

        public FilterViewModel(FilterStore filterStore, INavigationService closeModalService)
        {
            FilterStore = filterStore;
            SetFiltersCommand = new DelegateCommand(SetFilters);
            ClearFiltersCommand = new DelegateCommand(ClearFilters);
            CancelCommand = new DelegateCommand(Cancel);
            CloseFilterCommand = new NavigateCommand(closeModalService);
            FromDate = FilterStore.CurrentFilter.FromDate;
            ToDate = FilterStore.CurrentFilter.ToDate;
            MinAmount = FilterStore.CurrentFilter.MinAmount;
            MaxAmount = FilterStore.CurrentFilter.MaxAmount;
        }

        private void Cancel()
        {
            CloseFilterCommand.Execute(null);
        }

        private void ClearFilters()
        {
            FromDate = _defaultFromDate;
            ToDate = _defaultToDate;
            MinAmount = _defaultMinAmount;
            MaxAmount = _defaultMaxAmount;

            Filter clearFilter = new Filter()
            {
                FromDate = FromDate,
                ToDate = ToDate,
                MinAmount = MinAmount,
                MaxAmount = MaxAmount
            };
            FilterStore.CurrentFilter = clearFilter;
            CloseFilterCommand.Execute(null);
        }

        private void SetFilters()
        {
            if (FromDate > ToDate)
            {
                MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show("Are you sure that you want to set the from date higher than the to date?", "No results.", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            if (MinAmount > MaxAmount)
            {
                MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show("Are you sure that you want to set the minimum amount higher than the maximum amount?", "No results.", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            Filter clearFilter = new Filter()
            {
                FromDate = FromDate,
                ToDate = ToDate,
                MinAmount = MinAmount,
                MaxAmount = MaxAmount
            };
            FilterStore.CurrentFilter = clearFilter;
            CloseFilterCommand.Execute(null);
        }
    }
}
