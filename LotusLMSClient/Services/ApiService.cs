using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LotusLMSClient.Services
{
    public class ApiService
    {
        private readonly HttpClient _client;
        private const string BASE_URL = "https://staging-api.lotuslms.com";

        public ApiService()
        {
            _client = new HttpClient();
            _client.BaseAddress = new Uri(BASE_URL);
            _client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _client.DefaultRequestHeaders.Add("User-Agent", "LotusLMS-DesktopClient/1.0");
            _client.DefaultRequestHeaders.Add("Origin", "https://truongson.huelms.com");
            _client.DefaultRequestHeaders.Add("Referer", "https://truongson.huelms.com/user/login/?logout=1");
        }

        public void SetAuthToken(string token)
        {
            if (_client.DefaultRequestHeaders.Contains("Authorization"))
            {
                _client.DefaultRequestHeaders.Remove("Authorization");
            }
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        }

        public async Task<T> PostFormDataAsync<T>(string endpoint, Dictionary<string, string> formData)
        {
            try
            {
                var content = new MultipartFormDataContent();
                foreach (var kvp in formData)
                {
                    content.Add(new StringContent(kvp.Value), kvp.Key);
                }

                var response = await _client.PostAsync(endpoint, content);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(jsonString);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Lỗi kết nối tới server: {ex.Message}", ex);
            }
        }
    }
}
