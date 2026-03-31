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
        public int CurrentUserIid { get; private set; }
        public string CurrentUserId { get; private set; }

        public const string SandDeviceUuid = "acd42ed7-b678-4704-b07c-7128b06e4f16";
        public const string SandSessionId = "2c710456-c150-42e1-85a6-f6e7ccbe7383";
        public const string SandUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";

        public ApiService ApiService => _apiService;

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
                { "_sand_ga_sessionToken", string.Empty },
                { "_sand_ga_browserToken", string.Empty },
                { "_sand_domain", "truongson" },
                { "_sand_masked", string.Empty },
                { "_sand_web_url", ApiService.WEB_LOGIN_URL },
                { "_sand_device_uuid", SandDeviceUuid },
                { "_sand_session_id", SandSessionId },
                { "_sand_use_internal_network", "0" },
                { "allow_cache_api_cdn", "1" },
                { "_sand_user_agent", SandUserAgent },
                { "_sand_ri", "11f85ebe6b47c10449b6b7a63f62455c" },
                { "_sand_rit", "b93346eb488df0b749311e41637b5b94:qY5ll1uFeXNTM8+JGoyDGtutkEy9btf510RkFCzDnrJexiCQiKXo9THpuGntLFRD" }
            };

            try
            {
                var response = await _apiService.PostFormDataAsync<LoginResponse>("/user/login", formData);
                
                if (response == null || !response.Success || response.Result == null)
                {
                    return false;
                }

                if (response.Result.Pass != 1 || string.IsNullOrWhiteSpace(response.Result.Token))
                {
                    return false;
                }

                IsAuthenticated = true;
                CurrentToken = response.Result.Token;
                CurrentCccd = !string.IsNullOrWhiteSpace(response.Result.Lname) ? response.Result.Lname : username;
                CurrentName = !string.IsNullOrWhiteSpace(response.Result.Name) ? response.Result.Name : CurrentCccd;
                CurrentUserIid = response.Result.Iid;
                CurrentUserId = response.Result.Id;
                _apiService.SetAuthToken(CurrentToken);
                return true;
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
            CurrentUserId = null;
            CurrentUserIid = 0;
            _apiService.SetAuthToken(string.Empty);
        }
    }
}
