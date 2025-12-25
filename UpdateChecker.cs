using F95ZoneMetadataProvider.Helpers;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

#nullable enable

namespace F95ZoneMetadataProvider
{
    public class UpdateChecker
    {
        private readonly IPlayniteAPI _api;
        private readonly F95ZoneApiService _apiService;

        public UpdateChecker(IPlayniteAPI api, F95ZoneApiService apiService)
        {
            _api = api;
            _apiService = apiService;
        }

        /// <summary>
        /// Checks all games in the database for updates using the F95Zone API.
        /// </summary>
        public async void CheckAllGamesForUpdates()
        {
            await Task.Run(async () =>
            {
                try
                {
                    using (var semaphore = new System.Threading.SemaphoreSlim(5))
                    {
                        var tasks = _api.Database.Games.Where(g => g.Links != null && g.Links.Count > 0).Select(async game =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                Link? link = game.Links.FirstOrDefault(link => link.Url.StartsWith("https://f95zone.to/threads/"));

                                if (link != null)
                                {
                                    await CheckGameForUpdates(game, link);
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });

                        await Task.WhenAll(tasks);
                    }
                }
                catch (Exception ex)
                {
                    _api.Notifications.Add(Guid.NewGuid().ToString(),
                        "[F95Zone] Failed to check for updates (check your internet connection). Error: " + ex.Message + ex.StackTrace, NotificationType.Error);
                }
            });
        }

        /// <summary>
        /// Checks the specified game for updates by querying the API and comparing versions.
        /// </summary>
        private async Task CheckGameForUpdates(Game game, Link link)
        {
            try
            {
                var idStr = F95ZoneMetadataProviderProvider.GetIdFromLink(link.Url);
                if (string.IsNullOrEmpty(idStr)) return;

                // The ID usually comes as "name.123" or just "123".
                // If it has a dot, the last part is the ID.
                string threadIdStr = idStr;
                if (idStr.Contains("."))
                {
                    threadIdStr = idStr.Split('.').Last();
                }
                
                if (!int.TryParse(threadIdStr, out int threadId))
                {
                    return;
                }

                // Search by Game Name
                var results = await _apiService.GetGames(search: game.Name);
                
                // Find match by Thread ID
                var match = results.FirstOrDefault(x => x.ThreadId == threadId);

                if (match == null)
                {
                    // Fallback: If name search didn't find it (maybe name mismatch), 
                    // we could try searching by ID if the API supported it, but for now we skip.
                    return;
                }

                var latestVersion = match.Version;
                if (string.IsNullOrEmpty(latestVersion)) return;

                if (latestVersion != game.Version)
                {
                    NotificationMessage msg = new NotificationMessage(Guid.NewGuid().ToString(), $"Game update available: {game.Name}\nOld Version: {game.Version}\nNew Version: {latestVersion}", NotificationType.Info, new Action(() =>
                    {
                        System.Diagnostics.Process.Start(link.Url);
                    }));
                    _api.Notifications.Add(msg);
                }
            }
            catch (Exception)
            {
                // Suppress errors for individual games so others can continue
            }
        }
    }
}