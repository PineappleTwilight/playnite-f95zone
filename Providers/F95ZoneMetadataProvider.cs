using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Windows.Controls;

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
            SharedClient.DefaultRequestHeaders.UserAgent.ParseAdd("Playnite.Extensions");

            void UpdateCookies(object s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            {
                SharedHandler.CookieContainer = Settings.CreateCookieContainer();
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
                        SharedHandler.CookieContainer = Settings.CreateCookieContainer();
                    }
                }
            };
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
            F95ZoneMetadataProvider.Settings.RefreshCookies();

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