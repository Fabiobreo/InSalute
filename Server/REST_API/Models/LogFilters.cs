using BusinessLogic;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace REST_API.Models
{
    public class LogFilters : FilterModelBase
    {
        [FromQuery(Name = "fromDate")]
        public DateTime FromDate { get; set; } = default;

        [FromQuery(Name = "toDate")]
        public DateTime ToDate { get; set; } = DateTime.Now;

        public long user_id;

        public List<Log> Filter(DbSet<Log> logs)
        {
            List<Log> dateFilters = logs.Where(exp => exp.sending_time >= FromDate && exp.sending_time <= ToDate).ToList();
            return dateFilters.Where(log => user_id != 0 ? log.user_id == user_id : true).ToList().OrderBy(log => log.user_id).ThenByDescending(log => log.sending_time).ThenBy(log => log.id).ToList();
        }

        public override object Clone()
        {
            var jsonString = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject(jsonString, this.GetType());
        }
    }
}