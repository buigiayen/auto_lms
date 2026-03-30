using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LotusLMSClient.Models;

namespace LotusLMSClient.Services
{
    public class AuthService
    {
        private readonly ApiService _apiService;

        public bool IsAuthenticated { get; private set; }
        public string CurrentToken { get; private set; }
        public string CurrentCccd { get; private set; }
        public string CurrentName { get; private set; }

        public AuthService(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            var formData = new Dictionary<string, string>
            {
                { "lname", username },
                { "pass", password },
                { "submit", "1" },
                { "_sand_ajax", "1" },
                { "_sand_platform", "3" },
                { "_sand_readmin", "1" },
                { "_sand_is_wan", "false" },
                { "_sand_domain", "truongson" },
                { "_sand_web_url", "https://truongson.huelms.com/user/login/?logout=1" }
            };

            try
            {
                var response = await _apiService.PostFormDataAsync<LoginResponse>("/user/login", formData);
                
                if (response != null && !response.IsError)
                {
                    IsAuthenticated = true;
                    CurrentToken = response.Token ?? "fake_demo_token";
                    CurrentCccd = username; 
                    CurrentName = !string.IsNullOrEmpty(response.Username) ? response.Username : "Học viên (Dummy)";
                    _apiService.SetAuthToken(CurrentToken);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                // Xử lý lỗi từ API thật
                Console.WriteLine("Lỗi API Login: " + ex.Message);
                return false;
            }
        }

        public void Logout()
        {
            IsAuthenticated = false;
            CurrentToken = null;
            _apiService.SetAuthToken(string.Empty);
        }
    }
}
