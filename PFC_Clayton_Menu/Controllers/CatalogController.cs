using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

public class CatalogController : Controller
{
    private readonly FirestoreDb _firestore;
    private readonly HttpClient _httpClient;

    private const string TranslateFunctionUrl =
        "https://translate-menu-item-657622571209.europe-west1.run.app";

    public CatalogController()
    {
        _firestore = FirestoreDb.Create("cg-pfc-menu-ai-system");
        _httpClient = new HttpClient();
    }

    public async Task<IActionResult> Index(string searchTerm = "", string sortOrder = "asc")
    {
        var menuRef = _firestore
            .Collection("restaurants")
            .Document("rest1")
            .Collection("menus")
            .Document("menu1");

        var snapshot = await menuRef.GetSnapshotAsync();

        var model = new CatalogPageViewModel
        {
            MenuId = snapshot.Id,
            Status = snapshot.ContainsField("status") ? snapshot.GetValue<string>("status") : "",
            SearchTerm = searchTerm,
            SortOrder = sortOrder,
            Items = new List<MenuItemViewModel>()
        };

        if (snapshot.ContainsField("items"))
        {
            var rawItems = snapshot.GetValue<List<Dictionary<string, object>>>("items");

            foreach (var item in rawItems)
            {
                string name = item.ContainsKey("name") ? item["name"]?.ToString() ?? "" : "";
                double price = 0;

                if (item.ContainsKey("price"))
                {
                    double.TryParse(item["price"].ToString(), out price);
                }

                model.Items.Add(new MenuItemViewModel
                {
                    Name = name,
                    Price = price
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            model.Items = model.Items
                .Where(i => i.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        model.Items = sortOrder == "desc"
            ? model.Items.OrderByDescending(i => i.Price).ToList()
            : model.Items.OrderBy(i => i.Price).ToList();

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Translate([FromBody] TranslateRequest request)
    {
        if (request == null ||
            string.IsNullOrWhiteSpace(request.Text) ||
            string.IsNullOrWhiteSpace(request.TargetLanguage))
        {
            return Json(new { success = false, message = "Missing text or language" });
        }

        string restaurantId = "rest1";
        string menuId = "menu1";
        string cacheKey = $"{restaurantId}_{menuId}_{request.TargetLanguage}_{request.Text.GetHashCode()}";

        var cacheRef = _firestore.Collection("translationCache").Document(cacheKey);
        var cacheSnapshot = await cacheRef.GetSnapshotAsync();

        if (cacheSnapshot.Exists)
        {
            string cachedTranslation = cacheSnapshot.GetValue<string>("translatedText");

            return Json(new
            {
                success = true,
                translatedText = cachedTranslation,
                source = "cache"
            });
        }

        var response = await _httpClient.PostAsJsonAsync(TranslateFunctionUrl, new
        {
            text = request.Text,
            targetLanguage = request.TargetLanguage
        });

        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return Json(new
            {
                success = false,
                message = "Translation failed",
                details = responseText
            });
        }

        var result = await response.Content.ReadFromJsonAsync<TranslationResult>();

        if (result == null || string.IsNullOrWhiteSpace(result.translatedText))
        {
            return Json(new { success = false, message = "No translation returned" });
        }

        await cacheRef.SetAsync(new
        {
            restaurantId,
            menuId,
            originalText = request.Text,
            targetLanguage = request.TargetLanguage,
            translatedText = result.translatedText,
            cachedAt = DateTime.UtcNow
        });

        return Json(new
        {
            success = true,
            translatedText = result.translatedText,
            source = "api"
        });
    }
}

public class CatalogPageViewModel
{
    public string MenuId { get; set; }
    public string Status { get; set; }
    public string SearchTerm { get; set; }
    public string SortOrder { get; set; }
    public List<MenuItemViewModel> Items { get; set; }
}

public class MenuItemViewModel
{
    public string Name { get; set; }
    public double Price { get; set; }
}

public class TranslateRequest
{
    public string Text { get; set; }
    public string TargetLanguage { get; set; }
}

public class TranslationResult
{
    public string translatedText { get; set; }
}