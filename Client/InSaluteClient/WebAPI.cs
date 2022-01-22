using InSalute.Utilities;
using Microsoft.AspNetCore.WebUtilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace InSalute
{
    class WebAPI
    {
        /// <summary>  
        /// Template for a get call  
        /// </summary>
        /// <param name="url">The url to interrogate</param>
        /// <param name="query">The possible parameters</param>
        /// <param name="auth">Tha authentication string</param>
        /// <returns></returns>
        public static Task<HttpResponseMessage> GetCall(string url, Dictionary<string, string> query = null, string auth = "")
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string apiUrl = API_URIs.baseURI + url;
                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri(apiUrl),
                    Timeout = TimeSpan.FromSeconds(900)
                };
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Authorization", auth);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                Task<HttpResponseMessage> response = null;
                if (query != null)
                {
                    response = client.GetAsync(QueryHelpers.AddQueryString(apiUrl, query));
                }
                else
                {
                    response = client.GetAsync(apiUrl);
                }
                response.Wait();
                return response;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Template for a post call
        /// </summary>
        /// <typeparam name="T">The class to post</typeparam>
        /// <param name="url">The url to interrogate</param>
        /// <param name="model">An object of the class</param>
        /// <param name="auth">Tha authentication string</param>
        /// <returns></returns>
        public static Task<HttpResponseMessage> PostCall<T>(string url, T model, string auth = "") where T : class
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string apiUrl = API_URIs.baseURI + url;
                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri(apiUrl),
                    Timeout = TimeSpan.FromSeconds(900)
                };
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Authorization", auth);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                Task<HttpResponseMessage> response = client.PostAsJsonAsync(apiUrl, model);
                response.Wait();
                return response;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Template for a put call
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="url">The url to interrogate</param>
        /// <param name="query">The possible parameters</param>
        /// <param name="model">An object of the class</param>
        /// <param name="auth">Tha authentication string</param>
        /// <returns></returns>
        public static Task<HttpResponseMessage> PutCall<T>(string url, Dictionary<string, string> query, T model, string auth = "") where T : class
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string apiUrl = API_URIs.baseURI + url;
                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri(apiUrl),
                    Timeout = TimeSpan.FromSeconds(900)
                };
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Authorization", auth);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                Task<HttpResponseMessage> response = client.PutAsJsonAsync(QueryHelpers.AddQueryString(apiUrl, query), model);
                response.Wait();
                return response;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Template for a delete call
        /// </summary>
        /// <param name="url">The url to interrogate</param>
        /// <param name="query">The possible parameters</param>
        /// <param name="auth">Tha authentication string</param>
        /// <returns></returns>
        public static Task<HttpResponseMessage> DeleteCall(string url, Dictionary<string, string> query = null, string auth = "")
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string apiUrl = API_URIs.baseURI + url;
                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri(apiUrl),
                    Timeout = TimeSpan.FromSeconds(900)
                };
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Authorization", auth);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                Task<HttpResponseMessage> response;
                if (query != null)
                {
                    response = client.DeleteAsync(QueryHelpers.AddQueryString(apiUrl, query));
                }
                else
                {
                    response = client.DeleteAsync(apiUrl);
                }
                response.Wait();
                return response;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
