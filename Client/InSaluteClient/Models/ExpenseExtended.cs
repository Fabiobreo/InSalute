using DataAccessLayer;
using System;
using System.ComponentModel;

namespace InSalute.Models
{
    public class ExpenseExtended : INotifyPropertyChanged
    {
        private bool _isChecked;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
            }
        }

        private long _id;

        public long Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        private string _description;

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        private decimal _amount;

        public decimal Amount
        {
            get => _amount;
            set
            {
                _amount = value;
                OnPropertyChanged(nameof(Amount));
            }
        }

        private DateTime _date;

        public DateTime Date
        {
            get => _date;
            set
            {
                _date = value;
                OnPropertyChanged(nameof(Date));
            }
        }

        private TimeSpan _time;

        public TimeSpan Time
        {
            get => _time;
            set
            {
                _time = value;
                OnPropertyChanged(nameof(Time));
            }
        }

        private string _comment;

        public string Comment
        {
            get => _comment;
            set
            {
                _comment = value;
                OnPropertyChanged(nameof(Comment));
            }
        }

        private long _user_id;

        public long UserId
        {
            get => _user_id;
            set
            {
                _user_id = value;
                OnPropertyChanged(nameof(UserId));
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ExpenseExtended() { }

        public ExpenseExtended(Expenses expense)
        {
            Id = expense.id;
            Description = expense.description;
            Amount = expense.amount;
            Date = expense.date.Date;
            Time = expense.time;
            Comment = expense.comment;
            UserId = expense.user_id;
        }
    }
}
