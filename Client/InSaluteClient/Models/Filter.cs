using System;
using System.ComponentModel;

namespace InSalute.Models
{
    public class Filter : INotifyPropertyChanged
    {
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
