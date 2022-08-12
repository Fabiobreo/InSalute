using BusinessLogic;
using REST_API.Models;
using System;
using System.Net;
using System.Text;
using System.Web.Http;

namespace REST_API.Controllers
{
    public class LoginController : ApiController
    {
        #region GET
        /// <summary>
        /// Validate the login of a user
        /// </summary>
        /// <param name="username">The username of a user</param>
        /// <param name="password">The encrypted password of a user</param>
        /// <returns>Status ok if the user has successfully logged in</returns>
        [HttpGet]
        public IHttpActionResult Get(string username, string password)
        {
            string frombase64pwd = Encoding.UTF8.GetString(Convert.FromBase64String(password));
            if (UserValidate.Login(username, frombase64pwd))
            {
                Users user = UserValidate.GetUserDetails(username, frombase64pwd);
                var response = new { Id = user.id, Username = user.username, Role = user.role, CreationDate = user.creation_date, Email = user.email,
                    Token = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + frombase64pwd)),
                    Status = user.username + " successfully logged in as " + user.role };
                return Ok(response);
            }
            else
            {
                return Content(HttpStatusCode.NotFound, "User with these credentials was not found");
            }
        }

        #endregion GET
    }
}
