using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK.Models;

namespace F95ZoneMetadataProvider
{
    public class F95ZoneMetadataProvider : MetadataPlugin
    {
        public static readonly ILogger Logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("ab820846-6ffe-4883-ba22-e99af02a803f");

        /// <summary>
        /// Gets the default set of metadata fields to be retrieved or displayed.
        /// </summary>
        /// <remarks>
        /// This collection defines which <see cref="MetadataField"/> values are included
        /// by default in metadata queries or UI presentations.
        /// </remarks>
        public static List<MetadataField> Fields { get; } = new List<MetadataField>
        {
            MetadataField.Developers,
            MetadataField.Features,
            MetadataField.Genres,
            MetadataField.Icon,
            MetadataField.Links,
            MetadataField.Name,
            MetadataField.Tags,
            MetadataField.BackgroundImage,
            MetadataField.CommunityScore,
            MetadataField.CoverImage
        };

        public override List<MetadataField> SupportedFields { get; } = Fields;

        public override string Name => "F95Zone";
        public static IPlayniteAPI Api = null!;
        public static Settings Settings = null!;
        public static HttpClient SharedClient { get; private set; }
        public static HttpClientHandler SharedHandler { get; private set; }
        public static Helpers.F95ZoneApiService ApiService { get; private set; }

        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        /// <summary>
        /// Initializes a new instance of the <see cref="F95ZoneMetadataProvider"/> class.
        /// </summary>
        /// <param name="api">An instance of the Playnite API for interacting with the host application.</param>
        public F95ZoneMetadataProvider(IPlayniteAPI api) : base(api)
        {
            Api = api;
            Settings = new Settings(this, api);

            Properties = new MetadataPluginProperties
            {
                HasSettings = true
            };
        }

        private void InitializeClient()
        {
            SharedHandler = new HttpClientHandler();
            SharedHandler.AllowAutoRedirect = true;
            SharedHandler.CookieContainer = Settings.CreateCookieContainer();

            SharedClient = new HttpClient(SharedHandler);
            SharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            SharedClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            SharedClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

            ApiService = new Helpers.F95ZoneApiService(SharedClient);

            void UpdateCookies(object s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            {
                var container = SharedHandler.CookieContainer;
                var f95Uri = new Uri("https://f95zone.to");

                // Expire existing cookies for the domain to "clear" them
                var existingCookies = container.GetCookies(f95Uri);
                foreach (System.Net.Cookie c in existingCookies)
                {
                    c.Expired = true;
                }

                // Add new cookies from settings
                foreach (var cookie in Settings.ZoneCookies)
                {
                    container.Add(new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
                }
            }

            // Subscribe to initial collection
            if (Settings.ZoneCookies != null)
            {
                Settings.ZoneCookies.CollectionChanged += UpdateCookies;
            }

            // Handle property changes (if the whole collection is replaced)
            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Settings.ZoneCookies))
                {
                    if (Settings.ZoneCookies != null)
                    {
                        // Note: We are adding a new subscription.
                        // If the collection was reused, it might have multiple handlers, but here we assume it's a new or previously untracked one.
                        Settings.ZoneCookies.CollectionChanged += UpdateCookies;
                        UpdateCookies(null, null);
                    }
                }
            };
        }

        private async Task RefreshCloudflareCookies()
        {
            try
            {
                // Create a separate client that shares the cookie container/handler but mimics a browser
                using (var browserClient = new HttpClient(SharedHandler, disposeHandler: false))
                {
                    browserClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                    browserClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                    browserClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

                    var response = await browserClient.GetAsync("https://f95zone.to");
                    Logger.Info($"Cloudflare cookie refresh status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to refresh Cloudflare cookies");
            }
        }

        /// <summary>
        /// Overrides the base provider factory to return a zone-specific metadata provider.
        /// </summary>
        /// <param name="options">The options used to configure the metadata request.</param>
        /// <returns>
        /// An instance of <see cref="F95ZoneMetadataProviderProvider"/> configured with the provided options.
        /// </returns>
        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            return new F95ZoneMetadataProviderProvider(options, this);
        }

        /// <summary>
        /// Called when the application has finished starting.
        /// Initializes the metadata scrapper and, if enabled in settings, checks all games for updates.
        /// </summary>
        /// <param name="args">The event arguments for the application startup event.</param>
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            InitializeClient();
            _ = RefreshCloudflareCookies();

            // Check if user is logged in
            if (!F95ZoneMetadataProvider.Settings.ZoneCookies.Any(c => c.Name == "xf_user"))
            {
                this.PlayniteApi.Notifications.Add(new NotificationMessage(
                    "F95ZoneLogin",
                    "F95Zone: You are not logged in. Please visit Settings to log in.",
                    NotificationType.Info,
                    () => this.PlayniteApi.MainView.OpenPluginSettings(this.Id)
                ));
            }

            if (F95ZoneMetadataProvider.Settings.CheckForUpdates)
            {
                var apiService = new Helpers.F95ZoneApiService(SharedClient);
                // Create an update checker and initiate the update process for all games
                UpdateChecker checker = new UpdateChecker(this.PlayniteApi, apiService);
                checker.CheckAllGamesForUpdates();
            }

            // Ensure base implementation is executed
            base.OnApplicationStarted(args);
        }

        /// <summary>
        /// Retrieves the application settings.
        /// </summary>
        /// <param name="firstRunSettings">
        /// A boolean indicating whether settings are being requested for the first run.
        /// </param>
        /// <returns>
        /// An <see cref="ISettings"/> instance representing the current settings.
        /// </returns>
        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        /// <summary>
        /// Retrieves the settings view for the F95Zone metadata provider.
        /// </summary>
        /// <param name="firstRunSettings">
        /// True if this settings view is being requested as part of the first run wizard; otherwise, false.
        /// </param>
        /// <returns>
        /// A <see cref="UserControl"/> that hosts the F95Zone metadata provider settings UI.
        /// </returns>
        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new F95ZoneMetadataProviderSettingsView();
        }
    }
}