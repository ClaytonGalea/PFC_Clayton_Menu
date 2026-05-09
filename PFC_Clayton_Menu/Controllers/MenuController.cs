using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;
using Google.Cloud.PubSub.V1;
using System.Text;
using Microsoft.AspNetCore.Authorization;

[Authorize]
public class MenuController : Controller
{
    private readonly string bucketName = "menu-images-bucket-xyz";
    private readonly FirestoreDb _firestore;

    public IActionResult Upload()
    {
        return View();
    }

    public MenuController()
    {
        _firestore = FirestoreDb.Create("cg-pfc-menu-ai-system");
    }

    // 🔥 HELPER METHOD: Delete cache for this menu
    private async Task DeleteMenuTranslationCache(string restaurantId, string menuId)
    {
        var cacheQuery = _firestore.Collection("translationCache")
            .WhereEqualTo("restaurantId", restaurantId)
            .WhereEqualTo("menuId", menuId);

        var snapshot = await cacheQuery.GetSnapshotAsync();

        foreach (var doc in snapshot.Documents)
        {
            await doc.Reference.DeleteAsync();
        }

        Console.WriteLine("Cache invalidated for menu");
    }

    [HttpPost]
    public async Task<IActionResult> Upload(List<IFormFile> files)
    {
        var storage = await StorageClient.CreateAsync();

        TopicName topicName = TopicName.FromProjectTopic(
            "cg-pfc-menu-ai-system",
            "menu-uploads-topic"
        );

        PublisherClient publisher = await PublisherClient.CreateAsync(topicName);

        string restaurantId = "rest1";
        string menuId = "menu1";

        foreach (var file in files)
        {
            if (file.Length > 0)
            {
                string fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);

                using var stream = file.OpenReadStream();

                // ✅ 1. Upload to bucket
                await storage.UploadObjectAsync(
                    bucketName,
                    fileName,
                    file.ContentType,
                    stream
                );

                string imageUrl = $"https://storage.googleapis.com/{bucketName}/{fileName}";

                // ✅ 2. Save to Firestore
                var restaurantRef = _firestore.Collection("restaurants").Document(restaurantId);
                var menuRef = restaurantRef.Collection("menus").Document(menuId);

                await menuRef.Collection("images").AddAsync(new
                {
                    imageUrl = imageUrl,
                    uploadedAt = DateTime.UtcNow
                });

                // 🔥 3. SEND PUB/SUB MESSAGE
                string message = $"New image uploaded: {fileName}";
                await publisher.PublishAsync(message);
            }
        }

        // 🔥 4. AFTER upload → DELETE CACHE (VERY IMPORTANT)
        await DeleteMenuTranslationCache(restaurantId, menuId);

        return Ok("Upload complete");
    }
}