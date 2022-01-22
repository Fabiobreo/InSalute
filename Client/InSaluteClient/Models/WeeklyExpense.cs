using DataAccessLayer;
using System.Collections.Generic;

namespace InSalute.Models
{
    public class WeeklyExpense
    {
        public int WeeksAgo { get; set; }

        public List<Expenses> Expenses { get; set;}

        public decimal Total { get; set; }

        public decimal AvgDaily { get; set; }

        public long UserId { get; set; }
    }
}
