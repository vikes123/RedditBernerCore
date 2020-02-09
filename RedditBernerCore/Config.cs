using Newtonsoft.Json;
using System;

namespace RedditBerner
{
    [Serializable]
    public class Config
    {
        [JsonProperty("AppId")]
        public string AppId { get; set; }

        [JsonProperty("AccessToken")]
        public string AccessToken { get; set; }

        [JsonProperty("RefreshToken")]
        public string RefreshToken { get; set; }

        public Config(string appId, string accessToken = null, string refreshToken = null)
        {
            AppId = appId;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
        }

        public Config() { }
    }
}
