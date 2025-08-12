using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace F95ZoneMetadataProvider
{
    public class UpdateChecker
    {
        private readonly IPlayniteAPI _api;
        private readonly Scrapper _scrapper;

        public UpdateChecker(IPlayniteAPI api, Scrapper scrapper)
        {
            _api = api;
            _scrapper = scrapper;
        }

        /// <summary>
        /// Checks all games in the database for updates by analyzing their associated links.
        /// </summary>
        /// <remarks>This method iterates through all games in the database and checks for updates if a
        /// valid link  to an F95Zone thread is found. If an error occurs during the process, a notification is added
        /// to inform the user.</remarks>
        public async void CheckAllGamesForUpdates()
        {
            try
            {
                foreach (var game in _api.Database.Games)
                {
                    Link? link = game.Links?.FirstOrDefault(link => link.Url.StartsWith("https://f95zone.to/threads/"));
                    if (link == null) continue;
                    await CheckGameForUpdates(game, link);
                }
            }
            catch (Exception ex)
            {
                _api.Notifications.Add(Guid.NewGuid().ToString(),
                    "[F95Zone] Failed to check for updates (check your internet connection), error: " + ex.Message + ex.StackTrace, NotificationType.Info);
            }
        }

        /// <summary>
        /// Checks the specified game for updates by comparing its current version with the latest version available
        /// online.
        /// </summary>
        /// <remarks>If the latest version of the game differs from the current version, a notification is
        /// added to indicate that an update is available.</remarks>
        /// <param name="game">The game to check for updates. The <see cref="Game.Version"/> property is used for comparison.</param>
        /// <param name="link">The link to the game's online resource, which is used to retrieve the latest version information.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task CheckGameForUpdates(Game game, Link link)
        {
            // Check if game has f95zone link added
            var scraped = await this._scrapper.ScrapPage(link.Url.Split(new[] { "https://f95zone.to/threads/" },
                StringSplitOptions.None)[1]);
            var latestVersion = scraped?.Version;
            if (latestVersion == null || latestVersion == string.Empty) return;

            // Mismatched version, send notification!
            if (latestVersion != game.Version)
            {
                _api.Notifications.Add(Guid.NewGuid().ToString(), $"Game update available: {game.Name}, link: {link.Url}, (Old Version: {game.Version}, New Version: {latestVersion})",
                    /*"Game update available: " + game.Name + ", link: " + link.Url + " (Old Version: " + game.Version + ", New Version: " + latestVersion + ")",*/ NotificationType.Info);
            }
        }
    }
}