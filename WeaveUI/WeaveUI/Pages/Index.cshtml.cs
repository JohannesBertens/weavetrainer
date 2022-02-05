using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeaveUI.Pages
{

    public class IndexModel : PageModel
    {
        public class WeavesObject
        {
            public int intelligence;
            public int rank;
            public List<string> elements = new();
            public int elementPracs;
            public int weavePracs;
            public int totalPracs;
            public int levelRequired;
            public List<string> commands = new();
            public List<Weave> weavesDebug = new();
        }

        public class Weave
        {
            public string weave;
            public int percentage;
            public int pracsNeeded;
        }

        private readonly ILogger<IndexModel> _logger;
        private static readonly HttpClient _httpClient = new HttpClient();

        public WeavesObject? weavesObject = new WeavesObject();
        public string intelligence = "18";
        public string rank = "0";
        public string commands;
        public string error;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            if (string.IsNullOrEmpty(Request.QueryString.Value))
            {
                return;
            }

            // Rewrite URL from the OTHER tool
            if (Request.Query.ContainsKey("intl") || Request.QueryString.Value.Contains("_"))
            {
                Response.Redirect(Request.QueryString.Value.Replace("intl", "int").Replace("_", " "));
                return;
            }

            // set int & rank
            intelligence = Request.Query["int"];
            rank = Request.Query["rank"];

            var result = _httpClient.GetAsync(new Uri("https://weavepracs.azurewebsites.net/api/GetPracs" + Request.QueryString.Value)).Result;
            string jsonString = result.Content.ReadAsStringAsync().Result;
            if (!result.IsSuccessStatusCode)
            {
                error = jsonString;
                return;
            }
            

            error = "";
            weavesObject = JsonSerializer.Deserialize<WeavesObject>(jsonString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true,
            });
            if (weavesObject != null)
            {
                commands = string.Join(Environment.NewLine, weavesObject.commands);
            }
        }

        public RedirectResult OnPost()
        {
            Dictionary<string, string> requestElements = new();
            foreach (var element in Request.Form)
            {
                Console.WriteLine($"Key: {element.Key} Value: {element.Value}");
                if (!element.Key.StartsWith("_")) requestElements.Add(element.Key, element.Value);
            }

            List<string> elementList = new();
            foreach (var kvPair in requestElements)
            {
                if (kvPair.Key == "rank" || int.TryParse(kvPair.Value, out int percentage) && percentage > 0)
                    elementList.Add(kvPair.Key + "=" + kvPair.Value);
            }
            var queryString = "?" + string.Join("&", elementList);

            return Redirect(queryString);
        }
    }
}