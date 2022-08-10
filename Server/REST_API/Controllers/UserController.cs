using DataAccessLayer;
using REST_API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Http;

namespace REST_API.Controllers
{
    public class UserController : ApiController
    {
        #region GET
        /// <summary>  
        /// Get user details found by id
        /// </summary>  
        /// <param name="id">id of user</param>  
        /// <returns>user details of id</returns>  
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpGet]
        public IHttpActionResult Get(long id)
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    var user = entities.Users.FirstOrDefault(us => us.id == id);
                    if (user != null)
                    {
                        return Get(user);
                    }
                    else
                    {
                        return Content(HttpStatusCode.NotFound, "User with id: " + id + " not found");
                    }
                }
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.BadRequest, ex);
            }
        }

        /// <summary>  
        /// Get user details found by username   
        /// </summary>  
        /// <param name="id">Username of user</param>  
        /// <returns>user details of user with given username</returns>  
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpGet]
        public IHttpActionResult Get(string username)
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    var user = entities.Users.FirstOrDefault(us => us.username == username);
                    if (user != null)
                    {
                        return Get(user);
                    }
                    else
                    {
                        return Content(HttpStatusCode.NotFound, "User with username: " + username + " not found");
                    }
                }
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.BadRequest, ex);

            }
        }

        private IHttpActionResult Get(Users user)
        {
            IPrincipal principal = Thread.CurrentPrincipal;
            if (((ClaimsIdentity)principal.Identity).HasClaim("Id", user.id.ToString()))
            {
                return Ok(new
                {
                    Id = user.id,
                    Username = user.username,
                    Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(Security.Decrypt(user.password, Security.toCheck))),
                    Role = user.role,
                    CreationDate = user.creation_date.ToString("MM/dd/yyyy HH:mm:ss")
                });
            }
            else
            {
                if (principal.IsInRole("manager"))
                {
                    if (user.role.ToLower() == "admin" || user.role.ToLower() == "manager")
                    {
                        return Content(HttpStatusCode.BadRequest, "You can't get details of users that have the same or higher roles than you.");
                    }
                }
                else if (principal.IsInRole("user"))
                {
                    return Content(HttpStatusCode.BadRequest, "You can't get details of users other than you.");
                }

                return Ok(new
                {
                    Id = user.id,
                    Username = user.username,
                    Role = user.role,
                    CreationDate = user.creation_date.ToString("MM/dd/yyyy HH:mm:ss")
                });
            }
        }


        /// <summary>  
        /// Get all users
        /// </summary>
        /// <returns>a list of users</returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager")]
        [HttpGet]
        public IHttpActionResult Get()
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    var principal = Thread.CurrentPrincipal;
                    List<dynamic> users = new List<dynamic>();
                    if (principal.IsInRole("admin"))
                    {
                        foreach (var user in entities.Users)
                        {
                            users.Add(new { Id = user.id, Username = user.username,
                                Role = user.role, CreationDate = user.creation_date.ToString("MM/dd/yyyy HH:mm:ss") });
                        }
                    }
                    else
                    {
                        foreach (var user in entities.Users)
                        {
                            if (user.role.ToLower() != "admin" && user.role.ToLower() != "manager")
                            {
                                users.Add(new { Id = user.id, Username = user.username, Role = user.role,
                                    CreationDate = user.creation_date.ToString("MM/dd/yyyy HH:mm:ss")
                                });
                            }
                        }
                    }

                    if (users.Count > 0)
                    {
                        return Ok(users);
                    }
                    else
                    {
                        return Content(HttpStatusCode.NotFound, "There are no users in the database or you have not the permission to get them.");
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
        /// Create a new user, if it's an admin that creates the user, it can give them the role that they want
        /// </summary>
        /// <param name="user">details of the user to create (username, password, email)</param>
        /// <returns>details of newly created user (id, email, creation date)</returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager")]
        [HttpPost]
        public HttpResponseMessage PostAuth([FromBody] Users user)
        {
            return Post(user);
        }

        /// <summary>
        /// Register as a new user
        /// </summary>
        /// <param name="user">details of the user to create (username, password, email)</param>
        /// <returns>details of newly created user (id, email, creation date)</returns>
        [Route("api/user/register")]
        [HttpPost]
        public HttpResponseMessage PostNoAuth([FromBody] Users user)
        {
            return Post(user);
        }

        /// <summary>
        /// Register a new user. This is the method underneath the api
        /// </summary>
        /// <param name="user">details of the user to create (username, password, email)</param>
        /// <returns>details of newly created user (id, email, creation date)</returns>
        private HttpResponseMessage Post([FromBody] Users user)
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    #region ids and emails maps
                    HashSet<long> ids = new HashSet<long>();
                    HashSet<string> usernames = new HashSet<string>();
                    foreach (var us in entities.Users)
                    {
                        ids.Add(us.id);
                        usernames.Add(us.username);
                    }

                    if (ids.Contains(user.id) || user.id <= 0)
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
                        user.id = id;
                    }
                    #endregion ids and emails maps

                    #region Username check
                    if (string.IsNullOrWhiteSpace(user.username))
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "You are missing the username field, please provide one.");
                    }

                    if (usernames.Contains(user.username))
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.Conflict, "Conflict: there is already a user with this username");
                    }
                    #endregion Username check

                    #region Password check and encryption
                    if (string.IsNullOrWhiteSpace(user.password))
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "You should set a password for this account.");
                    }
                    string frombase64pwd = Encoding.UTF8.GetString(Convert.FromBase64String(user.password));
                    user.password = Security.Encrypt(frombase64pwd, Security.toCheck);
                    #endregion Password check and encryption

                    #region Creation date
                    user.creation_date = DateTime.Now;
                    #endregion Creation date

                    #region Role check
                    IPrincipal principal = Thread.CurrentPrincipal;
                    string errorMessage = "";
                    if (principal.IsInRole("admin"))
                    {
                        if (user.role == null)
                        {
                            user.role = "user";
                        }
                    }
                    else
                    {
                        if (user.role != null)
                        {
                            errorMessage = "You can't set your own role. Role set to user (default).";
                        }
                        user.role = "user";
                    }
                    user.role = user.role.ToLower();
                    #endregion Role check

                    entities.Users.Add(user);
                    entities.SaveChanges();
                    if (string.IsNullOrWhiteSpace(errorMessage))
                    {
                        var response = new { Id = user.id, CreationDate = user.creation_date.ToString("MM/dd/yyyy HH:mm:ss") };
                        var res = Request.CreateResponse(HttpStatusCode.Created, response);
                        return res;
                    }
                    else
                    {
                        var response = new { Id = user.id, CreationDate = user.creation_date.ToString("MM/dd/yyyy HH:mm:ss"), Message = errorMessage};
                        var res = Request.CreateResponse(HttpStatusCode.Created, response);
                        return res;
                    }
                }
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }
        #endregion POST

        #region PUT
        /// <summary>
        /// Edit a user, found by id
        /// </summary>
        /// <param name="id"></param> The id to find the user with
        /// <param name="edited_user">The new details of the user</param>
        /// <returns>Id, Email and status of the request, if successfull.</returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpPut]
        public HttpResponseMessage Put(long id, [FromBody] Users edited_user)
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    Users user = entities.Users.Where(us => us.id == id).FirstOrDefault();
                    if (user != null)
                    {
                        return Put(entities, edited_user, user);
                    }
                    else
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.NotFound, "User with id: " + id + " was not found!");
                    }
                }
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }

        /// <summary>
        /// Edit a user, found by email
        /// </summary>
        /// <param name="username">The username to find to user with</param>
        /// <param name="edited_user">The new details of the user</param>
        /// <returns>Id, Email and status of the request, if successfull.</returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpPut]
        public HttpResponseMessage Put(string username, [FromBody] Users edited_user)
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    Users user = entities.Users.Where(us => us.username == username).FirstOrDefault();
                    if (user != null)
                    {
                        return Put(entities, edited_user, user);
                    }
                    else
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.NotFound, "User with username " + username + " was not found!");
                    }
                }
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }

        /// <summary>
        /// Edit a user
        /// </summary>
        /// <param name="entities">The database entities</param>
        /// <param name="edited_user">The new details of the user</param>
        /// <param name="user_to_edit">The user to edit</param>
        /// <returns>Id, Email and status of the request, if successfull.</returns>
        private HttpResponseMessage Put(DatabaseEntities entities, Users edited_user, Users user_to_edit)
        {
            string errorMessage = "";
            IPrincipal principal = Thread.CurrentPrincipal;
            if (principal.IsInRole("admin"))
            {
                if (!string.IsNullOrWhiteSpace(edited_user.role))
                {
                    user_to_edit.role = edited_user.role.ToLower();
                }
            }
            else if (principal.IsInRole("manager"))
            {
                if (user_to_edit.id != edited_user.id)
                {
                    if (user_to_edit.role.ToLower() == "admin" || user_to_edit.role.ToLower() == "manager")
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.Unauthorized, "The user that you want to edit has higher permission or the same as you.");
                    }

                    if (!string.IsNullOrWhiteSpace(edited_user.role))
                    {
                        errorMessage = "You can't change this user role, you don't have the permission.\n";
                    }
                }
            }
            else if (principal.IsInRole("user"))
            {
                if (!((ClaimsIdentity)principal.Identity).HasClaim("Id", user_to_edit.id.ToString()))
                {
                    return Request.CreateErrorResponse(HttpStatusCode.Unauthorized, "You don't have the permission to change accounts that are not yours.");
                }

                if (!string.IsNullOrWhiteSpace(edited_user.role))
                {
                    errorMessage = "You can't change this user role, you don't have the permission.\n";
                }
            }

            #region Check Username
            if (!string.IsNullOrWhiteSpace(edited_user.username))
            {
                HashSet<string> usernames = new HashSet<string>();
                foreach (var us in entities.Users)
                {
                    if (us != user_to_edit)
                    {
                        usernames.Add(us.username);
                    }
                }

                if (usernames.Contains(edited_user.username))
                {
                    return Request.CreateErrorResponse(HttpStatusCode.Conflict, "Conflict: there is already a user with this username. No edit will be done to this user.");
                }
                user_to_edit.username = edited_user.username;
            }
            #endregion Check Email

            #region Check Password and encryption
            if (!string.IsNullOrWhiteSpace(edited_user.password))
            {
                if (((ClaimsIdentity)principal.Identity).HasClaim("Id", edited_user.id.ToString()))
                {
                    string frombase64pwd = Encoding.UTF8.GetString(Convert.FromBase64String(edited_user.password));
                    user_to_edit.password = Security.Encrypt(frombase64pwd, Security.toCheck);
                }
                else
                {
                    errorMessage += "Only the account owner can change the password. The password will not be changed.";
                }
            }
            #endregion Check Password

            entities.SaveChanges();

            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                var response = new { Id = user_to_edit.id, Token = Convert.ToBase64String(Encoding.UTF8.GetBytes(user_to_edit.username + ":" + Security.Decrypt(user_to_edit.password, Security.toCheck))),
                    Status = "updated successfully" };
                var res = Request.CreateResponse(HttpStatusCode.OK, response);
                return res;
            }
            else
            {
                var response = new { Id = user_to_edit.id, Token = Convert.ToBase64String(Encoding.UTF8.GetBytes(user_to_edit.username + ":" + Security.Decrypt(user_to_edit.password, Security.toCheck))),
                    Status = "updated successfully", Message = errorMessage };
                var res = Request.CreateResponse(HttpStatusCode.OK, response);
                return res;
            }
        }
        #endregion PUT

        #region DELETE
        /// <summary>
        /// Delete a user identified by its id
        /// </summary>
        /// <param name="id">The id of the user to delete</param>
        /// <returns></returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpDelete]
        public HttpResponseMessage Delete(long id)
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    var user = entities.Users.Where(us => us.id == id).FirstOrDefault();
                    if (user != null)
                    {
                        return Delete(entities, user);
                    }
                    else
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.NotFound, "User with id: " + id + " was not found!");
                    }
                }
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }

        /// <summary>
        /// Delete a user identified by its username
        /// </summary>
        /// <param name="username">The username of the user to delete</param>
        /// <returns></returns>
        [BasicAuthentication]
        [Authorization(Roles = "admin, manager, user")]
        [HttpDelete]
        public HttpResponseMessage Delete(string username)
        {
            try
            {
                using (DatabaseEntities entities = new DatabaseEntities())
                {
                    var user = entities.Users.Where(us => us.username == username).FirstOrDefault();
                    if (user != null)
                    {
                        return Delete(entities, user);
                    }
                    else
                    {
                        return Request.CreateErrorResponse(HttpStatusCode.NotFound, "User with username: " + username + " was not found!");
                    }
                }
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex);
            }
        }

        /// <summary>
        /// Delete a user
        /// </summary>
        /// <param name="entities">The database entities</param>
        /// <param name="user">The user to delete</param>
        /// <returns></returns>
        private HttpResponseMessage Delete(DatabaseEntities entities, Users user)
        {
            IPrincipal principal = Thread.CurrentPrincipal;
            if (principal.IsInRole("manager"))
            {
                if (user.role.ToLower() == "admin" || user.role.ToLower() == "manager")
                {
                    return Request.CreateErrorResponse(HttpStatusCode.Unauthorized, "You don't have the permission to delete accounts with the same or higher permission than yours.");
                }
            }
            else if (principal.IsInRole("user"))
            {
                if (!((ClaimsIdentity)principal.Identity).HasClaim("Id", user.id.ToString()))
                {
                    return Request.CreateErrorResponse(HttpStatusCode.Unauthorized, "You don't have the permission to delete accounts that are not yours.");
                }
            }
            entities.Users.Remove(user);
            entities.SaveChanges();
            var response = new { Id = user.id, Status = "User deleted." };
            var res = Request.CreateResponse(HttpStatusCode.OK, response);
            return res;
        }
        #endregion DELETE
    }
}
