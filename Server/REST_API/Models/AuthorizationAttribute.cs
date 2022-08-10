using System.Web;
using System.Web.Http.Controllers;

namespace REST_API.Models
{
    public class AuthorizationAttribute : System.Web.Http.AuthorizeAttribute
    {
        /// <summary>
        /// Handle unauthorized requests:
        /// 401 (Unauthorized) - indicates that the request has not been applied because it lacks valid 
        /// authentication credentials for the target resource.
        /// 403 (Forbidden) - when the user is authenticated but isn’t authorized to perform the requested 
        /// operation on the given resource.
        /// </summary>
        /// <param name="actionContext">the action context</param>
        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            if (!HttpContext.Current.User.Identity.IsAuthenticated)
            {
                base.HandleUnauthorizedRequest(actionContext);
            }
            else
            {
                actionContext.Response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
            }
        }
    }
}