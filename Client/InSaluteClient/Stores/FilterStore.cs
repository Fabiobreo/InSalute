using InSalute.Models;
using System;

namespace InSalute.Stores
{
    public class FilterStore
    {
        private Filter _currentFilter = new Filter();
        public Filter CurrentFilter
        {
            get => _currentFilter;
            set
            {
                _currentFilter = value;
                CurrentFilterChanged?.Invoke();
            }
        }

        public event Action CurrentFilterChanged;
    }
}
