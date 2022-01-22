using System.Collections.Generic;

namespace InSalute.Models
{
    public class UserExpenses
    {
        public long UserId { get; set; }

        public List<WeeklyExpense> WeeklyExpenses { get; set; }
    }
}
