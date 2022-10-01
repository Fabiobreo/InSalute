using BusinessLogic;
using REST_API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Web.Http;

namespace REST_API.Controllers
{
    public class LogController : ApiController
    {
        #region GET
        /// <summary>
        /// Get logs, with the possibility to filter them (check LogFilters class)
        /// </summary>
        /// <param name="filters">The filters to set</param>
        /// <returns>A list of the logs for the user(s) printed per week, with the total amount and average daily spending.</returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpGet]
        public IHttpActionResult Get([FromUri] LogFilters filters)
        {
            try
            {
                using (InSaluteEntities entities = new InSaluteEntities())
                {
                    IPrincipal principal = Thread.CurrentPrincipal;
                    if (!principal.IsInRole("admin"))
                    {
                        long loggedIn = Convert.ToInt32(((ClaimsIdentity)principal.Identity)?.FindFirst("Id").Value);
                        filters.user_id = loggedIn;
                    }

                    List<Log> filteredResults = filters.Filter(entities.Log);
                    if (filteredResults.Count > 0)
                    {
                        List<dynamic> logs = new List<dynamic>();
                        foreach (Log log in filteredResults)
                        {
                            var user = entities.Users.FirstOrDefault(us => us.id == log.user_id);
                            if (user != null)
                            {
                                logs.Add(new
                                {
                                    Id = log.id,
                                    Sender = user.username,
                                    ReceiverEmail = log.receiver_email,
                                    SendingTime = log.sending_time
                                });
                            }
                        }
                        return Ok(logs);
                    }
                    else
                    {
                        return Content(HttpStatusCode.NotFound, "Non ci sono log nel database che soddisfano i requisiti.");
                    }
                }
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.BadRequest, ex);
            }
        }
        #endregion GET

        #region POST
        /// <summary>
        /// Add a log
        /// </summary>
        /// <param name="log">The log to add</param>
        /// <returns></returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpPost]
        public HttpResponseMessage Post([FromBody] Log log)
        {
            try
            {
                using (InSaluteEntities entities = new InSaluteEntities())
                {
                    #region ids maps
                    HashSet<long> ids = new HashSet<long>();
                    foreach (var us in entities.Log)
                    {
                        ids.Add(us.id);
                    }

                    if (ids.Contains(log.id) || log.id <= 0)
                    {
                        // Search first available id
                        long id = -1;
                        for (long i = 1; ; ++i)
                        {
                            if (!ids.Contains(i))
                            {
                                id = i;
                                break;
                            }
                        }
                        log.id = id;
                    }
                    #endregion ids maps

                    #region User id check
                    if (log.user_id == 0)
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Manca il campo user id, per favore inseriscilo.");
                    }
                    #endregion User id check

                    #region Receiver email check
                    if (string.IsNullOrWhiteSpace(log.receiver_email))
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Manca il campo ricettore email, per favore inseriscilo.");
                    }
                    #endregion Receiver email check

                    #region Date check
                    DateTime now = DateTime.Now;
                    if (log.sending_time == null || log.sending_time == default)
                    {
                        log.sending_time = now.Date;
                    }
                    #endregion Date check

                    entities.Log.Add(log);
                    entities.SaveChanges();
                    var response = new
                    {
                        Id = log.id,
                        UserId = log.user_id,
                        ReceiverEmail = log.receiver_email,
                        SendingDate = log.sending_time.ToString("dd/MM/yyyy")
                    };
                    return Request.CreateResponse(HttpStatusCode.Created, response);
                }
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }
        #endregion POST

    }
}
