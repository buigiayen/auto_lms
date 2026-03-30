using Newtonsoft.Json;

namespace LotusLMSClient.Models
{
    public class LoginResponse
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }
        
        [JsonProperty("error")]
        public bool IsError { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
