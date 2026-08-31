using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.Services
{
    public class ApiService
    {
        private readonly HttpClient _client;
        private const string BASE_URL = "https://staging-api.lotuslms.com";
        public const string WEB_ORIGIN = "https://yenthuy.huelms.com";
        public const string WEB_LOGIN_URL = WEB_ORIGIN + "/user/login/?logout=1";

        public ApiService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _client = new HttpClient(handler);
            _client.BaseAddress = new Uri(BASE_URL);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", "\"Not-A.Brand\";v=\"24\", \"Chromium\";v=\"146\"");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Priority", "u=1, i");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", WEB_ORIGIN);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", WEB_LOGIN_URL);
        }

        private string _token;

        public void SetAuthToken(string token)
        {
            _token = token;
        }

        public async Task<T> PostFormDataAsync<T>(string endpoint, Dictionary<string, string> formData, Dictionary<string, string> requestHeaders = null, bool sendAuth = false)
        {
            try
            {
                // Dùng FormUrlEncodedContent (application/x-www-form-urlencoded) — giống curl --data-urlencode
                var content = new FormUrlEncodedContent(formData);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = content
                };

                if (sendAuth && !string.IsNullOrEmpty(_token))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_token}");
                }

                if (requestHeaders != null)
                {
                    foreach (var header in requestHeaders)
                    {
                        request.Headers.Remove(header.Key);
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                string reqPayload = string.Empty;
                if (formData != null)
                {
                    var parts = new List<string>();
                    foreach (var kvp in formData)
                    {
                        parts.Add($"{kvp.Key}={kvp.Value}");
                    }
                    reqPayload = string.Join("&", parts);
                }

                var response = await _client.SendAsync(request);
                var responseContent = string.Empty;
                try
                {
                    responseContent = await response.Content.ReadAsStringAsync();
                }
                catch {}

                AppLog.Info($"[API POST] {endpoint} | Payload: {reqPayload} | Status: {response.StatusCode} | Response: {responseContent}");

                response.EnsureSuccessStatusCode();

                return JsonConvert.DeserializeObject<T>(responseContent);
            }
            catch (Exception ex)
            {
                AppLog.Error($"[API Exception] POST {endpoint} failed: {ex.Message}");
                throw new ApplicationException($"Lỗi kết nối tới server: {ex.Message}", ex);
            }
        }

        public async Task<T> PostMultipartFormDataAsync<T>(string endpoint, List<KeyValuePair<string, string>> formData, Dictionary<string, string> requestHeaders = null)
        {
            try
            {
                var content = new MultipartFormDataContent();
                foreach (var kvp in formData)
                {
                    content.Add(new StringContent(kvp.Value ?? ""), kvp.Key);
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = content
                };

                if (requestHeaders != null)
                {
                    foreach (var header in requestHeaders)
                    {
                        request.Headers.Remove(header.Key);
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                var response = await _client.SendAsync(request);
                var responseContent = string.Empty;
                try
                {
                    responseContent = await response.Content.ReadAsStringAsync();
                }
                catch { }

                AppLog.Info($"[API POST Multipart] {endpoint} | Status: {response.StatusCode} | Response: {responseContent}");

                response.EnsureSuccessStatusCode();

                return JsonConvert.DeserializeObject<T>(responseContent);
            }
            catch (Exception ex)
            {
                AppLog.Error($"[API Exception] POST Multipart {endpoint} failed: {ex.Message}");
                throw new ApplicationException($"Lỗi kết nối tới server: {ex.Message}", ex);
            }
        }

        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string> requestHeaders = null, bool sendAuth = false)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

                if (sendAuth && !string.IsNullOrEmpty(_token))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_token}");
                }

                if (requestHeaders != null)
                {
                    foreach (var header in requestHeaders)
                    {
                        request.Headers.Remove(header.Key);
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                var response = await _client.SendAsync(request);
                var responseContent = string.Empty;
                try
                {
                    responseContent = await response.Content.ReadAsStringAsync();
                }
                catch {}

                AppLog.Info($"[API GET] {endpoint} | Status: {response.StatusCode} | Response: {responseContent}");

                response.EnsureSuccessStatusCode();

                return JsonConvert.DeserializeObject<T>(responseContent);
            }
            catch (Exception ex)
            {
                AppLog.Error($"[API Exception] GET {endpoint} failed: {ex.Message}");
                throw new ApplicationException($"Lỗi kết nối tới server: {ex.Message}", ex);
            }
        }
    }
}
