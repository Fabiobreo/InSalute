using BusinessLogic;
using System;
using System.ComponentModel;

namespace InSalute.Models
{
    public class LogExtended : INotifyPropertyChanged
    {
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

        private string _sender;

        public string Sender
        {
            get => _sender;
            set
            {
                _sender = value;
                OnPropertyChanged(nameof(Sender));
            }
        }

        private string _receiverEmail;

        public string ReceiverEmail
        {
            get => _receiverEmail;
            set
            {
                _receiverEmail = value;
                OnPropertyChanged(nameof(ReceiverEmail));
            }
        }

        private DateTime _sendingTime;

        public DateTime SendingTime
        {
            get => _sendingTime;
            set
            {
                _sendingTime = value;
                OnPropertyChanged(nameof(SendingTime));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public LogExtended() { }

        public LogExtended(Log log)
        {
            Id = log.id;
            Sender = log.user_id.ToString();
            ReceiverEmail = log.receiver_email;
            SendingTime = log.sending_time;
        }
    }
}
