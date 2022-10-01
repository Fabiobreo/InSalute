using BusinessLogic;
using System;
using System.Linq;

namespace REST_API.Models
{
    public class UserValidate
    {
        /// <summary>
        /// This method is used to check the user credentials
        /// </summary>
        /// <param name="credentials">The credentials to check</param>
        /// <param name="password">The password to check</param>
        /// <returns>True if there's a user with that email and password</returns>
        public static bool Login(string credentials, string password)
        {
            try
            {
                using (InSaluteEntities entities = new InSaluteEntities())
                {
                    entities.Database.Connection.Open();
                    string encryptedPwd = Security.Encrypt(password, Security.toCheck);
                    return entities.Users.Any(user =>
                        (user.username.Equals(credentials, StringComparison.OrdinalIgnoreCase) || user.email.Equals(credentials, StringComparison.OrdinalIgnoreCase))
                        && user.password == encryptedPwd);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// This method is used to return the User Details
        /// </summary>
        /// <param name="credentials">The credentials of the user to find</param>
        /// <param name="password">The password of the user to find</param>
        /// <returns>The user if found, null otherwise</returns>
        public static Users GetUserDetails(string credentials, string password)
        {
            try
            {
                using (InSaluteEntities entities = new InSaluteEntities())
                {
                    string encryptedPwd = Security.Encrypt(password, Security.toCheck);
                    return entities.Users.FirstOrDefault(user =>
                        (user.username.Equals(credentials, StringComparison.OrdinalIgnoreCase) || user.email.Equals(credentials, StringComparison.OrdinalIgnoreCase))
                        && user.password == encryptedPwd);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}