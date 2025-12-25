using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace F95ZoneMetadataProvider.Helpers
{
    public class F95ZoneApiService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://f95zone.to/sam/latest_alpha/latest_data.php";

        public F95ZoneApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<ApiGame>> GetGames(string search = "", int page = 1)
        {
            try
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["cmd"] = "list";
                query["cat"] = "games";
                query["page"] = page.ToString();
                query["sort"] = "date";
                query["_"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

                if (!string.IsNullOrEmpty(search))
                {
                    query["search"] = search;
                }

                var url = $"{BaseUrl}?{query}";
                var response = await _httpClient.GetStringAsync(url);

                var apiResponse = Serialization.FromJson<ApiResponse>(response);

                if (apiResponse?.Status == "ok" && apiResponse.Msg?.Data != null)
                {
                    return apiResponse.Msg.Data;
                }
            }
            catch (Exception ex)
            {
                F95ZoneMetadataProvider.Logger.Error(ex, "Failed to get games from F95Zone API");
            }

            return new List<ApiGame>();
        }
    }

    public class ApiResponse
    {
        public string Status { get; set; }
        public ApiResponseMsg Msg { get; set; }
    }

    public class ApiResponseMsg
    {
        public List<ApiGame> Data { get; set; }
    }

    public class ApiGame
    {
        [SerializationPropertyName("thread_id")]
        public int ThreadId { get; set; }

        [SerializationPropertyName("title")]
        public string Title { get; set; }

        [SerializationPropertyName("version")]
        public string Version { get; set; }
    }
}
