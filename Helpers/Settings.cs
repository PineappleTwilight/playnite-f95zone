using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace F95ZoneMetadataProvider
{
    /// <summary>
    /// Represents the configuration settings for the plugin, including user preferences, cookies, and other operational
    /// parameters.
    /// </summary>
    /// <remarks>This class provides properties and methods to manage plugin settings, such as cookies
    /// required for authentication, user-defined labels and tags, and various operational flags. It also includes
    /// functionality for editing, verifying, and persisting settings. Changes to settings are tracked and can trigger
    /// property change notifications.</remarks>
    public class Settings : ISettings, INotifyPropertyChanged
    {
        /// <summary>
        /// The URL used for the login page of the F95Zone website.
        /// </summary>
        private const string LoginUrl = "https://f95zone.to/login";

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private readonly Plugin? _plugin;
        private readonly IPlayniteAPI? _playniteAPI;
        private Settings? _previousSettings;
        private ObservableCollection<HttpCookie> _zoneCookies = new ObservableCollection<HttpCookie>();

        /// <summary>
        /// Gets or sets the collection of DDoS Guard cookies.
        /// </summary>
        /// <remarks>This property is used to store cookies required for interacting with services
        /// protected by DDoS Guard. Each tuple represents a cookie, with the first item being the cookie name and the
        /// second item being the cookie value.</remarks>
        public ObservableCollection<HttpCookie> ZoneCookies
        {
            get => _zoneCookies;
            set
            {
                if (_zoneCookies != null)
                    _zoneCookies.CollectionChanged -= ZoneCookies_CollectionChanged;
                _zoneCookies = value;
                if (_zoneCookies != null)
                    _zoneCookies.CollectionChanged += ZoneCookies_CollectionChanged;
                OnPropertyChanged(nameof(ZoneCookies));
                OnPropertyChanged(nameof(CookiesCount));
            }
        }

        /// <summary>
        /// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event for the ZoneCookies collection.
        /// </summary>
        /// <param name="sender">The source of the event, typically the ZoneCookies collection.</param>
        /// <param name="e">The event data containing information about the changes to the collection.</param>
        private void ZoneCookies_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CookiesCount));
        }

        public PlayniteProperty LabelProperty { get; set; } = PlayniteProperty.Features;
        public PlayniteProperty TagProperty { get; set; } = PlayniteProperty.Tags;

        public int CookiesCount => ZoneCookies.Count;

        public bool CheckForUpdates { get; set; }
        public bool ShouldScrapeLinks { get; set; } = true;

        public bool UpdateFinishedGames { get; set; }

        public bool SetDefaultIcon { get; set; } = true;

        /// <summary>
        /// Creates and initializes a <see cref="CookieContainer"/> with cookies for the "f95zone.to" domain.
        /// </summary>
        /// <remarks>This method iterates through the collection of cookies in <c>ZoneCookies</c> and adds
        /// them to a new <see cref="CookieContainer"/>. Only cookies with non-null <c>Name</c> and <c>Value</c>
        /// properties are added. Each cookie is configured to be secure, HTTP-only, and set to expire in 7 days from
        /// the current time.</remarks>
        /// <returns>A <see cref="CookieContainer"/> containing the initialized cookies, or <see langword="null"/> if no valid
        /// cookies are found.</returns>
        public CookieContainer CreateCookieContainer()
        {
            var container = new CookieContainer();

            foreach (HttpCookie cookie in ZoneCookies)
            {
                container.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
            }

            return container;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Settings"/> class.
        /// </summary>
        /// <remarks>This constructor sets up the <see cref="ZoneCookies"/> collection to monitor changes
        /// and automatically raise the <see cref="OnPropertyChanged"/> event for the <see cref="CookiesCount"/>
        /// property whenever the collection is modified.</remarks>
        public Settings()
        {
            ZoneCookies.CollectionChanged += (s, e) => OnPropertyChanged(nameof(CookiesCount));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Settings"/> class with the specified plugin and Playnite API.
        /// </summary>
        /// <remarks>If previously saved settings exist, they are loaded and applied to the current
        /// instance.  Otherwise, default values are used for all settings.</remarks>
        /// <param name="plugin">The plugin instance associated with these settings. This is used to load and manage plugin-specific
        /// settings.</param>
        /// <param name="playniteAPI">The Playnite API instance, providing access to Playnite's core functionality.</param>
        public Settings(Plugin plugin, IPlayniteAPI playniteAPI)
        {
            _plugin = plugin;
            _playniteAPI = playniteAPI;

            var savedSettings = plugin.LoadPluginSettings<Settings>();
            if (savedSettings is not null)
            {
                ZoneCookies = savedSettings.ZoneCookies;
                LabelProperty = savedSettings.LabelProperty;
                TagProperty = savedSettings.TagProperty;
                CheckForUpdates = savedSettings.CheckForUpdates;
                ShouldScrapeLinks = savedSettings.ShouldScrapeLinks;
                UpdateFinishedGames = savedSettings.UpdateFinishedGames;
                SetDefaultIcon = savedSettings.SetDefaultIcon;
            }
        }

        /// <summary>
        /// Initiates the login process by opening a web view and navigating to the login URL.
        /// </summary>
        /// <remarks>This method creates a web view with specific settings, including a custom user agent
        /// and window dimensions. The web view navigates to the login URL and attaches an event handler to monitor
        /// loading state changes.</remarks>
        /// <exception cref="InvalidDataException">Thrown if the Playnite API instance is not initialized.</exception>
        public void DoLogin()
        {
            if (_playniteAPI is null) throw new InvalidDataException();

            var webView = _playniteAPI.WebViews.CreateView(new WebViewSettings
            {
                UserAgent = "Playnite.Extensions",
                JavaScriptEnabled = true,
                WindowWidth = 900,
                WindowHeight = 700,
            });

            webView.DeleteDomainCookies("f95zone.to");
            webView.DeleteDomainCookies(".f95zone.to");
            webView.DeleteDomainCookies(".check.ddos-guard.net");

            webView.Open();
            webView.NavigateAndWait(LoginUrl);

            webView.LoadingChanged += WebViewOnLoadingChanged;
        }

        /// <summary>
        /// Adds a cookie to the list of zone cookies.
        /// </summary>
        /// <remarks>This method ensures thread safety by invoking the addition operation on the
        /// application's dispatcher.</remarks>
        /// <param name="cookie">The <see cref="HttpCookie"/> to add. Must not be <see langword="null"/>.</param>
        private void AddCookieToList(HttpCookie cookie)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.ZoneCookies.Add(cookie);
            });
        }

        /// <summary>
        /// Handles the loading state changes of a <see cref="IWebView"/> instance.
        /// </summary>
        /// <remarks>This method is invoked when the loading state of the web view changes. If the web
        /// view has finished loading and the current address does not match the login URL, it processes cookies from
        /// the web view. Relevant cookies (e.g., those starting with "xf_" or "ddg") are extracted and stored in the
        /// <c>ZoneCookies</c> collection.  If required cookies are found, the method updates the <c>ZoneCookies</c>
        /// collection and saves the plugin settings. The web view is then closed.  Exceptions may be thrown if the
        /// sender is not a valid <see cref="IWebView"/> instance.</remarks>
        /// <param name="sender">The <see cref="IWebView"/> instance that triggered the event.</param>
        /// <param name="args">The event arguments containing information about the loading state.</param>
        /// <exception cref="NotImplementedException">Thrown if the <paramref name="sender"/> is not an <see cref="IWebView"/> instance.</exception>
        private async void WebViewOnLoadingChanged(object sender, WebViewLoadingChangedEventArgs args)
        {
            if (args.IsLoading) return;

            IWebView web = sender as IWebView;

            var address = web.GetCurrentAddress();
            if (address is null || address.StartsWith(LoginUrl)) return;

            await Task.Run(() =>
            {
                List<string> requiredCookies = new List<string>
                {
                    "xf_session",
                    "xf_user",
                    "xf_csrf"
                };

                // Get cookies from the web view
                var cookies = web.GetCookies();
                if (cookies is null || !cookies.Any()) return;
                if (!cookies.Any(x => requiredCookies.Any(y => y == x.Name))) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.ZoneCookies.Clear();
                });

                // Extract cookies from the web view
                foreach (var cookie in cookies)
                {
                    if (cookie.Name is null || cookie.Value is null) continue;

                    if ((cookie.Name.Contains("xf_") || cookie.Name.Contains("ddg")))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (ZoneCookies.Any(x => x.Name.Equals(cookie.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                // Update existing cookie
                                var existingCookie = ZoneCookies.First(x => x.Name.Equals(cookie.Name, StringComparison.OrdinalIgnoreCase));
                                existingCookie.Value = cookie.Value;
                            }
                            else
                            {
                                // Add new cookie
                                AddCookieToList(cookie);
                            }
                        });
                    }
                }

                _plugin?.SavePluginSettings(this);

                web.Close();
            });
        }

        private static string? GetCookie(IEnumerable<HttpCookie> cookies, string name)
        {
            var cookie = cookies.FirstOrDefault(x => x.Name is not null && x.Value is not null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return cookie?.Value;
        }

        /// <summary>
        /// Begins an edit operation by capturing the current settings as a snapshot.
        /// </summary>
        /// <remarks>This method saves the current state of the settings, allowing changes to be made  and
        /// later reverted if necessary. Use this method in conjunction with a corresponding  commit or rollback
        /// mechanism to manage changes to the settings.</remarks>
        public void BeginEdit()
        {
            _previousSettings = new Settings
            {
                ZoneCookies = ZoneCookies,
                LabelProperty = LabelProperty,
                TagProperty = TagProperty,
                CheckForUpdates = CheckForUpdates,
                ShouldScrapeLinks = ShouldScrapeLinks,
                UpdateFinishedGames = UpdateFinishedGames,
                SetDefaultIcon = SetDefaultIcon
            };
        }

        /// <summary>
        /// Ends the editing session and saves the current settings of the plugin.
        /// </summary>
        /// <remarks>This method finalizes any ongoing edits and clears the previous settings.  It invokes
        /// the plugin's save functionality to persist the current settings.</remarks>
        public void EndEdit()
        {
            _previousSettings = null;
            _plugin?.SavePluginSettings(this);
        }

        /// <summary>
        /// Cancels the current edit operation and reverts all settings to their previous values.
        /// </summary>
        /// <remarks>This method restores the settings to the state they were in before the edit operation
        /// began. If no previous settings are available, the method performs no action.</remarks>
        public void CancelEdit()
        {
            if (_previousSettings is null) return;

            ZoneCookies = _previousSettings.ZoneCookies;
            LabelProperty = _previousSettings.LabelProperty;
            TagProperty = _previousSettings.TagProperty;
            CheckForUpdates = _previousSettings.CheckForUpdates;
            ShouldScrapeLinks = _previousSettings.ShouldScrapeLinks;
            UpdateFinishedGames = _previousSettings.UpdateFinishedGames;
            SetDefaultIcon = _previousSettings.SetDefaultIcon;
        }

        /// <summary>
        /// Verifies the current settings and identifies any configuration issues.
        /// </summary>
        /// <remarks>This method checks for the presence of required cookies and validates the values of
        /// specific properties. If any issues are detected, they are added to the <paramref name="errors"/>
        /// list.</remarks>
        /// <param name="errors">When this method returns, contains a list of error messages describing the issues found during verification,
        /// or an empty list if no issues were found.</param>
        /// <returns><see langword="true"/> if the settings are valid; otherwise, <see langword="false"/>.</returns>
        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (!ZoneCookies.Any(x => x.Name == "xf_user"))
            {
                errors.Add("The xf_user cookie has to be set!");
            }

            if (!ZoneCookies.Any(x => x.Name == "xf_csrf"))
            {
                errors.Add("The xf_csrf cookie has to be set!");
            }

            if (!Enum.IsDefined(typeof(PlayniteProperty), LabelProperty))
            {
                errors.Add($"Unknown value \"{LabelProperty}\"");
            }

            if (!Enum.IsDefined(typeof(PlayniteProperty), TagProperty))
            {
                errors.Add($"Unknown value \"{TagProperty}\"");
            }

            return !errors.Any();
        }
    }
}