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
        public const string WEB_ORIGIN = "https://truongson.huelms.com";
        public const string WEB_LOGIN_URL = WEB_ORIGIN + "/user/login?b=BLddg3cXu25CkuF7rOxu+n/sDKtXz5WrSYkcqndM47c=";

        public ApiService()
        {
            _client = new HttpClient();
            _client.BaseAddress = new Uri(BASE_URL);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", "\"Not-A.Brand\";v=\"24\", \"Chromium\";v=\"146\"");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", WEB_ORIGIN);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", WEB_LOGIN_URL);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "name=value");
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
