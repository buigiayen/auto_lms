using Newtonsoft.Json;

namespace LotusLMSClient.Models
{
    public class LoginResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public LoginResult Result { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class LoginResult
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("lname")]
        public string Lname { get; set; }

        [JsonProperty("pass")]
        public int Pass { get; set; }
    }
}
